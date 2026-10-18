using System;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class CommandDefinitionEditForm : Form
    {
        private readonly TextBox _nameTextBox;
        private readonly TextBox _commandTextBox;
        private readonly ComboBox _hostPlatformComboBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;

        public CommandDefinitionEditForm(string title, CommandDefinition definition, CommandHostPlatform defaultHostPlatform)
        {
            Text = string.IsNullOrWhiteSpace(title)
                ? UiLocalization.Text("CommandDefinitionEditForm.Title.EditCommand", "Edit Command")
                : title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(620, 260);
            UiTheme.ApplyForm(this);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 76F));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var nameLabel = new Label
            {
                Text = UiLocalization.Text("CommandDefinitionEditForm.Label.Name", "Name"),
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.Name", "Name"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(nameLabel, true);

            var hostPlatformLabel = new Label
            {
                Text = UiLocalization.Text("CommandDefinitionEditForm.Label.Target", "Target"),
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.Target", "Target"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(hostPlatformLabel, true);

            var commandLabel = new Label
            {
                Text = UiLocalization.Text("CommandDefinitionEditForm.Label.Command", "Command"),
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.Command", "Command"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Margin = new Padding(0, 6, 8, 6),
                TabStop = false
            };
            UiTheme.ApplyLabel(commandLabel, true);

            _nameTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.Name", "Name"),
                TabIndex = 0,
                Text = definition != null ? definition.Name ?? string.Empty : string.Empty
            };
            UiTheme.ApplyTextBox(_nameTextBox, false, false);

            _hostPlatformComboBox = new ComboBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.Target", "Target"),
                DropDownStyle = ComboBoxStyle.DropDownList,
                TabIndex = 1
            };
            UiTheme.ApplyComboBox(_hostPlatformComboBox);
            AddHostPlatformItems();
            SelectHostPlatform(definition != null ? definition.HostPlatform : defaultHostPlatform);

            _commandTextBox = new TextBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.Command", "Command"),
                TabIndex = 2,
                Text = definition != null ? definition.CommandText ?? string.Empty : string.Empty
            };
            UiTheme.ApplyTextBox(_commandTextBox, true, false);
            _commandTextBox.KeyDown += CommandTextBox_KeyDown;

            var buttonPanel = UiControlFactory.CreateDialogButtonBar();

            _okButton = new Button
            {
                Text = UiLocalization.Text("CommandDefinitionEditForm.Button.OK", "&OK"),
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.OK", "OK"),
                AutoSize = true,
                TabIndex = 3,
                UseMnemonic = true
            };
            UiTheme.ApplyDialogButton(_okButton, UiGlyph.Ok);
            _okButton.Click += OkButton_Click;

            _cancelButton = new Button
            {
                Text = UiLocalization.Text("CommandDefinitionEditForm.Button.Cancel", "&Cancel"),
                AccessibleName = UiLocalization.Text("CommandDefinitionEditForm.Accessible.Cancel", "Cancel"),
                AutoSize = true,
                TabIndex = 4,
                UseMnemonic = true,
                DialogResult = DialogResult.Cancel
            };
            UiTheme.ApplyDialogButton(_cancelButton, UiGlyph.Cancel);

            buttonPanel.Controls.Add(_cancelButton);
            buttonPanel.Controls.Add(_okButton);

            root.Controls.Add(nameLabel, 0, 0);
            root.Controls.Add(_nameTextBox, 1, 0);
            root.Controls.Add(hostPlatformLabel, 0, 1);
            root.Controls.Add(_hostPlatformComboBox, 1, 1);
            root.Controls.Add(commandLabel, 0, 2);
            root.Controls.Add(_commandTextBox, 1, 2);
            root.Controls.Add(buttonPanel, 0, 3);
            root.SetColumnSpan(buttonPanel, 2);

            Controls.Add(root);
            AcceptButton = _okButton;
            CancelButton = _cancelButton;
        }

        public CommandDefinition EditedDefinition { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _nameTextBox.Focus();
            _nameTextBox.SelectAll();
        }

        private void CommandTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            OkButton_Click(_okButton, EventArgs.Empty);
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private void OkButton_Click(object sender, EventArgs e)
        {
            var name = _nameTextBox.Text.Trim();
            var commandText = _commandTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("CommandDefinitionEditForm.Message.EnterName", "Please enter a command name."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _nameTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(commandText))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("CommandDefinitionEditForm.Message.EnterCommand", "Please enter a command."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _commandTextBox.Focus();
                return;
            }

            EditedDefinition = new CommandDefinition
            {
                Name = name,
                CommandText = commandText,
                HostPlatform = GetSelectedHostPlatform()
            };
            DialogResult = DialogResult.OK;
            Close();
        }

        private void AddHostPlatformItems()
        {
            _hostPlatformComboBox.Items.Add(new HostPlatformItem(
                CommandHostPlatform.Windows,
                UiLocalization.Text("CommandDefinitionEditForm.Target.Windows", "Windows")));
            _hostPlatformComboBox.Items.Add(new HostPlatformItem(
                CommandHostPlatform.Wsl,
                UiLocalization.Text("CommandDefinitionEditForm.Target.Wsl", "WSL")));
            _hostPlatformComboBox.Items.Add(new HostPlatformItem(
                CommandHostPlatform.RemoteMac,
                UiLocalization.Text("CommandDefinitionEditForm.Target.RemoteMac", "Remote Unix")));
            _hostPlatformComboBox.Items.Add(new HostPlatformItem(
                CommandHostPlatform.Any,
                UiLocalization.Text("CommandDefinitionEditForm.Target.Any", "All runtimes")));
        }

        private void SelectHostPlatform(CommandHostPlatform hostPlatform)
        {
            var normalized = NormalizeHostPlatform(hostPlatform);
            for (var i = 0; i < _hostPlatformComboBox.Items.Count; i++)
            {
                var item = _hostPlatformComboBox.Items[i] as HostPlatformItem;
                if (item != null && item.Value == normalized)
                {
                    _hostPlatformComboBox.SelectedIndex = i;
                    return;
                }
            }

            _hostPlatformComboBox.SelectedIndex = 0;
        }

        private CommandHostPlatform GetSelectedHostPlatform()
        {
            var item = _hostPlatformComboBox.SelectedItem as HostPlatformItem;
            return item != null ? item.Value : CommandHostPlatform.Windows;
        }

        private static CommandHostPlatform NormalizeHostPlatform(CommandHostPlatform hostPlatform)
        {
            return hostPlatform == CommandHostPlatform.Wsl ||
                   hostPlatform == CommandHostPlatform.RemoteMac ||
                   hostPlatform == CommandHostPlatform.Any
                ? hostPlatform
                : CommandHostPlatform.Windows;
        }

        private sealed class HostPlatformItem
        {
            public HostPlatformItem(CommandHostPlatform value, string text)
            {
                Value = value;
                Text = text;
            }

            public CommandHostPlatform Value { get; private set; }

            private string Text { get; set; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
