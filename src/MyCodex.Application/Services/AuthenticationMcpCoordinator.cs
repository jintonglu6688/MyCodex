using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public sealed class AuthenticationMcpCoordinator
    {
        private readonly IAuthenticationCenterClient _client;
        private readonly IAppServerMcpClient _runtime;
        private readonly Func<IDisposable> _acquireTransition;
        private AuthenticationMcpSnapshot _snapshot;

        public AuthenticationMcpCoordinator(IAuthenticationCenterClient client, IAppServerMcpClient runtime,
            Func<IDisposable> acquireTransition)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _acquireTransition = acquireTransition ?? throw new ArgumentNullException(nameof(acquireTransition));
        }

        public bool ReloadPending { get; private set; }
        public bool CanWrite => _snapshot != null && !ReloadPending;
        public bool IsManaged(string id) => _snapshot?.Servers.Any(server => string.Equals(server.Id, id, StringComparison.Ordinal)) == true;

        public async Task<List<AppServerMcpServerStatus>> ReadAsync(CancellationToken token)
        {
            _snapshot = null;
            var snapshot = await _client.ReadMcpAsync(token);
            var runtime = await _runtime.ListMcpServerStatusesAsync();
            token.ThrowIfCancellationRequested();
            var result = (runtime ?? new List<AppServerMcpServerStatus>()).ToList();
            // Runtime-only entries (project/plugin/external global) remain visible but
            // cannot be edited through the managed store until explicitly imported.
            foreach (var server in result) server.ConfigValues = null;
            foreach (var archived in snapshot.Servers)
            {
                var server = result.FirstOrDefault(item => string.Equals(item.ServerName, archived.Id, StringComparison.Ordinal));
                if (server == null)
                {
                    server = new AppServerMcpServerStatus { ServerName = archived.Id };
                    result.Add(server);
                }
                server.Enabled = archived.Enabled;
                server.ConfigValues = new Dictionary<string, object>(archived.Config) { ["enabled"] = archived.Enabled };
                if (archived.Config.TryGetValue("required", out var required) && required is bool isRequired)
                    server.IsRequired = isRequired;
            }
            _snapshot = snapshot;
            return result;
        }

        public Task<bool> SaveAsync(string id, IDictionary<string, object> config, CancellationToken token)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var enabled = !config.TryGetValue("enabled", out var value) || !(value is bool flag) || flag;
            return MutateAsync(version => _client.SaveMcpAsync(id, config, enabled, version, token), token);
        }

        public Task<bool> DeleteAsync(string id, CancellationToken token)
        {
            if (!IsManaged(id)) throw new AuthenticationCenterException("mcp_not_found");
            return MutateAsync(version => _client.DeleteMcpAsync(id, version, token), token);
        }

        public Task<bool> WriteAsync(string id, IReadOnlyCollection<AppServerConfigWriteEdit> edits, CancellationToken token)
        {
            if (!CanWrite) throw new AuthenticationCenterException("version_conflict");
            var key = "mcp_servers." + id;
            if (edits == null || edits.Count == 0) throw new AuthenticationCenterException("invalid_params");
            if (edits.Count == 1 && edits.First()?.KeyPath == key && edits.First().Value == null && edits.First().MergeStrategy == "replace")
                return DeleteAsync(id, token);
            var previous = _snapshot.Servers.FirstOrDefault(server => string.Equals(server.Id, id, StringComparison.Ordinal));
            var config = previous == null ? new Dictionary<string, object>() : new Dictionary<string, object>(previous.Config);
            if (previous != null) config["enabled"] = previous.Enabled;
            foreach (var edit in edits)
            {
                if (edit == null || edit.MergeStrategy != "replace" ||
                    edit.KeyPath == null || !edit.KeyPath.StartsWith(key + ".", StringComparison.Ordinal))
                    throw new AuthenticationCenterException("invalid_params");
                var field = edit.KeyPath.Substring(key.Length + 1);
                if (field.Length == 0) throw new AuthenticationCenterException("invalid_params");
                if (edit.Value == null) config.Remove(field);
                else config[field] = edit.Value;
            }
            return SaveAsync(id, config, token);
        }

        public Task<bool> ImportAsync(CancellationToken token)
        {
            return MutateAsync(async version => { await _client.ImportMcpAsync(version, token); }, token);
        }

        private async Task<bool> MutateAsync(Func<string, Task> mutation, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!CanWrite) throw new AuthenticationCenterException("version_conflict");
            using (var lease = _acquireTransition())
            {
                if (lease == null) throw new AuthenticationCenterException("runtime_busy");
                var version = _snapshot.Version;
                _snapshot = null;
                try { await mutation(version); }
                catch (AuthenticationCenterException error) when (IsPrewriteRejection(error.Code)) { throw; }
                catch
                {
                    ReloadPending = true;
                    throw new AuthenticationCenterException("operation_unknown");
                }
                ReloadPending = true;
                return await ReloadCoreAsync();
            }
        }

        public async Task<bool> RetryReloadAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using (var lease = _acquireTransition())
            {
                if (lease == null) throw new AuthenticationCenterException("runtime_busy");
                return await ReloadCoreAsync();
            }
        }

        private async Task<bool> ReloadCoreAsync()
        {
            try
            {
                await _runtime.ReloadMcpServerConfigAsync();
                ReloadPending = false;
                return true;
            }
            catch { return false; }
        }

        private static bool IsPrewriteRejection(string code)
        {
            return code == "version_conflict" || code == "invalid_params" || code == "invalid_mcp_config" ||
                code == "mcp_not_found" || code == "route_requests_active" || code == "unsupported_target" ||
                code == "mcp_not_imported" ||
                code == "method_not_supported" || code == "backend_version_mismatch" || code == "backend_incompatible";
        }
    }
}
