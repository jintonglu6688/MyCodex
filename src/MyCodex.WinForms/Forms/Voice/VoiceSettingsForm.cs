using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class VoiceSettingsForm : Form
    {
        private readonly IAppServerRealtimeClient _client;
        private readonly Func<VoiceChatSettings, Task<bool>> _applySettings;
        private readonly Func<VoiceChatSettings, string, Task> _previewSettings;
        private readonly Func<Task> _stopPreview;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly VoiceChatSettings _settings;
        private ComboBox _voices;
        private ComboBox _speed;
        private ComboBox _volume;
        private Button _preview;
        private Button _apply;
        private Label _status;
        private RealtimeVoiceCatalog _catalog;
        private bool _busy;
        private bool _previewing;
        private bool _closing;
        private bool _allowClose;
        private bool _resourcesDisposed;
        private Task _previewTask;

        internal VoiceSettingsForm(IAppServerRealtimeClient client, VoiceChatSettings settings,
            Func<VoiceChatSettings, Task<bool>> applySettings, Func<VoiceChatSettings, string, Task> previewSettings, Func<Task> stopPreview,
            RealtimeVoiceCatalog catalog = null)
        {
            _client = client;
            _settings = settings ?? new VoiceChatSettings();
            _applySettings = applySettings;
            _previewSettings = previewSettings;
            _stopPreview = stopPreview;
            _catalog = catalog;
            InitializeLayout();
            _apply.Click += async (sender, args) => await ApplyAsync();
            _preview.Click += async (sender, args) => await PreviewAsync();
            Shown += async (sender, args) => await LoadVoicesAsync();
            FormClosing += OnClosing;
            UpdateActions();
        }

        private void InitializeLayout()
        {
            Text = UiLocalization.Text("VoiceSettings.Title", "Voice settings");
            Name = "VoiceSettingsForm";
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(540, 330);
            Size = new Size(650, 390);
            UiTheme.ApplyForm(this);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6, TabStop = false };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (var row = 0; row < 6; row++) root.RowStyles.Add(new RowStyle(row == 4 ? SizeType.Percent : SizeType.AutoSize, row == 4 ? 100 : 0));
            _status = UiControlFactory.CreateMutedLabel(UiLocalization.Text("VoiceChatForm.Status.Loading", "Loading official voices…"));
            _status.AutoSize = true;
            root.Controls.Add(_status, 0, 0);
            root.SetColumnSpan(_status, 2);
            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("VoiceChatForm.Label.Voice", "Voice(&V)")), 0, 1);
            _voices = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceChatForm.Accessible.Voice", "Voice"), 0, DockStyle.Fill);
            _voices.Name = "VoiceChoice";
            _voices.FormattingEnabled = true;
            _voices.Format += (sender, args) =>
            {
                var index = _voices.Items.IndexOf(args.ListItem) + 1;
                args.Value = string.Format(CultureInfo.CurrentCulture,
                    UiLocalization.Text("VoiceSettings.NumberedVoice", "Voice {0:00}: {1}"), index, args.ListItem);
            };
            root.Controls.Add(_voices, 1, 1);
            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("VoiceChatForm.Label.Speed", "Speaking speed(&R)")), 0, 2);
            _speed = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceChatForm.Accessible.Speed", "Speaking speed"), 1, DockStyle.Fill);
            _speed.Name = "VoiceSpeed";
            _speed.Items.Add(UiLocalization.Text("VoiceChatForm.Speed.Default", "Official default"));
            _speed.Items.Add(UiLocalization.Text("VoiceChatForm.Speed.Faster", "Faster"));
            _speed.SelectedIndex = _settings.FasterSpeech ? 1 : 0;
            root.Controls.Add(_speed, 1, 2);
            root.Controls.Add(UiControlFactory.CreateMutedLabel(UiLocalization.Text("VoiceChatForm.Label.Volume", "Voice volume(&L)")), 0, 3);
            _volume = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceChatForm.Accessible.Volume", "Voice volume"), 2, DockStyle.Fill);
            _volume.Name = "VoiceVolume";
            _volume.FormattingEnabled = true;
            _volume.Format += FormatVolumeChoice;
            foreach (VoiceOutputLevel level in Enum.GetValues(typeof(VoiceOutputLevel))) _volume.Items.Add(level);
            _volume.SelectedItem = _settings.OutputLevel ?? VoiceOutputLevel.Original;
            root.Controls.Add(_volume, 1, 3);
            var hint = UiControlFactory.CreateMutedLabel(UiLocalization.Text("VoiceSettings.Hint", "Default keeps the official pace and volume. Preview pauses the microphone. You can adjust options while listening; stop and preview again to hear them. Volume affects only this voice."));
            hint.AutoSize = true;
            hint.MaximumSize = new Size(580, 0);
            root.Controls.Add(hint, 0, 4);
            root.SetColumnSpan(hint, 2);
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabStop = false, TabIndex = 3 };
            _preview = UiControlFactory.CreateActionButton(UiLocalization.Text("VoiceChatForm.Button.Preview", "Preview(&P)"), UiLocalization.Text("VoiceChatForm.Accessible.Preview", "Preview"), 0, 140);
            _preview.Name = "VoicePreviewButton";
            _apply = UiControlFactory.CreateActionButton(UiLocalization.Text("VoiceChatForm.Button.Apply", "Apply voice settings(&A)"), UiLocalization.Text("VoiceChatForm.Accessible.Apply", "Apply voice settings"), 1, 210);
            var close = UiControlFactory.CreateActionButton(UiLocalization.Text("VoiceChatForm.Button.Close", "Close(&C)"), UiLocalization.Text("VoiceChatForm.Accessible.Close", "Close"), 2, 120);
            UiTheme.ApplyButton(_preview, UiGlyph.Send);
            UiTheme.ApplyButton(_apply, UiGlyph.Ok);
            UiTheme.ApplyButton(close, UiGlyph.Cancel);
            close.Click += (sender, args) => Close();
            CancelButton = close;
            actions.Controls.AddRange(new Control[] { _preview, _apply, close });
            root.Controls.Add(actions, 0, 5);
            root.SetColumnSpan(actions, 2);
            Controls.Add(root);
        }

        internal async Task LoadVoicesAsync()
        {
            try
            {
                if (_catalog == null) _catalog = await _client.ListRealtimeVoicesAsync(_lifetime.Token);
                if (_closing || IsDisposed) return;
                _voices.Items.AddRange(_catalog.Voices.Cast<object>().ToArray());
                _voices.SelectedItem = _catalog.Voices.Contains(_settings.Voice) ? _settings.Voice : _catalog.DefaultVoice;
                _status.Text = UiLocalization.Text("VoiceSettings.Ready", "Choose settings, then preview or apply them.");
                UpdateActions();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex.Message); }
        }

        private VoiceChatSettings ReadSelection()
        {
            var voice = _voices.SelectedItem as string;
            if (_catalog == null || !_catalog.Voices.Contains(voice)) return null;
            return new VoiceChatSettings { Voice = voice, FasterSpeech = _speed.SelectedIndex == 1, OutputLevel = (VoiceOutputLevel)_volume.SelectedItem };
        }

        internal async Task ApplyAsync()
        {
            if (_busy || _closing || _previewing) return;
            var selected = ReadSelection();
            if (selected == null) return;
            _busy = true;
            UpdateActions();
            try
            {
                if (await _applySettings(selected))
                {
                    if (!_closing)
                    {
                        _status.Text = UiLocalization.Text("VoiceChatForm.Status.SettingsSaved", "Voice settings saved.");
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                }
                else ShowError(UiLocalization.Text("VoiceChatForm.Error.Save", "Voice settings could not be saved."));
            }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { _busy = false; UpdateActions(); }
        }

        internal async Task PreviewAsync()
        {
            if (_previewing) { await _stopPreview(); return; }
            if (_busy || _closing) return;
            var selected = ReadSelection();
            if (selected == null) return;
            _previewing = true;
            _status.Text = UiLocalization.Text("VoiceChatForm.Preview.Connecting", "Preparing the voice preview. The microphone stays off.");
            UpdateActions();
            try
            {
                _previewTask = _previewSettings(selected, UiLocalization.Text("VoiceChatForm.Preview.Sample", "Hello. This is a preview of the selected voice and speaking speed."));
                await _previewTask;
                if (!_closing) _status.Text = UiLocalization.Text("VoiceSettings.PreviewFinished", "Preview finished.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ShowError(ex.Message); }
            finally { _previewing = false; _previewTask = null; UpdateActions(); }
        }

        private void UpdateActions()
        {
            if (IsDisposed) return;
            var ready = !_busy && !_closing && _catalog?.Voices.Count > 0;
            _voices.Enabled = ready;
            _speed.Enabled = ready;
            _volume.Enabled = ready;
            _apply.Enabled = ready && !_previewing;
            _preview.Enabled = !_closing && (ready || _previewing);
            _preview.Text = _previewing ? UiLocalization.Text("VoiceChatForm.Button.StopPreview", "Stop preview(&P)") : UiLocalization.Text("VoiceChatForm.Button.Preview", "Preview(&P)");
            _preview.AccessibleName = _previewing ? UiLocalization.Text("VoiceChatForm.Accessible.StopPreview", "Stop preview") : UiLocalization.Text("VoiceChatForm.Accessible.Preview", "Preview");
        }

        private async void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose) return;
            if (!_previewing) { _closing = true; _lifetime.Cancel(); return; }
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            _lifetime.Cancel();
            UpdateActions();
            await _stopPreview();
            if (_previewTask != null) { try { await _previewTask; } catch { } }
            _allowClose = true;
            if (!IsDisposed) Close();
        }

        private void ShowError(string message)
        {
            if (!_closing && !IsDisposed) _status.Text = message;
        }

        private static void FormatVolumeChoice(object sender, ListControlConvertEventArgs e)
        {
            if (!(e.ListItem is VoiceOutputLevel)) return;
            var level = (VoiceOutputLevel)e.ListItem;
            var label = level == VoiceOutputLevel.Original ? UiLocalization.Text("VoiceChatForm.Volume.Original", "Original volume") :
                level == VoiceOutputLevel.Boost ? UiLocalization.Text("VoiceChatForm.Volume.Boost", "Boost") : UiLocalization.Text("VoiceChatForm.Volume.StrongBoost", "Stronger boost");
            e.Value = label + " (" + ((int)level).ToString(CultureInfo.InvariantCulture) + "%)";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_resourcesDisposed)
            {
                _resourcesDisposed = true;
                _closing = true;
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
