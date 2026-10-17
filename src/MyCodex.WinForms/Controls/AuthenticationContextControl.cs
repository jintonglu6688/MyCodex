using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    public sealed class AuthenticationContextControl : UserControl
    {
        private readonly bool _supported;
        private readonly CheckBox _remote = new CheckBox { AutoSize = true, TabIndex = 0 };
        private readonly CheckBox _window = new CheckBox { AutoSize = true, TabIndex = 1 };
        private readonly ThemedTextBox _limit = new ThemedTextBox { Dock = DockStyle.Top, TabIndex = 3 };
        private bool _remoteChanged;
        private bool _windowChanged;
        private bool _limitChanged;

        public AuthenticationContextControl(IDictionary<string, object> original, bool chatGpt, bool supported)
        {
            _supported = supported;
            TabStop = supported;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            var fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, TabStop = false };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _remote.Text = UiLocalization.Text("AuthenticationContext.Remote", "Remote compaction (&2)");
            _window.Text = UiLocalization.Text("AuthenticationContext.Window", "1M context window (&1)");
            foreach (var check in new[] { _remote, _window })
            {
                check.AccessibleName = AuthenticationCenterUiText.Name(check.Text);
                check.Enabled = supported;
                UiTheme.ApplyCheckBox(check);
                fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                fields.Controls.Add(check, 0, fields.RowCount++);
                fields.SetColumnSpan(check, 2);
            }
            _remote.Visible = !chatGpt;
            var caption = UiLocalization.Text("AuthenticationContext.Limit", "Auto-compaction threshold (&3)");
            var label = new Label { Text = caption, AutoSize = true, TabStop = false, TabIndex = 2 };
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(label, 0, fields.RowCount);
            fields.Controls.Add(_limit, 1, fields.RowCount++);
            _limit.AccessibleName = AuthenticationCenterUiText.Name(caption);
            UiTheme.ApplyTextBox(_limit, false, false);
            _remote.Checked = Value(original, "remoteCompaction") is bool enabled && enabled;
            _window.Checked = string.Equals(Convert.ToString(Value(original, "modelContextWindow"), CultureInfo.InvariantCulture), "1000000", StringComparison.Ordinal);
            _limit.Text = Convert.ToString(Value(original, "autoCompactTokenLimit"), CultureInfo.InvariantCulture);
            if (_limit.Text.Length == 0) { _limit.Text = "900000"; }
            _limit.Enabled = supported && _window.Checked;
            _remote.CheckedChanged += (sender, args) => _remoteChanged = true;
            _window.CheckedChanged += (sender, args) =>
            {
                _windowChanged = true;
                _limitChanged = true;
                _limit.Enabled = supported && _window.Checked;
                if (!_window.Checked) { _limit.Text = "900000"; }
            };
            _limit.TextChanged += (sender, args) => _limitChanged = true;
            var hint = new Label { AutoSize = true, Dock = DockStyle.Top, TabStop = false,
                Text = supported
                    ? UiLocalization.Text("AuthenticationContext.Hint", "1M writes a context setting; it does not verify provider support. Turning it off removes the context length and compaction threshold overrides. Common configuration can override these settings.")
                    : UiLocalization.Text("AuthenticationContext.Unavailable", "Update this target's authentication backend to edit context and compaction settings.") };
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            fields.Controls.Add(hint, 0, fields.RowCount++);
            fields.SetColumnSpan(hint, 2);
            Controls.Add(fields);
        }

        public void Apply(IDictionary<string, object> advanced)
        {
            if (!_supported) { return; }
            if (_remoteChanged) { advanced["remoteCompaction"] = _remote.Checked; }
            if (_windowChanged) { advanced["modelContextWindow"] = _window.Checked ? (object)1000000L : null; }
            if (!_limitChanged) { return; }
            if (!_window.Checked) { advanced["autoCompactTokenLimit"] = null; }
            else if (long.TryParse(_limit.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var limit) && limit > 0)
            {
                advanced["autoCompactTokenLimit"] = limit;
            }
            else
            {
                _limit.Focus();
                throw new AuthenticationCenterException("invalid_params");
            }
        }

        private static object Value(IDictionary<string, object> source, string key)
        {
            return source != null && source.TryGetValue(key, out var value) ? value : null;
        }
    }
}
