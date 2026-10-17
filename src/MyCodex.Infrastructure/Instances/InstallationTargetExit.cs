using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Infrastructure.Instances
{
    public static class InstallationTargetExit
    {
        // The installer owns SharedUpdateMutexName until file replacement finishes.
        // This process runs from the installed executable to retain authenticated IPC.
        public static async Task<int> RunAsync(string[] sharedRoots, CancellationToken cancellationToken)
        {
            // A direct/legacy invocation must not close targets without blocking new startups.
            try
            {
                using (var available = TargetInstanceCoordinator.TryReserveSharedUpdate())
                    if (available != null) return 2;
            }
            catch { return 2; }
            int processId;
            using (var process = Process.GetCurrentProcess()) processId = process.Id;
            Func<TargetInstanceRecord, TargetInstanceRequest, Task<TargetInstanceReply>> send = (instance, request) =>
                Task.Run(() =>
                {
                    try { return TargetInstanceCoordinator.SendToInstance(instance, FindRoot(instance, sharedRoots), request); }
                    catch
                    {
                        // Normal shutdown can remove the record before the process finishes.
                        // Keep that process in discovery until its actual exit can be verified.
                        return new TargetInstanceReply { Instance = instance, Error = "The target's data directory cannot be verified." };
                    }
                });
            var coordinator = new TargetExitAllCoordinator(null, () =>
                TargetInstanceCoordinator.DiscoverInstallationUsers(false, processId, sharedRoots)
                    .Select(instance => send(instance, new TargetInstanceRequest { Operation = TargetInstanceOperation.Status })
                        .GetAwaiter().GetResult()).ToArray(), send);
            try
            {
                if (coordinator.CaptureScope().Count == 0) return 0;
            }
            catch { return 2; } // Unknown/legacy owner, inspection or IPC failure; nobody stopped.
            try
            {
                await coordinator.PrepareAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await coordinator.CommitOtherTargetsAsync().ConfigureAwait(false);
                return (await coordinator.WaitForOtherTargetsAsync(cancellationToken).ConfigureAwait(false)).Count == 0 ? 0 : 4;
            }
            catch { return 3; } // Cancelled save, expired preparation, or failed commit.
            finally { await coordinator.AbortAsync().ConfigureAwait(false); }
        }

        private static string FindRoot(TargetInstanceRecord instance, IEnumerable<string> roots)
        {
            var name = TargetInstanceSecurity.Route(instance.ChannelId, instance.TargetId) + ".json";
            var matches = roots.Where(root => File.Exists(Path.Combine(root, "targets", ".instances", name))).ToArray();
            if (matches.Length != 1) throw new InvalidDataException("The target's data directory cannot be verified.");
            return matches[0];
        }
    }
}
