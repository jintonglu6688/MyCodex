using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Localization;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Services
{
    public sealed class RealtimeVoiceSession : IDisposable
    {
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
            public string OwnedPreviewThreadId;
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
            _audio.PreviewCompleted += OnPreviewCompleted;
        }

        public event EventHandler<VoiceChatStateEvent> StateChanged;
        public event EventHandler<RealtimeVoiceEvent> TranscriptReceived;
        public VoiceChatState State { get; private set; }
        public string ThreadId { get { lock (_gate) return _active?.Request.ThreadId; } }
        public bool IsPreview { get { lock (_gate) return _active?.Request.IsPreview == true; } }
        public bool? SystemEchoCancellationEnabled => _audio.SystemEchoCancellationEnabled;

        public async Task StartAsync(RealtimeVoiceStart request)
        {
            if (request == null || (!request.IsPreview && string.IsNullOrWhiteSpace(request.ThreadId)) ||
                string.IsNullOrWhiteSpace(request.Voice) || !Enum.IsDefined(typeof(VoiceOutputLevel), request.OutputLevel))
                throw new ArgumentException(nameof(request));
            Attempt attempt;
            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(RealtimeVoiceSession));
                if (_active != null) throw new InvalidOperationException("A voice session is already active.");
                request.SessionId = Guid.NewGuid().ToString("N");
                attempt = new Attempt { Request = request };
                _active = attempt;
            }
            Publish(attempt, VoiceChatState.Connecting);
            var token = attempt.Cancellation.Token;
            try
            {
                var audioOptions = new VoiceAudioOptions
                {
                    Muted = request.MicrophoneMuted, Preview = request.IsPreview, OutputLevel = request.OutputLevel,
                    Voice = request.Voice, FasterSpeech = request.FasterSpeech, PreviewText = request.PreviewText
                };
                if (request.IsPreview && await _audio.TryReplayPreviewAsync(request.SessionId, audioOptions, token).ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    Publish(attempt, VoiceChatState.Active);
                    return;
                }
                var prepareAudio = _audio.PrepareAsync(request.SessionId, audioOptions, token);
                if (request.IsPreview)
                {
                    await Task.WhenAll(prepareAudio, CreatePreviewThreadAsync(attempt)).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                }
                request.OfferSdp = await prepareAudio.ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                lock (_gate) attempt.StartIssued = true;
                try { await _client.StartRealtimeVoiceAsync(request, token).ConfigureAwait(false); }
                finally { attempt.RequestFinished.TrySetResult(true); }
                var answer = await WaitAsync(attempt.Answer.Task, token, 30000).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                await _audio.ConnectAsync(request.SessionId, answer, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                Publish(attempt, VoiceChatState.Active);
                if (request.IsPreview)
                    await _client.AppendRealtimeSpeechAsync(request.ThreadId,
                        "Say this voice preview once now, then remain silent: " + request.PreviewText, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await StopAttemptAsync(attempt, null).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await StopAttemptAsync(attempt, ex.Message).ConfigureAwait(false);
            }
            finally
            {
                if (attempt.StopIssued) await ReleasePreviewAsync(attempt).ConfigureAwait(false);
            }
        }

        private async Task CreatePreviewThreadAsync(Attempt attempt)
        {
            // Keep a late result observable so cancellation can still release the ephemeral thread.
            var id = await _client.CreateVoicePreviewThreadAsync(attempt.Request.WorkspacePath, CancellationToken.None).ConfigureAwait(false);
            attempt.OwnedPreviewThreadId = id;
            attempt.Request.ThreadId = id;
        }

        public Task StopAsync()
        {
            Attempt attempt;
            lock (_gate) attempt = _active;
            return attempt == null ? Task.CompletedTask : StopAttemptAsync(attempt, null);
        }

        public async Task SetMutedAsync(bool muted)
        {
            Attempt attempt;
            lock (_gate) attempt = _active;
            if (attempt == null || State != VoiceChatState.Active) return;
            await _audio.SetMutedAsync(attempt.Request.SessionId, muted).ConfigureAwait(false);
        }

        public async Task SetOutputLevelAsync(VoiceOutputLevel level)
        {
            if (!Enum.IsDefined(typeof(VoiceOutputLevel), level)) throw new ArgumentOutOfRangeException(nameof(level));
            Attempt attempt;
            lock (_gate) attempt = _active;
            if (attempt == null || State != VoiceChatState.Active) return;
            await _audio.SetOutputLevelAsync(attempt.Request.SessionId, level).ConfigureAwait(false);
            attempt.Request.OutputLevel = level;
        }

        public async Task SetPlaybackMutedAsync(bool muted)
        {
            Attempt attempt;
            lock (_gate) attempt = _active;
            if (attempt == null || State != VoiceChatState.Active) return;
            await _audio.SetPlaybackMutedAsync(attempt.Request.SessionId, muted).ConfigureAwait(false);
        }

        public Task ConnectionLostAsync(string reason)
        {
            Attempt attempt;
            lock (_gate) attempt = _active;
            return attempt == null ? Task.CompletedTask : StopAttemptAsync(attempt, reason);
        }

        private Task StopAttemptAsync(Attempt attempt, string error)
        {
            TaskCompletionSource<bool> completion;
            lock (_gate)
            {
                if (attempt.StopTask != null) return attempt.StopTask;
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
                try { await _audio.StopAsync(attempt.Request.SessionId).ConfigureAwait(false); }
                catch (Exception ex) { error = error ?? ex.Message; }
                if (attempt.StartIssued)
                {
                    try
                    {
                        await WaitAsync(attempt.RequestFinished.Task, CancellationToken.None, 6000).ConfigureAwait(false);
                        await _client.StopRealtimeVoiceAsync(attempt.Request.ThreadId, CancellationToken.None).ConfigureAwait(false);
                        // Drain the old session's closed notification before allowing a new voice on this thread.
                        if (attempt.Started && !attempt.Closed.Task.IsCompleted)
                            await Task.WhenAny(attempt.Closed.Task, Task.Delay(1500)).ConfigureAwait(false);
                    }
                    catch (Exception ex) { error = error ?? ex.Message; }
                }
                await ReleasePreviewAsync(attempt).ConfigureAwait(false);
                lock (_gate)
                {
                    if (ReferenceEquals(_active, attempt)) _active = null;
                    State = error == null ? VoiceChatState.Idle : VoiceChatState.Failed;
                }
                NotifyState(new VoiceChatStateEvent { State = State, Error = error });
            }
            catch (Exception ex)
            {
                // A subscriber must not turn shutdown into an unobserved async exception.
                System.Diagnostics.Trace.TraceWarning("Voice cleanup failed: {0}", ex.GetType().Name);
            }
            finally { completion.TrySetResult(true); }
        }

        private void OnRealtimeEvent(object sender, RealtimeVoiceEvent message)
        {
            Attempt attempt;
            lock (_gate) attempt = _active;
            if (attempt == null || !attempt.StartIssued || !string.Equals(message.ThreadId, attempt.Request.ThreadId, StringComparison.Ordinal)) return;
            if (message.Kind == RealtimeVoiceEventKind.Closed)
            {
                attempt.Closed.TrySetResult(true);
                if (!attempt.StopIssued) StopAttemptAsync(attempt, null);
                return;
            }
            if (attempt.StopIssued && message.Kind != RealtimeVoiceEventKind.Transcript) return;
            switch (message.Kind)
            {
                case RealtimeVoiceEventKind.Started:
                    if (string.Equals(message.SessionId, attempt.Request.SessionId, StringComparison.Ordinal)) attempt.Started = true;
                    break;
                case RealtimeVoiceEventKind.Sdp:
                    if (attempt.Started && !string.IsNullOrWhiteSpace(message.Sdp)) attempt.Answer.TrySetResult(message.Sdp);
                    break;
                case RealtimeVoiceEventKind.Error:
                    StopAttemptAsync(attempt, message.Text ?? SharedLocalization.Text("VoiceChat.Error.ConnectionFailed", "The voice connection failed."));
                    break;
                case RealtimeVoiceEventKind.Transcript:
                    if (attempt.Started && string.Equals(message.SessionId, attempt.Request.SessionId, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(message.Text)) TranscriptReceived?.Invoke(this, message);
                    break;
            }
        }

        private void OnAudioFailed(object sender, VoiceAudioEvent message)
        {
            Attempt attempt;
            lock (_gate) attempt = _active;
            if (attempt != null && string.Equals(message.SessionId, attempt.Request.SessionId, StringComparison.Ordinal))
                StopAttemptAsync(attempt, message.Error);
        }

        private void OnPreviewCompleted(object sender, VoiceAudioEvent message)
        {
            Attempt attempt;
            lock (_gate) attempt = _active;
            if (attempt?.Request.IsPreview == true && string.Equals(message.SessionId, attempt.Request.SessionId, StringComparison.Ordinal))
                StopAttemptAsync(attempt, null);
        }

        private async Task ReleasePreviewAsync(Attempt attempt)
        {
            var id = Interlocked.Exchange(ref attempt.OwnedPreviewThreadId, null);
            if (id == null) return;
            try { await _client.ReleaseVoicePreviewThreadAsync(id, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Voice preview release failed: {0}", ex.GetType().Name); }
        }

        private void Publish(Attempt attempt, VoiceChatState state)
        {
            lock (_gate)
            {
                if (_disposed || !ReferenceEquals(_active, attempt) || (attempt.StopIssued && state != VoiceChatState.Stopping)) return;
                State = state;
            }
            NotifyState(new VoiceChatStateEvent { State = state });
        }

        private void NotifyState(VoiceChatStateEvent notification)
        {
            if (_disposed) return;
            try { StateChanged?.Invoke(this, notification); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning("Voice state observer failed: {0}", ex.GetType().Name); }
        }

        private static TaskCompletionSource<T> NewCompletion<T>()
        {
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static async Task<T> WaitAsync<T>(Task<T> task, CancellationToken token, int timeout)
        {
            using (var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                var delay = Task.Delay(timeout, delayCancellation.Token);
                var finished = await Task.WhenAny(task, delay).ConfigureAwait(false);
                delayCancellation.Cancel();
                token.ThrowIfCancellationRequested();
                if (finished != task) throw new TimeoutException(SharedLocalization.Text("VoiceChat.Error.Timeout", "The voice connection timed out. Please try again."));
                return await task.ConfigureAwait(false);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client.RealtimeVoiceEventReceived -= OnRealtimeEvent;
            _audio.Failed -= OnAudioFailed;
            _audio.PreviewCompleted -= OnPreviewCompleted;
            StopAsync();
            _audio.Dispose();
        }
    }
}
