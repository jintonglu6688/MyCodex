using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // Usage (quota) query settings of one saved provider. Saving tests the query and shows the result.
    public sealed class AuthenticationUsageForm : Form
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly string _providerId;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Dictionary<Control, Label> _captions = new Dictionary<Control, Label>();
        private readonly CheckBox _enabled = new ThemedCheckBox { AutoSize = true };
        private readonly ComboBox _template = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ThemedTextBox _baseUrl = new ThemedTextBox();
        private readonly ThemedTextBox _apiKey = new ThemedTextBox { UseSystemPasswordChar = true };
        private readonly CheckBox _clearApiKey = new ThemedCheckBox { AutoSize = true };
        private readonly ThemedTextBox _accessToken = new ThemedTextBox { UseSystemPasswordChar = true };
        private readonly CheckBox _clearAccessToken = new ThemedCheckBox { AutoSize = true };
        private readonly ThemedTextBox _userId = new ThemedTextBox();
        private readonly ThemedTextBox _timeout = new ThemedTextBox();
        private readonly ThemedTextBox _interval = new ThemedTextBox();
        private readonly ThemedTextBox _code = new ThemedTextBox { Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Both, WordWrap = false, Height = 180 };
        private readonly ThemedTextBox _status;
        private readonly Button _save;
        private AuthenticationUsageSettings _settings;
        private bool _busy;
        private bool _disposed;

        // Settings are read before the dialog opens: controls start in their final state, so
        // screen readers announce each one once instead of hearing it change after opening.
        internal static async Task<bool> EditAsync(Control owner, IAuthenticationCenterClient client, string providerId, CancellationToken token)
        {
            var settings = await client.ReadUsageAsync(providerId, token);
            if (owner.IsDisposed) return false;
            using (var form = new AuthenticationUsageForm(client, settings))
            {
                form.ShowDialog(owner);
                return form.Saved;
            }
        }

        public AuthenticationUsageForm(IAuthenticationCenterClient client, AuthenticationUsageSettings settings)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _providerId = settings.ProviderId;
            Text = string.Format(UiLocalization.Text("AuthenticationUsage.TitleFor", "Usage query settings: {0}"), settings.ProviderName);
            AccessibleName = Text;
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(600, 560);
            Size = new Size(720, 700);
            UiTheme.ApplyForm(this);
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 2, TabStop = false, TabIndex = 0, Padding = new Padding(12) };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Check(fields, _enabled, UiLocalization.Text("AuthenticationUsage.Enabled", "&Enable usage queries"));
            Add(fields, UiLocalization.Text("AuthenticationUsage.Template", "Query &method"), _template);
            Add(fields, UiLocalization.Text("AuthenticationUsage.BaseUrl", "Query &URL"), _baseUrl);
            Add(fields, UiLocalization.Text("AuthenticationUsage.ApiKey", "API &key"), _apiKey);
            Check(fields, _clearApiKey, UiLocalization.Text("AuthenticationUsage.ClearApiKey", "Clear the saved key (&X)"));
            Add(fields, UiLocalization.Text("AuthenticationUsage.AccessToken", "Access &token"), _accessToken);
            Check(fields, _clearAccessToken, UiLocalization.Text("AuthenticationUsage.ClearAccessToken", "Clear the saved token (&L)"));
            Add(fields, UiLocalization.Text("AuthenticationUsage.UserId", "User &ID"), _userId);
            Add(fields, UiLocalization.Text("AuthenticationUsage.Timeout", "Time&out in seconds (2-30)"), _timeout);
            Add(fields, UiLocalization.Text("AuthenticationUsage.Interval", "Auto &refresh in minutes (0 turns it off)"), _interval);
            Add(fields, UiLocalization.Text("AuthenticationUsage.Code", "Query &script"), _code);
            foreach (var box in new[] { _baseUrl, _apiKey, _accessToken, _userId, _timeout, _interval }) UiTheme.ApplyTextBox(box, false, false);
            UiTheme.ApplyTextBox(_code, true, false);
            UiTheme.ApplyComboBox(_template);
            _template.Items.AddRange(AuthenticationUsagePresentation.Templates.Cast<object>().ToArray());
            _template.FormattingEnabled = true;
            _template.Format += (sender, args) => args.Value = AuthenticationUsagePresentation.TemplateName(args.ListItem as string);
            _template.SelectedIndexChanged += (sender, args) => UpdateFields();
            _clearApiKey.CheckedChanged += (sender, args) => { _apiKey.Enabled = !_clearApiKey.Checked; if (_clearApiKey.Checked) _apiKey.Clear(); };
            _clearAccessToken.CheckedChanged += (sender, args) => { _accessToken.Enabled = !_clearAccessToken.Checked; if (_clearAccessToken.Checked) _accessToken.Clear(); };
            // The test result is read here, so it stays reachable with Tab.
            _status = new ThemedTextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 110, TabIndex = 1,
                AccessibleName = UiLocalization.Text("AuthenticationUsage.Status", "Usage query status") };
            UiTheme.ApplyTextBox(_status, false, false);
            _save = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationUsage.Save", "S&ave and test"), 0);
            var close = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationUsage.Close", "&Close"), 1);
            close.DialogResult = DialogResult.Cancel;
            var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabStop = false, TabIndex = 2 };
            actions.Controls.AddRange(new Control[] { _save, close });
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, TabStop = false };
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(fields, 0, 0);
            root.Controls.Add(_status, 0, 1);
            root.Controls.Add(actions, 0, 2);
            Controls.Add(root);
            CancelButton = close;
            _save.Click += async (sender, args) => await SaveAsync();
            Display(settings);
            var hint = UiLocalization.Text("AuthenticationUsage.Hint", "An empty query URL or key uses the provider's own address and key.");
            _status.Text = settings.Configured || settings.Detected == null ? hint
                : string.Format(UiLocalization.Text("AuthenticationUsage.Recommended", "Recommended for this provider: {0}."),
                    AuthenticationUsagePresentation.TemplateName(settings.Detected)) + " " + hint;
            SetBusy(false);
            ActiveControl = _enabled;
        }

        // True once settings were stored, so the caller can refresh what it shows.
        public bool Saved { get; private set; }

        private void Add(TableLayoutPanel fields, string caption, Control control)
        {
            var row = fields.RowCount++;
            fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var label = new Label { Text = caption, AutoSize = true, TabStop = false, TabIndex = row * 2, Padding = new Padding(0, 6, 0, 0) };
            fields.Controls.Add(label, 0, row);
            control.Dock = DockStyle.Top;
            control.TabIndex = row * 2 + 1;
            control.AccessibleName = AuthenticationCenterUiText.Name(caption);
            fields.Controls.Add(control, 1, row);
            _captions[control] = label;
        }

        private void Check(TableLayoutPanel fields, CheckBox box, string caption)
        {
            box.Text = caption;
            UiTheme.ApplyCheckBox(box);
            Add(fields, string.Empty, box);
            box.AccessibleName = AuthenticationCenterUiText.Name(caption);
        }

        private void Display(AuthenticationUsageSettings settings)
        {
            _enabled.Checked = settings.Configured ? settings.Enabled : true;
            var template = settings.Configured ? settings.TemplateType : settings.Detected ?? "newapi";
            _template.SelectedItem = AuthenticationUsagePresentation.Templates.Contains(template) ? template : "newapi";
            _baseUrl.Text = settings.BaseUrl ?? string.Empty;
            _apiKey.Clear();
            _accessToken.Clear();
            _clearApiKey.Checked = _clearAccessToken.Checked = false;
            _userId.Text = settings.UserId ?? string.Empty;
            _timeout.Text = settings.Timeout.ToString(CultureInfo.InvariantCulture);
            _interval.Text = settings.AutoQueryInterval.ToString(CultureInfo.InvariantCulture);
            _code.Text = settings.Code ?? string.Empty;
            UpdateFields();
        }

        // Only the fields the selected method uses are shown, so Tab skips the rest.
        private void UpdateFields()
        {
            var template = _template.SelectedItem as string;
            var script = template != null && !AuthenticationUsagePresentation.IsNative(template);
            var key = template == "general" || template == "sub2api" || template == "custom";
            var token = template == "newapi" || template == "custom";
            SetVisible(_baseUrl, script);
            SetVisible(_apiKey, key);
            SetVisible(_clearApiKey, key && _settings?.HasApiKey == true);
            SetVisible(_accessToken, token);
            SetVisible(_clearAccessToken, token && _settings?.HasAccessToken == true);
            SetVisible(_userId, token);
            SetVisible(_timeout, script);
            SetVisible(_code, template == "custom");
        }

        private void SetVisible(Control control, bool visible)
        {
            control.Visible = visible;
            _captions[control].Visible = visible;
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _save.Enabled = !busy && _settings != null;
        }

        internal async Task SaveAsync()
        {
            if (_busy || _settings == null) return;
            var template = _template.SelectedItem as string;
            if (!int.TryParse(_timeout.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var timeout) || timeout < 2 || timeout > 30)
            {
                Invalid(UiLocalization.Text("AuthenticationUsage.Invalid.Timeout", "Enter a timeout from 2 to 30 seconds."), _timeout);
                return;
            }
            if (!int.TryParse(_interval.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var interval) || interval > 1440)
            {
                Invalid(UiLocalization.Text("AuthenticationUsage.Invalid.Interval", "Enter an auto refresh interval from 0 to 1440 minutes."), _interval);
                return;
            }
            if (template == "custom" && string.IsNullOrWhiteSpace(_code.Text))
            {
                Invalid(UiLocalization.Text("AuthenticationUsage.Invalid.Code", "Enter the query script."), _code);
                return;
            }
            var edit = new AuthenticationUsageEdit
            {
                ProviderId = _providerId,
                Enabled = _enabled.Checked,
                TemplateType = template,
                Timeout = timeout,
                AutoQueryInterval = interval
            };
            // Settings of hidden fields are not sent, so switching methods leaves no stray values.
            if (_baseUrl.Visible) edit.BaseUrl = _baseUrl.Text.Trim();
            if (_apiKey.Visible) { edit.ApiKey = _apiKey.Text.Trim(); edit.ClearApiKey = _clearApiKey.Checked; }
            if (_accessToken.Visible) { edit.AccessToken = _accessToken.Text.Trim(); edit.ClearAccessToken = _clearAccessToken.Checked; }
            if (_userId.Visible) edit.UserId = _userId.Text.Trim();
            if (_code.Visible) edit.Code = _code.Text;
            SetBusy(true);
            _status.Text = UiLocalization.Text("AuthenticationUsage.Saving", "Saving…");
            try
            {
                _settings = await _client.SaveUsageAsync(edit, _lifetime.Token);
                Saved = true;
                if (IsDisposed) return;
                Display(_settings);
                if (!_settings.Enabled)
                {
                    _status.Text = UiLocalization.Text("AuthenticationUsage.SavedDisabled", "Saved. Usage queries are turned off for this provider.");
                }
                else
                {
                    _status.Text = UiLocalization.Text("AuthenticationUsage.Testing", "Saved. Testing the query…");
                    var result = await _client.QueryUsageAsync(_providerId, _lifetime.Token);
                    if (IsDisposed) return;
                    _status.Text = (result.Success
                            ? UiLocalization.Text("AuthenticationUsage.TestPassed", "Saved. The test query succeeded:")
                            : UiLocalization.Text("AuthenticationUsage.TestFailed", "Saved, but the test query failed:"))
                        + Environment.NewLine + string.Join(Environment.NewLine, AuthenticationUsagePresentation.Lines(result));
                }
                _status.Focus();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (IsDisposed) return;
                _status.Text = AuthenticationCenterUiText.Error(error);
                AuthenticationCenterUiText.ShowError(this, _status.Text);
            }
            finally
            {
                if (!IsDisposed) SetBusy(false);
            }
        }

        private void Invalid(string text, Control field)
        {
            _status.Text = text;
            field.Focus();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
