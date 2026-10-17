using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal static class ComputerUseRuntimeExecutableResolver
    {
        internal static string Resolve(AppServerLaunchInfo launch, Process root)
        {
            if (launch == null || root == null || launch.HostPlatform != AppServerHostPlatform.Windows
                || Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
            try
            {
                if (root.HasExited) return null;
                var rootIdentity = new RuntimeProcessIdentity(root.MainModule.FileName, root.StartTime.ToUniversalTime());
                if (Path.IsPathRooted(launch.FileName) && File.Exists(launch.FileName)
                    && string.Equals(Path.GetExtension(launch.FileName), ".exe", StringComparison.OrdinalIgnoreCase)
                    && !IsWrapper(launch.FileName))
                {
                    return string.Equals(Path.GetFullPath(launch.FileName), rootIdentity.ExecutablePath, StringComparison.OrdinalIgnoreCase)
                        ? launch.FileName : null;
                }

                // Do not inspect command lines or environment variables. The running launcher's
                // owned tree is the only source of the native binary; never search PATH or bundles.
                var parents = new Dictionary<int, int>();
                using (var searcher = new ManagementObjectSearcher("root\\cimv2", "SELECT ProcessId, ParentProcessId FROM Win32_Process"))
                {
                    searcher.Options.Timeout = TimeSpan.FromSeconds(2);
                    using (var processes = searcher.Get())
                    {
                        foreach (ManagementBaseObject process in processes)
                        {
                            using (process) parents[Convert.ToInt32(process["ProcessId"])] = Convert.ToInt32(process["ParentProcessId"]);
                        }
                    }
                }
                var selected = SelectUniqueCodexDescendant(root.Id, rootIdentity, parents, ReadIdentity);
                root.Refresh();
                return root.HasExited ? null : selected;
            }
            catch (Exception exception) when (IsInspectionFailure(exception)) { return null; }
        }

        internal static string SelectUniqueCodexDescendant(int rootId, RuntimeProcessIdentity root,
            IDictionary<int, int> parents, Func<int, RuntimeProcessIdentity> readIdentity)
        {
            if (rootId <= 0 || root == null || parents == null || readIdentity == null) return null;
            var children = parents.ToLookup(pair => pair.Value, pair => pair.Key);
            var queue = new Queue<KeyValuePair<int, RuntimeProcessIdentity>>();
            var visited = new HashSet<int> { rootId };
            queue.Enqueue(new KeyValuePair<int, RuntimeProcessIdentity>(rootId, root));
            string candidate = null;
            while (queue.Count != 0)
            {
                var parent = queue.Dequeue();
                foreach (var childId in children[parent.Key])
                {
                    if (!visited.Add(childId)) return null;
                    var child = readIdentity(childId);
                    if (child == null) return null;
                    // A stale ParentProcessId can refer to a reused PID. Such a process is
                    // not a descendant of the current owned parent and its subtree is ignored.
                    if (child.StartTimeUtc < parent.Value.StartTimeUtc) continue;
                    if (string.Equals(Path.GetFileName(child.ExecutablePath), "codex.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!Path.IsPathRooted(child.ExecutablePath) || candidate != null) return null;
                        candidate = child.ExecutablePath;
                    }
                    queue.Enqueue(new KeyValuePair<int, RuntimeProcessIdentity>(childId, child));
                }
            }
            return candidate;
        }

        private static RuntimeProcessIdentity ReadIdentity(int processId)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    if (process.HasExited) return null;
                    return new RuntimeProcessIdentity(process.MainModule.FileName, process.StartTime.ToUniversalTime());
                }
            }
            catch (Exception exception) when (IsInspectionFailure(exception)) { return null; }
        }

        private static bool IsWrapper(string path)
        {
            var name = Path.GetFileName(path);
            return new[] { "cmd.exe", "powershell.exe", "pwsh.exe", "node.exe", "wscript.exe", "cscript.exe" }
                .Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        private static bool IsInspectionFailure(Exception exception)
        {
            return exception is Win32Exception || exception is ManagementException || exception is COMException
                || exception is IOException || exception is InvalidOperationException || exception is UnauthorizedAccessException
                || exception is SecurityException || exception is ArgumentException || exception is NotSupportedException;
        }

        internal sealed class RuntimeProcessIdentity
        {
            internal RuntimeProcessIdentity(string executablePath, DateTime startTimeUtc)
            {
                ExecutablePath = executablePath;
                StartTimeUtc = startTimeUtc;
            }

            internal string ExecutablePath { get; private set; }
            internal DateTime StartTimeUtc { get; private set; }
        }
    }
}
