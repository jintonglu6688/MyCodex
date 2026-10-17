using System;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;

namespace MyCodex.Application.Services
{
    public sealed class OfficeRuntimeScopeService
    {
        public OfficeRuntimeTarget ResolveTarget(AppConfigSnapshot config)
        {
            config = config ?? new AppConfigSnapshot();
            var runtimeSource = CodexRuntimeSources.CanonicalizeForPersistence(config.CodexRuntimeSource);
            if (string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                return new OfficeRuntimeTarget(
                    "wsl|" + NormalizeScopePart(config.WslAppServer.DistributionName),
                    AppServerHostPlatform.Wsl,
                    runtimeSource);
            }

            if (CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource))
            {
                var profile = config.GetSelectedRemoteUnixSshProfile();
                string scopeId;
                if (profile != null && !string.IsNullOrWhiteSpace(profile.Id))
                {
                    scopeId = "remote-unix|" + NormalizeScopePart(profile.Id);
                }
                else
                {
                    var ssh = config.GetSelectedRemoteUnixSshConfig() ?? new RemoteMacSshConfigSnapshot();
                    var port = ssh.Port > 0 && ssh.Port <= 65535
                        ? ssh.Port
                        : RemoteMacSshConfigSnapshot.DefaultPort;
                    scopeId = string.Join(
                        "|",
                        new[]
                        {
                            "remote-unix",
                            NormalizeScopePart(ssh.UserName),
                            NormalizeScopePart(ssh.HostName),
                            port.ToString()
                        });
                }

                return new OfficeRuntimeTarget(
                    scopeId,
                    AppServerHostPlatform.RemoteMac,
                    runtimeSource);
            }

            return new OfficeRuntimeTarget("windows", AppServerHostPlatform.Windows, runtimeSource);
        }

        private static string NormalizeScopePart(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "-"
                : value.Trim().ToLowerInvariant();
        }

    }
}
