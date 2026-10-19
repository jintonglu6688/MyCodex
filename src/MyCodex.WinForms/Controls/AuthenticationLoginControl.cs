using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    public sealed class AuthenticationLoginControl : UserControl
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly Action<string> _copyCode;
        private readonly Action<string> _openUrl;
        private readonly ThemedTextBox _code;
        private readonly ThemedTextBox _status;
        private readonly Button _start;
        private readonly Button _copy;
        private readonly Button _open;
        private readonly Button _cancel;
        private readonly Button _useSaved;
        private readonly Button _device;
        private bool _supportsBrowser;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 2000 };
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private AuthenticationDeviceLogin _login;
        private string _automaticLoginId;
        private int _generation;
        private bool _cancelling;
        private bool _polling;
        private bool _disposed;
        private bool _restoreSucceeded;

        public AuthenticationLoginControl(IAuthenticationCenterClient client, IScreenReaderService reader = null, string accountId = null)
            : this(client, reader, accountId, Clipboard.SetText, ExternalUrlLauncher.Open)
        {
        }

        internal AuthenticationLoginControl(IAuthenticationCenterClient client, IScreenReaderService reader, string accountId,
            Action<string> copyCode, Action<string> openUrl)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _copyCode = copyCode ?? throw new ArgumentNullException(nameof(copyCode));
            _openUrl = openUrl ?? throw new ArgumentNullException(nameof(openUrl));
            AccountId = accountId;
            AutoSize = true;
            Dock = DockStyle.Top;
            TabStop = true;
            _code = new ThemedTextBox { Dock = DockStyle.Fill, ReadOnly = true, TabIndex = 0,
                AccessibleName = UiLocalization.Text("AuthenticationAccounts.Code", "Device code") };
            _status = new ThemedTextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, Height = 70, TabStop = false, TabIndex = 1,
                AccessibleName = UiLocalization.Text("AuthenticationLogin.Status", "Sign-in status") };
            UiTheme.ApplyTextBox(_code, false, false);
            UiTheme.ApplyTextBox(_status, true, false);
            _start = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationLogin.Start", "Log &in / retry"), 0);
            _copy = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationAccounts.Copy", "Co&py code"), 1);
            _open = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationAccounts.Open", "&Open verification page"), 2);
            _cancel = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationLogin.Cancel", "Cancel sign-in (&T)"), 3);
            _useSaved = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationLogin.UseSaved", "Use saved acco&unt"), 4);
            _device = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationLogin.DeviceFallback", "Use device code (&5)"), 5);
            _device.Visible = false;
            _device.Click += async (sender, args) => await StartDeviceFallbackAsync();
            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, TabStop = false, TabIndex = 2 };
            actions.Controls.AddRange(new Control[] { _start, _copy, _open, _cancel, _useSaved, _device });
            var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, TabStop = false };
            layout.Controls.Add(_code);
            layout.Controls.Add(_status);
            layout.Controls.Add(actions);
            Controls.Add(layout);
            _start.Click += async (sender, args) => await StartLoginAsync();
            _copy.Click += (sender, args) => CopyCode();
            _open.Click += (sender, args) => OpenPage();
            _cancel.Click += async (sender, args) => await CancelLoginAsync();
            _useSaved.Click += async (sender, args) =>
            {
                if (IsBusy || _cancelling) return;
                if (IsLoginActive) await CancelLoginAsync();
                if (_disposed || IsLoginActive) return;
                using (var chooser = new AuthenticationAccountsForm(_client, null, selectAccount: true))
                {
                    if (chooser.ShowDialog(FindForm()) != DialogResult.OK) return;
                    AccountId = chooser.Selected.Id;
                    _login = null;
                    _code.Clear();
                    _timer.Stop();
                    Announce((chooser.Selected.Label ?? chooser.Selected.Email ?? AccountId) + ". " +
                        UiLocalization.Text("AuthenticationAccounts.Saved", "Saved"));
                    UpdateActions();
                }
            };
            _timer.Tick += async (sender, args) => await PollLoginAsync();
            UpdateActions();
        }

        public string AccountId { get; private set; }
        public string StatusText => _status.Text;
        public bool IsBusy { get; private set; }
        public bool IsLoginActive => _login != null && (_login.Status == "starting" || _login.Status == "pending" || _login.Status == "committing");
        public event EventHandler StateChanged;

        public async Task RestoreAsync()
        {
            if (_disposed || IsBusy || IsLoginActive || _cancelling) return;
            _restoreSucceeded = false;
            await RunAsync(async () =>
            {
                var snapshot = await _client.ReadAccountsAsync(_lifetime.Token);
                _supportsBrowser = snapshot.SupportsBrowserLogin;
                if (_disposed) return;
                if (!string.IsNullOrEmpty(AccountId) && !snapshot.Accounts.Any(account => account.Id == AccountId))
                    AccountId = null;
                if (snapshot.ActiveLogin == null)
                {
                    var lastError = snapshot.LastLogin?.Status == "failed" &&
                        string.Equals(AccountId, snapshot.LastLogin.TargetAccountId, StringComparison.Ordinal)
                        ? AuthenticationCenterUiText.Error(new AuthenticationCenterException(snapshot.LastLogin.Error)) : null;
                    if (lastError != null)
                    {
                        Announce(lastError);
                    }
                    foreach (var account in snapshot.Accounts)
                    {
                        if (account.Id != AccountId) continue;
                        Announce((account.Label ?? account.Email ?? account.Id) + ". " +
                            (lastError ?? (account.NeedsReauthentication ? UiLocalization.Text("AuthenticationAccounts.NeedsLogin", "Reauthentication needed") :
                            UiLocalization.Text("AuthenticationAccounts.Saved", "Saved"))));
                        break;
                    }
                    _restoreSucceeded = true;
                    return;
                }
                if (!string.Equals(AccountId, snapshot.ActiveLogin.TargetAccountId, StringComparison.Ordinal))
                    throw new AuthenticationCenterException("login_busy");
                _automaticLoginId = snapshot.ActiveLogin.LoginId;
                SetLogin(snapshot.ActiveLogin);
                _restoreSucceeded = true;
                if (IsLoginActive && _login.Method == "browser")
                    Announce(UiLocalization.Text("AuthenticationLogin.BrowserPending", "Complete authorization in the browser, then return here. You can reopen the sign-in page."));
                else if (IsLoginActive)
                    Announce(string.Format(UiLocalization.Text("AuthenticationAccounts.Pending", "Open {0} and enter the code above. Expires: {1}."),
                        snapshot.ActiveLogin.VerificationUri, snapshot.ActiveLogin.ExpiresAt));
            });
        }

        public Task StartLoginAsync() => StartLoginCoreAsync(false);

        internal async Task StartDeviceFallbackAsync()
        {
            if (!_supportsBrowser || IsBusy || _cancelling) return;
            if (IsLoginActive) await CancelLoginAsync();
            if (!IsLoginActive && !_disposed) await StartLoginCoreAsync(true);
        }

        private async Task StartLoginCoreAsync(bool device)
        {
            if (_disposed || IsBusy || IsLoginActive || _cancelling) return;
            await RestoreAsync();
            if (_disposed || IsBusy || IsLoginActive || _cancelling || !_restoreSucceeded) return;
            var generation = ++_generation;
            var id = Guid.NewGuid().ToString();
            SetLogin(new AuthenticationDeviceLogin { LoginId = id, Status = "starting", Method = _supportsBrowser && !device ? "browser" : "device" });
            await RunAsync(async () =>
            {
                var login = _supportsBrowser && !device
                    ? await _client.StartBrowserLoginAsync(AccountId, id, _lifetime.Token)
                    : await _client.StartDeviceLoginAsync(AccountId, id, _lifetime.Token);
                if (_disposed || generation != _generation)
                {
                    await CancelDetachedAsync(id);
                    return;
                }
                SetLogin(login);
            });
        }

        internal async Task PollLoginAsync()
        {
            if (_disposed || IsBusy || _polling || _cancelling || !IsLoginActive) return;
            var generation = _generation;
            var id = _login.LoginId;
            // Background reads must not disable actions and move keyboard focus.
            _polling = true;
            try
            {
                var login = await _client.ReadDeviceLoginAsync(id, _lifetime.Token);
                if (!_disposed && generation == _generation) SetLogin(login);
            }
            catch (AuthenticationCenterException error) when (error.Code == "login_not_found")
            {
                if (!_disposed && generation == _generation)
                    SetLogin(new AuthenticationDeviceLogin { LoginId = id, Status = "failed", Error = error.Code });
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (!_disposed && generation == _generation) AnnounceError(AuthenticationCenterUiText.Error(error), true);
            }
            finally { _polling = false; }
        }

        public async Task CancelLoginAsync()
        {
            if (_disposed || _cancelling || !IsLoginActive) return;
            _cancelling = true;
            ++_generation;
            UpdateActions();
            try
            {
                var login = await _client.CancelDeviceLoginAsync(_login.LoginId, _lifetime.Token);
                if (!_disposed) SetLogin(login);
            }
            catch (AuthenticationCenterException error) when (error.Code == "login_not_found")
            {
                if (!_disposed) SetLogin(new AuthenticationDeviceLogin { LoginId = _login.LoginId, Status = "cancelled" });
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_disposed) AnnounceError(AuthenticationCenterUiText.Error(error)); }
            finally
            {
                _cancelling = false;
                if (!_disposed) UpdateActions();
            }
        }

        private void SetLogin(AuthenticationDeviceLogin login)
        {
            if (_disposed) return;
            _login = login ?? throw new AuthenticationCenterException("invalid_response");
            _code.Text = IsLoginActive ? login.UserCode ?? string.Empty : string.Empty;
            _timer.Enabled = IsLoginActive;
            switch (login.Status)
            {
                case "starting": Announce(login.Method == "browser"
                    ? UiLocalization.Text("AuthenticationLogin.BrowserStarting", "Preparing browser sign-in...")
                    : UiLocalization.Text("AuthenticationAccounts.Starting", "Requesting a device code…")); break;
                case "pending":
                    if (login.Method == "browser")
                    {
                        if (_automaticLoginId != login.LoginId)
                        {
                            _automaticLoginId = login.LoginId;
                            OpenPage();
                        }
                        break;
                    }
                    if (_automaticLoginId != login.LoginId && _code.Text.Length != 0)
                    {
                        _automaticLoginId = login.LoginId;
                        if (CopyCode() && OpenPage())
                            Announce(UiLocalization.Text("AuthenticationLogin.Automatic", "Device code copied and verification page opened. Complete authorization in the browser, then return here."));
                    }
                    break;
                case "committing": Announce(UiLocalization.Text("AuthenticationAccounts.Committing", "Saving the authenticated account…")); break;
                case "completed":
                    if (string.IsNullOrWhiteSpace(login.Account?.Id)) throw new AuthenticationCenterException("invalid_response");
                    if (!string.IsNullOrEmpty(AccountId) && !string.Equals(AccountId, login.Account.Id, StringComparison.Ordinal))
                        throw new AuthenticationCenterException("identity_mismatch");
                    AccountId = login.Account.Id;
                    Announce((login.Account.Label ?? login.Account.Email ?? AccountId) + ". " + UiLocalization.Text("AuthenticationAccounts.Completed", "Sign-in succeeded and the account is archived. Click Save to finish the provider configuration. The active identity has not been switched."));
                    break;
                case "cancelled": Announce(UiLocalization.Text("AuthenticationAccounts.Cancelled", "Sign-in cancelled.")); break;
                case "expired": Announce(login.Method == "browser"
                    ? UiLocalization.Text("AuthenticationLogin.BrowserExpired", "Browser sign-in expired. Start sign-in again.")
                    : UiLocalization.Text("AuthenticationAccounts.Expired", "The device code expired. Start sign-in again.")); break;
                default: AnnounceError(AuthenticationCenterUiText.Error(new AuthenticationCenterException(login.Error)), true); break;
            }
            UpdateActions();
        }

        private bool CopyCode()
        {
            try
            {
                if (!IsLoginActive || string.IsNullOrEmpty(_code.Text)) return false;
                _copyCode(_code.Text);
                Announce(UiLocalization.Text("AuthenticationAccounts.Copied", "Device code copied."));
                return true;
            }
            catch (Exception)
            {
                Announce(UiLocalization.Text("AuthenticationLogin.CopyFailed", "Could not copy the device code. Select the code and copy it manually, then open the verification page."));
                return false;
            }
        }

        private bool OpenPage()
        {
            try
            {
                if (!IsLoginActive || (_login.Method == "browser"
                    ? !MyCodex.Application.Services.AuthenticationLoginUrl.IsBrowserAuthorization(_login.VerificationUri)
                    : _login.VerificationUri != "https://auth.openai.com/codex/device"))
                    throw new AuthenticationCenterException("invalid_response");
                _openUrl(_login.VerificationUri);
                Announce(_login.Method == "browser"
                    ? UiLocalization.Text("AuthenticationLogin.BrowserPending", "Complete authorization in the browser, then return here. You can reopen the sign-in page.")
                    : UiLocalization.Text("AuthenticationAccounts.Opened", "Verification page opened."));
                return true;
            }
            catch (Exception)
            {
                AnnounceError(_login.Method == "browser"
                    ? UiLocalization.Text("AuthenticationLogin.BrowserOpenFailed", "Could not open the browser. Retry Open verification page, or use device code sign-in.")
                    : UiLocalization.Text("AuthenticationLogin.OpenFailed", "Could not open the browser. Use Open verification page to retry, or visit https://auth.openai.com/codex/device manually."));
                return false;
            }
        }

        private async Task RunAsync(Func<Task> action)
        {
            IsBusy = true;
            UpdateActions();
            try { await action(); }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_disposed) AnnounceError(AuthenticationCenterUiText.Error(error)); }
            finally { IsBusy = false; if (!_disposed) UpdateActions(); }
        }

        private void UpdateActions()
        {
            _start.Enabled = !IsBusy && !_cancelling && !IsLoginActive;
            var browser = _login?.Method == "browser" || (_login == null && _supportsBrowser);
            _code.Visible = _copy.Visible = !browser;
            _code.TabStop = !browser;
            _device.Visible = _supportsBrowser;
            _device.Enabled = !IsBusy && !_cancelling && _login?.Status != "committing";
            _copy.Enabled = IsLoginActive && !string.IsNullOrEmpty(_code.Text);
            _open.Enabled = IsLoginActive && (browser || !string.IsNullOrEmpty(_code.Text));
            _cancel.Enabled = IsLoginActive && !_cancelling && _login.Status != "committing";
            _useSaved.Enabled = !IsBusy && !_cancelling;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Announce(string message)
        {
            if (_status.Text == message) return;
            _status.Text = message;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        // Polling reports the same failure repeatedly; once shows it a single time.
        private void AnnounceError(string message, bool once = false)
        {
            if (once && _status.Text == message) return;
            Announce(message);
            AuthenticationCenterUiText.ShowError(this, message);
        }

        private async Task CancelDetachedAsync(string id)
        {
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                    await _client.CancelDeviceLoginAsync(id, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                ++_generation;
                _timer.Dispose();
                if (IsLoginActive) _ = CancelDetachedAsync(_login.LoginId);
                _lifetime.Cancel();
                _lifetime.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
