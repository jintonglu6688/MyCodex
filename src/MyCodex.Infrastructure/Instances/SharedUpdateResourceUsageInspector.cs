using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.RemoteRelay;

namespace MyCodex.Infrastructure.Instances
{
    internal static class SharedUpdateResourceUsageInspector
    {
        internal sealed class ProcessEntry
        {
            internal int Id;
            internal int ParentId;
            internal long StartTicks;
            internal string Name;
            internal string Path;
        }

        internal static List<TargetInstanceRecord> FindCoordinatedRelayHosts(string[] roots,
            IReadOnlyList<TargetInstanceRecord> owners, Func<TargetInstanceRecord, bool?> isAlive = null)
        {
            isAlive = isAlive ?? TargetInstanceSecurity.IsAlive;
            var hosts = new List<TargetInstanceRecord>();
            foreach (var root in roots)
            {
                var path = Path.Combine(EmbeddedRelayDirectoryLayout.Create(root).RootDirectory, "shared-host.json");
                if (!File.Exists(path)) continue;
                SharedRelayState state;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length < 1 || stream.Length > TargetInstanceCoordinator.MaximumPayloadBytes)
                        throw new InvalidDataException("The shared service ownership cannot be inspected.");
                    using (var reader = new StreamReader(stream))
                        state = new JavaScriptSerializer().Deserialize<SharedRelayState>(reader.ReadToEnd());
                }
                if (state == null || state.Leases == null) throw new InvalidDataException("The shared service ownership is invalid.");
                if (state.Host == null) continue;
                var host = RelayIdentity(state.Host);
                var hostAlive = isAlive(host);
                if (hostAlive == false) continue;
                if (hostAlive == null) throw new InvalidOperationException("The shared service host identity cannot be verified.");
                var coordinated = true;
                foreach (var lease in state.Leases)
                {
                    if (lease?.User == null) throw new InvalidDataException("A shared service user cannot be verified.");
                    var user = RelayIdentity(lease.User);
                    var live = isAlive(user);
                    if (live == false) continue;
                    if (live == null) throw new InvalidOperationException("A shared service user's identity cannot be verified.");
                    if (!owners.Any(owner => TargetInstanceCoordinator.SameProcess(owner, user))) coordinated = false;
                }
                if (coordinated) hosts.Add(host);
            }
            return hosts;
        }

        private static TargetInstanceRecord RelayIdentity(SharedRelayProcessRecord record)
        {
            return new TargetInstanceRecord { ProcessId = record.ProcessId, StartTimeUtcTicks = record.StartTimeUtcTicks,
                ExecutablePath = record.ExecutablePath, UserSid = TargetInstanceSecurity.CurrentSid };
        }

        internal static IEnumerable<TargetInstanceRecord> FindUncoordinatedRuntimeUsers(string installation,
            IReadOnlyList<TargetInstanceRecord> owners)
        {
            var entries = new List<ProcessEntry>();
            using (var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT ProcessId, ParentProcessId, CreationDate, Name, ExecutablePath FROM Win32_Process"))
            using (var results = searcher.Get())
            {
                foreach (ManagementObject candidate in results)
                {
                    using (candidate)
                    {
                        var creation = candidate["CreationDate"] as string;
                        entries.Add(new ProcessEntry
                        {
                            Id = Convert.ToInt32(candidate["ProcessId"]), ParentId = Convert.ToInt32(candidate["ParentProcessId"]),
                            Name = candidate["Name"] as string, Path = candidate["ExecutablePath"] as string,
                            StartTicks = string.IsNullOrEmpty(creation) ? 0 : ManagementDateTimeConverter.ToDateTime(creation).ToUniversalTime().Ticks
                        });
                    }
                }
            }
            return FindUncoordinatedRuntimeUsers(installation, owners, entries);
        }

        internal static IEnumerable<TargetInstanceRecord> FindUncoordinatedRuntimeUsers(string installation,
            IReadOnlyList<TargetInstanceRecord> owners, IReadOnlyList<ProcessEntry> entries)
        {
            var runtime = Path.Combine(Path.GetFullPath(installation), "runtime") + Path.DirectorySeparatorChar;
            var processes = entries.ToDictionary(entry => entry.Id);
            var uncoordinated = new List<TargetInstanceRecord>();
            foreach (var process in entries)
            {
                if (string.IsNullOrWhiteSpace(process.Path))
                {
                    if (string.Equals(process.Name, "codex.exe", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("A runtime owner cannot be inspected; updating is blocked.");
                    continue;
                }
                if (!Path.GetFullPath(process.Path).StartsWith(runtime, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(process.Name, "mycodex-auth-host.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(process.Name, "mycodex-relay.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var ancestor = process;
                var seen = new HashSet<int>();
                var coordinated = false;
                while (ancestor != null && seen.Add(ancestor.Id))
                {
                    if (owners.Any(owner => owner.ProcessId == ancestor.Id && ancestor.StartTicks > 0 &&
                        Math.Abs(owner.StartTimeUtcTicks - ancestor.StartTicks) < 10))
                    {
                        coordinated = true;
                        break;
                    }
                    ProcessEntry parent;
                    ancestor = processes.TryGetValue(ancestor.ParentId, out parent) && parent.StartTicks > 0 &&
                        parent.StartTicks <= ancestor.StartTicks ? parent : null;
                }
                if (!coordinated)
                    uncoordinated.Add(new TargetInstanceRecord { ProcessId = process.Id, StartTimeUtcTicks = process.StartTicks,
                        ExecutablePath = process.Path, ChannelId = "UncoordinatedRuntime", TargetId = process.Name });
            }
            return uncoordinated;
        }
    }
}
