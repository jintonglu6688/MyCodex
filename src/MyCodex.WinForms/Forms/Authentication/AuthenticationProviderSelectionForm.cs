using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AuthenticationProviderSelectionForm : Form
    {
        private readonly IReadOnlyList<AuthenticationProviderPreset> _presets;

        private readonly ThemedTextBox _searchTextBox;
        private readonly ListBox _providersList;
        private readonly Label _statusLabel;
        private readonly Button _nextButton;

        public AuthenticationProviderSelectionForm(IReadOnlyList<AuthenticationProviderPreset> presets)
        {
            if (presets == null) throw new ArgumentNullException(nameof(presets));
            _presets = presets.OrderBy(AuthenticationPresetPresentation.Priority)
                .ThenBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase).ToList();
            Text = UiLocalization.Text("AuthenticationProviderSelection.Title", "Choose provider");
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(540, 390);
            Size = new Size(620, 480);
            UiTheme.ApplyForm(this);

            _searchTextBox = new ThemedTextBox
            {
                Dock = DockStyle.Fill,
                TabIndex = 0,
                AccessibleName = UiLocalization.Text("AuthenticationProviderSelection.Search", "Search providers")
            };
            UiTheme.ApplyTextBox(_searchTextBox, false, false);
            _providersList = new ListBox
            {
                FormattingEnabled = true,
                Dock = DockStyle.Fill,
                TabIndex = 1,
                AccessibleName = UiLocalization.Text("AuthenticationProviderSelection.Providers", "Available providers")
            };
            UiTheme.ApplyListBox(_providersList);
            _providersList.Format += (sender, args) =>
            {
                if (args.ListItem is AuthenticationProviderPreset preset)
                    args.Value = AuthenticationPresetPresentation.DisplayName(preset);
            };
            _statusLabel = new Label { Dock = DockStyle.Fill, AutoSize = true, TabStop = false };
            _nextButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderSelection.Next", "&Next"), 0);
            var cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Cancel", "&Cancel"), 1);
            cancel.DialogResult = DialogResult.Cancel;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 3 };
            actions.Controls.AddRange(new Control[] { _nextButton, cancel });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12), TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(new Label { Text = UiLocalization.Text("AuthenticationProviderSelection.Search", "Search providers"), AutoSize = true, TabStop = false }, 0, 0);
            root.Controls.Add(_searchTextBox, 0, 1);
            root.Controls.Add(_providersList, 0, 2);
            root.Controls.Add(_statusLabel, 0, 3);
            root.Controls.Add(actions, 0, 4);
            Controls.Add(root);
            AcceptButton = _nextButton;
            CancelButton = cancel;
            _searchTextBox.TextChanged += (sender, args) => RebuildList();
            _providersList.SelectedIndexChanged += (sender, args) => UpdateActions();
            _nextButton.Click += (sender, args) => Confirm();
            Shown += (sender, args) => _searchTextBox.Focus();
            RebuildList();
        }

        public AuthenticationProviderPreset SelectedTemplate { get; private set; }

        private AuthenticationProviderPreset Selected
        {
            get { return _providersList.SelectedItem as AuthenticationProviderPreset; }
        }

        private void RebuildList()
        {
            var query = _searchTextBox.Text.Trim();
            var matches = _presets.Where(template =>
                AuthenticationPresetPresentation.DisplayName(template).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (template.Name ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (template.Id ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            _providersList.BeginUpdate();
            try
            {
                _providersList.Items.Clear();
                foreach (var template in matches)
                {
                    _providersList.Items.Add(template);
                }
                if (_providersList.Items.Count > 0)
                {
                    _providersList.SelectedIndex = 0;
                }
            }
            finally
            {
                _providersList.EndUpdate();
            }
            UpdateActions();
        }

        private void UpdateActions()
        {
            var selected = Selected;
            _nextButton.Enabled = selected != null && selected.Available;
            _statusLabel.Text = selected != null && !selected.Available
                ? UiLocalization.Text("AuthenticationProviderSelection.Unavailable", "This provider requires a sign-in method that is not supported yet.")
                : string.Empty;
        }

        private void Confirm()
        {
            if (Selected == null || !Selected.Available)
            {
                return;
            }
            SelectedTemplate = Selected;
            DialogResult = DialogResult.OK;
        }
    }

}
