using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AuthenticationKeyValueEditForm : Form
    {
        internal const string StringType = "string";
        private const string PreservedType = "preserved";
        private readonly ThemedTextBox _name = new ThemedTextBox();
        private readonly ThemedTextBox _value = new ThemedTextBox();
        private readonly ComboBox _type;
        private readonly Label _status = new Label { AutoSize = true, Dock = DockStyle.Fill, TabStop = false };
        private readonly HashSet<string> _existingNames;
        private readonly AuthenticationKeyValueTexts _texts;
        private readonly object _preserved;

        internal AuthenticationKeyValueEditForm(AuthenticationKeyValueTexts texts, string name, object value, string type, IEnumerable<string> existingNames)
        {
            _texts = texts ?? throw new ArgumentNullException(nameof(texts));
            _existingNames = new HashSet<string>(existingNames, StringComparer.Ordinal);
            Text = texts.Title;
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(480, texts.Typed ? 280 : 240);
            Size = new Size(600, texts.Typed ? 320 : 280);
            UiTheme.ApplyForm(this);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, TabStop = false, TabIndex = 0 };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddField(fields, texts.Name, _name, 0);
            AddField(fields, texts.Value, _value, 1);
            _name.Text = name ?? string.Empty;
            _value.Text = Display(value, type);
            if (texts.Typed)
            {
                _type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
                AddField(fields, UiLocalization.Text("AuthenticationKeyValue.Type", "Value &type"), _type, 2);
                if (type == PreservedType)
                {
                    // Structured values have no text form; they can only be renamed or deleted.
                    _preserved = value;
                    _type.Items.Add(PreservedType);
                    _type.Enabled = false;
                    _value.ReadOnly = true;
                }
                else
                {
                    _type.Items.AddRange(new object[] { StringType, "number", "boolean", "null" });
                }
                _type.SelectedItem = type ?? "null";
                if (_type.SelectedIndex < 0) _type.SelectedIndex = 0;
                _type.SelectedIndexChanged += (sender, args) => UpdateValueState();
                UpdateValueState();
            }
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
        internal object ParameterValue { get; private set; }
        internal string ValueType { get; private set; }

        internal static string TypeOf(object value)
        {
            return value is IDictionary || (value is IEnumerable && !(value is string)) ? PreservedType
                : value == null ? "null" : value is bool ? "boolean" : value is string ? StringType : "number";
        }

        internal static string Display(object value, string type)
        {
            if (type == PreservedType) return UiLocalization.Text("AuthenticationAdvanced.Preserved", "Existing structured value (preserved)");
            if (value is bool flag) return flag ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private void UpdateValueState()
        {
            // A null value has no text to enter.
            _value.Enabled = _type.SelectedItem as string != "null";
        }

        private static void AddField(TableLayoutPanel fields, string caption, Control control, int row)
        {
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(new Label { Text = caption, AutoSize = true, TabStop = false, TabIndex = row * 2, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            control.AccessibleName = AuthenticationCenterUiText.Name(caption);
            control.Dock = DockStyle.Top;
            control.TabIndex = row * 2 + 1;
            if (control is TextBox textBox) UiTheme.ApplyTextBox(textBox, false, false);
            if (control is ComboBox combo) UiTheme.ApplyComboBox(combo);
            fields.Controls.Add(control, 1, row);
        }

        internal bool Save()
        {
            var name = _name.Text.Trim();
            if (name.Length == 0 || _existingNames.Contains(name))
            {
                _status.Text = _texts.InvalidName;
                _name.Focus();
                return false;
            }
            var type = _texts.Typed ? _type.SelectedItem as string : null;
            if (!TryReadValue(type, out var value))
            {
                _status.Text = string.Format(UiLocalization.Text("AuthenticationKeyValue.InvalidValue", "Enter a valid {0} value."), type);
                _value.Focus();
                return false;
            }
            ParameterName = name;
            ParameterValue = value;
            ValueType = type;
            DialogResult = DialogResult.OK;
            return true;
        }

        private bool TryReadValue(string type, out object value)
        {
            var text = _value.Text;
            value = null;
            switch (type)
            {
                case PreservedType: value = _preserved; return true;
                case "null": return true;
                case "number":
                    if (!decimal.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return false;
                    value = number;
                    return true;
                case "boolean":
                    if (!bool.TryParse(text.Trim(), out var flag)) return false;
                    value = flag;
                    return true;
                default: value = text; return true;
            }
        }
    }
}
