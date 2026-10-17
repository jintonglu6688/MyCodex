using System.Collections;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient
    {
        public static IAuthenticationCenterClient CreateMcpManagementClient(string runtimeSource,
            WslAppServerConfigSnapshot wslConfig = null, RemoteMacSshConfigSnapshot remoteConfig = null)
        {
            if (string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
                return wslConfig == null ? null : CreateWsl(wslConfig);
            if (CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource))
                return remoteConfig == null ? null : CreateRemote(remoteConfig);
            return CreateLocal(runtimeSource);
        }

        public async Task<AuthenticationMcpSnapshot> ReadMcpAsync(CancellationToken cancellationToken)
        {
            var result = await _request("mcp/list", Empty(), cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrEmpty(Text(result, "version")) ||
                !result.TryGetValue("servers", out var items) || !(items is IEnumerable sequence))
                throw new AuthenticationCenterException("invalid_response");
            var snapshot = new AuthenticationMcpSnapshot { Version = Text(result, "version") };
            foreach (var item in sequence)
            {
                var row = item as IDictionary<string, object>;
                if (row == null || string.IsNullOrEmpty(Text(row, "id")) ||
                    !row.TryGetValue("config", out var config) || !(config is IDictionary<string, object> values) ||
                    !row.TryGetValue("enabled", out var enabled) || !(enabled is bool))
                    throw new AuthenticationCenterException("invalid_response");
                snapshot.Servers.Add(new AuthenticationMcpServer
                {
                    Id = Text(row, "id"), Enabled = (bool)enabled,
                    Config = new Dictionary<string, object>(values)
                });
            }
            return snapshot;
        }

        public async Task SaveMcpAsync(string serverId, IDictionary<string, object> config, bool enabled,
            string expectedVersion, CancellationToken cancellationToken)
        {
            var result = await _request("mcp/save", new Dictionary<string, object>
            {
                { "serverId", serverId }, { "config", config }, { "enabled", enabled }, { "expectedVersion", expectedVersion }
            }, cancellationToken).ConfigureAwait(false);
            if (!Flag(result, "saved")) throw new AuthenticationCenterException("operation_unknown");
        }

        public async Task DeleteMcpAsync(string serverId, string expectedVersion, CancellationToken cancellationToken)
        {
            var result = await _request("mcp/delete", new Dictionary<string, object>
            {
                { "serverId", serverId }, { "expectedVersion", expectedVersion }
            }, cancellationToken).ConfigureAwait(false);
            if (!Flag(result, "deleted")) throw new AuthenticationCenterException("operation_unknown");
        }

        public async Task<int> ImportMcpAsync(string expectedVersion, CancellationToken cancellationToken)
        {
            var result = await _request("mcp/import", new Dictionary<string, object>
            {
                { "expectedVersion", expectedVersion }
            }, cancellationToken).ConfigureAwait(false);
            if (!result.TryGetValue("imported", out var count) || !(count is int imported) || imported < 0)
                throw new AuthenticationCenterException("operation_unknown");
            return imported;
        }

    }
}
