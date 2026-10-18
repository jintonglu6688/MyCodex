using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AuthenticationQueryParameterEditForm : Form
    {
        private readonly ThemedTextBox _name = new ThemedTextBox();
        private readonly ThemedTextBox _value = new ThemedTextBox();
        private readonly Label _status = new Label { AutoSize = true, Dock = DockStyle.Fill, TabStop = false };
        private readonly HashSet<string> _existingNames;

        internal AuthenticationQueryParameterEditForm(string name, string value, IEnumerable<string> existingNames)
        {
            _existingNames = new HashSet<string>(existingNames, StringComparer.Ordinal);
            Text = UiLocalization.Text("AuthenticationQuery.Title", "Query parameter");
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(480, 240);
            Size = new Size(600, 280);
            UiTheme.ApplyForm(this);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, TabStop = false, TabIndex = 0 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(fields, UiLocalization.Text("AuthenticationQuery.Name", "Parameter &name"), _name, 0);
            AddField(fields, UiLocalization.Text("AuthenticationQuery.Value", "Parameter &value"), _value, 1);
            _name.Text = name ?? string.Empty;
            _value.Text = value ?? string.Empty;
            var save = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Save", "&Save"), 0);
            var cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationProviderEdit.Button.Cancel", "&Cancel"), 1);
            cancel.DialogResult = DialogResult.Cancel;
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 2 };
            actions.Controls.AddRange(new Control[] { save, cancel });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, TabStop = false, Padding = new Padding(12) };
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

        internal string ParameterName { get; private set; }
        internal string ParameterValue { get; private set; }

        private static void AddField(TableLayoutPanel fields, string caption, TextBox textBox, int row)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { Text = caption, AutoSize = true, TabStop = false, TabIndex = row * 2, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            textBox.AccessibleName = AuthenticationCenterUiText.Name(caption);
            textBox.Dock = DockStyle.Top;
            textBox.TabIndex = row * 2 + 1;
            UiTheme.ApplyTextBox(textBox, false, false);
            fields.Controls.Add(textBox, 1, row);
        }

        internal bool Save()
        {
            var name = _name.Text.Trim();
            if (name.Length == 0 || _existingNames.Contains(name))
            {
                _status.Text = UiLocalization.Text("AuthenticationQuery.InvalidName", "Enter a nonempty, unique parameter name.");
                _name.Focus();
                return false;
            }
            ParameterName = name;
            ParameterValue = _value.Text;
            DialogResult = DialogResult.OK;
            return true;
        }
    }
}
