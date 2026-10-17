using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    public sealed class AuthenticationAdvancedControl : UserControl
    {
        private readonly Dictionary<string, object> _original;
        private readonly TableLayoutPanel _fields = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, TabStop = false };
        private readonly CheckBox _full = new CheckBox { AutoSize = true };
        private readonly ComboBox _auth = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ThemedTextBox _max = new ThemedTextBox();
        private readonly CheckBox _impersonate = new CheckBox { AutoSize = true };
        private readonly ThemedTextBox _env = new ThemedTextBox();
        private readonly ThemedTextBox _agent = new ThemedTextBox();
        private readonly ComboBox _cache = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox _thinking = new CheckBox { ThreeState = true, AutoSize = true };
        private readonly CheckBox _effort = new CheckBox { ThreeState = true, AutoSize = true };
        private readonly ThemedTextBox _effortParameter = new ThemedTextBox();
        private readonly ComboBox _thinkingParameter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _effortMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Dictionary<string, DataGridView> _maps = new Dictionary<string, DataGridView>();
        private readonly AuthenticationQueryParametersControl _query;
        private string _kind;

        public AuthenticationAdvancedControl(Dictionary<string, object> original)
        {
            _original = new Dictionary<string, object>(original ?? new Dictionary<string, object>());
            TabStop = true;
            AutoScroll = true;
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            _fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Add(UiLocalization.Text("AuthenticationAdvanced.FullUrl", "&Full request URL"), _full);
            Add(UiLocalization.Text("AuthenticationAdvanced.Auth", "Authentication &header"), _auth);
            Add(UiLocalization.Text("AuthenticationAdvanced.Max", "&Maximum output tokens"), _max);
            Add(UiLocalization.Text("AuthenticationAdvanced.Impersonate", "Emulate Claude Code (&X)"), _impersonate);
            Add(UiLocalization.Text("AuthenticationAdvanced.Environment", "API key &environment variable"), _env);
            Add(UiLocalization.Text("AuthenticationAdvanced.Agent", "&User-Agent"), _agent);
            Add(UiLocalization.Text("AuthenticationAdvanced.Cache", "&Prompt cache routing"), _cache);
            Add(UiLocalization.Text("AuthenticationAdvanced.Thinking", "Supports &thinking"), _thinking);
            Add(UiLocalization.Text("AuthenticationAdvanced.Effort", "Supports &reasoning effort"), _effort);
            Add(UiLocalization.Text("AuthenticationAdvanced.EffortParameter", "Reason&ing effort parameter"), _effortParameter);
            Add(UiLocalization.Text("AuthenticationAdvanced.ThinkingParameter", "Thinking &option"), _thinkingParameter);
            Add(UiLocalization.Text("AuthenticationAdvanced.EffortMode", "Effort value mo&de"), _effortMode);
            AddMap("requestHeaders", UiLocalization.Text("AuthenticationAdvanced.Headers", "Request headers(&V)"));
            AddMap("requestBody", UiLocalization.Text("AuthenticationAdvanced.Body", "Request body overrides(&W)"));
            _query = new AuthenticationQueryParametersControl(Value(_original, "queryParams") as IDictionary<string, object>);
            Add(UiLocalization.Text("AuthenticationAdvanced.Query", "&Query parameters"), _query);
            _auth.Items.AddRange(new object[] { "bearer", "x-api-key" });
            _auth.SelectedItem = Value(_original, "anthropicAuthHeader") ?? "x-api-key";
            if (_auth.SelectedIndex < 0) { _auth.SelectedIndex = 0; }
            _cache.Items.AddRange(new object[] { "auto", "enabled", "disabled" });
            _cache.SelectedItem = Value(_original, "promptCacheRouting") ?? "auto";
            if (_cache.SelectedIndex < 0) { _cache.SelectedIndex = 0; }
            _full.Checked = Value(_original, "isFullUrl") is bool full && full;
            _max.Text = Convert.ToString(Value(_original, "maxTokens"), CultureInfo.InvariantCulture);
            _impersonate.Checked = Value(_original, "impersonateClaudeCode") is bool enabled && enabled;
            _env.Text = Value(_original, "envKey") as string ?? string.Empty;
            _agent.Text = Value(_original, "userAgent") as string ?? string.Empty;
            var reasoning = Value(_original, "codexChatReasoning") as IDictionary<string, object>;
            _thinking.CheckState = State(Value(reasoning, "supportsThinking"));
            _effort.CheckState = State(Value(reasoning, "supportsEffort"));
            _effortParameter.Text = Value(reasoning, "effortParam") as string ?? string.Empty;
            Fill(_thinkingParameter, new[] { "", "none", "thinking", "enable_thinking", "reasoning_split" }, Value(reasoning, "thinkingParam") as string);
            Fill(_effortMode, new[] { "", "passthrough", "low_high", "deepseek", "openrouter", "zen" }, Value(reasoning, "effortValueMode") as string);
            var hint = new Label { AutoSize = true, Dock = DockStyle.Top, TabStop = false,
                Text = UiLocalization.Text("AuthenticationAdvanced.SecretHint", "******** retains a stored secret. Replace the value to change it, or delete its row to remove it. Conversion settings take effect after enabling this provider.") };
            _fields.Controls.Add(hint, 0, _fields.RowCount++);
            _fields.SetColumnSpan(hint, 2);
            Controls.Add(_fields);
        }

        public string Kind
        {
            get { return _kind; }
            set
            {
                _kind = value;
                SetVisible(_auth, value == "anthropic");
                SetVisible(_max, value == "anthropic");
                SetVisible(_impersonate, value == "anthropic");
                var routed = value == "anthropic" || value == "chat_completions";
                SetVisible(_agent, routed);
                SetVisible(_cache, value == "chat_completions");
                SetVisible(_maps["requestHeaders"], routed);
                SetVisible(_maps["requestBody"], routed);
                SetVisible(_thinking, value == "chat_completions");
                SetVisible(_effort, value == "chat_completions");
                SetVisible(_effortParameter, value == "chat_completions");
                SetVisible(_thinkingParameter, value == "chat_completions");
                SetVisible(_effortMode, value == "chat_completions");
            }
        }

        public Dictionary<string, object> Read()
        {
            var result = new Dictionary<string, object>(_original);
            result["isFullUrl"] = _full.Checked;
            Set(result, "envKey", _env.Text.Trim());
            result["queryParams"] = _query.Read();
            if (_kind == "anthropic")
            {
                result.Remove("codexChatReasoning");
                result.Remove("promptCacheRouting");
                result["anthropicAuthHeader"] = _auth.SelectedItem as string;
                result["impersonateClaudeCode"] = _impersonate.Checked;
                if (_max.Text.Trim().Length == 0) { result.Remove("maxTokens"); }
                else if (long.TryParse(_max.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var tokens) && tokens > 0) { result["maxTokens"] = tokens; }
                else { _max.Focus(); throw new AuthenticationCenterException("invalid_params"); }
            }
            else
            {
                result.Remove("anthropicAuthHeader");
                result.Remove("maxTokens");
                result.Remove("impersonateClaudeCode");
            }
            if (_kind == "chat_completions")
            {
                var reasoning = Value(_original, "codexChatReasoning") as IDictionary<string, object>;
                var copy = reasoning == null ? new Dictionary<string, object>() : new Dictionary<string, object>(reasoning);
                SetBoolean(copy, "supportsThinking", _thinking.CheckState);
                SetBoolean(copy, "supportsEffort", _effort.CheckState);
                Set(copy, "effortParam", _effortParameter.Text.Trim());
                Set(copy, "thinkingParam", _thinkingParameter.SelectedItem as string ?? string.Empty);
                Set(copy, "effortValueMode", _effortMode.SelectedItem as string ?? string.Empty);
                result["codexChatReasoning"] = copy;
            }
            if (_kind == "anthropic" || _kind == "chat_completions")
            {
                Set(result, "userAgent", _agent.Text.Trim());
                if (_kind == "chat_completions") result["promptCacheRouting"] = _cache.SelectedItem as string;
                result["requestHeaders"] = ReadMap("requestHeaders");
                result["requestBody"] = ReadMap("requestBody");
            }
            return result;
        }

        private void Add(string caption, Control control)
        {
            var row = _fields.RowCount++;
            _fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _fields.Controls.Add(new Label { Text = caption, AutoSize = true, TabStop = false, TabIndex = row * 2, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            control.AccessibleName = AuthenticationCenterUiText.Name(caption);
            control.TabIndex = row * 2 + 1;
            control.Dock = DockStyle.Top;
            if (control is TextBox box) { UiTheme.ApplyTextBox(box, false, false); }
            if (control is ComboBox combo) { UiTheme.ApplyComboBox(combo); }
            if (control is CheckBox check) { UiTheme.ApplyCheckBox(check); }
            _fields.Controls.Add(control, 1, row);
        }
        private void SetVisible(Control control, bool visible)
        {
            control.Visible = visible;
            _fields.GetControlFromPosition(0, _fields.GetRow(control)).Visible = visible;
        }
        private void AddMap(string key, string caption)
        {
            var grid = new DataGridView { Height = 145, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = true, AllowUserToAddRows = true, AllowUserToDeleteRows = true };
            grid.BackgroundColor = UiTheme.CardBackColor;
            grid.DefaultCellStyle.BackColor = UiTheme.CardBackColor;
            grid.DefaultCellStyle.ForeColor = UiTheme.TextColor;
            grid.Columns.Add("key", UiLocalization.Text("AuthenticationAdvanced.Key", "Key"));
            grid.Columns.Add("value", UiLocalization.Text("AuthenticationAdvanced.Value", "Value"));
            var type = new DataGridViewComboBoxColumn { Name = "type", HeaderText = UiLocalization.Text("AuthenticationAdvanced.Type", "Type") };
            type.Items.AddRange("string", "number", "boolean", "null", "preserved");
            grid.Columns.Add(type);
            var map = Value(_original, key) as IDictionary<string, object>;
            if (map != null)
            {
                foreach (var pair in map)
                {
                    var compound = pair.Value is IDictionary || (pair.Value is IEnumerable && !(pair.Value is string));
                    var typeName = compound ? "preserved" : pair.Value == null ? "null" : pair.Value is bool ? "boolean" : pair.Value is string ? "string" : "number";
                    var index = grid.Rows.Add(pair.Key, compound ? UiLocalization.Text("AuthenticationAdvanced.Preserved", "Existing structured value (preserved)") : Convert.ToString(pair.Value, CultureInfo.InvariantCulture), typeName);
                    if (compound) { grid.Rows[index].ReadOnly = true; grid.Rows[index].Tag = pair.Value; }
                }
            }
            grid.DefaultValuesNeeded += (sender, args) => args.Row.Cells[2].Value = "string";
            _maps.Add(key, grid);
            Add(caption, grid);
        }
        private Dictionary<string, object> ReadMap(string key)
        {
            var grid = _maps[key];
            grid.EndEdit();
            var values = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow) { continue; }
                var name = Convert.ToString(row.Cells[0].Value)?.Trim();
                var value = Convert.ToString(row.Cells[1].Value) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name) || values.ContainsKey(name)) { throw new AuthenticationCenterException("invalid_params"); }
                switch (Convert.ToString(row.Cells[2].Value))
                {
                    case "preserved": values[name] = row.Tag; break;
                    case "null": values[name] = null; break;
                    case "number":
                        if (!decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) { throw new AuthenticationCenterException("invalid_params"); }
                        values[name] = number; break;
                    case "boolean":
                        if (!bool.TryParse(value, out var boolean)) { throw new AuthenticationCenterException("invalid_params"); }
                        values[name] = boolean; break;
                    default: values[name] = value; break;
                }
            }
            return values;
        }
        private static object Value(IDictionary<string, object> source, string key) { return source != null && source.TryGetValue(key, out var value) ? value : null; }
        private static void Fill(ComboBox control, string[] values, string selected)
        {
            control.Items.AddRange(values);
            if (!string.IsNullOrEmpty(selected) && !control.Items.Contains(selected)) { control.Items.Add(selected); }
            control.SelectedItem = selected ?? string.Empty;
        }
        private static CheckState State(object value) { return value is bool boolean ? boolean ? CheckState.Checked : CheckState.Unchecked : CheckState.Indeterminate; }
        private static void Set(IDictionary<string, object> target, string key, string value) { if (value.Length == 0) { target.Remove(key); } else { target[key] = value; } }
        private static void SetBoolean(IDictionary<string, object> target, string key, CheckState state) { if (state == CheckState.Indeterminate) { target.Remove(key); } else { target[key] = state == CheckState.Checked; } }
    }
}
