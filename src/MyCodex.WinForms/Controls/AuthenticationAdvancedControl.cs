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
        private readonly Dictionary<string, AuthenticationKeyValueListControl> _maps = new Dictionary<string, AuthenticationKeyValueListControl>();
        private readonly AuthenticationKeyValueListControl _query;
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
            AddMap("requestHeaders", AuthenticationKeyValueTexts.Headers());
            AddMap("requestBody", AuthenticationKeyValueTexts.Body());
            var query = AuthenticationKeyValueTexts.Query();
            _query = new AuthenticationKeyValueListControl(Value(_original, "queryParams") as IDictionary<string, object>, query);
            Add(query.List, _query);
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
                result["requestHeaders"] = _maps["requestHeaders"].Read();
                result["requestBody"] = _maps["requestBody"].Read();
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
        private void AddMap(string key, AuthenticationKeyValueTexts texts)
        {
            var list = new AuthenticationKeyValueListControl(Value(_original, key) as IDictionary<string, object>, texts);
            _maps.Add(key, list);
            Add(texts.List, list);
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
