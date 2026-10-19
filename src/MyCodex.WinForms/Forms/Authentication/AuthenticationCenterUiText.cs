using System;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal static class AuthenticationCenterUiText
    {
        // Errors stay in the status line and also open a standard error box, which screen readers read on display without competing speech.
        internal static Action<IWin32Window, string, string> ErrorPresenter = (owner, text, caption) =>
            MessageBox.Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.Error);

        internal static void ShowError(Control owner, string text)
        {
            if (string.IsNullOrWhiteSpace(text) || owner == null || owner.IsDisposed)
            {
                return;
            }
            var form = owner.FindForm();
            var caption = Name(form?.Text ?? string.Empty);
            ErrorPresenter(form ?? (IWin32Window)owner, text, string.IsNullOrEmpty(caption) ? AppConstants.AppName : caption);
        }

        internal static string Name(string caption)
        {
            var index = caption.LastIndexOf("(&", StringComparison.Ordinal);
            return (index >= 0 ? caption.Substring(0, index) : caption).Replace("&", string.Empty).Trim();
        }

        internal static Button Button(string caption, int tabIndex)
        {
            var button = new Button
            {
                Text = caption, AccessibleName = Name(caption), TabIndex = tabIndex,
                AutoSize = true, MinimumSize = new System.Drawing.Size(88, UiTheme.ButtonHeight),
                Margin = new Padding(4), UseMnemonic = true
            };
            UiTheme.ApplyButton(button, UiGlyph.Command);
            return button;
        }

        internal static string Kind(string kind)
        {
            return kind == "chatgpt"
                ? UiLocalization.Text("AuthenticationCenter.Kind.ChatGpt", "ChatGPT account")
                : kind == "official_api"
                ? UiLocalization.Text("AuthenticationCenter.Kind.Official", "Official OpenAI API")
                : kind == "responses"
                    ? UiLocalization.Text("AuthenticationCenter.Kind.Responses", "Third-party Responses API")
                    : kind == "chat_completions"
                        ? UiLocalization.Text("AuthenticationCenter.Kind.ChatCompletions", "Third-party Chat Completions API")
                        : kind == "anthropic"
                            ? UiLocalization.Text("AuthenticationCenter.Kind.Anthropic", "Third-party Anthropic Messages API")
                            : UiLocalization.Text("AuthenticationCenter.Kind.Other", "Other provider");
        }

        // Format choices name the connection too; provider lists show it in a separate column.
        internal static string FormatChoice(string kind)
        {
            return kind == "chat_completions"
                ? UiLocalization.Text("AuthenticationProviderEdit.Format.ChatCompletions", "Chat Completions (protocol conversion)")
                : kind == "anthropic"
                    ? UiLocalization.Text("AuthenticationProviderEdit.Format.Anthropic", "Anthropic (protocol conversion)")
                    : UiLocalization.Text("AuthenticationProviderEdit.Format.Responses", "Responses (direct)");
        }

        internal static string Connection(string kind)
        {
            return kind == "chat_completions" || kind == "anthropic"
                ? UiLocalization.Text("AuthenticationCenter.Connection.Converted", "Protocol conversion")
                : UiLocalization.Text("AuthenticationCenter.Connection.Direct", "Direct");
        }

        internal static string Error(Exception exception)
        {
            var code = (exception as AuthenticationCenterException)?.Code;
            switch (code)
            {
                case "incomplete_account_credentials":
                    return UiLocalization.Text("AuthenticationCenter.Error.CaptureAccount", "The current account has incomplete or inconsistent file credentials. It was not archived. Sign in again to save this account.");
                case "external_proxy_unavailable":
                    return UiLocalization.Text("AuthenticationCenter.Error.CaptureProxy", "The current external proxy does not expose enough upstream configuration to archive it safely. Add its actual provider settings manually.");
                case "capture_incomplete":
                    return UiLocalization.Text("AuthenticationCenter.Error.CaptureIncomplete", "Automatic capture did not finish. Existing credentials and configurations were retained. Refresh the authentication center to retry.");
                case "invalid_live_config":
                    return UiLocalization.Text("AuthenticationCenter.Error.CaptureConfig", "The current global configuration is incomplete or invalid. Correct it before retrying automatic capture; saved configurations were retained.");
                case "credential_reference_unavailable":
                    return UiLocalization.Text("AuthenticationCenter.Error.CaptureReference", "The current configuration references an unavailable credential environment variable and cannot be archived automatically. Add a configuration with an API key, or select a saved configuration, then choose Enable. The old configuration will be backed up automatically before switching; no environment changes are needed.");
                case "credential_recovery_confirmation_required":
                    return UiLocalization.Text("AuthenticationCenter.Error.RecoveryConfirmation", "Save the provider without global configuration edits, then select Enable. You can edit global settings after switching.");
                case "config_backup_failed":
                    return UiLocalization.Text("AuthenticationCenter.Error.RecoveryBackup", "The old configuration could not be backed up. It was not replaced. Check access to the authentication data directory and try enabling again.");
                case "unsupported_auth_source":
                case "external_profile_unsupported":
                case "unsupported_api_format":
                    return UiLocalization.Text("AuthenticationCenter.Error.SyncUnsupported", "This global configuration cannot be synchronized automatically. Review its authentication source and provider settings, or add a configuration manually.");
                case "auth_backend_unsupported":
                case "unsupported_auth_store":
                case "external_auth_unsupported":
                    return UiLocalization.Text("AuthenticationCenter.Error.AuthBackend", "This authentication backend cannot be safely switched. File-based authentication is required; keyring and externally managed tokens are not migrated automatically.");
                case "auth_ownership_conflict":
                case "credential_generation_conflict":
                case "account_identity_ambiguous":
                case "unrecognized_account_identity":
                case "invalid_live_auth":
                case "recovery_required":
                    return UiLocalization.Text("AuthenticationCenter.Error.CredentialsChanged", "The current credentials or their ownership could not be safely reconciled. They were retained. Refresh and review the current identity before retrying; do not restore an older credential backup.");
                case "official_provider_override":
                    return UiLocalization.Text("AuthenticationCenter.Error.OfficialOverride", "The built-in OpenAI provider has a custom override. Review and remove that override before enabling a ChatGPT account.");
                case "startup_registration_failed":
                    return UiLocalization.Text("AuthenticationCenter.Error.Startup", "Windows sign-in recovery could not be registered. Check your user startup permissions before enabling conversion.");
                case "stack_official_requires_unified_history":
                    return UiLocalization.Text("Aggregation.Error.OfficialNeedsUnified", "A ChatGPT account can be the aggregation default only after Unify Codex session history is turned on. It also stays on while that account is the default.");
                case "stack_official_cannot_join":
                    return UiLocalization.Text("Aggregation.Error.OfficialCannotJoin", "A ChatGPT account can only be the aggregation default; it cannot be added as another provider.");
                case "stack_default_cannot_be_removed":
                    return UiLocalization.Text("Aggregation.Error.DefaultCannotLeave", "The default provider cannot leave aggregation. Make another provider the default first.");
                case "stack_member_rejected":
                    return UiLocalization.Text("Aggregation.Error.MemberRejected", "A selected provider cannot be added to aggregation. Check its configuration and try again.");
                case "stack_already_active":
                case "stack_not_active":
                case "stack_enter_failed":
                case "stack_exit_failed":
                case "stack_operation_failed":
                case "stack_read_failed":
                    return UiLocalization.Text("Aggregation.Error.Failed", "The aggregation change did not finish. Refresh to check the current state before trying again.");
                case "route_busy":
                case "route_requests_active":
                    return UiLocalization.Text("AuthenticationCenter.Error.RouteBusy", "Conversion requests are still running. Wait for them to finish before switching or updating; no request was stopped.");
                case "login_in_progress":
                    return UiLocalization.Text("AuthenticationCenter.Error.LoginBusy", "A sign-in is still in progress. Finish it before updating the backend; no account was changed.");
                case "route_required":
                    return UiLocalization.Text("AuthenticationCenter.Error.RouteRequired", "The active configuration requires this route. Switch to a direct provider before stopping or uninstalling the backend.");
                case "route_bind_failed":
                case "route_restore_failed":
                case "route_start_failed":
                case "route_stop_failed":
                case "route_state_invalid":
                    return UiLocalization.Text("AuthenticationCenter.Error.RouterUnavailable", "The current configuration requires conversion, but its route is unavailable. Refresh or switch to a direct provider.");
                case "route_option_unsupported":
                case "official_requires_native_responses":
                case "route_request_unsupported":
                    return UiLocalization.Text("AuthenticationCenter.Error.RouteCapability", "This conversion route does not support the selected option or request capability. Edit the configuration before enabling it.");
                case "unsupported":
                case "unsupported_target":
                case "unsupported_platform":
                    return UiLocalization.Text("AuthenticationCenter.Error.Unsupported", "This execution target is not supported yet.");
                case "backend_unavailable":
                case "backend_not_found":
                case "host_not_found":
                    return UiLocalization.Text("AuthenticationCenter.Error.Unavailable", "The authentication backend is unavailable. Check its installation and try Refresh.");
                case "backend_missing":
                case "backend_manifest_missing":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendMissing", "The authentication backend package is missing. For a development build, prepare the backend and rebuild MyCodex using the authentication center testing guide, then restart the development app.");
                case "wsl_backend_missing":
                    return UiLocalization.Text("AuthenticationCenter.Error.WslBackendMissing", "The WSL authentication backend is not installed in this distribution. Install the matching Linux host there, then refresh.");
                case "remote_backend_missing":
                    return UiLocalization.Text("AuthenticationCenter.Error.RemoteBackendMissing", "The authentication backend is not installed for this SSH target. Install the matching Unix host there, then refresh.");
                case "backend_manifest_invalid":
                case "backend_integrity_failed":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendIntegrity", "The authentication backend package is incomplete or damaged. Prepare or reinstall the matching package, then restart MyCodex. No authentication operation was sent.");
                case "backend_incompatible":
                case "backend_version_mismatch":
                case "protocol_mismatch":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendVersion", "The authentication backend version does not match this app or the running backend. Finish any sign-in or switching work, then restart with the matching backend package. The existing backend was not stopped.");
                case "backend_update_available":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendUpdateAvailable", "This target has an older authentication backend. Choose Update backend to install the version required by MyCodex.");
                case "backend_download_failed":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendDownload", "The authentication backend could not be downloaded from GitCode. Check the connection and retry Refresh. Existing files were kept.");
                case "backend_dependencies_missing":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendDependencies", "The downloaded backend cannot run on this target. Install its required system libraries or use a supported target, then retry. Existing files were kept.");
                case "invalid_codex_home":
                    return UiLocalization.Text("AuthenticationCenter.Error.CodexHome", "This target's Codex configuration directory was not found. Set up Codex CLI on the target before retrying.");
                case "backend_install_unsafe":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendUnsafe", "The target's backend directory has unsafe permissions or unexpected files. Automatic installation stopped without overwriting them; inspect that directory before retrying.");
                case "backend_install_busy":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendInstallBusy", "Another backend installation is in progress on this target. Wait for it to finish, then refresh.");
                case "editor_conflict":
                    return UiLocalization.Text("AuthenticationCenter.Error.EditorConflict", "Settings you edited were changed by another program while the editor was open. Nothing was saved. Save again to choose which changes to keep.");
                case "store_busy":
                    return UiLocalization.Text("AuthenticationCenter.Error.StoreBusy", "An earlier backend process on this target still holds its data but can no longer be reached. End the mycodex-auth-host process on the target, or restart the target computer, then refresh.");
                case "backend_probe_failed":
                case "backend_install_failed":
                case "backend_update_unsafe":
                    return UiLocalization.Text("AuthenticationCenter.Error.BackendInstall", "The target backend could not be installed safely. Check the target and retry; existing credentials and configuration were kept.");
                case "version_conflict":
                case "route_live_changed_externally":
                case "live_conflict":
                case "config_conflict":
                    return UiLocalization.Text("AuthenticationCenter.Error.Conflict", "The configuration changed. Close this editor and refresh before editing again.");
                case "current_provider":
                case "provider_is_current":
                case "provider_in_use":
                    return UiLocalization.Text("AuthenticationCenter.Error.Current", "The current provider cannot be deleted.");
                case "api_key_required":
                case "invalid_api_key":
                    return UiLocalization.Text("AuthenticationCenter.Error.Key", "Check the API key and try again.");
                case "invalid_base_url":
                    return UiLocalization.Text("AuthenticationCenter.Error.Url", "Enter a valid API base URL without embedded credentials or a query string.");
                case "invalid_config":
                    return UiLocalization.Text("AuthenticationConfig.Invalid", "Invalid TOML configuration. Check syntax and field types, then save again.");
                case "invalid_params":
                case "invalid_models":
                    return UiLocalization.Text("AuthenticationCenter.Error.Fields", "Check the provider name, model and API settings.");
                case "model_fetch_failed":
                    return UiLocalization.Text("AuthenticationCenter.Error.Models", "Models could not be retrieved. Check the address and authentication, or add a model manually. Existing models have been retained.");
                case "account_not_found":
                case "reauthentication_required":
                case "reauth_required":
                case "account_unavailable":
                case "identity_mismatch":
                    return UiLocalization.Text("AuthenticationCenter.Error.Account", "The saved account could not be used. Sign in again with the account bound to this configuration.");
                case "credential_target_mismatch":
                    return UiLocalization.Text("AuthenticationCenter.Error.CredentialTarget", "The address or protocol changed. Enter the API key for this endpoint before retrieving models.");
                case "route_not_ready":
                    return UiLocalization.Text("AuthenticationCenter.Error.Route", "This configuration is saved. Protocol conversion will be available in a later development stage.");
                case "runtime_busy":
                    return UiLocalization.Text("AuthenticationCenter.Error.Busy", "Work or a configuration operation is active. Finish it before applying a provider; no work was stopped.");
                case "operation_unknown":
                case "save_outcome_unknown":
                    return UiLocalization.Text("AuthenticationCenter.Error.UnknownOperation", "The application result could not be confirmed. Check the global configuration before starting another switch.");
                case "account_in_use":
                    return UiLocalization.Text("AuthenticationAccounts.Error.InUse", "This account is active or referenced by a provider. Its archive cannot be deleted.");
                case "account_write_failed":
                    return UiLocalization.Text("AuthenticationCenter.Error.AccountDeleteFailed", "The account credentials could not be removed. The configuration was kept; refresh and try again.");
                case "account_delete_recovery_required":
                    return UiLocalization.Text("AuthenticationCenter.Error.AccountDeleteRecovery", "Deletion did not finish safely. Retry the connection, then refresh and review the configuration, account, and current sign-in.");
                case "duplicate_account":
                case "account_already_exists":
                case "account_exists":
                case "login_conflict":
                    return UiLocalization.Text("AuthenticationAccounts.Error.Conflict", "This account or sign-in already exists. Refresh the account list before trying again.");
                case "login_not_found":
                    return UiLocalization.Text("AuthenticationAccounts.Error.MissingLogin", "This sign-in is no longer available. Start sign-in again.");
                case "account_store_invalid":
                    return UiLocalization.Text("AuthenticationAccounts.Error.Store", "Saved account data could not be read. It has not been overwritten.");
                case "login_network_failed":
                    return UiLocalization.Text("AuthenticationAccounts.Error.Network", "The authentication service could not be reached. Check the connection and start sign-in again.");
                case "login_token_exchange_failed":
                    return UiLocalization.Text("AuthenticationLogin.ExchangeFailed", "Authorization was received, but the account credentials could not be retrieved. Check the network connection and sign in again.");
                case "login_refresh_token_missing":
                case "login_identity_missing":
                    return UiLocalization.Text("AuthenticationLogin.IncompleteIdentity", "The authentication service returned incomplete account information. No account was archived; sign in again.");
                case "authorization_denied":
                case "browser_callback_unavailable":
                    return UiLocalization.Text("AuthenticationLogin.CallbackUnavailable", "The local sign-in callback port is unavailable. Close another browser sign-in attempt and retry, or use device code sign-in.");
                case "login_denied":
                    return UiLocalization.Text("AuthenticationAccounts.Error.Denied", "Authorization was denied. No account was saved.");
                case "login_busy":
                    return UiLocalization.Text("AuthenticationAccounts.Error.Busy", "A sign-in is already running. Refresh to view or cancel it.");
                case "login_limit":
                    return UiLocalization.Text("AuthenticationAccounts.Error.Limit", "The backend has reached its retained sign-in limit. Restart the authentication backend before starting another sign-in.");
                default:
                    return UiLocalization.Text("AuthenticationCenter.Error.Failed", "The operation could not be completed. Refresh to check the current state before trying again.");
            }
        }

        internal static string LiveStatus(AuthenticationCenterSnapshot snapshot, string readyStatus)
        {
            string status;
            switch (snapshot.LiveState)
            {
                case null:
                case "":
                case "current":
                    status = readyStatus;
                    break;
                case "signed_out":
                    status = UiLocalization.Text("AuthenticationCenter.Status.SignedOut", "The current target is not signed in. Saved configurations remain available.");
                    break;
                default:
                    status = UiLocalization.Text("AuthenticationCenter.Status.LiveUnavailable", "The current global configuration could not be confirmed. Saved configurations can still be edited.");
                    break;
            }
            var error = SyncErrorText(snapshot);
            if (error != null)
            {
                status += Environment.NewLine + error;
            }
            return status;
        }

        // Automatic capture of the global configuration failed; null when it succeeded.
        internal static string SyncErrorText(AuthenticationCenterSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(snapshot.SyncError))
            {
                return null;
            }
            switch (snapshot.SyncError)
            {
                case "config_conflict":
                    return UiLocalization.Text("AuthenticationCenter.Error.SyncChanged", "The global configuration changed while it was being read. Refresh to check its current state.");
                case "account_write_failed":
                case "database_failed":
                case "provider_write_failed":
                    return UiLocalization.Text("AuthenticationCenter.Error.SyncSave", "The global authentication could not be saved in the authentication center. Refresh to retry.");
                case "unsupported_auth_source":
                case "external_profile_unsupported":
                case "unsupported_protocol":
                case "unsupported_api_format":
                    return UiLocalization.Text("AuthenticationCenter.Error.SyncUnsupported", "This global configuration cannot be synchronized automatically. Review its authentication source and provider settings, or add a configuration manually.");
                default:
                    return Error(new AuthenticationCenterException(snapshot.SyncError));
            }
        }
    }
}
