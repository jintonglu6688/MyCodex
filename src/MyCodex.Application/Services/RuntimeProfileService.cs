using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;

namespace MyCodex.Application.Services
{
    public sealed class RuntimeProfileService
    {
        public const string WindowsRuntimeProfileId = "windows";
        private const string WslRuntimeProfilePrefix = "wsl";
        private const string RemoteUnixRuntimeProfilePrefix = "remote-unix";

        public string GetCurrentRuntimeProfileId(AppConfigSnapshot appConfigSnapshot)
        {
            if (appConfigSnapshot != null &&
                string.Equals(appConfigSnapshot.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                var wslConfig = appConfigSnapshot.WslAppServer ?? new WslAppServerConfigSnapshot();
                return string.Join(
                    "|",
                    new[]
                    {
                        WslRuntimeProfilePrefix,
                        NormalizeRuntimeProfilePart(wslConfig.DistributionName)
                    });
            }

            if (appConfigSnapshot != null &&
                CodexRuntimeSources.IsRemoteUnixSsh(appConfigSnapshot.CodexRuntimeSource))
            {
                var remoteProfile = appConfigSnapshot.GetSelectedRemoteUnixSshProfile();
                if (remoteProfile != null && !string.IsNullOrWhiteSpace(remoteProfile.Id))
                {
                    return string.Join(
                        "|",
                        new[]
                        {
                            RemoteUnixRuntimeProfilePrefix,
                            NormalizeRuntimeProfilePart(remoteProfile.Id)
                        });
                }

                var remoteConfig = appConfigSnapshot.GetSelectedRemoteUnixSshConfig() ?? new RemoteMacSshConfigSnapshot();
                return string.Join(
                    "|",
                    new[]
                    {
                        RemoteUnixRuntimeProfilePrefix,
                        NormalizeRuntimeProfilePart(remoteConfig.UserName),
                        NormalizeRuntimeProfilePart(remoteConfig.HostName),
                        NormalizeRuntimeProfilePart(remoteConfig.Port.ToString())
                    });
            }

            return WindowsRuntimeProfileId;
        }

        public string NormalizeRuntimeProfileId(string runtimeProfileId)
        {
            return string.IsNullOrWhiteSpace(runtimeProfileId)
                ? WindowsRuntimeProfileId
                : runtimeProfileId.Trim();
        }

        public bool IsSameRuntimeProfile(string left, string right)
        {
            return string.Equals(
                NormalizeRuntimeProfileId(left),
                NormalizeRuntimeProfileId(right),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeRuntimeProfilePart(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "-"
                : value.Trim().ToLowerInvariant();
        }

    }
}
