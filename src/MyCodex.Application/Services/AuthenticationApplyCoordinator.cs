using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public sealed class AuthenticationApplyCoordinator
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly Func<IDisposable> _acquireTransition;
        private readonly Func<Task<bool>> _reconnect;
        private readonly Func<Task<AppServerAuthenticationState>> _readAuthentication;
        private readonly Func<Task<AppServerConfigSnapshot>> _readConfig;

        public AuthenticationApplyCoordinator(IAuthenticationCenterClient client, Func<IDisposable> acquireTransition, Func<Task<bool>> reconnect,
            Func<Task<AppServerAuthenticationState>> readAuthentication = null,
            Func<Task<AppServerConfigSnapshot>> readConfig = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _acquireTransition = acquireTransition ?? throw new ArgumentNullException(nameof(acquireTransition));
            _reconnect = reconnect ?? throw new ArgumentNullException(nameof(reconnect));
            _readAuthentication = readAuthentication;
            _readConfig = readConfig;
        }

        public async Task<AuthenticationApplyResult> ApplyAsync(AuthenticationProvider provider, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var lease = _acquireTransition())
            {
                if (lease == null)
                {
                    throw new AuthenticationCenterException("runtime_busy");
                }
                await CheckAuthenticationAsync();
                var plan = await _client.PreflightAsync(provider, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var operationId = Guid.NewGuid().ToString("N");
                AuthenticationOperation operation;
                try
                {
                    operation = await _client.ApplyAsync(plan, operationId, cancellationToken);
                }
                catch (AuthenticationCenterException error) when (error.Code == "version_conflict" || error.Code == "config_conflict" || error.Code == "route_requests_active" ||
                    error.Code == "provider_not_found" || error.Code == "invalid_params" || error.Code == "config_backup_failed" ||
                    error.Code == "credential_recovery_confirmation_required" || error.Code == "invalid_live_config")
                {
                    // These checks run before the native switch begins.
                    throw;
                }
                catch (Exception original)
                {
                    try
                    {
                        // A cancelled or lost reply does not prove that the global write failed.
                        operation = await _client.GetOperationAsync(operationId, CancellationToken.None);
                    }
                    catch (AuthenticationCenterException queryError) when (queryError.Code == "operation_not_found")
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(original).Throw();
                        throw;
                    }
                    catch
                    {
                        throw new AuthenticationCenterException("operation_unknown");
                    }
                }
                if (operation.OperationId != operationId || operation.ProviderId != provider.Id || operation.Status != "applied")
                {
                    throw new AuthenticationCenterException("operation_unknown");
                }
                return new AuthenticationApplyResult { GlobalApplied = true, Reconnected = await TryReconnectAsync(), OperationId = operationId };
            }
        }

        public async Task<AuthenticationSaveResult> SaveAsync(AuthenticationProviderEdit edit, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IDisposable lease;
            try
            {
                lease = _acquireTransition();
            }
            catch (AuthenticationCenterException busy) when (busy.Code == "runtime_busy" && edit.AllowWhileBusy)
            {
                // Running work keeps its configuration: store the row only, without reconnecting.
                lease = null;
            }
            if (lease == null && !edit.AllowWhileBusy) throw new AuthenticationCenterException("runtime_busy");
            using (lease)
            {
                edit.InactiveOnly = lease == null;
                if (lease != null) await CheckAuthenticationAsync();
                AuthenticationProvider saved;
                try
                {
                    saved = await _client.SaveAsync(edit, cancellationToken);
                }
                catch (Exception error) when (IsUncertainWrite(error))
                {
                    // No retry: the upstream service may have already written live files.
                    throw new AuthenticationCenterException("operation_unknown");
                }
                return new AuthenticationSaveResult
                {
                    Provider = saved,
                    Reconnected = !saved.GlobalApplied || (lease != null && await TryReconnectAsync())
                };
            }
        }

        // Moving sessions runs inside the transition, so no turn writes the files being rewritten.
        public async Task<AuthenticationSessionHistoryResult> SetSessionHistoryAsync(bool unified, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var lease = _acquireTransition())
            {
                if (lease == null) throw new AuthenticationCenterException("runtime_busy");
                AuthenticationSessionHistoryResult result;
                try
                {
                    result = await _client.SetSessionHistoryAsync(unified, cancellationToken);
                }
                catch (Exception error) when (IsUncertainWrite(error))
                {
                    throw new AuthenticationCenterException("operation_unknown");
                }
                // Codex rereads the route and the moved sessions only after a restart;
                // a failed move may still have rewritten some of them.
                var changed = result.GlobalApplied || Touched(result.Moved);
                result.Reconnected = !changed || await TryReconnectAsync();
                return result;
            }
        }

        private static bool Touched(AuthenticationSessionHistoryMove move)
        {
            return move != null && (move.Moved || move.Error != null);
        }

        private static bool IsUncertainWrite(Exception error)
        {
            var code = (error as AuthenticationCenterException)?.Code;
            return code == null || code == "backend_unavailable" || code == "invalid_response" ||
                code == "operation_unknown" || code == "save_outcome_unknown" || code == "ipc_disconnected" || code == "ipc_timeout";
        }

        public async Task<bool> DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var lease = _acquireTransition())
            {
                if (lease == null) throw new AuthenticationCenterException("runtime_busy");
                await CheckAuthenticationAsync();
                try { await _client.DeleteAccountAsync(accountId, cancellationToken); }
                catch (AuthenticationCenterException error) when (error.Code == "account_not_found" || error.Code == "invalid_params" || error.Code == "route_requests_active")
                {
                    throw;
                }
                catch
                {
                    // Removal can clear live credentials before its archive write fails.
                    throw new AuthenticationCenterException("operation_unknown");
                }
                return await TryReconnectAsync();
            }
        }

        private async Task<bool> TryReconnectAsync()
        {
            try { return await _reconnect(); }
            catch { return false; }
        }

        private async Task CheckAuthenticationAsync()
        {
            if (_readAuthentication != null)
            {
                // Read without refreshing tokens while the transition excludes other writers.
                var state = await _readAuthentication();
                if (state == null || !IsFileCompatibleMode(state.AccountType) || !IsFileCompatibleMode(state.AuthMode))
                {
                    throw new AuthenticationCenterException("auth_backend_unsupported");
                }
            }
            if (_readConfig != null)
            {
                var config = await _readConfig();
                if (config == null || (!string.IsNullOrWhiteSpace(config.AuthCredentialsStore) &&
                    !string.Equals(config.AuthCredentialsStore, "file", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new AuthenticationCenterException("auth_backend_unsupported");
                }
            }
        }

        private static bool IsFileCompatibleMode(string mode)
        {
            return string.IsNullOrWhiteSpace(mode) ||
                string.Equals(mode, "chatgpt", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "apikey", StringComparison.OrdinalIgnoreCase);
        }
    }
}
