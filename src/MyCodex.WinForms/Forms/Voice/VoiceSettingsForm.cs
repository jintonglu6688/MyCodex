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
    // Edits the voice preferences of the current target; returns the result in Settings after OK.
    internal sealed class VoiceSettingsForm : Form
    {
        private readonly Func<CancellationToken, Task<RealtimeVoiceCatalog>> _loadVoices;
        private readonly CancellationTokenSource _closing = new CancellationTokenSource();
        private readonly VoiceChatSettings _initial;
        private readonly ComboBox _talkKey;
        private readonly ComboBox _talkMode;
        private readonly ComboBox _voice;
        private readonly ComboBox _pace;
        private readonly ComboBox _volume;
        private readonly Label _status;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        // loadVoices is null when voice is unavailable on this target; the saved voice is then kept as is.
        internal VoiceSettingsForm(VoiceChatSettings settings, Func<CancellationToken, Task<RealtimeVoiceCatalog>> loadVoices)
        {
            _initial = settings ?? new VoiceChatSettings();
            _loadVoices = loadVoices;
            Text = UiLocalization.Text("VoiceSettings.Title", "Voice Settings");
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(560, 330);
            MinimumSize = new Size(460, 330);
            UiTheme.ApplyForm(this);

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
            _okButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("VoiceSettings.Button.Save", "&Save"),
                UiLocalization.Text("VoiceSettings.Accessible.Save", "Save"),
                10,
                112);
            _cancelButton = UiControlFactory.CreateActionButton(
                UiLocalization.Text("VoiceSettings.Button.Cancel", "&Cancel"),
                UiLocalization.Text("VoiceSettings.Accessible.Cancel", "Cancel"),
                11,
                112,
                DialogResult.Cancel);
            UiTheme.ApplyButton(_okButton, UiGlyph.Ok);
            UiTheme.ApplyButton(_cancelButton, UiGlyph.Cancel);
            _okButton.Click += (sender, args) => Accept();

            SelectValue(_talkKey, VoiceChatPreferences.NormalizeTalkVirtualKey(_initial.TalkVirtualKey));
            SelectValue(_talkMode, _initial.TalkMode == VoiceTalkMode.Toggle ? VoiceTalkMode.Toggle : VoiceTalkMode.Hold);
            SelectValue(_pace, _initial.FasterSpeech);
            SelectValue(_volume, VoiceChatPreferences.NormalizeOutputVolumePercent(_initial.OutputVolumePercent));
            FillVoices(null);

            BuildLayout();
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
            Shown += async (sender, args) => await LoadVoicesAsync();
            FormClosed += (sender, args) => _closing.Cancel();
        }

        public VoiceChatSettings Settings { get; private set; }

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
                _status.Text = string.Empty;
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

        private void Accept()
        {
            Settings = new VoiceChatSettings
            {
                TalkVirtualKey = (int)((Choice)_talkKey.SelectedItem).Value,
                TalkMode = (VoiceTalkMode)((Choice)_talkMode.SelectedItem).Value,
                Voice = (_voice.SelectedItem as Choice)?.Value as string,
                FasterSpeech = (bool)((Choice)_pace.SelectedItem).Value,
                OutputVolumePercent = (int)((Choice)_volume.SelectedItem).Value
            };
            DialogResult = DialogResult.OK;
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
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new Padding(UiTheme.OuterPadding),
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            for (var i = 0; i < 5; i++)
            {
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            }

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddRow(root, 0, UiLocalization.Text("VoiceSettings.Label.TalkKey", "Talk &key"), _talkKey);
            AddRow(root, 1, UiLocalization.Text("VoiceSettings.Label.TalkMode", "Talk &mode"), _talkMode);
            AddRow(root, 2, UiLocalization.Text("VoiceSettings.Label.Voice", "&Voice"), _voice);
            AddRow(root, 3, UiLocalization.Text("VoiceSettings.Label.Pace", "Speaking &pace"), _pace);
            AddRow(root, 4, UiLocalization.Text("VoiceSettings.Label.Volume", "Reply vo&lume"), _volume);

            var hint = UiControlFactory.CreateMutedLabel(UiLocalization.Text(
                "VoiceSettings.Hint",
                "The talk key and mode apply right away. Voice, pace and volume apply the next time voice is turned on."));
            hint.AutoSize = true;
            hint.MaximumSize = new Size(520, 0);
            hint.Padding = new Padding(0, UiTheme.ControlSpacing, 0, 0);
            root.Controls.Add(hint, 0, 5);
            root.SetColumnSpan(hint, 2);
            root.Controls.Add(_status, 0, 6);
            root.SetColumnSpan(_status, 2);

            var footer = UiControlFactory.CreateDialogButtonBar(UiTheme.WindowBackColor);
            footer.Controls.Add(_cancelButton);
            footer.Controls.Add(_okButton);
            root.Controls.Add(footer, 0, 7);
            root.SetColumnSpan(footer, 2);
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
                _closing.Cancel();
                _closing.Dispose();
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
