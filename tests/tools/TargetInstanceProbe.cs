// Isolated native process/IPC probe; compiled by TargetInstances.Tests.ps1, never shipped.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.Instances;

// Direct csc builds need the same compatibility defaults as the net48 application.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class TargetInstanceProbe
{
    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--exit-for-install")
        {
            var dataRoot = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe-root.txt"));
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                return InstallationTargetExit.RunAsync(new[] { dataRoot }, timeout.Token).GetAwaiter().GetResult();
        }
        var mode = args[0];
        var root = args[1];
        var channel = args[2];
        var target = args[3];
        var marker = Path.Combine(root, args[4]);
        try
        {
            if (mode == "hold")
            {
                File.WriteAllText(marker + ".pid", Process.GetCurrentProcess().Id.ToString());
                File.WriteAllText(marker + ".ready", "isolated runtime user");
                var deadline = DateTime.UtcNow.AddSeconds(90);
                while (!File.Exists(marker + ".exit") && DateTime.UtcNow < deadline) Thread.Sleep(25);
                return 0;
            }
            if (mode == "owner")
            {
                using (var owner = TargetInstanceCoordinator.Acquire(channel, target, root))
                {
                    File.WriteAllText(marker + ".owner", owner.IsPrimaryInstance.ToString());
                    if (!owner.IsPrimaryInstance) return 0;
                    File.WriteAllText(marker + ".record", new JavaScriptSerializer().Serialize(owner.InstanceRecord));
                    owner.StartServer(async incoming =>
                    {
                        if (incoming.Operation == TargetInstanceOperation.PrepareExit)
                        {
                            File.WriteAllText(marker + ".prepare-started", incoming.OperationId);
                            var preparationDeadline = DateTime.UtcNow.AddSeconds(10);
                            while (File.Exists(marker + ".slow") && !File.Exists(marker + ".release") && DateTime.UtcNow < preparationDeadline)
                                await Task.Delay(25).ConfigureAwait(false);
                            File.WriteAllText(marker + ".prepare-finished", incoming.OperationId);
                            if (File.Exists(marker + ".reject")) return TargetInstanceReply.Failure("Fixture save cancelled.");
                            return new TargetInstanceReply { Success = true };
                        }
                        if (incoming.Operation == TargetInstanceOperation.AbortExit)
                        {
                            File.WriteAllText(marker + ".aborted", incoming.OperationId);
                            return new TargetInstanceReply { Success = true };
                        }
                        if (incoming.Operation == TargetInstanceOperation.CommitExit)
                        {
                            return new TargetInstanceReply
                            {
                                Success = true,
                                AfterResponse = () =>
                                {
                                    owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Closing });
                                    File.WriteAllText(marker + ".committed", incoming.OperationId);
                                    ThreadPool.QueueUserWorkItem(_ =>
                                    {
                                        Thread.Sleep(150);
                                        File.WriteAllText(marker + ".exit", "after-response exit");
                                    });
                                }
                            };
                        }
                        var value = incoming.Operation == TargetInstanceOperation.Activate
                            ? incoming.WorkspacePath : incoming.RuntimeSettings.Name;
                        File.WriteAllText(marker + ".request", value);
                        return new TargetInstanceReply { Success = true };
                    });
                    owner.UpdateStatus(new TargetInstanceStatus
                    {
                        Lifecycle = TargetInstanceLifecycle.Running, IsWorking = true,
                        HasPendingInteraction = true, IsConnected = false, ConnectionDetail = "probe-disconnected"
                    });
                    File.WriteAllText(marker + ".ready", "ready");
                    var deadline = DateTime.UtcNow.AddSeconds(90);
                    while (!File.Exists(marker + ".exit") && DateTime.UtcNow < deadline)
                    {
                        if (File.Exists(marker + ".close"))
                            owner.UpdateStatus(new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Closing });
                        Thread.Sleep(25);
                    }
                }
                if (File.Exists(marker + ".linger"))
                {
                    int milliseconds;
                    if (!int.TryParse(File.ReadAllText(marker + ".linger"), out milliseconds)) milliseconds = 1500;
                    Thread.Sleep(Math.Max(0, Math.Min(milliseconds, 60000)));
                }
                return 0;
            }
            if (mode == "oversized")
            {
                var records = Directory.GetFiles(Path.Combine(root, "targets", ".instances"), "*.json");
                foreach (var path in records)
                {
                    var record = new JavaScriptSerializer().Deserialize<TargetInstanceRecord>(File.ReadAllText(path));
                    if (record.TargetId != target) continue;
                    using (var pipe = new NamedPipeClientStream(".", Path.GetFileNameWithoutExtension(path), PipeDirection.InOut))
                    {
                        pipe.Connect(2000);
                        var header = BitConverter.GetBytes(1024 * 1024);
                        pipe.Write(header, 0, header.Length);
                        pipe.Flush();
                    }
                }
                return 0;
            }
            if (mode == "reserve")
            {
                using (var reservation = TargetInstanceCoordinator.TryReserveOfflineEdit(channel, target, root))
                    File.WriteAllText(marker + ".result", reservation == null ? "occupied" : "reserved");
                return 0;
            }
            if (mode == "exitflow" || mode == "slowflow")
            {
                var ownerMarker = Path.Combine(root, args[5]);
                var initial = TargetInstanceCoordinator.Send(channel, target, root, new TargetInstanceRequest { Operation = TargetInstanceOperation.Status });
                Require(initial.Success && initial.Instance != null, "Exit-flow owner is unavailable.");
                var instance = initial.Instance;
                var operation = Guid.NewGuid().ToString("N");
                Require(!ExitRequest(instance, root, TargetInstanceOperation.CommitExit, operation).Success, "Unprepared owner accepted commit.");
                var prepared = ExitRequest(instance, root, TargetInstanceOperation.PrepareExit, operation);
                Require(prepared.Success && prepared.ExitPreparation == TargetExitPreparation.Pending, "Preparation did not return promptly as pending.");
                WaitUntil(() => File.Exists(ownerMarker + ".prepare-started"), "Preparation callback did not start.");
                if (mode == "slowflow")
                {
                    var pending = TargetInstanceCoordinator.SendToInstance(instance, root, new TargetInstanceRequest { Operation = TargetInstanceOperation.Status });
                    Require(pending.Success && pending.Status.IsExitPrepared && pending.Status.IsWorking, "Pending preparation stopped work or blocked status.");
                    Require(!File.Exists(ownerMarker + ".prepare-finished"), "Slow preparation finished before the cancellation check.");
                    Require(ExitRequest(instance, root, TargetInstanceOperation.AbortExit, operation).Success, "Slow preparation could not be cancelled.");
                    WaitUntil(() => File.Exists(ownerMarker + ".aborted"), "Abort callback was not delivered.");
                    Require(!File.Exists(ownerMarker + ".prepare-finished"), "Slow preparation finished before the explicit late completion.");
                    File.WriteAllText(ownerMarker + ".release", "complete the obsolete preparation");
                    WaitUntil(() => File.Exists(ownerMarker + ".prepare-finished"), "Late preparation did not complete.");
                    Thread.Sleep(100);
                    var resumed = TargetInstanceCoordinator.SendToInstance(instance, root, new TargetInstanceRequest { Operation = TargetInstanceOperation.Status });
                    Require(resumed.Success && !resumed.Status.IsExitPrepared && resumed.Status.IsWorking, "Late preparation resurrected a cancelled exit.");
                    Require(!ExitRequest(instance, root, TargetInstanceOperation.CommitExit, operation).Success, "Cancelled preparation accepted a late commit.");
                    File.WriteAllText(marker + ".result", new JavaScriptSerializer().Serialize(resumed));
                    return 0;
                }
                WaitUntil(() => ExitRequest(instance, root, TargetInstanceOperation.PrepareExit, operation).ExitPreparation == TargetExitPreparation.Ready,
                    "Preparation did not become ready.");
                var paused = TargetInstanceCoordinator.SendToInstance(instance, root, new TargetInstanceRequest { Operation = TargetInstanceOperation.Status });
                Require(paused.Success && paused.Status.IsExitPrepared && paused.Status.IsWorking, "Ready preparation changed work or lost its lease.");
                var settingsBlocked = TargetInstanceCoordinator.SendToInstance(instance, root, new TargetInstanceRequest
                {
                    Operation = TargetInstanceOperation.ApplyRuntimeSettings, RuntimeSettings = new TargetRuntimeSettings { Name = "must not save" }
                });
                Require(!settingsBlocked.Success, "A prepared owner accepted runtime edits.");
                Require(!ExitRequest(instance, root, TargetInstanceOperation.CommitExit, Guid.NewGuid().ToString("N")).Success, "A different operation committed the prepared owner.");
                Require(ExitRequest(instance, root, TargetInstanceOperation.AbortExit, operation).Success, "Prepared exit could not be aborted.");
                var running = TargetInstanceCoordinator.SendToInstance(instance, root, new TargetInstanceRequest { Operation = TargetInstanceOperation.Status });
                Require(running.Success && !running.Status.IsExitPrepared && running.Status.IsWorking, "Abort did not restore the live owner.");
                operation = Guid.NewGuid().ToString("N");
                Require(ExitRequest(instance, root, TargetInstanceOperation.PrepareExit, operation).Success, "Second preparation failed.");
                WaitUntil(() => ExitRequest(instance, root, TargetInstanceOperation.PrepareExit, operation).ExitPreparation == TargetExitPreparation.Ready,
                    "Second preparation did not become ready.");
                var committed = ExitRequest(instance, root, TargetInstanceOperation.CommitExit, operation);
                Require(committed.Success, "Owner closed before returning its commit acknowledgement.");
                File.WriteAllText(marker + ".result", new JavaScriptSerializer().Serialize(committed));
                return 0;
            }
            if (mode == "stale")
            {
                var previous = new JavaScriptSerializer().Deserialize<TargetInstanceReply>(File.ReadAllText(Path.Combine(root, "status.result")));
                var stale = ExitRequest(previous.Instance, root, TargetInstanceOperation.CommitExit, Guid.NewGuid().ToString("N"));
                Require(!stale.Success && stale.Error != null && stale.Error.Contains("owner changed"), "A stale owner identity was not rejected distinctly.");
                File.WriteAllText(marker + ".result", new JavaScriptSerializer().Serialize(stale));
                return 0;
            }
            if (mode == "recordloss")
            {
                // Exercise extended WMI paths even when no such process is running.
                Require(Path.GetFullPath(@"\\?\C:\fixture\conhost.exe") == @"\\?\C:\fixture\conhost.exe",
                    "Extended WMI paths must use the application's .NET path handling.");
                var ownerMarker = Path.Combine(root, args[5]);
                var expected = new JavaScriptSerializer().Deserialize<TargetInstanceRecord>(File.ReadAllText(ownerMarker + ".record"));
                Require(expected.ChannelId == channel && expected.TargetId == target,
                    "The dedicated record-loss owner does not match the fixture route.");
                Require(TargetInstanceCoordinator.DiscoverInstallationUsers(root).Any(record => TargetInstanceCoordinator.SameInstance(record, expected)),
                    "The live fixture owner was not discovered before record removal.");
                var removed = false;
                foreach (var path in Directory.GetFiles(Path.Combine(root, "targets", ".instances"), "*.json"))
                {
                    var record = new JavaScriptSerializer().Deserialize<TargetInstanceRecord>(File.ReadAllText(path));
                    if (!TargetInstanceCoordinator.SameInstance(record, expected)) continue;
                    File.Delete(path);
                    removed = true;
                }
                Require(removed, "The dedicated fixture's instance record was not removed.");
                Require(TargetInstanceCoordinator.DiscoverInstallationUsers(root).Any(record => record.ProcessId == expected.ProcessId &&
                    record.StartTimeUtcTicks == expected.StartTimeUtcTicks && record.ChannelId == "Uncoordinated" && string.IsNullOrEmpty(record.InstanceId)),
                    "The actual live process was lost when its instance record disappeared.");
                using (var reservation = TargetInstanceCoordinator.TryReserveOfflineEdit(channel, target, root))
                    Require(reservation == null, "Record loss released the actual live owner's target gate.");
                File.WriteAllText(marker + ".result", new JavaScriptSerializer().Serialize(new TargetInstanceReply { Success = true, Instance = expected }));
                return 0;
            }
            if (mode == "runtime-usage")
            {
                var expectedPid = int.Parse(File.ReadAllText(Path.Combine(root, args[5]) + ".pid"));
                var runtimeUser = TargetInstanceCoordinator.DiscoverInstallationUsers(root)
                    .FirstOrDefault(record => record.ProcessId == expectedPid && record.ChannelId == "UncoordinatedRuntime");
                Require(runtimeUser != null, "The actual record-free fixture runtime process was not detected.");
                File.WriteAllText(marker + ".result", new JavaScriptSerializer().Serialize(new TargetInstanceReply { Success = true, Instance = runtimeUser }));
                return 0;
            }
            var request = new TargetInstanceRequest { Operation = TargetInstanceOperation.Status };
            if (mode == "activate")
            {
                request.Operation = TargetInstanceOperation.Activate;
                request.WorkspacePath = @"C:\probe workspace";
            }
            if (mode == "settings")
            {
                request.Operation = TargetInstanceOperation.ApplyRuntimeSettings;
                request.RuntimeSettings = new TargetRuntimeSettings { Name = "Probe renamed target" };
            }
            var reply = TargetInstanceCoordinator.Send(channel, target, root, request);
            File.WriteAllText(marker + ".result", new JavaScriptSerializer().Serialize(reply));
            return 0;
        }
        catch (Exception exception)
        {
            File.WriteAllText(marker + ".error", exception.ToString());
            return 1;
        }
    }

    private static TargetInstanceReply ExitRequest(TargetInstanceRecord instance, string root, TargetInstanceOperation operation, string operationId)
    {
        return TargetInstanceCoordinator.SendToInstance(instance, root,
            new TargetInstanceRequest { Operation = operation, OperationId = operationId });
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return;
            Thread.Sleep(25);
        }
        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
