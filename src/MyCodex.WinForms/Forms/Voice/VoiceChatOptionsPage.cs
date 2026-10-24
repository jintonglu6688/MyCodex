using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // The Voice Chat tab of Options: edits the current target's voice preferences; OK reads them with ReadSettings.
    internal sealed class VoiceChatOptionsPage : UserControl
    {
        private readonly Func<CancellationToken, Task<RealtimeVoiceCatalog>> _loadVoices;
        private readonly Func<VoiceChatSettings, CancellationToken, Task> _previewVoice;
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private CancellationTokenSource _preview;
        private readonly VoiceChatSettings _initial;
        private readonly ComboBox _talkKey;
        private readonly ComboBox _talkMode;
        private readonly ComboBox _voice;
        private readonly ComboBox _pace;
        private readonly ComboBox _volume;
        private readonly Label _status;
        private readonly Button _previewButton;
        private bool _voicesRequested;

        // loadVoices and previewVoice are null when voice is unavailable on this target; the saved voice is then kept as is.
        internal VoiceChatOptionsPage(
            VoiceChatSettings settings,
            Func<CancellationToken, Task<RealtimeVoiceCatalog>> loadVoices,
            Func<VoiceChatSettings, CancellationToken, Task> previewVoice)
        {
            _initial = settings ?? new VoiceChatSettings();
            _loadVoices = loadVoices;
            _previewVoice = previewVoice;
            BackColor = UiTheme.WindowBackColor;
            // Tab never enters a UserControl's children on its own; the page must be a stop that hands focus to its first list.
            TabStop = true;

            _talkKey = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceSettings.Accessible.TalkKey", "Talk key"), 1, DockStyle.Fill);
            _talkKey.Name = "VoiceTalkKey";
            foreach (var key in VoiceChatPreferences.TalkVirtualKeys)
            {
                _talkKey.Items.Add(new Choice(VoiceChatPreferences.DescribeTalkKey(key), key));
            }

            _talkMode = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceSettings.Accessible.TalkMode", "Talk mode"), 3, DockStyle.Fill);
            _talkMode.Name = "VoiceTalkMode";
            _talkMode.Items.Add(new Choice(UiLocalization.Text("VoiceSettings.TalkMode.Hold", "Hold to talk, release to send"), VoiceTalkMode.Hold));
            _talkMode.Items.Add(new Choice(UiLocalization.Text("VoiceSettings.TalkMode.Toggle", "Press to talk, press again to send"), VoiceTalkMode.Toggle));

            _voice = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceSettings.Accessible.Voice", "Voice"), 5, DockStyle.Fill);
            _voice.Name = "VoiceName";

            _pace = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceSettings.Accessible.Pace", "Speaking pace"), 7, DockStyle.Fill);
            _pace.Name = "VoicePace";
            _pace.Items.Add(new Choice(UiLocalization.Text("VoiceSettings.Pace.Default", "Official default"), false));
            _pace.Items.Add(new Choice(UiLocalization.Text("VoiceSettings.Pace.Faster", "Slightly faster"), true));

            _volume = UiControlFactory.CreateDropDownList(UiLocalization.Text("VoiceSettings.Accessible.Volume", "Reply volume"), 9, DockStyle.Fill);
            _volume.Name = "VoiceVolume";
            foreach (var percent in VoiceChatPreferences.OutputVolumePercents)
            {
                _volume.Items.Add(new Choice(
                    percent == VoiceChatPreferences.DefaultOutputVolumePercent
                        ? UiLocalization.Text("VoiceSettings.Volume.Original", "Original volume")
                        : percent + "%",
                    percent));
            }

            _status = UiControlFactory.CreateMutedLabel(string.Empty);
            _previewButton = UiControlFactory.CreateActionButton(string.Empty, string.Empty, 10, 160);
            _previewButton.Name = "VoicePreviewButton";
            _previewButton.Enabled = _previewVoice != null;
            RefreshPreviewButton();
            _previewButton.Click += async (sender, args) => await TogglePreviewAsync();

            SelectValue(_talkKey, VoiceChatPreferences.NormalizeTalkVirtualKey(_initial.TalkVirtualKey));
            SelectValue(_talkMode, _initial.TalkMode == VoiceTalkMode.Toggle ? VoiceTalkMode.Toggle : VoiceTalkMode.Hold);
            SelectValue(_pace, _initial.FasterSpeech);
            SelectValue(_volume, VoiceChatPreferences.NormalizeOutputVolumePercent(_initial.OutputVolumePercent));
            FillVoices(null);

            BuildLayout();
            // The voice list is read only when the tab is first shown, so opening Options stays offline.
            VisibleChanged += async (sender, args) =>
            {
                if (Visible && !_voicesRequested)
                {
                    _voicesRequested = true;
                    await LoadVoicesAsync();
                }
            };
        }

        private async Task LoadVoicesAsync()
        {
            if (_loadVoices == null)
            {
                _status.Text = UiLocalization.Text("VoiceSettings.Status.VoicesUnavailable", "Voices can be listed only while official voice is available on this target.");
                return;
            }

            _status.Text = UiLocalization.Text("VoiceSettings.Status.Loading", "Loading official voices…");
            try
            {
                var catalog = await _loadVoices(_closing.Token);
                if (IsDisposed)
                {
                    return;
                }

                FillVoices(catalog?.Voices);
                if (_preview == null)
                {
                    _status.Text = string.Empty;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    _status.Text = ex.Message;
                }
            }
        }

        // Keeps the saved voice selectable even when the catalog is not loaded, so saving never drops it.
        private void FillVoices(IEnumerable<string> voices)
        {
            var selected = (_voice.SelectedItem as Choice)?.Value as string ?? _initial.Voice;
            _voice.Items.Clear();
            _voice.Items.Add(new Choice(UiLocalization.Text("VoiceSettings.Voice.Default", "Official default"), null));
            var names = new List<string>(voices ?? new string[0]);
            if (!string.IsNullOrWhiteSpace(selected) && !names.Contains(selected))
            {
                names.Insert(0, selected);
            }

            foreach (var name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _voice.Items.Add(new Choice(name, name));
                }
            }

            SelectValue(_voice, selected);
        }

        internal VoiceChatSettings ReadSettings()
        {
            return new VoiceChatSettings
            {
                TalkVirtualKey = (int)((Choice)_talkKey.SelectedItem).Value,
                TalkMode = (VoiceTalkMode)((Choice)_talkMode.SelectedItem).Value,
                Voice = (_voice.SelectedItem as Choice)?.Value as string,
                FasterSpeech = (bool)((Choice)_pace.SelectedItem).Value,
                OutputVolumePercent = (int)((Choice)_volume.SelectedItem).Value
            };
        }

        // Plays the selected voice, pace and volume without saving; pressing the button again stops it.
        internal async Task TogglePreviewAsync()
        {
            if (_preview != null)
            {
                _preview.Cancel();
                return;
            }

            if (_previewVoice == null || _closing.IsCancellationRequested)
            {
                return;
            }

            _preview = CancellationTokenSource.CreateLinkedTokenSource(_closing.Token);
            _status.Text = UiLocalization.Text("VoiceSettings.Status.Previewing", "Playing a preview. The microphone stays off.");
            RefreshPreviewButton();
            string status;
            try
            {
                await _previewVoice(ReadSettings(), _preview.Token);
                status = _preview.IsCancellationRequested ? string.Empty : UiLocalization.Text("VoiceSettings.Status.PreviewFinished", "Preview finished.");
            }
            catch (OperationCanceledException)
            {
                status = string.Empty;
            }
            catch (Exception ex)
            {
                status = ex.Message;
            }
            finally
            {
                _preview.Dispose();
                _preview = null;
            }

            if (!IsDisposed)
            {
                _status.Text = status;
                RefreshPreviewButton();
            }
        }

        private void RefreshPreviewButton()
        {
            var playing = _preview != null;
            _previewButton.Text = playing
                ? UiLocalization.Text("VoiceSettings.Button.StopPreview", "S&top Preview")
                : UiLocalization.Text("VoiceSettings.Button.Preview", "&Try Voice");
            _previewButton.AccessibleName = playing
                ? UiLocalization.Text("VoiceSettings.Accessible.StopPreview", "Stop preview")
                : UiLocalization.Text("VoiceSettings.Accessible.Preview", "Try voice");
            UiTheme.ApplyButton(_previewButton, playing ? UiGlyph.Stop : UiGlyph.Send);
        }

        private static void SelectValue(ComboBox comboBox, object value)
        {
            comboBox.SelectedIndex = 0;
            for (var i = 0; i < comboBox.Items.Count; i++)
            {
                if (Equals(((Choice)comboBox.Items[i]).Value, value))
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }

        private void BuildLayout()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 5; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            }

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddRow(root, 0, UiLocalization.Text("VoiceSettings.Label.TalkKey", "Talk &key"), _talkKey);
            AddRow(root, 1, UiLocalization.Text("VoiceSettings.Label.TalkMode", "Talk &mode"), _talkMode);
            AddRow(root, 2, UiLocalization.Text("VoiceSettings.Label.Voice", "&Voice"), _voice);
            AddRow(root, 3, UiLocalization.Text("VoiceSettings.Label.Pace", "Speaking &pace"), _pace);
            AddRow(root, 4, UiLocalization.Text("VoiceSettings.Label.Volume", "Reply vo&lume"), _volume);

            var actions = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, UiTheme.ControlSpacing, 0, 0),
                TabStop = false
            };
            actions.Controls.Add(_previewButton);
            root.Controls.Add(actions, 1, 5);

            var hint = UiControlFactory.CreateMutedLabel(UiLocalization.Text(
                "VoiceSettings.Hint",
                "The talk key and mode apply after OK. Voice, pace and volume apply the next time voice chat starts; Try Voice plays them without saving."));
            hint.AutoSize = true;
            hint.MaximumSize = new Size(620, 0);
            hint.Padding = new Padding(0, UiTheme.ControlSpacing, 0, 0);
            root.Controls.Add(hint, 0, 6);
            root.SetColumnSpan(hint, 2);
            _status.AutoSize = true;
            _status.MaximumSize = new Size(620, 0);
            root.Controls.Add(_status, 0, 7);
            root.SetColumnSpan(_status, 2);
            Controls.Add(root);
        }

        private static void AddRow(TableLayoutPanel root, int row, string labelText, ComboBox comboBox)
        {
            // The label sits just before its list in tab order so its mnemonic moves focus to the list.
            var label = UiControlFactory.CreateMutedLabel(labelText);
            label.TabIndex = comboBox.TabIndex - 1;
            root.Controls.Add(label, 0, row);
            root.Controls.Add(comboBox, 1, row);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // Closing Options stops a running preview; the source stays undisposed while that preview unwinds.
                _closing.Cancel();
            }

            base.Dispose(disposing);
        }

        private sealed class Choice
        {
            internal Choice(string text, object value)
            {
                Text = text;
                Value = value;
            }

            internal string Text { get; }

            internal object Value { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
