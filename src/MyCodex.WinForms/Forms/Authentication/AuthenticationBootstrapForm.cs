using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AuthenticationBootstrapForm : Form
    {
        private readonly IAppServerAuthenticationClient _authenticationClient;
        private readonly Action<string> _openExternalUrl;
        private readonly Label _statusLabel;
        private readonly TextBox _apiKeyTextBox;
        private readonly Button _chatGptButton;
        private readonly Button _deviceCodeButton;
        private readonly TextBox _deviceCodeTextBox;
        private readonly Button _apiKeyButton;
        private readonly Button _refreshButton;
        private readonly Button _cancelButton;
        private readonly Button _closeButton;
        private string _loginId;
        private Control _loginFocusControl;
        private bool _isBusy;

        public AuthenticationBootstrapForm(
            IAppServerAuthenticationClient authenticationClient,
            IScreenReaderService screenReaderService)
            : this(authenticationClient, screenReaderService, ExternalUrlLauncher.Open)
        {
        }

        internal AuthenticationBootstrapForm(
            IAppServerAuthenticationClient authenticationClient,
            IScreenReaderService screenReaderService,
            Action<string> openExternalUrl)
        {
            _authenticationClient = authenticationClient ?? throw new ArgumentNullException(nameof(authenticationClient));
            _openExternalUrl = openExternalUrl ?? throw new ArgumentNullException(nameof(openExternalUrl));

            Text = UiLocalization.Text("AuthenticationBootstrapForm.Title", "Sign in to Codex");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(520, 310);
            Size = new Size(600, 360);
            ShowInTaskbar = false;
            UiTheme.ApplyForm(this);

            _chatGptButton = CreateButton("AuthenticationBootstrapForm.Button.ChatGpt", "Sign in with &ChatGPT", "AuthenticationBootstrapForm.Accessible.ChatGpt", "Sign in with ChatGPT", 0);
            _deviceCodeButton = CreateButton("AuthenticationBootstrapForm.Button.DeviceCode", "Use &device code", "AuthenticationBootstrapForm.Accessible.DeviceCode", "Sign in with device code", 1);
            _deviceCodeTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("AuthenticationBootstrapForm.Accessible.DeviceCodeValue", "Device code"),
                ReadOnly = true,
                TabIndex = 2,
                TabStop = false,
                Visible = false,
                Width = 330
            };
            UiTheme.ApplyTextBox(_deviceCodeTextBox, false, false);
            _apiKeyTextBox = new TextBox
            {
                AccessibleName = UiLocalization.Text("AuthenticationBootstrapForm.Accessible.ApiKey", "OpenAI API key"),
                UseSystemPasswordChar = true,
                TabIndex = 3,
                Width = 330
            };
            UiTheme.ApplyTextBox(_apiKeyTextBox, false, false);
            _apiKeyButton = CreateButton("AuthenticationBootstrapForm.Button.ApiKey", "Use &API key", "AuthenticationBootstrapForm.Accessible.ApiKeySubmit", "Use API key", 4);
            _refreshButton = CreateButton("AuthenticationBootstrapForm.Button.Refresh", "&Check sign-in", "AuthenticationBootstrapForm.Accessible.Refresh", "Check sign-in", 5);
            _cancelButton = CreateButton("AuthenticationBootstrapForm.Button.CancelLogin", "Cancel sign-&in", "AuthenticationBootstrapForm.Accessible.CancelLogin", "Cancel sign-in", 6);
            _closeButton = CreateButton("AuthenticationBootstrapForm.Button.Close", "&Close", "AuthenticationBootstrapForm.Accessible.Close", "Close", 7);
            _closeButton.DialogResult = DialogResult.Cancel;
            CancelButton = _closeButton;

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("AuthenticationBootstrapForm.Accessible.Status", "Sign-in status"),
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            _chatGptButton.Click += OnChatGptButtonClick;
            _deviceCodeButton.Click += OnDeviceCodeButtonClick;
            _apiKeyButton.Click += OnApiKeyButtonClick;
            _refreshButton.Click += OnRefreshButtonClick;
            _cancelButton.Click += OnCancelButtonClick;
            BuildLayout();
            SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.Required", "Codex requires an OpenAI sign-in."));
            RefreshActionState();
            Shown += (sender, e) => _chatGptButton.Focus();
        }

        public AppServerAuthenticationState AuthenticationState { get; private set; }

        private Button CreateButton(string textKey, string textFallback, string accessibleNameKey, string accessibleNameFallback, int tabIndex)
        {
            var button = new Button
            {
                Text = UiLocalization.Text(textKey, textFallback),
                AccessibleName = UiLocalization.Text(accessibleNameKey, accessibleNameFallback),
                AutoSize = true,
                MinimumSize = new Size(120, 32),
                TabIndex = tabIndex,
                UseMnemonic = true
            };
            UiTheme.ApplyButton(button, UiGlyph.Account);
            return button;
        }

        private void BuildLayout()
        {
            var mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(UiTheme.OuterPadding),
                ColumnCount = 2,
                RowCount = 6,
                TabStop = false
            };
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var instruction = new Label
            {
                Text = UiLocalization.Text("AuthenticationBootstrapForm.Instruction", "Choose an official sign-in method. MyCodex does not save your credentials."),
                AutoSize = true,
                MaximumSize = new Size(500, 0),
                TabStop = false
            };
            mainPanel.SetColumnSpan(instruction, 2);
            mainPanel.Controls.Add(instruction, 0, 0);
            mainPanel.Controls.Add(_chatGptButton, 0, 1);
            mainPanel.Controls.Add(_deviceCodeButton, 1, 1);
            mainPanel.SetColumnSpan(_deviceCodeTextBox, 2);
            mainPanel.Controls.Add(_deviceCodeTextBox, 0, 2);
            mainPanel.Controls.Add(_apiKeyTextBox, 0, 3);
            mainPanel.Controls.Add(_apiKeyButton, 1, 3);
            mainPanel.SetColumnSpan(_statusLabel, 2);
            mainPanel.Controls.Add(_statusLabel, 0, 4);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                Dock = DockStyle.Right,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                TabStop = false
            };
            buttons.Controls.Add(_refreshButton);
            buttons.Controls.Add(_cancelButton);
            buttons.Controls.Add(_closeButton);
            mainPanel.SetColumnSpan(buttons, 2);
            mainPanel.Controls.Add(buttons, 0, 5);
            Controls.Add(mainPanel);
        }

        private async void OnChatGptButtonClick(object sender, EventArgs e)
        {
            await StartLoginAsync(AuthenticationLoginKind.Browser, null);
        }

        private async void OnDeviceCodeButtonClick(object sender, EventArgs e)
        {
            await StartLoginAsync(AuthenticationLoginKind.DeviceCode, null);
        }

        private async void OnApiKeyButtonClick(object sender, EventArgs e)
        {
            var apiKey = _apiKeyTextBox.Text;
            _apiKeyTextBox.Clear();
            await StartLoginAsync(AuthenticationLoginKind.ApiKey, apiKey);
        }

        private async System.Threading.Tasks.Task StartLoginAsync(AuthenticationLoginKind loginKind, string apiKey)
        {
            if (_isBusy)
            {
                return;
            }

            if (loginKind == AuthenticationLoginKind.ApiKey && string.IsNullOrWhiteSpace(apiKey))
            {
                SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.ApiKeyRequired", "Enter an OpenAI API key."));
                _apiKeyTextBox.Focus();
                return;
            }

            _isBusy = true;
            RefreshActionState();
            SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.Starting", "Starting official sign-in..."));
            try
            {
                AppServerLoginResult result;
                if (loginKind == AuthenticationLoginKind.Browser)
                {
                    _loginFocusControl = _chatGptButton;
                    result = await _authenticationClient.StartChatGptLoginAsync();
                }
                else if (loginKind == AuthenticationLoginKind.DeviceCode)
                {
                    _loginFocusControl = _deviceCodeButton;
                    result = await _authenticationClient.StartChatGptDeviceCodeLoginAsync();
                }
                else
                {
                    _loginFocusControl = _apiKeyButton;
                    result = await _authenticationClient.StartOpenAiApiKeyLoginAsync(apiKey);
                }

                _loginId = result == null ? null : result.LoginId;
                if (loginKind == AuthenticationLoginKind.Browser)
                {
                    try
                    {
                        _openExternalUrl(result == null ? null : result.AuthUrl);
                    }
                    catch
                    {
                        SetStatus(UiLocalization.Text(
                            "AuthenticationBootstrapForm.Status.BrowserOpenFailed",
                            "The sign-in page could not be opened. Check your default browser, then retry or cancel sign-in."));
                        return;
                    }
                }
                else if (loginKind == AuthenticationLoginKind.DeviceCode)
                {
                    _deviceCodeTextBox.Text = result == null ? string.Empty : result.UserCode ?? string.Empty;
                    _deviceCodeTextBox.Visible = true;
                    _deviceCodeTextBox.TabStop = true;
                    _deviceCodeTextBox.Focus();
                    _deviceCodeTextBox.SelectAll();
                    var verificationUrl = result == null ? string.Empty : result.VerificationUrl ?? string.Empty;
                    var instructions = string.Format(
                        CultureInfo.CurrentCulture,
                        UiLocalization.Text(
                            "AuthenticationBootstrapForm.Status.DeviceCodeStarted.Template",
                            "Open {1} and enter code {0}."),
                        _deviceCodeTextBox.Text,
                        verificationUrl);
                    try
                    {
                        _openExternalUrl(verificationUrl);
                    }
                    catch
                    {
                        SetStatus(string.Format(
                            CultureInfo.CurrentCulture,
                            UiLocalization.Text(
                                "AuthenticationBootstrapForm.Status.DeviceCodeBrowserOpenFailed.Template",
                                "Open {1} and enter code {0}; the browser could not be opened automatically."),
                            _deviceCodeTextBox.Text,
                            verificationUrl));
                        return;
                    }

                    SetStatus(instructions);
                }

                if (loginKind == AuthenticationLoginKind.Browser)
                {
                    SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.BrowserStarted", "Continue sign-in in the browser, then check sign-in."));
                }
                else if (loginKind == AuthenticationLoginKind.ApiKey)
                {
                    SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.ApiKeySubmitted", "API key submitted. Checking sign-in..."));
                }
                await RefreshAuthenticationStateAsync(true);
            }
            catch
            {
                SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.Failed", "Sign-in could not be started."));
            }
            finally
            {
                _isBusy = false;
                RefreshActionState();
            }
        }

        private async void OnRefreshButtonClick(object sender, EventArgs e)
        {
            await RefreshAuthenticationStateAsync();
        }

        private async System.Threading.Tasks.Task RefreshAuthenticationStateAsync(bool loginAlreadyRunning = false)
        {
            if (_isBusy && !loginAlreadyRunning)
            {
                return;
            }

            var wasBusy = _isBusy;
            _isBusy = true;
            RefreshActionState();
            try
            {
                AuthenticationState = await _authenticationClient.GetAuthenticationStateAsync(true);
                if (AuthenticationState != null && AuthenticationState.HasAccount)
                {
                    SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.Completed", "Sign-in completed."));
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
                }

                SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.Pending", "Sign-in is still pending."));
            }
            catch
            {
                SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.CheckFailed", "Unable to check sign-in status."));
            }
            finally
            {
                _isBusy = wasBusy;
                RefreshActionState();
            }
        }

        private async void OnCancelButtonClick(object sender, EventArgs e)
        {
            if (_isBusy || string.IsNullOrWhiteSpace(_loginId))
            {
                return;
            }

            _isBusy = true;
            RefreshActionState();
            var canceled = false;
            try
            {
                await _authenticationClient.CancelLoginAsync(_loginId);
                _loginId = null;
                canceled = true;
                SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.Canceled", "Sign-in canceled."));
            }
            catch
            {
                SetStatus(UiLocalization.Text("AuthenticationBootstrapForm.Status.CancelFailed", "Unable to cancel sign-in."));
            }
            finally
            {
                _isBusy = false;
                RefreshActionState();
                (canceled ? _loginFocusControl ?? _chatGptButton : _cancelButton).Focus();
            }
        }

        private void RefreshActionState()
        {
            _chatGptButton.Enabled = !_isBusy;
            _deviceCodeButton.Enabled = !_isBusy;
            _apiKeyTextBox.Enabled = !_isBusy;
            _apiKeyButton.Enabled = !_isBusy;
            _refreshButton.Enabled = !_isBusy;
            _cancelButton.Enabled = !_isBusy && !string.IsNullOrWhiteSpace(_loginId);
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private enum AuthenticationLoginKind
        {
            Browser,
            DeviceCode,
            ApiKey
        }
    }
}
