using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public static class SharedEmbeddedRelayHost
    {
        internal const string Argument = "--shared-relay-host";

        public static bool TryRun(string[] args, out int exitCode)
        {
            exitCode = 0;
            if (args == null || args.Length == 0 || !string.Equals(args[0], Argument, StringComparison.Ordinal))
            {
                return false;
            }

            if (args.Length != 4)
            {
                exitCode = 2;
                return true;
            }

            try
            {
                var root = Path.GetFullPath(args[1]);
                var config = new AppConfigService(root, args[2], args[3], root);
                var store = new SharedRelayConfigStore(root);
                Run(root, store, new EmbeddedRelayService(config, store));
            }
            catch
            {
                // Startup errors are returned through the ownership record when it can be read safely.
                exitCode = 1;
            }

            return true;
        }

        internal static void Run(string root, IRemoteServiceConfigStore configStore, IEmbeddedRelayService service)
        {
            var ownership = new SharedRelayStateStore(root);
            SharedRelayProcessRecord self;
            using (var process = Process.GetCurrentProcess())
            {
                self = SharedRelayProcessRecord.Capture(process);
            }

            var first = true;
            string appliedPublicHost = null;
            var appliedPublicPort = 0;
            var hasAppliedSettings = false;
            var appliedRevision = 0L;
            while (ownership.WithLock(state =>
            {
                // A launched helper may run only after its launcher publishes the verified identity.
                if (!SameProcess(state.Host, self))
                {
                    return false;
                }

                try
                {
                    var snapshot = configStore.Load();
                    ValidateRelayIdentity(snapshot);
                    if (first)
                    {
                        // A predecessor's stdout pipes are gone. Restart only its verified child.
                        if (snapshot.EmbeddedRelayProcessId > 0)
                        {
                            service.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                            snapshot = configStore.Load();
                        }

                        first = false;
                    }

                    if (state.Leases.Count == 0)
                    {
                        service.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                        state.Host = null;
                        state.InstallationExecutable = null;
                        state.Ready = false;
                        state.Error = null;
                        return false;
                    }

                    if (hasAppliedSettings && (appliedRevision != state.SettingsRevision ||
                        !string.Equals(appliedPublicHost, snapshot.EmbeddedRelayPublicHostOverride, StringComparison.Ordinal) ||
                        appliedPublicPort != snapshot.EmbeddedRelayPublicPortOverride))
                    {
                        if (state.Leases.Count > 1 && appliedRevision == state.SettingsRevision)
                        {
                            throw new InvalidOperationException("Shared Relay settings cannot be applied while other targets are using the service.");
                        }

                        service.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
                        snapshot = configStore.Load();
                    }

                    if (service.GetStatus(snapshot).State != Application.RemoteRelay.EmbeddedRelayRunState.Running)
                    {
                        state.Ready = false;
                        EnsureInitializationIsSafe(root, snapshot);
                        var result = service.StartAsync(snapshot, CancellationToken.None).GetAwaiter().GetResult();
                        if (result == null || !result.Success)
                        {
                            // A different channel may have won the port race after the first check.
                            EnsureInitializationIsSafe(root, snapshot);
                            throw new InvalidOperationException(result == null ? "Shared Relay startup failed." : result.ErrorMessage);
                        }
                    }

                    state.Ready = true;
                    state.Error = null;
                    appliedPublicHost = snapshot.EmbeddedRelayPublicHostOverride;
                    appliedPublicPort = snapshot.EmbeddedRelayPublicPortOverride;
                    hasAppliedSettings = true;
                    appliedRevision = state.SettingsRevision;
                }
                catch (Exception ex)
                {
                    state.Ready = false;
                    state.Error = RelaySecretRedactor.Redact(ex.Message);
                    // Keep the helper alive so users see the failure instead of an endless spawn loop.
                    if (state.Leases.Count == 0)
                    {
                        state.Host = null;
                        return false;
                    }
                }

                return true;
            }, CancellationToken.None))
            {
                Thread.Sleep(500);
            }
        }

        internal static bool SameProcess(SharedRelayProcessRecord left, SharedRelayProcessRecord right)
        {
            return left != null && right != null && left.ProcessId == right.ProcessId &&
                left.StartTimeUtcTicks == right.StartTimeUtcTicks &&
                string.Equals(left.ExecutablePath, right.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }

        private static void ValidateRelayIdentity(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot.EmbeddedRelayProcessId <= 0) return;
            var record = new SharedRelayProcessRecord
            {
                ProcessId = snapshot.EmbeddedRelayProcessId,
                ExecutablePath = snapshot.EmbeddedRelayProcessExecutablePath,
                StartTimeUtcTicks = snapshot.EmbeddedRelayProcessStartTimeUtcTicks
            };
            SharedRelayStateStore.IsProcessAlive(record);
            if (!string.Equals(snapshot.EmbeddedRelayProcessExecutablePath,
                    Path.GetFullPath(new EmbeddedRelayBinaryLocator().Resolve()), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("The recorded Relay belongs to another installation; it was not stopped.");
            }
        }

        internal static void EnsureInitializationIsSafe(string root, RemoteServiceConfigSnapshot snapshot)
        {
            if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endpoint =>
                endpoint.Port == 38443 || (snapshot.EmbeddedRelayInternalPort > 0 && endpoint.Port == snapshot.EmbeddedRelayInternalPort)))
            {
                throw new InvalidOperationException("A Relay port (38443 or the saved internal endpoint) is occupied by another service, channel or installation; no process or credentials were changed.");
            }

            var layout = EmbeddedRelayDirectoryLayout.Create(root);
            if ((File.Exists(layout.ConfigPath) || File.Exists(layout.StatePath)) &&
                string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret))
            {
                throw new InvalidOperationException("Existing Relay data has no saved shared credentials; restore the shared configuration before starting it.");
            }
        }
    }
}
