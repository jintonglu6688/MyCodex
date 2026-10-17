using System;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using MyCodex.Infrastructure.Instances;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal static class SharedRelayOwnershipRecovery
    {
        // Called only while the shared ownership lock is held. New users cannot acquire a lease until recovery ends.
        internal static string GetBlockReason(string sharedRoot, Func<string, bool> hasOtherProcess = null,
            Func<SharedRelayProcessRecord, bool> isAlive = null, Func<int[]> listeningPorts = null)
        {
            using (var current = Process.GetCurrentProcess())
            {
                if (TargetInstanceCoordinator.HasOtherRegisteredUsers(sharedRoot, current.Id)) return "RegisteredTarget";
                hasOtherProcess = hasOtherProcess ?? (name => HasOtherProcess(name, current.Id));
                foreach (var name in new[] { current.ProcessName, "MyCodex", "MyCodex.Dev", "mycodex-relay" }
                    .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    // Include other installations/channels: their ownership cannot be recovered from a zero-filled file.
                    if (hasOtherProcess(name)) return "OtherProcess:" + name;
                }
            }

            var snapshot = new SharedRelayConfigStore(sharedRoot).Load();
            if (snapshot.EmbeddedRelayProcessId > 0 && (isAlive ?? SharedRelayStateStore.IsProcessAlive)(new SharedRelayProcessRecord
            {
                ProcessId = snapshot.EmbeddedRelayProcessId,
                ExecutablePath = snapshot.EmbeddedRelayProcessExecutablePath,
                StartTimeUtcTicks = snapshot.EmbeddedRelayProcessStartTimeUtcTicks
            })) return "RecordedRelayAlive";

            var ports = listeningPorts == null
                ? IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(endpoint => endpoint.Port).ToArray()
                : listeningPorts();
            if (ports.Any(port => port == 38443 || port == snapshot.EmbeddedRelayInternalPort ||
                port == snapshot.EmbeddedRelayPublicPortOverride)) return "RelayPortOccupied";
            return null;
        }

        private static bool HasOtherProcess(string name, int currentId)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                return processes.Any(process => process.Id != currentId && !process.HasExited);
            }
            finally
            {
                foreach (var process in processes) process.Dispose();
            }
        }
    }
}
