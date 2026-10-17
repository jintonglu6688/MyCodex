using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;

namespace MyCodex.Application.Services
{
    public sealed class TargetRuntimeSettingsPlanner
    {
        public TargetRuntimeSettings Build(TargetProfileSnapshot target, AppConfigSnapshot config)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (config == null) throw new ArgumentNullException(nameof(config));
            var settings = new TargetRuntimeSettings
            {
                Name = target.Name,
                CodexRuntimeSource = config.CodexRuntimeSource,
                CodexCustomExecutablePath = config.CodexCustomExecutablePath,
                WindowsShellPreference = config.WindowsShellPreference,
                WslAppServer = CloneWsl(config.WslAppServer),
                RemoteSsh = CloneSsh(GetSsh(target, config))
            };
            using (var stream = new MemoryStream())
            using (var hash = SHA256.Create())
            {
                new DataContractJsonSerializer(typeof(TargetRuntimeSettings)).WriteObject(stream, settings);
                settings.ExpectedRuntimeRevision = BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", string.Empty).ToLowerInvariant();
            }
            return settings;
        }

        public void Apply(TargetProfileSnapshot target, AppConfigSnapshot config, TargetRuntimeSettings settings)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.BundledRuntimeUpdated && (target.Kind != "Windows" ||
                !string.Equals(settings.CodexRuntimeSource, CodexRuntimeSources.Bundled, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Only the local Windows bundled runtime can report an installed update.", nameof(settings));
            ValidateRevision(target, config, settings);
            ValidateName(settings.Name);
            if (target.Kind == "Windows")
            {
                if (!new[] { CodexRuntimeSources.Bundled, CodexRuntimeSources.System, CodexRuntimeSources.Custom }
                    .Contains(settings.CodexRuntimeSource, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException("Windows must retain its local runtime.", nameof(settings));
                if (string.Equals(settings.CodexRuntimeSource, CodexRuntimeSources.Custom, StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(settings.CodexCustomExecutablePath))
                    throw new ArgumentException("The custom Codex executable path is required.", nameof(settings));
                config.CodexRuntimeSource = CodexRuntimeSources.CanonicalizeForPersistence(settings.CodexRuntimeSource);
                config.CodexCustomExecutablePath = settings.CodexCustomExecutablePath;
                config.WindowsShellPreference = settings.WindowsShellPreference;
            }
            else if (target.Kind == "Wsl")
            {
                if (!string.Equals(settings.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) ||
                    settings.WslAppServer == null || !string.Equals(target.WslDistributionName,
                        settings.WslAppServer.DistributionName, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Changing the WSL distribution requires a new target.", nameof(settings));
                config.CodexRuntimeSource = CodexRuntimeSources.Wsl;
                config.WslAppServer = CloneWsl(settings.WslAppServer);
                config.WslAppServer.DistributionName = target.WslDistributionName;
            }
            else if (target.Kind == "Ssh")
            {
                ValidateSsh(settings.RemoteSsh);
                var current = GetSsh(target, config);
                if (!CodexRuntimeSources.IsRemoteUnixSsh(settings.CodexRuntimeSource) ||
                    !string.Equals(current.UserName, settings.RemoteSsh.UserName, StringComparison.Ordinal))
                    throw new ArgumentException("Changing the SSH user requires a new target.", nameof(settings));
                var updated = CloneSsh(settings.RemoteSsh);
                if (!IsSameSshHost(current.HostName, updated.HostName) &&
                    string.Equals(current.HostKeyFingerprint, updated.HostKeyFingerprint, StringComparison.Ordinal))
                    updated.HostKeyFingerprint = null;
                updated.UserName = current.UserName;
                var profile = FindSshProfile(target, config);
                if (profile != null)
                {
                    profile.Name = settings.Name.Trim();
                    profile.Ssh = updated;
                    config.SelectedRemoteUnixSshProfileId = profile.Id;
                }
                config.RemoteMacSsh = CloneSsh(updated);
                config.CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh;
            }
            else throw new ArgumentException("Unknown target kind.", nameof(target));
        }

        public bool RequiresReconnect(TargetProfileSnapshot target, AppConfigSnapshot originalConfig, TargetRuntimeSettings settings)
        {
            var next = new AppConfigSnapshot
            {
                CodexRuntimeSource = originalConfig.CodexRuntimeSource,
                CodexCustomExecutablePath = originalConfig.CodexCustomExecutablePath,
                WindowsShellPreference = originalConfig.WindowsShellPreference,
                WslAppServer = CloneWsl(originalConfig.WslAppServer),
                RemoteMacSsh = CloneSsh(GetSsh(target, originalConfig)),
                SelectedRemoteUnixSshProfileId = target.SshProfileId
            };
            if (target.Kind == "Ssh" && !string.IsNullOrWhiteSpace(target.SshProfileId))
                next.RemoteUnixSshProfiles.Add(new RemoteUnixSshProfileConfigSnapshot
                {
                    Id = target.SshProfileId, Name = target.Name, Ssh = CloneSsh(GetSsh(target, originalConfig))
                });
            Apply(target, next, settings);
            return settings.BundledRuntimeUpdated || RequiresReconnect(originalConfig, next);
        }

        // Preserve the existing runtime restart comparison, including case-sensitive Unix commands and usernames.
        public bool RequiresReconnect(AppConfigSnapshot previous, AppConfigSnapshot next)
        {
            if (previous == null || next == null) throw new ArgumentNullException(previous == null ? nameof(previous) : nameof(next));
            var source = CodexRuntimeSources.CanonicalizeForExecution(next.CodexRuntimeSource);
            if (!EqualsIgnoreCase(CodexRuntimeSources.CanonicalizeForExecution(previous.CodexRuntimeSource), source)) return true;
            if (source == CodexRuntimeSources.Custom &&
                !EqualsIgnoreCase(previous.CodexCustomExecutablePath?.Trim(), next.CodexCustomExecutablePath?.Trim())) return true;
            if (source == CodexRuntimeSources.Bundled || source == CodexRuntimeSources.System || source == CodexRuntimeSources.Custom)
                return !EqualsIgnoreCase(WindowsShellPreferences.Normalize(previous.WindowsShellPreference),
                    WindowsShellPreferences.Normalize(next.WindowsShellPreference));
            if (source == CodexRuntimeSources.Wsl)
            {
                var left = previous.WslAppServer ?? new WslAppServerConfigSnapshot();
                var right = next.WslAppServer ?? new WslAppServerConfigSnapshot();
                return !EqualsIgnoreCase(left.DistributionName, right.DistributionName) ||
                    left.CodexCommand != right.CodexCommand || left.ShellCommand != right.ShellCommand;
            }
            if (CodexRuntimeSources.IsRemoteUnixSsh(source))
            {
                var left = previous.GetSelectedRemoteUnixSshConfig();
                var right = next.GetSelectedRemoteUnixSshConfig();
                return !EqualsIgnoreCase(left.HostName, right.HostName) || left.Port != right.Port || left.UserName != right.UserName ||
                    left.AuthenticationMode != right.AuthenticationMode || !EqualsIgnoreCase(left.PrivateKeyPath, right.PrivateKeyPath) ||
                    left.ProtectedPassword != right.ProtectedPassword || left.HostKeyFingerprint != right.HostKeyFingerprint ||
                    !EqualsIgnoreCase(left.PlinkExecutablePath, right.PlinkExecutablePath) || left.ShellCommand != right.ShellCommand ||
                    left.CodexCommand != right.CodexCommand;
            }
            return false;
        }

        public void ValidateRevision(TargetProfileSnapshot target, AppConfigSnapshot config, TargetRuntimeSettings settings, bool required = false)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (string.IsNullOrWhiteSpace(settings.ExpectedRuntimeRevision))
            {
                if (required) throw new TargetRuntimeSettingsConflictException("Reload this target's current runtime settings before saving.");
                return;
            }
            if (!string.Equals(settings.ExpectedRuntimeRevision, Build(target, config).ExpectedRuntimeRevision, StringComparison.Ordinal))
                throw new TargetRuntimeSettingsConflictException("This target's runtime settings changed. Reload before saving; your draft was not applied.");
        }

        public static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 || name.Any(char.IsControl))
                throw new ArgumentException("A target name of 1 to 120 characters without control characters is required.", nameof(name));
        }

        public static void ValidateSsh(RemoteMacSshConfigSnapshot ssh)
        {
            if (ssh == null || string.IsNullOrWhiteSpace(ssh.HostName) || string.IsNullOrWhiteSpace(ssh.UserName) ||
                ssh.Port < 1 || ssh.Port > 65535 || !Enum.IsDefined(typeof(RemoteSshAuthenticationMode), ssh.AuthenticationMode) ||
                ssh.HostName.Any(char.IsControl) || ssh.UserName.Any(char.IsControl))
                throw new ArgumentException("SSH host, username, authentication mode and port must be valid.", nameof(ssh));
        }

        public static bool IsSameSshEndpoint(RemoteMacSshConfigSnapshot left, RemoteMacSshConfigSnapshot right)
        {
            return left != null && right != null && left.Port == right.Port &&
                IsSameSshHost(left.HostName, right.HostName) &&
                string.Equals(left.UserName, right.UserName, StringComparison.Ordinal);
        }

        public static bool IsSameSshHost(string left, string right)
        {
            return string.Equals(NormalizeSshHost(left), NormalizeSshHost(right), StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSshHost(string host)
        {
            var normalized = host?.Trim().TrimEnd('.').Trim('[', ']');
            IPAddress address;
            return IPAddress.TryParse(normalized, out address) ? address.ToString() : normalized;
        }

        private static RemoteUnixSshProfileConfigSnapshot FindSshProfile(TargetProfileSnapshot target, AppConfigSnapshot config)
        {
            if (string.IsNullOrWhiteSpace(target.SshProfileId)) return null;
            var profile = config.RemoteUnixSshProfiles.FirstOrDefault(p => p != null &&
                string.Equals(p.Id, target.SshProfileId, StringComparison.OrdinalIgnoreCase));
            if (profile == null && target.Kind == "Ssh") throw new InvalidOperationException("The target's SSH profile is missing.");
            return profile;
        }

        private static RemoteMacSshConfigSnapshot GetSsh(TargetProfileSnapshot target, AppConfigSnapshot config)
        {
            return FindSshProfile(target, config)?.Ssh ?? config.RemoteMacSsh;
        }

        private static bool EqualsIgnoreCase(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static WslAppServerConfigSnapshot CloneWsl(WslAppServerConfigSnapshot value)
        {
            return new WslAppServerConfigSnapshot
            {
                DistributionName = value.DistributionName, CodexCommand = value.CodexCommand, ShellCommand = value.ShellCommand
            };
        }

        private static RemoteMacSshConfigSnapshot CloneSsh(RemoteMacSshConfigSnapshot value)
        {
            return new RemoteMacSshConfigSnapshot
            {
                HostName = value.HostName, UserName = value.UserName, Port = value.Port,
                AuthenticationMode = value.AuthenticationMode, PrivateKeyPath = value.PrivateKeyPath,
                ProtectedPassword = value.ProtectedPassword, HostKeyFingerprint = value.HostKeyFingerprint,
                PlinkExecutablePath = value.PlinkExecutablePath, ShellCommand = value.ShellCommand, CodexCommand = value.CodexCommand
            };
        }
    }

    public sealed class TargetRuntimeSettingsConflictException : InvalidOperationException
    {
        public TargetRuntimeSettingsConflictException(string message) : base(message)
        {
        }
    }
}
