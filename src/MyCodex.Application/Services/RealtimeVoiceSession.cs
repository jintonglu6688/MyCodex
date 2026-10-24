using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    // One voice connection at a time: audio offer -> thread/realtime/start -> SDP answer -> media connected.
    public sealed class RealtimeVoiceSession : IDisposable
    {
        private const int AnswerTimeoutMs = 30000;
        private const int StopRequestTimeoutMs = 6000;
        private const int ClosedDrainTimeoutMs = 1500;

        private sealed class Attempt
        {
            public RealtimeVoiceStart Request;
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly TaskCompletionSource<string> Answer = NewCompletion<string>();
            public readonly TaskCompletionSource<bool> RequestFinished = NewCompletion<bool>();
            public readonly TaskCompletionSource<bool> Closed = NewCompletion<bool>();
            public Task StopTask;
            public bool StartIssued;
            public bool Started;
            public bool StopIssued;
        }

        private readonly object _gate = new object();
        private readonly IAppServerRealtimeClient _client;
        private readonly IRealtimeVoiceAudio _audio;
        private Attempt _active;
        private bool _disposed;

        public RealtimeVoiceSession(IAppServerRealtimeClient client, IRealtimeVoiceAudio audio)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _client.RealtimeVoiceEventReceived += OnRealtimeEvent;
            _audio.Failed += OnAudioFailed;
        }

        public event EventHandler<VoiceChatStateEvent> StateChanged;

        public event EventHandler<RealtimeVoiceEvent> TranscriptReceived;

        public VoiceChatState State { get; private set; }

        public string ThreadId
        {
            get
            {
                lock (_gate)
                {
                    return _active?.Request.ThreadId;
                }
            }
        }

        public bool? SystemEchoCancellationEnabled => _audio.SystemEchoCancellationEnabled;

        public async Task StartAsync(RealtimeVoiceStart request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ThreadId) || string.IsNullOrWhiteSpace(request.Voice))
            {
                throw new ArgumentException("A thread and voice are required.", nameof(request));
            }

            Attempt attempt;
            lock (_gate)
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(nameof(RealtimeVoiceSession));
                }

                if (_active != null)
                {
                    throw new InvalidOperationException("A voice session is already active.");
                }

                request.SessionId = Guid.NewGuid().ToString("N");
                attempt = new Attempt { Request = request };
                _active = attempt;
            }

            Publish(attempt, VoiceChatState.Connecting);
            var token = attempt.Cancellation.Token;
            try
            {
                request.OfferSdp = await _audio.PrepareAsync(request.SessionId, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (_gate)
                {
                    attempt.StartIssued = true;
                }

                try
                {
                    await _client.StartRealtimeVoiceAsync(request, token).ConfigureAwait(false);
                }
                finally
                {
                    attempt.RequestFinished.TrySetResult(true);
                }

                var answer = await WaitAsync(attempt.Answer.Task, token, AnswerTimeoutMs).ConfigureAwait(false);
                await _audio.ConnectAsync(request.SessionId, answer, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Publish(attempt, VoiceChatState.Active);
            }
            catch (OperationCanceledException)
            {
                await StopAttemptAsync(attempt, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await StopAttemptAsync(attempt, ex.Message).ConfigureAwait(false);
            }
        }

        public Task StopAsync()
        {
            return StopActiveAsync(null);
        }

        public Task SetMicrophoneEnabledAsync(bool enabled)
        {
            Attempt attempt;
            lock (_gate)
            {
                attempt = _active;
                if (attempt == null || attempt.StopIssued || State != VoiceChatState.Active)
                {
                    return Task.CompletedTask;
                }
            }

            return _audio.SetMicrophoneEnabledAsync(attempt.Request.SessionId, enabled);
        }

        public Task ConnectionLostAsync(string reason)
        {
            return StopActiveAsync(reason);
        }

        private Task StopActiveAsync(string error)
        {
            Attempt attempt;
            lock (_gate)
            {
                attempt = _active;
            }

            return attempt == null ? Task.CompletedTask : StopAttemptAsync(attempt, error);
        }

        private Task StopAttemptAsync(Attempt attempt, string error)
        {
            TaskCompletionSource<bool> completion;
            lock (_gate)
            {
                if (attempt.StopTask != null)
                {
                    return attempt.StopTask;
                }

                completion = NewCompletion<bool>();
                attempt.StopTask = completion.Task;
                attempt.StopIssued = true;
            }

            attempt.Cancellation.Cancel();
            CompleteStopAsync(attempt, error, completion);
            return completion.Task;
        }

        private async void CompleteStopAsync(Attempt attempt, string error, TaskCompletionSource<bool> completion)
        {
            try
            {
                Publish(attempt, VoiceChatState.Stopping);
                // Release the microphone first; the server stop may be slow or fail.
                try
                {
                    await _audio.StopAsync(attempt.Request.SessionId).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    error = error ?? ex.Message;
                }

                if (attempt.StartIssued)
                {
                    try
                    {
                        await WaitAsync(attempt.RequestFinished.Task, CancellationToken.None, StopRequestTimeoutMs).ConfigureAwait(false);
                        await _client.StopRealtimeVoiceAsync(attempt.Request.ThreadId, CancellationToken.None).ConfigureAwait(false);
                        // Drain the old session's closed notification before a new voice may start on this thread.
                        if (attempt.Started && !attempt.Closed.Task.IsCompleted)
                        {
                            await Task.WhenAny(attempt.Closed.Task, Task.Delay(ClosedDrainTimeoutMs)).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        error = error ?? ex.Message;
                    }
                }

                VoiceChatState state;
                lock (_gate)
                {
                    if (ReferenceEquals(_active, attempt))
                    {
                        _active = null;
                    }

                    state = error == null ? VoiceChatState.Idle : VoiceChatState.Failed;
                    State = state;
                }

                NotifyState(new VoiceChatStateEvent { State = state, Error = error });
            }
            catch (Exception ex)
            {
                // A subscriber must not turn shutdown into an unobserved async exception.
                System.Diagnostics.Trace.TraceWarning("Voice cleanup failed: {0}", ex.GetType().Name);
            }
            finally
            {
                completion.TrySetResult(true);
            }
        }

        private void OnRealtimeEvent(object sender, RealtimeVoiceEvent message)
        {
            Attempt attempt;
            lock (_gate)
            {
                attempt = _active;
            }

            if (message == null ||
                attempt == null ||
                !attempt.StartIssued ||
                !string.Equals(message.ThreadId, attempt.Request.ThreadId, StringComparison.Ordinal))
            {
                return;
            }

            if (message.Kind == RealtimeVoiceEventKind.Closed)
            {
                attempt.Closed.TrySetResult(true);
                if (!attempt.StopIssued)
                {
                    StopAttemptAsync(attempt, null);
                }

                return;
            }

            if (attempt.StopIssued && message.Kind != RealtimeVoiceEventKind.Transcript)
            {
                return;
            }

            switch (message.Kind)
            {
                case RealtimeVoiceEventKind.Started:
                    if (string.Equals(message.SessionId, attempt.Request.SessionId, StringComparison.Ordinal))
                    {
                        attempt.Started = true;
                    }

                    break;
                case RealtimeVoiceEventKind.Sdp:
                    if (attempt.Started && !string.IsNullOrWhiteSpace(message.Sdp))
                    {
                        attempt.Answer.TrySetResult(message.Sdp);
                    }

                    break;
                case RealtimeVoiceEventKind.Error:
                    StopAttemptAsync(attempt, message.Text ?? SharedLocalization.Text("VoiceChat.Error.ConnectionFailed", "The voice connection failed."));
                    break;
                case RealtimeVoiceEventKind.Transcript:
                    if (attempt.Started &&
                        string.Equals(message.SessionId, attempt.Request.SessionId, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(message.Text))
                    {
                        TranscriptReceived?.Invoke(this, message);
                    }

                    break;
            }
        }

        private void OnAudioFailed(object sender, VoiceAudioEvent message)
        {
            Attempt attempt;
            lock (_gate)
            {
                attempt = _active;
            }

            if (attempt != null && message != null && string.Equals(message.SessionId, attempt.Request.SessionId, StringComparison.Ordinal))
            {
                StopAttemptAsync(attempt, message.Error);
            }
        }

        private void Publish(Attempt attempt, VoiceChatState state)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_active, attempt) || (attempt.StopIssued && state != VoiceChatState.Stopping))
                {
                    return;
                }

                State = state;
            }

            NotifyState(new VoiceChatStateEvent { State = state });
        }

        private void NotifyState(VoiceChatStateEvent notification)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                StateChanged?.Invoke(this, notification);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceWarning("Voice state observer failed: {0}", ex.GetType().Name);
            }
        }

        private static TaskCompletionSource<T> NewCompletion<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken, int timeoutMs)
        {
            using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var delay = Task.Delay(timeoutMs, delayCancellation.Token);
                var finished = await Task.WhenAny(task, delay).ConfigureAwait(false);
                delayCancellation.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                if (finished != task)
                {
                    throw new TimeoutException(SharedLocalization.Text("VoiceChat.Error.Timeout", "The voice connection timed out. Please try again."));
                }

                return await task.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _client.RealtimeVoiceEventReceived -= OnRealtimeEvent;
            _audio.Failed -= OnAudioFailed;
            // Dispose the audio only after the stop finished with it.
            StopAsync().ContinueWith(_ => _audio.Dispose(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
