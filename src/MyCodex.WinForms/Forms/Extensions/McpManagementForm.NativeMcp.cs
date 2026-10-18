using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class McpManagementForm
    {
        private readonly AuthenticationMcpCoordinator _nativeMcp;
        private Button _importNativeMcpButton;
        private Button _reloadNativeMcpButton;

        private void InitializeNativeMcpActions()
        {
            _importNativeMcpButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Native.Import", "Import global settings (&I)"),
                UiLocalization.Text("McpManagementForm.Native.ImportName", "Import global settings"), 8);
            _reloadNativeMcpButton = CreateButton(
                UiLocalization.Text("McpManagementForm.Native.Reload", "Retry loading MCP (&L)"),
                UiLocalization.Text("McpManagementForm.Native.ReloadName", "Retry loading MCP"), 9);
            _importNativeMcpButton.Visible = _nativeMcp != null;
            _reloadNativeMcpButton.Visible = false;
            _closeButton.TabIndex = 10;
            UiTheme.ApplyButton(_importNativeMcpButton, UiGlyph.Refresh);
            UiTheme.ApplyButton(_reloadNativeMcpButton, UiGlyph.Refresh);
            _importNativeMcpButton.Click += async (sender, args) => await ImportNativeMcpAsync();
            _reloadNativeMcpButton.Click += async (sender, args) => await RetryNativeMcpAsync();
            FormClosing += (sender, args) => { if (_nativeMcp != null && _isBusy) args.Cancel = true; };
        }

        private string NativeMcpHint()
        {
            return _nativeMcp?.ReloadPending == true
                ? UiLocalization.Text("McpManagementForm.Native.ReloadPending", "MCP changes may already be saved. Refresh to review them, then retry loading MCP. Do not repeat the change.")
                : UiLocalization.Text("McpManagementForm.Native.Hint", "Managed MCP settings are kept when switching providers. Use Import global settings for manually registered servers; existing managed parameters are retained. Other runtime entries are read-only.");
        }

        internal async Task ImportNativeMcpAsync()
        {
            if (_nativeMcp == null || !_nativeMcp.CanWrite || _isBusy) return;
            await RunNativeMcpMutationAsync(() => _nativeMcp.ImportAsync(_lifetimeCancellation.Token),
                UiLocalization.Text("McpManagementForm.Native.Imported", "Import completed. Existing managed parameters were retained; review the server list."), GetSelectedServerName());
        }

        private async Task RetryNativeMcpAsync()
        {
            if (_nativeMcp == null || !_nativeMcp.ReloadPending || _isBusy) return;
            await RunNativeMcpMutationAsync(() => _nativeMcp.RetryReloadAsync(_lifetimeCancellation.Token),
                UiLocalization.Text("McpManagementForm.Native.Reloaded", "MCP configuration loaded."), GetSelectedServerName());
            if (!IsDisposed && !Disposing) _serverListView.Focus();
        }

        private async Task RunNativeMcpMutationAsync(Func<Task<bool>> mutation, string success, string selection)
        {
            if (_isBusy) return;
            var restoreFocus = _serverListView.ContainsFocus;
            SetBusy(true);
            string outcome;
            try
            {
                SetStatus(UiLocalization.Text("McpManagementForm.Native.Working", "Updating managed MCP settings..."));
                outcome = await mutation() ? success : NativeMcpHint();
            }
            catch (Exception error)
            {
                var code = (error as AuthenticationCenterException)?.Code;
                outcome = code == "operation_unknown" ? NativeMcpHint()
                    : code == "version_conflict" || code == "invalid_mcp_config" || code == "mcp_not_found" || code == "mcp_not_imported"
                    ? UiLocalization.Text("McpManagementForm.Native.Review", "The MCP configuration changed or is invalid. Refresh and review it before trying again.")
                    : AuthenticationCenterUiText.Error(error);
            }
            finally { SetBusy(false); }
            if (IsDisposed || Disposing) return;
            _preferredServerName = selection;
            await LoadServerStatusesAsync(true);
            SetStatus(outcome);
            if (restoreFocus && !IsDisposed) _serverListView.Focus();
        }

        private void RefreshNativeMcpActions()
        {
            if (_nativeMcp == null || _importNativeMcpButton == null) return;
            var selected = GetSelectedServer();
            var writable = !_isBusy && _nativeMcp.CanWrite;
            var managed = writable && selected != null && _nativeMcp.IsManaged(selected.ServerName);
            _addButton.Enabled = _addMenuItem.Enabled = writable;
            _settingsButton.Enabled = _settingsMenuItem.Enabled = managed;
            _deleteButton.Enabled = _deleteMenuItem.Enabled = managed && selected.IsRequired != true;
            _enableButton.Enabled = _enableMenuItem.Enabled = managed && selected.Enabled != true;
            _disableButton.Enabled = _disableMenuItem.Enabled = managed && selected.Enabled != false;
            _importNativeMcpButton.Enabled = writable;
            _reloadNativeMcpButton.Visible = _nativeMcp.ReloadPending;
            _reloadNativeMcpButton.Enabled = !_isBusy && _nativeMcp.ReloadPending;
        }
    }
}
