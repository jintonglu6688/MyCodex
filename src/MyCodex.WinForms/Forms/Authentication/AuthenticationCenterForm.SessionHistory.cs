using System;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // Unified Codex session history for the current execution target. Turning it on moves the
    // earlier official sessions to the shared identity; turning it off moves them back.
    public sealed partial class AuthenticationCenterForm
    {
        private readonly FlowLayoutPanel _sessionHistoryRow;
        private readonly CheckBox _sessionHistoryCheckBox;
        private readonly Button _sessionHistoryRetryButton;
        private AuthenticationSessionHistoryState _sessionHistory;

        // Replaces the confirmation dialog in tests.
        internal Func<string, string, bool> ConfirmSessionHistory { get; set; }

        private bool SessionHistoryOffered
        {
            get { return _supportsSessionHistory && _setSessionHistory != null; }
        }

        private static CheckBox CreateSessionHistoryCheckBox()
        {
            var caption = UiLocalization.Text("SessionHistory.Unified", "Unify Codex session &history");
            var checkBox = new CheckBox
            {
                Text = caption, AccessibleName = AuthenticationCenterUiText.Name(caption),
                AccessibleDescription = UiLocalization.Text("SessionHistory.Description", "When on, ChatGPT accounts use the same Codex session identity as third-party providers, so earlier conversations continue with the configuration in use after you switch. This applies to the current execution target only. Off by default."),
                AutoCheck = false, AutoSize = true, TabIndex = 0, UseMnemonic = true
            };
            UiTheme.ApplyCheckBox(checkBox);
            return checkBox;
        }

        // Returns a note for the status line when the setting needs attention.
        private async Task<string> ReadSessionHistoryStateAsync()
        {
            _sessionHistory = null;
            if (!SessionHistoryOffered) return null;
            try
            {
                _sessionHistory = await _client.ReadSessionHistoryAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                // The providers still load; only the session history setting is unavailable.
                return AuthenticationCenterUiText.Error(error);
            }
            return SessionHistoryPendingText(_sessionHistory);
        }

        private void UpdateSessionHistoryControls()
        {
            var available = !_busy && _loaded && !_reconnectPending && _sessionHistory != null;
            var pending = _sessionHistory != null && _sessionHistory.Pending;
            _sessionHistoryRow.Visible = SessionHistoryOffered;
            _sessionHistoryCheckBox.Checked = _sessionHistory != null && _sessionHistory.Unified;
            _sessionHistoryCheckBox.Enabled = available;
            _sessionHistoryRetryButton.Visible = pending;
            _sessionHistoryRetryButton.Enabled = available && pending;
        }

        internal async Task ToggleSessionHistoryAsync()
        {
            if (_busy || !_loaded || _reconnectPending || _sessionHistory == null || !SessionHistoryOffered) return;
            var enable = !_sessionHistory.Unified;
            var previousStatus = _statusLabel.Text;
            AuthenticationSessionHistoryPreview preview = null;
            _busy = true;
            UpdateActions();
            Announce(UiLocalization.Text("SessionHistory.Status.Counting", "Counting sessions…"));
            try
            {
                preview = await _client.PreviewSessionHistoryAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (!IsDisposed) Announce(AuthenticationCenterUiText.Error(error));
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    UpdateActions();
                    _sessionHistoryCheckBox.Focus();
                }
            }
            if (preview == null || IsDisposed) return;
            var confirm = ConfirmSessionHistory ?? ShowSessionHistoryConfirmation;
            if (!confirm(SessionHistoryConfirmTitle(enable), SessionHistoryConfirmMessage(enable, enable ? preview.ToMigrate : preview.ToRestore)))
            {
                Announce(previousStatus);
                return;
            }
            await ApplySessionHistoryAsync(enable);
        }

        // Setting the same value again finishes an interrupted move.
        internal async Task RetrySessionHistoryAsync()
        {
            if (_busy || !_loaded || _reconnectPending || _sessionHistory == null || !_sessionHistory.Pending) return;
            await ApplySessionHistoryAsync(_sessionHistory.Unified);
        }

        private async Task ApplySessionHistoryAsync(bool unified)
        {
            _busy = true;
            UpdateActions();
            Announce(UiLocalization.Text("SessionHistory.Status.Working", "Applying…"));
            try
            {
                var result = await _setSessionHistory(unified, _lifetime.Token);
                if (IsDisposed) return;
                _sessionHistory = result.State;
                _reconnectPending |= !result.Reconnected;
                _busy = false;
                // The ChatGPT account's live route may have changed; show the state Codex now uses.
                await RefreshAsync();
                if (!IsDisposed && _loaded) Announce(SessionHistoryResultText(result));
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                {
                    if (error is AuthenticationCenterException failure && failure.Code == "operation_unknown")
                        _reconnectPending = true;
                    Announce(AuthenticationCenterUiText.Error(error));
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    UpdateActions();
                    if (_sessionHistoryRetryButton.Visible && _sessionHistoryRetryButton.Enabled) _sessionHistoryRetryButton.Focus();
                    else if (_sessionHistoryCheckBox.Enabled) _sessionHistoryCheckBox.Focus();
                }
            }
        }

        private bool ShowSessionHistoryConfirmation(string title, string message)
        {
            using (var form = new AuthenticationSessionHistoryConfirmForm(title, message))
            {
                return form.ShowDialog(this) == DialogResult.OK;
            }
        }

        private static string SessionHistoryConfirmTitle(bool enable)
        {
            return enable
                ? UiLocalization.Text("SessionHistory.Enable.Title", "Unify Codex session history")
                : UiLocalization.Text("SessionHistory.Disable.Title", "Turn off unified session history");
        }

        private static string SessionHistoryConfirmMessage(bool enable, int count)
        {
            var message = enable
                ? UiLocalization.Text("SessionHistory.Enable.Message", "ChatGPT accounts will use the shared session identity. After a switch, earlier conversations continue with the configuration in use.\r\n\r\nSessions created earlier with a ChatGPT account move to the shared identity too. Only one field in each session changes, so nothing is copied; the official ChatGPT client stops listing them until this is turned off. MyCodex lists them either way.\r\n\r\nWhen a ChatGPT account continues a conversation another provider wrote, MyCodex first removes what ChatGPT cannot read, such as that provider's reasoning.\r\n\r\nClose other Codex clients, such as the ChatGPT desktop app, first.")
                : UiLocalization.Text("SessionHistory.Disable.Message", "ChatGPT accounts will use their own session identity again, and the sessions moved earlier get it back. Turns added meanwhile are kept.\r\n\r\nSessions created while this was on keep the shared identity. Afterwards they continue only with third-party providers, and the official ChatGPT client does not list them.\r\n\r\nClose other Codex clients, such as the ChatGPT desktop app, first.");
            if (count <= 0) return message;
            return message + "\r\n\r\n" + string.Format(CultureInfo.CurrentCulture, enable
                ? UiLocalization.Text("SessionHistory.Enable.Count", "{0} earlier official sessions will move.")
                : UiLocalization.Text("SessionHistory.Disable.Count", "{0} moved sessions will get their official identity back."), count);
        }

        private static string SessionHistoryPendingText(AuthenticationSessionHistoryState state)
        {
            if (state == null || !state.Pending) return null;
            return state.Unified
                ? UiLocalization.Text("SessionHistory.Status.Pending", "Unified session history is on, but moving the earlier official sessions did not finish; retry.")
                : UiLocalization.Text("SessionHistory.Status.RestorePending", "Unified session history is off, but moving the sessions back did not finish; retry.");
        }

        private static string SessionHistoryResultText(AuthenticationSessionHistoryResult result)
        {
            var unified = result.State.Unified;
            var text = unified
                ? UiLocalization.Text("SessionHistory.Result.Enabled", "Unified session history is on.")
                : UiLocalization.Text("SessionHistory.Result.Disabled", "Unified session history is off.");
            var move = result.Moved;
            if (move != null && move.Error != null)
            {
                text += " " + (unified
                    ? UiLocalization.Text("SessionHistory.Result.MoveFailed", "Moving sessions did not finish. Nothing was deleted; close other Codex clients and retry.")
                    : UiLocalization.Text("SessionHistory.Result.RestoreFailed", "Moving sessions back did not finish. Nothing was deleted; close other Codex clients and retry."));
            }
            else if (move != null && move.Sessions > 0)
            {
                text += " " + string.Format(CultureInfo.CurrentCulture, unified
                    ? UiLocalization.Text("SessionHistory.Result.Moved", "Moved {0} sessions.")
                    : UiLocalization.Text("SessionHistory.Result.Restored", "Restored {0} sessions."), move.Sessions);
            }
            if (!result.Reconnected)
                text += " " + UiLocalization.Text("AuthenticationCenter.Status.ReconnectFailed", "Global configuration is applied, but MyCodex could not reconnect. Retry the connection; the global change has not been rolled back.");
            return text;
        }
    }
}
