using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Updates;

namespace MyCodex.Infrastructure.Instances
{
    public sealed partial class TargetInstanceCoordinator
    {
        private sealed class ExitPreparationState
        {
            internal TargetInstanceRequest Request;
            internal TargetInstanceReply Reply;
            internal DateTime LastContactUtc;
            internal bool WasCancelled;
        }

        private ExitPreparationState _exitPreparation;
        private Timer _exitPreparationTimer;

        private sealed class StartupReservation : IDisposable
        {
            private readonly IDisposable _lifecycle;
            private readonly IDisposable _update;
            internal StartupReservation(IDisposable lifecycle, IDisposable update) { _lifecycle = lifecycle; _update = update; }
            public void Dispose() { try { _lifecycle.Dispose(); } finally { _update.Dispose(); } }
        }

        public static string SharedUpdateMutexName => @"Global\" + UpdateGateRoute();

        public static IDisposable TryReserveAllTargets(string channelId, string sharedRoot)
        {
            Path.GetFullPath(sharedRoot);
            return TargetInstanceGate.TryAcquire(TargetInstanceSecurity.Route(channelId, ".lifecycle"));
        }

        public static IDisposable TryReserveSharedUpdate()
        {
            return TargetInstanceGate.TryAcquire(UpdateGateRoute());
        }

        private static IDisposable ReserveTargetStartup(string channelId)
        {
            DeferredUpdateInstallerLauncher.ThrowIfUpdatePending();
            var update = AcquireStartupGate(UpdateGateRoute());
            if (update == null) throw new InvalidOperationException("An update is preparing. Retry after it finishes.");
            try
            {
                var lifecycle = AcquireStartupGate(TargetInstanceSecurity.Route(channelId, ".lifecycle"));
                if (lifecycle == null) throw new InvalidOperationException("All targets are preparing to exit. Cancel or complete that operation first.");
                return new StartupReservation(lifecycle, update);
            }
            catch { update.Dispose(); throw; }
        }

        private static IDisposable AcquireStartupGate(string route)
        {
            // Concurrent normal startups hold this only while registering. A long-held gate is exit/update preparation.
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var gate = TargetInstanceGate.TryAcquire(route);
                if (gate != null) return gate;
                Thread.Sleep(20);
            }
            return null;
        }

        private static string UpdateGateRoute()
        {
            using (var hash = SHA256.Create())
            {
                var directory = Encoding.UTF8.GetBytes(Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).ToUpperInvariant());
                return TargetInstanceSecurity.Route("SharedUpdate", BitConverter.ToString(hash.ComputeHash(directory)).Replace("-", string.Empty));
            }
        }

        public static IReadOnlyList<TargetInstanceRecord> DiscoverInstallationUsers(params string[] sharedRoots)
        {
            return DiscoverInstallationUsers(true, 0, sharedRoots);
        }

        internal static bool HasOtherRegisteredUsers(string sharedRoot, int excludedProcessId)
        {
            var directory = RecordsDirectory(sharedRoot);
            if (!Directory.Exists(directory)) return false;
            foreach (var path in Directory.EnumerateFiles(directory, "MyCodex.Target.*.json"))
            {
                var record = ReadRecord(path);
                var alive = TargetInstanceSecurity.IsAlive(record);
                if (alive == false) continue;
                if (alive != true || record.ProcessId != excludedProcessId) return true;
            }
            return false;
        }

        internal static IReadOnlyList<TargetInstanceRecord> DiscoverInstallationUsers(bool includeRuntimeUsers,
            int excludedProcessId, params string[] sharedRoots)
        {
            var records = new List<TargetInstanceRecord>();
            foreach (var root in sharedRoots)
            {
                var directory = RecordsDirectory(root);
                if (!Directory.Exists(directory)) continue;
                foreach (var path in Directory.EnumerateFiles(directory, "MyCodex.Target.*.json"))
                {
                    var record = ReadRecord(path);
                    if (!MatchesRoute(record, record.ChannelId, record.TargetId) ||
                        !string.Equals(Path.GetFileName(path), TargetInstanceSecurity.Route(record.ChannelId, record.TargetId) + ".json", StringComparison.Ordinal))
                        throw new InvalidDataException("An instance record could not be verified; updating is blocked.");
                    if (TargetInstanceSecurity.IsAlive(record) == false) continue;
                    // Both release channels can share one runtime tree even when their executable names differ.
                    if (string.Equals(Path.GetDirectoryName(record.ExecutablePath), Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        records.Add(record);
                }
            }
            var relayHosts = SharedUpdateResourceUsageInspector.FindCoordinatedRelayHosts(sharedRoots, records);
            // Older clients have no target records. They are resource users, not participants we can safely close.
            foreach (var name in new[] { "MyCodex", "MyCodex.Dev" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    using (process)
                    {
                        try
                        {
                            if (process.Id == excludedProcessId || process.HasExited) continue;
                            var executable = Path.GetFullPath(process.MainModule.FileName);
                            if (!string.Equals(Path.GetDirectoryName(executable), Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
                            var start = process.StartTime.ToUniversalTime().Ticks;
                            if (records.Exists(record => record.ProcessId == process.Id && record.StartTimeUtcTicks == start)) continue;
                            if (relayHosts.Exists(record => record.ProcessId == process.Id && record.StartTimeUtcTicks == start &&
                                string.Equals(record.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase))) continue;
                            records.Add(new TargetInstanceRecord
                            {
                                ProcessId = process.Id, StartTimeUtcTicks = start, ExecutablePath = executable,
                                ChannelId = "Uncoordinated", TargetId = name, InstanceId = string.Empty
                            });
                        }
                        catch (InvalidOperationException) { if (!process.HasExited) throw; }
                    }
                }
            }
            if (includeRuntimeUsers)
                records.AddRange(SharedUpdateResourceUsageInspector.FindUncoordinatedRuntimeUsers(
                    Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory), records));
            return records;
        }

        public static bool SameInstance(TargetInstanceRecord first, TargetInstanceRecord second)
        {
            return first != null && second != null && SameProcess(first, second) &&
                string.Equals(first.ChannelId, second.ChannelId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(first.TargetId, second.TargetId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(first.InstanceId, second.InstanceId, StringComparison.Ordinal);
        }

        internal static bool SameProcess(TargetInstanceRecord first, TargetInstanceRecord second)
        {
            return first != null && second != null && first.ProcessId == second.ProcessId &&
                first.StartTimeUtcTicks == second.StartTimeUtcTicks && first.UserSid == second.UserSid &&
                string.Equals(first.ExecutablePath, second.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExitOperation(TargetInstanceOperation operation)
        {
            return operation == TargetInstanceOperation.PrepareExit || operation == TargetInstanceOperation.AbortExit || operation == TargetInstanceOperation.CommitExit;
        }

        private async Task<TargetInstanceReply> DispatchExitAsync(TargetInstanceRequest request,
            Func<TargetInstanceRequest, Task<TargetInstanceReply>> handler)
        {
            CheckExitCoordinator(DateTime.UtcNow);
            ExitPreparationState preparation;
            lock (_sync)
            {
                preparation = _exitPreparation;
                if (preparation != null && (preparation.Request.OperationId != request.OperationId ||
                    !SameProcess(preparation.Request.Caller, request.Caller)))
                    return TargetInstanceReply.Failure("Another window is coordinating this target's exit.");
                if (request.Operation == TargetInstanceOperation.PrepareExit)
                {
                    if (preparation != null)
                    {
                        if (!preparation.WasCancelled) preparation.LastContactUtc = DateTime.UtcNow;
                        return preparation.Reply ?? PendingExitReply();
                    }
                    preparation = new ExitPreparationState { Request = request, LastContactUtc = DateTime.UtcNow };
                    _exitPreparation = preparation;
                    if (_exitPreparationTimer == null)
                        _exitPreparationTimer = new Timer(CheckExitCoordinator, null, 1000, 1000);
                    // Preparation can display a slow save dialog. Polling leaves the secured pipe available.
                    Task.Run(() => PrepareExitAsync(preparation, handler));
                    return PendingExitReply();
                }
                if (request.Operation == TargetInstanceOperation.CommitExit &&
                    (preparation == null || preparation.Reply?.ExitPreparation != TargetExitPreparation.Ready))
                    return TargetInstanceReply.Failure("Only this operation's prepared target can be closed.");
                if (request.Operation == TargetInstanceOperation.AbortExit) _exitPreparation = null;
            }
            try { return await handler(request).ConfigureAwait(false) ?? TargetInstanceReply.Failure("The target did not confirm exit."); }
            catch { return TargetInstanceReply.Failure("The target could not complete exit."); }
        }

        private async Task PrepareExitAsync(ExitPreparationState preparation,
            Func<TargetInstanceRequest, Task<TargetInstanceReply>> handler)
        {
            TargetInstanceReply reply;
            try { reply = await handler(preparation.Request).ConfigureAwait(false) ?? TargetInstanceReply.Failure("Exit preparation was not confirmed."); }
            catch { reply = TargetInstanceReply.Failure("Exit preparation failed."); }
            reply.ExitPreparation = reply.Success ? TargetExitPreparation.Ready : TargetExitPreparation.Rejected;
            lock (_sync)
            {
                if (ReferenceEquals(_exitPreparation, preparation) && !preparation.WasCancelled) preparation.Reply = reply;
            }
        }

        private static TargetInstanceReply PendingExitReply()
        {
            return new TargetInstanceReply { Success = true, ExitPreparation = TargetExitPreparation.Pending };
        }

        private void CheckExitCoordinator(object state)
        {
            CheckExitCoordinator(DateTime.UtcNow);
        }

        internal void CheckExitCoordinator(DateTime now)
        {
            ExitPreparationState preparation;
            Func<TargetInstanceRequest, Task<TargetInstanceReply>> handler;
            lock (_sync)
            {
                preparation = _exitPreparation;
                handler = _handler;
                if (preparation == null) return;
                var alive = TargetInstanceSecurity.IsAlive(preparation.Request.Caller) == true;
                if (alive && (preparation.WasCancelled || now - preparation.LastContactUtc < TimeSpan.FromMinutes(10))) return;
                if (!alive) _exitPreparation = null;
                else
                {
                    preparation.WasCancelled = true;
                    preparation.Reply = TargetInstanceReply.Failure("Exit preparation expired. Cancel this operation and retry.");
                    preparation.Reply.ExitPreparation = TargetExitPreparation.Rejected;
                }
            }
            if (handler == null || Volatile.Read(ref _disposed) != 0) return;
            // A crashed coordinating window must not leave this target frozen, including pending dialogs.
            var request = new TargetInstanceRequest { Operation = TargetInstanceOperation.AbortExit, OperationId = preparation.Request.OperationId };
            Task.Run(async () => { try { await handler(request).ConfigureAwait(false); } catch { } });
        }
    }
}
