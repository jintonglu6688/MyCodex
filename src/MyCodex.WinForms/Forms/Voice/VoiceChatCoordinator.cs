using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.InternalTools;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;
using MyCodex.WinForms.Controls;

namespace MyCodex.WinForms.Forms
{
    internal sealed class VoiceConversationBinding
    {
        public string ConversationId { get; set; }
        public RealtimeVoiceStart Request { get; set; }
    }

    // The main composer owns one voice mode. Settings and preview never own a user conversation.
    internal sealed class VoiceChatCoordinator : IDisposable
    {
        private readonly Form _owner;
        private readonly IAppServerRealtimeClient _client;
        private readonly IAppServerConnectionClient _connection;
        private readonly Func<Task<VoiceConversationBinding>> _prepare;
        private readonly Func<VoiceChatSettings> _readSettings;
        private readonly Func<VoiceChatSettings, bool> _saveSettings;
        private readonly Action<string> _status;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly RealtimeVoiceSession _session;
        private readonly RealtimeVoiceSession _previewSession;
        private RealtimeVoiceCatalog _catalog;
        private readonly VoiceReplyPresentation _presentation = new VoiceReplyPresentation();
        private readonly SemaphoreSlim _backendSettingsGate = new SemaphoreSlim(1, 1);
        private VoiceConversationBinding _binding;
        private VoiceChatSettings _activeSettings;
        private VoiceSettingsForm _settingsForm;
        private TaskCompletionSource<VoiceChatStateEvent> _previewFinished;
        private bool _changing;
        private bool _previewing;
        private bool _cancelPreview;
        private bool _disposed;
        private int _generation;

        public Button ToggleButton { get; }
        public Button MuteButton { get; }
        public Button SettingsButton { get; }
        public bool ModeEnabled { get; private set; }
        public bool Muted { get; private set; }
        public event EventHandler<RealtimeVoiceEvent> TranscriptReceived;

        internal bool IsSpeakingForThread(string threadId)
        {
            return ModeEnabled && _binding?.Request.ThreadId == threadId;
        }

        internal VoiceChatCoordinator(Form owner, IAppServerRealtimeClient client, IAppServerConnectionClient connection,
            string profilePath, Func<Task<VoiceConversationBinding>> prepare, Func<VoiceChatSettings> readSettings,
            Func<VoiceChatSettings, bool> saveSettings, Action<string> status, IRealtimeVoiceAudio testAudio = null,
            IRealtimeVoiceAudio testPreviewAudio = null)
        {
            _owner = owner;
            _client = client;
            _connection = connection;
            _prepare = prepare;
            _readSettings = readSettings;
            _saveSettings = saveSettings;
            _status = status;
            ToggleButton = UiControlFactory.CreateActionButton(string.Empty, string.Empty, 7, 160);
            ToggleButton.Name = "VoiceChatButton";
            MuteButton = UiControlFactory.CreateActionButton(string.Empty, string.Empty, 8, 180);
            MuteButton.Name = "VoiceMuteButton";
            SettingsButton = UiControlFactory.CreateActionButton(string.Empty, string.Empty, 9, 160);
            SettingsButton.Name = "VoiceSettingsButton";
            UiTheme.ApplyButton(ToggleButton, UiGlyph.Send);
            UiTheme.ApplyButton(MuteButton, UiGlyph.Stop);
            UiTheme.ApplyButton(SettingsButton, UiGlyph.Info);
            var audio = testAudio;
            if (audio == null && client != null)
            {
                audio = new WebViewVoiceAudio(owner, profilePath);
            }
            if (client == null) { RefreshControls(); return; }
            _session = new RealtimeVoiceSession(client, audio);
            _previewSession = new RealtimeVoiceSession(client, testPreviewAudio ?? new WebViewVoiceAudio(owner, profilePath));
            _session.StateChanged += OnStateChanged;
            _session.TranscriptReceived += OnTranscript;
            _previewSession.StateChanged += OnPreviewStateChanged;
            _client.RealtimeTextInputSubmitted += OnTextInputSubmitted;
            if (_connection != null) _connection.EventReceived += OnConnectionEvent;
            ToggleButton.Click += async (sender, args) => await ToggleAsync();
            MuteButton.Click += async (sender, args) => await ToggleMuteAsync();
            SettingsButton.Click += (sender, args) => ShowSettings();
            _owner.VisibleChanged += OnOwnerVisibilityChanged;
            RefreshControls();
        }

        internal Task ToggleAsync() { return SetEnabledAsync(!ModeEnabled); }

        internal async Task SetEnabledAsync(bool enabled, string expectedThreadId = null, bool initiallyMuted = false)
        {
            if (_client == null) return;
            if (!enabled) { await StopAsync(); return; }
            if (ModeEnabled) return;
            if (_changing || _disposed || _session.State == VoiceChatState.Stopping) return;
            var generation = ++_generation;
            _binding = null;
            _presentation.NewInput();
            ModeEnabled = true;
            _changing = true;
            Muted = initiallyMuted;
            RefreshControls();
            try
            {
                var binding = await _prepare();
                if (!IsCurrent(generation)) return;
                if (binding?.Request == null) { ModeEnabled = false; return; }
                if (expectedThreadId != null && binding.Request.ThreadId != expectedThreadId) { ModeEnabled = false; return; }
                _binding = binding;
                var catalog = await _client.ListRealtimeVoicesAsync(_lifetime.Token);
                if (!IsCurrent(generation)) return;
                _catalog = catalog;
                var saved = _readSettings() ?? new VoiceChatSettings();
                var voice = System.Linq.Enumerable.Contains(catalog.Voices, saved.Voice) ? saved.Voice : catalog.DefaultVoice;
                if (string.IsNullOrWhiteSpace(voice)) throw new InvalidOperationException(UiLocalization.Text("VoiceChatForm.Error.NoVoices", "This runtime does not provide supported official voices."));
                _activeSettings = new VoiceChatSettings { Voice = voice, FasterSpeech = saved.FasterSpeech, OutputLevel = saved.OutputLevel };
                await StartConversationAsync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReportError(ex.Message); }
            finally
            {
                _changing = false;
                if (_session.State != VoiceChatState.Active) ModeEnabled = false;
                RefreshControls();
            }
        }

        internal async Task StopAsync()
        {
            ModeEnabled = false;
            ++_generation;
            RefreshControls();
            if (_settingsForm != null && !_settingsForm.IsDisposed) _settingsForm.Close();
            await Task.WhenAll(_session.StopAsync(), _previewSession.StopAsync());
            if (!_disposed) _status(UiLocalization.Text("VoiceMode.Status.Text", "Voice off. Continue in the same conversation using text."));
        }

        internal void SelectConversation(string conversationId)
        {
            if (ModeEnabled && _binding != null && _binding.ConversationId != conversationId) _ = StopAsync();
        }

        private bool IsCurrent(int generation) { return !_disposed && ModeEnabled && generation == _generation; }

        private async Task StartConversationAsync()
        {
            var generation = _generation;
            await _backendSettingsGate.WaitAsync(_lifetime.Token);
            try
            {
                if (!IsCurrent(generation)) return;
                var bound = _binding.Request;
                await _session.StartAsync(new RealtimeVoiceStart
                {
                    ThreadId = bound.ThreadId, WorkspacePath = bound.WorkspacePath, ApprovalMode = bound.ApprovalMode,
                    Voice = _activeSettings.Voice, FasterSpeech = _activeSettings.FasterSpeech, MicrophoneMuted = Muted,
                    OutputLevel = _activeSettings.OutputLevel ?? VoiceOutputLevel.Original, BackendMode = bound.BackendMode
                });
            }
            finally { _backendSettingsGate.Release(); }
        }

        internal Task ToggleMuteAsync() { return SetMutedAsync(!Muted); }

        internal async Task SetMutedAsync(bool muted)
        {
            if (!ModeEnabled || _changing || _previewing || _session.State != VoiceChatState.Active) return;
            var generation = _generation;
            MuteButton.Enabled = false;
            try { await _session.SetMutedAsync(muted); if (IsCurrent(generation)) Muted = muted; }
            catch (Exception ex) { if (IsCurrent(generation)) ReportError(ex.Message); }
            finally { RefreshControls(); }
        }

        internal async Task<bool> ApplySettingsAsync(VoiceChatSettings selected)
        {
            if (_changing || _previewing || _disposed || !_saveSettings(selected)) return false;
            var reconnect = _activeSettings != null && (selected.Voice != _activeSettings.Voice || selected.FasterSpeech != _activeSettings.FasterSpeech);
            _activeSettings = selected;
            if (!ModeEnabled) return true;
            _changing = true;
            var generation = _generation;
            RefreshControls();
            try
            {
                if (reconnect)
                {
                    await _session.StopAsync();
                    if (IsCurrent(generation)) await StartConversationAsync();
                }
                else await _session.SetOutputLevelAsync(selected.OutputLevel ?? VoiceOutputLevel.Original);
                return true;
            }
            finally { _changing = false; RefreshControls(); }
        }

        internal async Task PreviewAsync(VoiceChatSettings selected, string sample)
        {
            if (!ModeEnabled || _changing || _previewing || _disposed) return;
            var generation = _generation;
            _previewing = true;
            _cancelPreview = false;
            RefreshControls();
            try
            {
                await _session.SetMutedAsync(true);
                await _session.SetPlaybackMutedAsync(true);
                if (!IsCurrent(generation) || _cancelPreview) return;
                _previewFinished = new TaskCompletionSource<VoiceChatStateEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
                await _previewSession.StartAsync(new RealtimeVoiceStart
                {
                    IsPreview = true, WorkspacePath = _binding.Request.WorkspacePath, Voice = selected.Voice,
                    FasterSpeech = selected.FasterSpeech, OutputLevel = selected.OutputLevel ?? VoiceOutputLevel.Original, PreviewText = sample
                });
                var result = await _previewFinished.Task;
                if (result.State == VoiceChatState.Failed) throw new InvalidOperationException(result.Error);
            }
            finally
            {
                _previewFinished = null;
                _previewing = false;
                if (IsCurrent(generation))
                {
                    await _session.SetPlaybackMutedAsync(false);
                    await _session.SetMutedAsync(Muted);
                }
                RefreshControls();
            }
        }

        internal Task StopPreviewAsync()
        {
            _cancelPreview = true;
            return _previewing ? _previewSession.StopAsync() : Task.CompletedTask;
        }

        internal async Task<bool> RefreshBackendSettingsAsync(string conversationId)
        {
            if (!ModeEnabled || _binding?.ConversationId != conversationId) return true;
            var generation = _generation;
            try
            {
                await _backendSettingsGate.WaitAsync(_lifetime.Token);
                try
                {
                    if (!IsCurrent(generation)) return true;
                    var refreshed = await _prepare();
                    if (!IsCurrent(generation) || refreshed?.Request.ThreadId != _binding.Request.ThreadId) return true;
                    await _client.UpdateRealtimeBackendAsync(refreshed.Request, _lifetime.Token);
                    if (IsCurrent(generation)) _binding = refreshed;
                }
                finally { _backendSettingsGate.Release(); }
                return true;
            }
            catch (Exception ex)
            {
                if (!IsCurrent(generation)) return true;
                await StopAsync();
                ReportError(UiLocalization.Text("VoiceMode.Error.ModelSync", "Voice was stopped because its backend settings could not be synchronized: ") + ex.Message);
                return false;
            }
        }

        internal async Task<InternalToolResult> ApplyControlAsync(VoiceControlCommand command, string threadId, Func<bool> isCurrent)
        {
            var mutates = command.Enabled.HasValue || command.MicrophoneMuted.HasValue || command.HasSettings;
            if (_disposed || ((_changing || _previewing) && mutates && command.Enabled != false)) return InternalToolResult.FromText(false, "Voice controls are busy. Try again after the current transition.");
            if (command.VoiceNumber.HasValue && _catalog == null) _catalog = await _client.ListRealtimeVoicesAsync(_lifetime.Token);
            if (command.VoiceNumber.HasValue && command.VoiceNumber > _catalog.Voices.Count) return InternalToolResult.FromText(false, "Voice number is outside the official voice catalog.");
            if (!isCurrent()) return InternalToolResult.FromText(false, "The selected conversation changed.");
            if (command.HasSettings)
            {
                var prior = _activeSettings ?? _readSettings() ?? new VoiceChatSettings();
                var selected = new VoiceChatSettings
                {
                    Voice = command.VoiceNumber.HasValue ? _catalog.Voices[command.VoiceNumber.Value - 1] : prior.Voice,
                    FasterSpeech = command.Faster ?? prior.FasterSpeech,
                    OutputLevel = command.OutputLevel ?? prior.OutputLevel
                };
                if (!await ApplySettingsAsync(selected)) return InternalToolResult.FromText(false, "Could not apply voice settings.");
            }
            if (!isCurrent()) return InternalToolResult.FromText(false, "The selected conversation changed.");
            if (command.Enabled.HasValue) await SetEnabledAsync(command.Enabled.Value, threadId, command.MicrophoneMuted == true);
            if (command.Enabled == true && !ModeEnabled) return InternalToolResult.FromText(false, "Voice could not be enabled. Check the client's status message.");
            if (command.MicrophoneMuted.HasValue && command.Enabled != false)
            {
                if (!ModeEnabled) return InternalToolResult.FromText(false, "Enable voice before changing the microphone state.");
                await SetMutedAsync(command.MicrophoneMuted.Value);
                if (Muted != command.MicrophoneMuted.Value) return InternalToolResult.FromText(false, "The microphone state could not be changed.");
            }
            if (!isCurrent()) return InternalToolResult.FromText(false, "The selected conversation changed.");
            var settings = _activeSettings ?? _readSettings() ?? new VoiceChatSettings();
            var number = _catalog == null ? 0 : System.Linq.Enumerable.ToList(_catalog.Voices).IndexOf(settings.Voice) + 1;
            return InternalToolResult.FromText(true, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "enabled={0}; microphone_muted={1}; voice_number={2}; voice_id={3}; faster={4}; output_percent={5}",
                ModeEnabled, Muted, number, settings.Voice, settings.FasterSpeech, (int)(settings.OutputLevel ?? VoiceOutputLevel.Original)));
        }

        private void ShowSettings()
        {
            if (!ModeEnabled || _changing || _previewing) return;
            if (_settingsForm != null && !_settingsForm.IsDisposed) { _settingsForm.Activate(); return; }
            _settingsForm = new VoiceSettingsForm(_client, _activeSettings, ApplySettingsAsync, PreviewAsync, StopPreviewAsync, _catalog);
            _settingsForm.Show(_owner);
        }

        private void OnTranscript(object sender, RealtimeVoiceEvent e)
        {
            if (!VoiceReplyPresentation.HasSpeechContent(e.Text)) return;
            Dispatch(() => TranscriptReceived?.Invoke(this, new RealtimeVoiceEvent
            {
                Kind = e.Kind, ThreadId = e.ThreadId, SessionId = e.SessionId, ItemId = e.ItemId, Role = e.Role, Text = e.Text,
                DisplayAsProcessing = _presentation.Transcript(e.Role) == ChatHistoryCategory.Processing
            }));
        }

        internal ChatHistoryCategory BackendReplyCategory(string threadId, string phase)
        {
            return IsSpeakingForThread(threadId) ? _presentation.Backend(phase) : ChatHistoryCategory.Conversation;
        }

        private void OnTextInputSubmitted(object sender, RealtimeVoiceInputEvent e)
        {
            Dispatch(() =>
            {
                if (IsSpeakingForThread(e.ThreadId)) _presentation.NewInput();
                _ = ForwardTypedContextAsync(e);
            });
        }

        private async Task ForwardTypedContextAsync(RealtimeVoiceInputEvent e)
        {
            if (!ModeEnabled || _session.State != VoiceChatState.Active || _session.ThreadId != e.ThreadId) return;
            try { await _client.AppendRealtimeContextAsync(e.ThreadId, e.Text, _lifetime.Token); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ReportError(ex.Message); }
        }

        private void OnStateChanged(object sender, VoiceChatStateEvent e)
        {
            Dispatch(() =>
            {
                if (e.State == VoiceChatState.Failed) { ModeEnabled = false; ReportError(e.Error); }
                else if (e.State == VoiceChatState.Idle && !_changing) ModeEnabled = false;
                else if (!_previewing)
                {
                    if (e.State == VoiceChatState.Active) _status(_session.SystemEchoCancellationEnabled == true
                        ? UiLocalization.Text("VoiceMode.Status.Active", "Voice on. Speak or type here; replies include audio and text.")
                        : UiLocalization.Text("VoiceMode.Status.BasicEcho", "Voice on. System-audio filtering could not be confirmed. Use headphones or mute the microphone while navigating with screen-reader audio."));
                    else if (e.State == VoiceChatState.Connecting) _status(UiLocalization.Text("VoiceChatForm.Status.Connecting", "Connecting voice…"));
                }
                RefreshControls();
            });
        }

        private void OnConnectionEvent(object sender, AppServerEventArgs e)
        {
            if (e.EventType == "app_server_disconnected")
                _ = Task.WhenAll(_session.ConnectionLostAsync(e.Content), _previewSession.ConnectionLostAsync(e.Content));
            else if (e.ThreadId == _session.ThreadId && (e.EventType == "thread_deleted" || e.EventType == "thread_archived" || e.EventType == "thread_closed")) Dispatch(() => { _ = StopAsync(); });
        }

        private void OnPreviewStateChanged(object sender, VoiceChatStateEvent e)
        {
            if (e.State == VoiceChatState.Idle || e.State == VoiceChatState.Failed) _previewFinished?.TrySetResult(e);
        }

        private void OnOwnerVisibilityChanged(object sender, EventArgs args)
        {
            if (!_owner.Visible && ModeEnabled) _ = StopAsync();
        }

        internal void RefreshControls()
        {
            if (_disposed) return;
            ToggleButton.Text = ModeEnabled ? UiLocalization.Text("VoiceMode.Button.Disable", "Disable voice(&J)") : UiLocalization.Text("VoiceMode.Button.Enable", "Enable voice(&J)");
            ToggleButton.AccessibleName = ModeEnabled ? UiLocalization.Text("VoiceMode.Accessible.Disable", "Disable voice") : UiLocalization.Text("VoiceMode.Accessible.Enable", "Enable voice");
            ToggleButton.Enabled = _client != null && (ModeEnabled || !_changing) && _session?.State != VoiceChatState.Stopping;
            MuteButton.Text = Muted ? UiLocalization.Text("VoiceMode.Button.Unmute", "Unmute microphone(&M)") : UiLocalization.Text("VoiceMode.Button.Mute", "Mute microphone(&M)");
            MuteButton.AccessibleName = Muted ? UiLocalization.Text("VoiceChatForm.Accessible.Unmute", "Unmute microphone") : UiLocalization.Text("VoiceChatForm.Accessible.Mute", "Mute microphone");
            MuteButton.Visible = ModeEnabled;
            MuteButton.Enabled = ModeEnabled && !_changing && !_previewing && _session?.State == VoiceChatState.Active;
            SettingsButton.Text = UiLocalization.Text("VoiceMode.Button.Settings", "Voice settings(&V)");
            SettingsButton.AccessibleName = UiLocalization.Text("VoiceSettings.Title", "Voice settings");
            SettingsButton.Visible = ModeEnabled;
            SettingsButton.Enabled = ModeEnabled && !_changing && !_previewing;
        }

        private void ReportError(string message) { if (!_disposed) _status(message); }
        private void Dispatch(Action action)
        {
            if (_disposed || _owner.IsDisposed) return;
            if (!_owner.InvokeRequired) { action(); return; }
            try { _owner.BeginInvoke(new Action(() => { if (!_disposed) action(); })); }
            catch (InvalidOperationException) { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ModeEnabled = false;
            ++_generation;
            _previewFinished?.TrySetCanceled();
            _lifetime.Cancel();
            if (_connection != null) _connection.EventReceived -= OnConnectionEvent;
            if (_client != null) _client.RealtimeTextInputSubmitted -= OnTextInputSubmitted;
            _owner.VisibleChanged -= OnOwnerVisibilityChanged;
            if (_session != null)
            {
                _session.StateChanged -= OnStateChanged;
                _session.TranscriptReceived -= OnTranscript;
            }
            _settingsForm?.Dispose();
            _session?.Dispose();
            if (_previewSession != null)
            {
                _previewSession.StateChanged -= OnPreviewStateChanged;
                _previewSession.Dispose();
            }
            _lifetime.Dispose();
        }
    }
}
