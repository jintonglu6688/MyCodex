using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
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
        private readonly Func<VoiceChatSettings> _settings;
        private readonly Action<string> _status;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly RealtimeVoiceSession _session;
        private readonly PushToTalkKeyFilter _keyFilter;
        private VoiceConversationBinding _binding;
        private bool _available;
        private bool _changing;
        private bool _talking;
        private bool _keyHeld;
        private bool _disposed;
        private int _generation;
        // Completes with the preview's error, or null, when the borrowed connection is idle again.
        private TaskCompletionSource<string> _preview;

        internal VoiceChatCoordinator(
            Form owner,
            Button toggleButton,
            IAppServerRealtimeClient client,
            IAppServerConnectionClient connection,
            IRealtimeVoiceAudio audio,
            Func<Task<VoiceConversationBinding>> prepare,
            Func<VoiceChatSettings> settings,
            Action<string> status)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            ToggleButton = toggleButton ?? throw new ArgumentNullException(nameof(toggleButton));
            _client = client;
            _connection = connection;
            _prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
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
                _keyFilter = new PushToTalkKeyFilter(() => Form.ActiveForm == _owner, () => (Keys)TalkVirtualKey, HandleTalkKey);
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

        private VoiceChatSettings Settings => _settings() ?? new VoiceChatSettings();

        private int TalkVirtualKey => VoiceChatPreferences.NormalizeTalkVirtualKey(Settings.TalkVirtualKey);

        private bool ToggleToTalk => Settings.TalkMode == VoiceTalkMode.Toggle;

        // The talk key opens the microphone; the server answers once it hears the silence after it closes.
        internal bool HandleTalkKey(bool pressed)
        {
            if (_session == null || _disposed || !ModeEnabled)
            {
                return false;
            }

            if (!ToggleToTalk)
            {
                return SetTalking(pressed);
            }

            // Toggle on the first key down only; auto-repeats and the key up are swallowed.
            var firstPress = pressed && !_keyHeld;
            _keyHeld = pressed;
            return firstPress ? SetTalking(!_talking) : true;
        }

        // Called after the voice settings were saved: the talk key and mode apply right away.
        internal void SettingsChanged()
        {
            _keyHeld = false;
            if (_talking)
            {
                SetTalking(false);
            }
            else if (ModeEnabled && _session?.State == VoiceChatState.Active)
            {
                ShowActiveHint();
            }
        }

        // The Voice Chat tab of Options. Listing and previewing voices need no workspace or conversation,
        // so they follow runtime support only, not whether the voice button is usable right now.
        internal VoiceChatOptionsPage CreateOptionsPage(bool voiceSupported)
        {
            var available = _client != null && voiceSupported && !_disposed;
            return new VoiceChatOptionsPage(
                Settings,
                available ? _client.ListRealtimeVoicesAsync : (Func<CancellationToken, Task<RealtimeVoiceCatalog>>)null,
                available ? PreviewAsync : (Func<VoiceChatSettings, CancellationToken, Task>)null);
        }

        // The preview borrows the voice connection on a temporary thread, so voice chat must be off meanwhile.
        internal async Task PreviewAsync(VoiceChatSettings settings, CancellationToken cancellationToken)
        {
            if (_session == null || _disposed || ModeEnabled || _changing || _preview != null ||
                (_session.State != VoiceChatState.Idle && _session.State != VoiceChatState.Failed))
            {
                throw new InvalidOperationException(UiLocalization.Text("VoiceSettings.Error.PreviewBusy", "Stop voice chat before trying a voice."));
            }

            var finished = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _preview = finished;
            RefreshControls();
            string threadId = null;
            try
            {
                var catalog = await _client.ListRealtimeVoicesAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(catalog?.DefaultVoice))
                {
                    throw new InvalidOperationException(UiLocalization.Text("VoiceChat.Error.NoVoices", "This runtime does not provide official voices."));
                }

                threadId = await _client.CreateVoicePreviewThreadAsync(cancellationToken);
                using (cancellationToken.Register(() => _session.StopAsync()))
                {
                    await _session.StartAsync(new RealtimeVoiceStart
                    {
                        ThreadId = threadId,
                        Voice = VoiceChatPreferences.NormalizeVoice(settings?.Voice, catalog.Voices) ?? catalog.DefaultVoice,
                        FasterSpeech = settings?.FasterSpeech == true,
                        OutputVolumePercent = VoiceChatPreferences.NormalizeOutputVolumePercent(settings?.OutputVolumePercent ?? 0),
                        PreviewText = UiLocalization.Text("VoiceSettings.Preview.Sample", "Hello. This is how I sound with the selected voice and speaking pace.")
                    });
                    var error = await finished.Task;
                    if (error != null)
                    {
                        throw new InvalidOperationException(error);
                    }
                }
            }
            finally
            {
                if (threadId != null)
                {
                    try
                    {
                        await _client.ReleaseVoicePreviewThreadAsync(threadId);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceWarning("Voice preview thread was not released: {0}", ex.GetType().Name);
                    }
                }

                _preview = null;
                RefreshControls();
            }
        }

        private bool SetTalking(bool talking)
        {
            if (talking == _talking || (talking && _session.State != VoiceChatState.Active))
            {
                return true;
            }

            _talking = talking;
            _ = SetTalkingAsync(talking);
            return true;
        }

        private async Task SetTalkingAsync(bool talking)
        {
            _status(talking
                ? TalkText(ToggleToTalk
                    ? UiLocalization.Text("VoiceChat.Status.Listening.Toggle", "Listening. Press {0} again to send.")
                    : UiLocalization.Text("VoiceChat.Status.Listening.Hold", "Listening. Release {0} to send."))
                : TalkText(ToggleToTalk
                    ? UiLocalization.Text("VoiceChat.Status.Sent.Toggle", "Sent. Press {0} to talk again.")
                    : UiLocalization.Text("VoiceChat.Status.Sent.Hold", "Sent. Hold {0} to talk again.")));
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
            if (_session == null || !_available || ModeEnabled || _changing || _disposed || _preview != null || _session.State == VoiceChatState.Stopping)
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
                var settings = Settings;
                await _session.StartAsync(new RealtimeVoiceStart
                {
                    ThreadId = bound.ThreadId,
                    WorkspacePath = bound.WorkspacePath,
                    ApprovalMode = bound.ApprovalMode,
                    BackendMode = bound.BackendMode,
                    Voice = VoiceChatPreferences.NormalizeVoice(settings.Voice, catalog.Voices) ?? catalog.DefaultVoice,
                    FasterSpeech = settings.FasterSpeech,
                    OutputVolumePercent = VoiceChatPreferences.NormalizeOutputVolumePercent(settings.OutputVolumePercent)
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
                _status(UiLocalization.Text("VoiceChat.Status.Off", "Voice chat off. Continue in this conversation by typing."));
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
            Dispatch(() =>
            {
                if (_preview == null)
                {
                    TranscriptReceived?.Invoke(this, e);
                }
            });
        }

        private void OnStateChanged(object sender, VoiceChatStateEvent e)
        {
            Dispatch(() =>
            {
                if (_preview != null)
                {
                    if (e.State == VoiceChatState.Idle || e.State == VoiceChatState.Failed)
                    {
                        _preview.TrySetResult(e.State == VoiceChatState.Failed ? e.Error : null);
                    }

                    RefreshControls();
                    return;
                }

                if (e.State != VoiceChatState.Active)
                {
                    _talking = false;
                    _keyHeld = false;
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
                    ShowActiveHint();
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
            // The talk key works only in this window, so leaving it must not leave the microphone open.
            _keyHeld = false;
            if (_talking)
            {
                SetTalking(false);
            }
        }

        internal void RefreshControls()
        {
            if (_disposed)
            {
                return;
            }

            ToggleButton.Text = ModeEnabled
                ? UiLocalization.Text("VoiceChat.Button.Stop", "Stop &Voice Chat")
                : UiLocalization.Text("VoiceChat.Button.Start", "Start &Voice Chat");
            ToggleButton.AccessibleName = ModeEnabled
                ? UiLocalization.Text("VoiceChat.Accessible.Stop", "Stop voice chat")
                : UiLocalization.Text("VoiceChat.Accessible.Start", "Start voice chat");
            // While connecting the button stays enabled so the attempt can be cancelled.
            ToggleButton.Enabled = _session != null &&
                _preview == null &&
                (ModeEnabled || (_available && !_changing)) &&
                _session.State != VoiceChatState.Stopping;
        }

        private void ShowActiveHint()
        {
            var hint = TalkText(ToggleToTalk
                ? UiLocalization.Text("VoiceChat.Status.Active.Toggle", "Voice on. Press {0} to talk, press it again to send.")
                : UiLocalization.Text("VoiceChat.Status.Active.Hold", "Voice on. Hold {0} to talk, release to send."));
            _status(_session.SystemEchoCancellationEnabled == true
                ? hint
                : hint + " " + UiLocalization.Text("VoiceChat.Status.BasicEcho", "System audio filtering is unavailable, so screen reader speech may be picked up while you talk."));
        }

        private string TalkText(string template)
        {
            return string.Format(CultureInfo.CurrentCulture, template, VoiceChatPreferences.DescribeTalkKey(TalkVirtualKey));
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
            _preview?.TrySetCanceled();
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
