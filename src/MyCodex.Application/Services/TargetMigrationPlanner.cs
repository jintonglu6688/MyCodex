using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;

namespace MyCodex.Application.Services
{
    public sealed class TargetMigrationPlanner
    {
        public TargetCatalogSnapshot CreateCatalog(AppConfigSnapshot config, AppStateSnapshot state)
        {
            config = config ?? new AppConfigSnapshot();
            state = state ?? new AppStateSnapshot();
            var catalog = new TargetCatalogSnapshot();
            Add(catalog, "Windows", "Windows", "windows");
            var runtimeService = new RuntimeProfileService();
            var activeScope = runtimeService.GetCurrentRuntimeProfileId(config);
            var wsl = config.WslAppServer;
            var wslScope = "wsl|" + wsl.DistributionName.Trim().ToLowerInvariant();
            if (string.Equals(activeScope, wslScope, StringComparison.OrdinalIgnoreCase) ||
                (state.RuntimeProfiles ?? new List<RuntimeProfileStateSnapshot>()).Any(p =>
                    p != null && string.Equals(p.RuntimeProfileId, wslScope, StringComparison.OrdinalIgnoreCase)) ||
                !string.Equals(wsl.DistributionName, WslAppServerConfigSnapshot.DefaultDistributionName, StringComparison.Ordinal) ||
                !string.Equals(wsl.CodexCommand, WslAppServerConfigSnapshot.DefaultCodexCommand, StringComparison.Ordinal))
            {
                Add(catalog, "Wsl", wsl.DistributionName, wslScope).WslDistributionName = wsl.DistributionName;
            }
            var savedScopes = (state.RuntimeProfiles ?? new List<RuntimeProfileStateSnapshot>())
                .Where(p => p != null).Select(p => p.RuntimeProfileId)
                .Concat((state.OfficeRuntimeStates ?? new List<OfficeRuntimeStateSnapshot>()).Where(p => p != null).Select(p => p.RuntimeScopeId))
                .Concat((config.RuntimeProfiles ?? new List<RuntimeProfileConfigSnapshot>()).Where(p => p != null).Select(p => p.RuntimeProfileId));
            foreach (var savedScope in savedScopes.Where(s => !string.IsNullOrWhiteSpace(s) &&
                s.StartsWith("wsl|", StringComparison.OrdinalIgnoreCase) && s.Substring(4) != "-"))
            {
                var distribution = savedScope.Substring(4).Trim();
                if (distribution.Length > 0)
                {
                    Add(catalog, "Wsl", distribution, savedScope).WslDistributionName = distribution;
                }
            }

            foreach (var profile in config.RemoteUnixSshProfiles.Where(p => p != null))
            {
                if (string.IsNullOrWhiteSpace(profile.Id) || string.IsNullOrWhiteSpace(profile.Ssh.HostName))
                {
                    continue;
                }
                Add(catalog, "Ssh", profile.Name ?? profile.Ssh.HostName,
                    "remote-unix|" + profile.Id.ToLowerInvariant()).SshProfileId = profile.Id;
            }

            if (CodexRuntimeSources.IsRemoteUnixSsh(config.CodexRuntimeSource) &&
                !catalog.Targets.Any(t => string.Equals(t.RuntimeScopeId, activeScope, StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(config.GetSelectedRemoteUnixSshConfig().HostName))
                {
                    throw new InvalidOperationException("The active SSH target has no saved host. Migration cannot select another target.");
                }
                Add(catalog, "Ssh", config.GetSelectedRemoteUnixSshConfig().HostName, activeScope);
            }

            catalog.LastOpenedTargetId = catalog.Targets.Single(t =>
                string.Equals(t.RuntimeScopeId, activeScope, StringComparison.OrdinalIgnoreCase)).Id;
            return catalog;
        }

        public void ConfigureTarget(AppConfigSnapshot config, TargetProfileSnapshot target, bool inheritsLegacyPreferences)
        {
            if (!inheritsLegacyPreferences)
            {
                config.MainWindowHotKeyEnabled = false;
                config.EnableAutomaticChatReading = false;
            }
            if (target.Kind == "Wsl")
            {
                config.CodexRuntimeSource = CodexRuntimeSources.Wsl;
                config.WslAppServer.DistributionName = target.WslDistributionName;
            }
            else if (target.Kind == "Ssh")
            {
                config.CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh;
                config.SelectedRemoteUnixSshProfileId = target.SshProfileId;
            }
            else if (CodexRuntimeSources.IsRemoteUnixSsh(config.CodexRuntimeSource) ||
                     string.Equals(config.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase))
            {
                config.CodexRuntimeSource = CodexRuntimeSources.Bundled;
            }
        }

        public AppConfigSnapshot CreateTargetDefaults(AppConfigSnapshot legacy, TargetProfileSnapshot target)
        {
            var config = new AppConfigSnapshot();
            if (target.Kind == "Wsl" && string.Equals(target.WslDistributionName, legacy.WslAppServer.DistributionName, StringComparison.OrdinalIgnoreCase))
            {
                config.WslAppServer.CodexCommand = legacy.WslAppServer.CodexCommand;
                config.WslAppServer.ShellCommand = legacy.WslAppServer.ShellCommand;
            }
            if (target.Kind == "Ssh")
            {
                config.RemoteUnixSshProfiles = legacy.RemoteUnixSshProfiles.Where(p => p != null && p.Id == target.SshProfileId).ToList();
                config.SelectedRemoteUnixSshProfileId = target.SshProfileId;
                config.RemoteMacSsh = target.SshProfileId == null ? legacy.RemoteMacSsh : config.RemoteUnixSshProfiles.Single().Ssh;
            }
            return config;
        }

        public static bool IsSafeTargetId(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && id.Length <= 80 &&
                id.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-');
        }

        private static TargetProfileSnapshot Add(TargetCatalogSnapshot catalog, string kind, string name, string scope)
        {
            var existing = catalog.Targets.FirstOrDefault(t =>
                string.Equals(t.RuntimeScopeId, scope, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }
            var target = new TargetProfileSnapshot { Id = GetTargetId(kind, scope), Name = name, Kind = kind, RuntimeScopeId = scope };
            catalog.Targets.Add(target);
            return target;
        }

        public static string GetTargetId(string kind, string scope)
        {
            if (kind != "Windows" && kind != "Wsl" && kind != "Ssh") throw new ArgumentException("Unknown target kind.", nameof(kind));
            if (string.IsNullOrWhiteSpace(scope)) throw new ArgumentException("A runtime scope is required.", nameof(scope));
            using (var hash = SHA256.Create())
            {
                return kind == "Windows" ? "windows" : kind.ToLowerInvariant() + "-" +
                    BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(scope.ToLowerInvariant()))).Replace("-", "").Substring(0, 24).ToLowerInvariant();
            }
        }
    }
}
