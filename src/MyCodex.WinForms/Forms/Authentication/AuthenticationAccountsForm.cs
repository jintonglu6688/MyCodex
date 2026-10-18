using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class AuthenticationAccountsForm : Form
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly Func<string, CancellationToken, Task<bool>> _delete;
        private readonly ListView _accounts;
        private readonly Label _status;
        private readonly Button _deleteButton;
        private readonly Button _refresh;
        private readonly Button _select;
        private readonly ContextMenu _menu;
        private readonly MenuItem _deleteItem;
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private bool _busy;
        private bool _disposed;

        public AuthenticationAccountsForm(IAuthenticationCenterClient client, Func<string, CancellationToken, Task<bool>> delete, bool selectAccount = false)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _delete = delete;
            Text = UiLocalization.Text("AuthenticationSavedAccounts.Title", "Saved accounts");
            AccessibleName = Text;
            Size = new Size(720, 420);
            MinimumSize = new Size(600, 340);
            StartPosition = FormStartPosition.CenterParent;
            UiTheme.ApplyForm(this);
            _accounts = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, TabIndex = 0, AccessibleName = Text
            };
            UiTheme.ApplyListView(_accounts);
            _accounts.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.Account", "Account"), 430);
            _accounts.Columns.Add(UiLocalization.Text("AuthenticationCenter.Column.State", "State"), 170);
            _status = new Label { AutoSize = true, Dock = DockStyle.Fill, TabStop = false };
            _deleteButton = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.Delete", "&Delete"), 0);
            _deleteButton.Visible = !selectAccount;
            _select = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationSavedAccounts.Select", "&Use selected account"), 0);
            _select.Visible = selectAccount;
            _refresh = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.Refresh", "&Refresh"), 1);
            FormInteractionConventions.ConfigureRefreshButton(_refresh);
            var close = AuthenticationCenterUiText.Button(UiLocalization.Text("AuthenticationCenter.Button.Close", "&Close"), 2);
            close.DialogResult = DialogResult.Cancel;
            CancelButton = close;
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, TabStop = false, TabIndex = 2 };
            buttons.Controls.AddRange(new Control[] { _select, _deleteButton, _refresh, close });
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12), TabStop = false };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_accounts, 0, 0);
            layout.Controls.Add(_status, 0, 1);
            layout.Controls.Add(buttons, 0, 2);
            Controls.Add(layout);
            _menu = new ContextMenu();
            _deleteItem = new MenuItem(_deleteButton.Text);
            _menu.MenuItems.Add(_deleteItem);
            _accounts.ContextMenu = _menu;
            _menu.Popup += (sender, args) => UpdateActions();
            _accounts.SelectedIndexChanged += (sender, args) => UpdateActions();
            _deleteItem.Click += async (sender, args) => await DeleteSelectedAsync();
            _deleteButton.Click += async (sender, args) => await DeleteSelectedAsync();
            _select.Click += (sender, args) =>
            {
                if (SelectedAccount == null || SelectedAccount.NeedsReauthentication || _busy) return;
                Selected = SelectedAccount;
                DialogResult = DialogResult.OK;
            };
            if (selectAccount) AcceptButton = _select;
            _refresh.Click += async (sender, args) => await RefreshAsync();
            Shown += async (sender, args) => { await RefreshAsync(); if (!IsDisposed) _accounts.Focus(); };
            FormClosing += (sender, args) => { if (_busy) args.Cancel = true; };
            KeyPreview = true;
            KeyDown += async (sender, args) =>
            {
                if (args.KeyCode != Keys.F5) return;
                args.SuppressKeyPress = true;
                await RefreshAsync();
            };
            UpdateActions();
        }

        public bool ReconnectPending { get; private set; }
        public AuthenticationAccount Selected { get; private set; }

        internal async Task RefreshAsync()
        {
            if (_busy || _disposed) return;
            var selectedId = SelectedAccount?.Id;
            var restoreListFocus = _accounts.ContainsFocus;
            _busy = true;
            UpdateActions();
            try
            {
                var snapshot = await _client.ReadAccountsAsync(_lifetime.Token);
                if (_disposed) return;
                _accounts.Items.Clear();
                foreach (var account in snapshot.Accounts)
                {
                    var item = new ListViewItem(new[] { account.Label ?? account.Email ?? account.Id,
                        account.NeedsReauthentication ? UiLocalization.Text("AuthenticationAccounts.NeedsLogin", "Reauthentication needed")
                        : UiLocalization.Text("AuthenticationAccounts.Saved", "Saved") }) { Tag = account };
                    _accounts.Items.Add(item);
                    if (account.Id == selectedId) item.Selected = item.Focused = true;
                }
                if (_accounts.SelectedItems.Count == 0 && _accounts.Items.Count > 0)
                    _accounts.Items[0].Selected = _accounts.Items[0].Focused = true;
                _status.Text = UiLocalization.Text("AuthenticationSavedAccounts.Hint", "Deleting a provider keeps its saved account. Accounts can be removed here.");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!_disposed) _status.Text = AuthenticationCenterUiText.Error(error); }
            finally
            {
                _busy = false;
                if (!_disposed)
                {
                    UpdateActions();
                    if (restoreListFocus) _accounts.Focus();
                }
            }
        }

        private AuthenticationAccount SelectedAccount => _accounts.SelectedItems.Count == 1 ? _accounts.SelectedItems[0].Tag as AuthenticationAccount : null;

        private async Task DeleteSelectedAsync()
        {
            var account = SelectedAccount;
            if (_busy || account == null || _delete == null || ReconnectPending) return;
            if (MessageBox.Show(this, UiLocalization.Text("AuthenticationSavedAccounts.ConfirmDelete", "Remove this saved account? Its matching global sign-in may also be cleared. Providers using it will need to sign in again."), Text,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _busy = true;
            UpdateActions();
            try
            {
                ReconnectPending = !await _delete(account.Id, _lifetime.Token);
                _busy = false;
                await RefreshAsync();
                if (!_disposed && ReconnectPending)
                    _status.Text = UiLocalization.Text("AuthenticationSavedAccounts.Reconnect", "Account removed. Close this window and retry the connection in Authentication Center.");
            }
            catch (Exception error)
            {
                var code = (error as AuthenticationCenterException)?.Code;
                ReconnectPending = code == "operation_unknown";
                if (!_disposed) _status.Text = AuthenticationCenterUiText.Error(error);
            }
            finally { _busy = false; if (!_disposed) { UpdateActions(); _accounts.Focus(); } }
        }

        private void UpdateActions()
        {
            _accounts.Enabled = _refresh.Enabled = !_busy;
            _deleteButton.Enabled = _deleteItem.Enabled = !_busy && !ReconnectPending && _delete != null && SelectedAccount != null;
            _select.Enabled = !_busy && SelectedAccount != null && !SelectedAccount.NeedsReauthentication;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                _lifetime.Cancel();
                _lifetime.Dispose();
                _menu.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
