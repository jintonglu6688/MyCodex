using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class VoiceConversationBinding
    {
        public string ConversationId { get; set; }

        public RealtimeVoiceStart Request { get; set; }
    }

    // Owns the composer's voice toggle: one voice connection bound to the selected conversation.
    internal sealed class VoiceChatCoordinator : IDisposable
    {
        private readonly Form _owner;
        private readonly IAppServerRealtimeClient _client;
        private readonly IAppServerConnectionClient _connection;
        private readonly Func<Task<VoiceConversationBinding>> _prepare;
        private readonly Action<string> _status;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly RealtimeVoiceSession _session;
        private readonly PushToTalkKeyFilter _keyFilter;
        private VoiceConversationBinding _binding;
        private bool _available;
        private bool _changing;
        private bool _talking;
        private bool _disposed;
        private int _generation;

        internal VoiceChatCoordinator(
            Form owner,
            Button toggleButton,
            IAppServerRealtimeClient client,
            IAppServerConnectionClient connection,
            IRealtimeVoiceAudio audio,
            Func<Task<VoiceConversationBinding>> prepare,
            Action<string> status)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            ToggleButton = toggleButton ?? throw new ArgumentNullException(nameof(toggleButton));
            _client = client;
            _connection = connection;
            _prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
            _status = status ?? throw new ArgumentNullException(nameof(status));
            ToggleButton.Name = "VoiceChatButton";
            if (_client != null)
            {
                _session = new RealtimeVoiceSession(_client, audio ?? throw new ArgumentNullException(nameof(audio)));
                _session.StateChanged += OnStateChanged;
                _session.TranscriptReceived += OnTranscript;
                ToggleButton.Click += async (sender, args) => await ToggleAsync();
                if (_connection != null)
                {
                    _connection.EventReceived += OnConnectionEvent;
                }

                _owner.VisibleChanged += OnOwnerVisibilityChanged;
                _owner.Deactivate += OnOwnerDeactivate;
                _keyFilter = new PushToTalkKeyFilter(() => Form.ActiveForm == _owner, HandlePushToTalkKey);
                System.Windows.Forms.Application.AddMessageFilter(_keyFilter);
            }

            RefreshControls();
        }

        public Button ToggleButton { get; }

        public bool ModeEnabled { get; private set; }

        public event EventHandler<RealtimeVoiceEvent> TranscriptReceived;

        internal bool IsSpeakingForThread(string threadId)
        {
            return ModeEnabled && _binding != null && string.Equals(_binding.Request.ThreadId, threadId, StringComparison.Ordinal);
        }

        // Hold to talk, release to send: the server answers once it hears the silence after release.
        internal bool HandlePushToTalkKey(bool pressed)
        {
            if (_session == null || _disposed || !ModeEnabled)
            {
                return false;
            }

            if (pressed == _talking || (pressed && _session.State != VoiceChatState.Active))
            {
                return true;
            }

            _talking = pressed;
            _ = SetTalkingAsync(pressed);
            return true;
        }

        private async Task SetTalkingAsync(bool talking)
        {
            _status(talking
                ? UiLocalization.Text("VoiceChat.Status.Listening", "Listening. Release F8 to send.")
                : UiLocalization.Text("VoiceChat.Status.Sent", "Sent. Hold F8 to talk again."));
            try
            {
                await _session.SetMicrophoneEnabledAsync(talking);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _talking = false;
                // A failed release could leave the microphone live; end voice instead.
                if (!talking)
                {
                    await StopAsync();
                }

                ReportError(ex.Message);
            }
        }

        internal Task ToggleAsync()
        {
            return ModeEnabled ? StopAsync() : StartAsync();
        }

        internal async Task StartAsync()
        {
            if (_session == null || !_available || ModeEnabled || _changing || _disposed || _session.State == VoiceChatState.Stopping)
            {
                return;
            }

            var generation = ++_generation;
            _binding = null;
            ModeEnabled = true;
            _changing = true;
            RefreshControls();
            try
            {
                var binding = await _prepare();
                if (!IsCurrent(generation))
                {
                    return;
                }

                if (binding?.Request == null)
                {
                    ModeEnabled = false;
                    return;
                }

                _binding = binding;
                var catalog = await _client.ListRealtimeVoicesAsync(_lifetime.Token);
                if (!IsCurrent(generation))
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(catalog?.DefaultVoice))
                {
                    throw new InvalidOperationException(UiLocalization.Text("VoiceChat.Error.NoVoices", "This runtime does not provide official voices."));
                }

                var bound = binding.Request;
                await _session.StartAsync(new RealtimeVoiceStart
                {
                    ThreadId = bound.ThreadId,
                    WorkspacePath = bound.WorkspacePath,
                    ApprovalMode = bound.ApprovalMode,
                    BackendMode = bound.BackendMode,
                    Voice = catalog.DefaultVoice
                });
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                ReportError(ex.Message);
            }
            finally
            {
                _changing = false;
                if (_session.State != VoiceChatState.Active)
                {
                    ModeEnabled = false;
                }

                RefreshControls();
            }
        }

        internal async Task StopAsync()
        {
            if (_session == null)
            {
                return;
            }

            var wasEnabled = ModeEnabled;
            ModeEnabled = false;
            ++_generation;
            RefreshControls();
            await _session.StopAsync();
            if (wasEnabled && !_disposed)
            {
                _status(UiLocalization.Text("VoiceChat.Status.Off", "Voice off. Continue in this conversation by typing."));
            }
        }

        // Called whenever the composer state refreshes: voice follows the selected conversation and target.
        internal void Refresh(string conversationId, bool available)
        {
            _available = available;
            if (ModeEnabled && _binding != null && !string.Equals(_binding.ConversationId, conversationId, StringComparison.Ordinal))
            {
                _ = StopAsync();
            }

            RefreshControls();
        }

        private bool IsCurrent(int generation)
        {
            return !_disposed && ModeEnabled && generation == _generation;
        }

        private void OnTranscript(object sender, RealtimeVoiceEvent e)
        {
            Dispatch(() => TranscriptReceived?.Invoke(this, e));
        }

        private void OnStateChanged(object sender, VoiceChatStateEvent e)
        {
            Dispatch(() =>
            {
                if (e.State != VoiceChatState.Active)
                {
                    _talking = false;
                }

                if (e.State == VoiceChatState.Failed)
                {
                    ModeEnabled = false;
                    ReportError(e.Error);
                }
                else if (e.State == VoiceChatState.Idle && !_changing)
                {
                    ModeEnabled = false;
                }
                else if (e.State == VoiceChatState.Active)
                {
                    _status(_session.SystemEchoCancellationEnabled == true
                        ? UiLocalization.Text("VoiceChat.Status.Active", "Voice on. Hold F8 to talk, release to send.")
                        : UiLocalization.Text("VoiceChat.Status.BasicEcho", "Voice on. Hold F8 to talk, release to send. System audio filtering is unavailable, so screen reader speech may be picked up while F8 is held."));
                }
                else if (e.State == VoiceChatState.Connecting)
                {
                    _status(UiLocalization.Text("VoiceChat.Status.Connecting", "Connecting voice…"));
                }

                RefreshControls();
            });
        }

        private void OnConnectionEvent(object sender, AppServerEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (e.EventType == "app_server_disconnected")
            {
                _ = _session.ConnectionLostAsync(UiLocalization.Text("VoiceChat.Error.Disconnected", "Voice stopped because the connection to Codex was lost."));
            }
            else if ((e.EventType == "thread_deleted" || e.EventType == "thread_archived" || e.EventType == "thread_closed") &&
                string.Equals(e.ThreadId, _session.ThreadId, StringComparison.Ordinal))
            {
                Dispatch(() => { _ = StopAsync(); });
            }
        }

        private void OnOwnerVisibilityChanged(object sender, EventArgs args)
        {
            // Never keep the microphone open behind a hidden window.
            if (!_owner.Visible && ModeEnabled)
            {
                _ = StopAsync();
            }
        }

        private void OnOwnerDeactivate(object sender, EventArgs args)
        {
            // Switching windows while holding F8 would never deliver its key up here.
            if (_talking)
            {
                HandlePushToTalkKey(false);
            }
        }

        internal void RefreshControls()
        {
            if (_disposed)
            {
                return;
            }

            ToggleButton.Text = ModeEnabled
                ? UiLocalization.Text("VoiceChat.Button.Stop", "Stop &Voice")
                : UiLocalization.Text("VoiceChat.Button.Start", "Start &Voice");
            ToggleButton.AccessibleName = ModeEnabled
                ? UiLocalization.Text("VoiceChat.Accessible.Stop", "Stop voice")
                : UiLocalization.Text("VoiceChat.Accessible.Start", "Start voice");
            // While connecting the button stays enabled so the attempt can be cancelled.
            ToggleButton.Enabled = _session != null &&
                (ModeEnabled || (_available && !_changing)) &&
                _session.State != VoiceChatState.Stopping;
        }

        private void ReportError(string message)
        {
            if (!_disposed && !string.IsNullOrWhiteSpace(message))
            {
                _status(message);
            }
        }

        private void Dispatch(Action action)
        {
            if (_disposed || _owner.IsDisposed)
            {
                return;
            }

            if (!_owner.InvokeRequired)
            {
                action();
                return;
            }

            try
            {
                _owner.BeginInvoke(new Action(() =>
                {
                    if (!_disposed)
                    {
                        action();
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            ModeEnabled = false;
            ++_generation;
            _lifetime.Cancel();
            if (_session != null)
            {
                if (_connection != null)
                {
                    _connection.EventReceived -= OnConnectionEvent;
                }

                _owner.VisibleChanged -= OnOwnerVisibilityChanged;
                _owner.Deactivate -= OnOwnerDeactivate;
                System.Windows.Forms.Application.RemoveMessageFilter(_keyFilter);
                _session.StateChanged -= OnStateChanged;
                _session.TranscriptReceived -= OnTranscript;
                _session.Dispose();
            }

            _lifetime.Dispose();
        }
    }
}
