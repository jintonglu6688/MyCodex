using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AuthenticationModelEditForm : Form
    {
        private readonly AuthenticationCatalogModel _original;
        private readonly ThemedTextBox _name = new ThemedTextBox();
        private readonly ThemedTextBox _id = new ThemedTextBox();
        private readonly ThemedTextBox _context = new ThemedTextBox();
        private readonly CheckedListBox _levels = new CheckedListBox { CheckOnClick = true, Height = 150 };
        private readonly ComboBox _default = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _parallel = new CheckBox { ThreeState = true, AutoSize = true };
        private readonly Label _status = new Label { AutoSize = true, TabStop = false };

        public AuthenticationModelEditForm(AuthenticationCatalogModel model = null, IScreenReaderService reader = null)
        {
            _original = model;
            Text = UiLocalization.Text("AuthenticationModel.Title", "Model settings");
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(560, 640);
            Size = new Size(680, 720);
            UiTheme.ApplyForm(this);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, TabStop = false, Padding = new Padding(12) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(fields, UiLocalization.Text("AuthenticationModel.Name", "&Display name"), _name);
            Add(fields, UiLocalization.Text("AuthenticationModel.Id", "Model &ID"), _id);
            Add(fields, UiLocalization.Text("AuthenticationModel.Context", "&Context length"), _context);
            Add(fields, UiLocalization.Text("AuthenticationModel.Levels", "Reasoning &levels"), _levels);
            Add(fields, UiLocalization.Text("AuthenticationModel.Default", "Default &reasoning"), _default);
            _parallel.Text = UiLocalization.Text("AuthenticationModel.Parallel", "&Parallel tool calls");
            _parallel.AccessibleName = AuthenticationCenterUiText.Name(_parallel.Text);
            Add(fields, string.Empty, _parallel);
            _parallel.AccessibleName = AuthenticationCenterUiText.Name(_parallel.Text);
            UiTheme.ApplyTextBox(_name, false, false);
            UiTheme.ApplyTextBox(_id, false, false);
            UiTheme.ApplyTextBox(_context, false, false);
            UiTheme.ApplyListBox(_levels);
            UiTheme.ApplyComboBox(_default);
            _levels.FormattingEnabled = true;
            _levels.Format += (sender, args) => args.Value = MainFormModelOptionComposer.GetReasoningEffortText(args.ListItem as string);
            _default.FormattingEnabled = true;
            _default.Format += (sender, args) => args.Value = MainFormModelOptionComposer.GetReasoningEffortText(args.ListItem as string);
            UiTheme.ApplyCheckBox(_parallel);
            _name.Text = model?.DisplayName ?? string.Empty;
            _id.Text = model?.Model ?? string.Empty;
            _context.Text = model?.ContextWindow?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            foreach (var level in new[] { "none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra" }.Concat(model?.ReasoningLevels ?? new List<string>()).Distinct(StringComparer.Ordinal))
            {
                _levels.Items.Add(level, model?.ReasoningLevels?.Contains(level) == true);
            }
            _parallel.CheckState = model?.SupportsParallelToolCalls == null ? CheckState.Indeterminate : model.SupportsParallelToolCalls.Value ? CheckState.Checked : CheckState.Unchecked;
            UpdateDefaults(-1, CheckState.Unchecked, model?.DefaultReasoningLevel);
            _levels.ItemCheck += (sender, args) => UpdateDefaults(args.Index, args.NewValue, _default.SelectedIndex > 0 ? _default.SelectedItem as string : null);
            var save = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationModel.Save", "&Save"), 0);
            var cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationModel.Cancel", "C&ancel"), 1);
            cancel.DialogResult = DialogResult.Cancel;
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabStop = false, TabIndex = 2 };
            actions.Controls.AddRange(new Control[] { save, cancel });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(fields, 0, 0);
            root.Controls.Add(_status, 0, 1);
            root.Controls.Add(actions, 0, 2);
            Controls.Add(root);
            AcceptButton = save;
            CancelButton = cancel;
            save.Click += (sender, args) => Save();
            Shown += (sender, args) => _name.Focus();
        }

        public AuthenticationCatalogModel Model { get; private set; }

        private static void Add(TableLayoutPanel fields, string caption, Control control)
        {
            var row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { Text = caption, AutoSize = true, TabStop = false, TabIndex = row * 2, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            control.Dock = DockStyle.Top;
            control.TabIndex = row * 2 + 1;
            control.AccessibleName = AuthenticationCenterUiText.Name(caption);
            fields.Controls.Add(control, 1, row);
        }

        private void UpdateDefaults(int changingIndex, CheckState newState, string selected)
        {
            _default.Items.Clear();
            _default.Items.Add(UiLocalization.Text("AuthenticationModel.Automatic", "Automatic"));
            for (var i = 0; i < _levels.Items.Count; i++)
            {
                if (i == changingIndex ? newState == CheckState.Checked : _levels.GetItemChecked(i))
                {
                    _default.Items.Add(_levels.Items[i]);
                }
            }
            _default.SelectedIndex = selected == null ? 0 : Math.Max(0, _default.Items.IndexOf(selected));
        }

        internal bool Save()
        {
            long context = 0;
            if (string.IsNullOrWhiteSpace(_id.Text) || string.IsNullOrWhiteSpace(_name.Text) ||
                (_context.Text.Trim().Length > 0 && (!long.TryParse(_context.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out context) || context <= 0)))
            {
                _status.Text = UiLocalization.Text("AuthenticationModel.Invalid", "Enter a name, model ID and a positive context length, or leave the context length empty.");
                (string.IsNullOrWhiteSpace(_name.Text) ? _name : string.IsNullOrWhiteSpace(_id.Text) ? _id : _context).Focus();
                return false;
            }
            Model = new AuthenticationCatalogModel
            {
                Model = _id.Text.Trim(), DisplayName = _name.Text.Trim(), ContextWindow = _context.Text.Trim().Length == 0 ? (long?)null : context,
                ReasoningLevels = _levels.CheckedItems.Count == 0 && _original?.ReasoningLevels == null ? null : _levels.CheckedItems.Cast<string>().ToList(),
                DefaultReasoningLevel = _default.SelectedIndex > 0 ? _default.SelectedItem as string : null,
                InputModalities = _original?.InputModalities == null ? null : new List<string>(_original.InputModalities),
                SupportsParallelToolCalls = _parallel.CheckState == CheckState.Indeterminate ? (bool?)null : _parallel.Checked,
                BaseInstructions = _original?.BaseInstructions,
                Metadata = AuthenticationModelsControl.CloneMetadata(_original?.Metadata)
            };
            DialogResult = DialogResult.OK;
            return true;
        }
    }
}
