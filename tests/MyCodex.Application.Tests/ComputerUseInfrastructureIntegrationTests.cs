using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.CSharp;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Timeout(30000)]
    [Explicit("Windows integration: starts only owned synthetic console processes and named pipes; no desktop actions or model requests.")]
    public sealed class ComputerUseInfrastructureIntegrationTests
    {
        private string root;
        private string fixturePath;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "cua-infrastructure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            fixturePath = Path.Combine(root, "OwnedCuaFixture.exe");
            using (var provider = new CSharpCodeProvider())
            {
                var options = new CompilerParameters(new[] { "System.dll", "System.Core.dll", "System.Web.Extensions.dll" }, fixturePath)
                {
                    GenerateExecutable = true,
                    CompilerOptions = "/platform:x64"
                };
                var results = provider.CompileAssemblyFromSource(options, FixtureSource);
                Assert.That(results.Errors.HasErrors, Is.False, string.Join(Environment.NewLine, results.Errors.Cast<CompilerError>()));
            }
        }

        [TearDown]
        public void TearDown()
        {
            // Windows may release a terminated executable's image mapping shortly after
            // the process handle is signalled. This only retries our unique fixture root.
            Assert.That(SpinWait.SpinUntil(() =>
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); return true; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }, 3000), Is.True, "An owned fixture file remained locked after cleanup.");
        }

        [Test]
        public async Task LeaseRejectsOtherInstancesAndStaleRelease()
        {
            using (var first = new ComputerUseDesktopLease())
            using (var second = new ComputerUseDesktopLease())
            {
                if (!await first.TryAcquireAsync("old")) Assert.Ignore("The desktop lease is in use by another MyCodex instance.");
                Assert.That(await second.TryAcquireAsync("other"), Is.False);
                first.Release("old");
                Assert.That(await first.TryAcquireAsync("new"), Is.True);
                first.Release("old");
                Assert.That(await first.TryAcquireAsync("new"), Is.True);
                Assert.That(await second.TryAcquireAsync("other"), Is.False);
                first.Release("new");
                Assert.That(await first.TryAcquireAsync("flush"), Is.True);
                first.Release("flush");
                first.Dispose();
                Assert.That(await second.TryAcquireAsync("other"), Is.True);
            }
        }

        [Test]
        public async Task LeaseExcludesAnotherProcess()
        {
            using (var lease = new ComputerUseDesktopLease())
            using (var fixture = StartFixture("mutex"))
            {
                try
                {
                    if (!await lease.TryAcquireAsync("owner")) Assert.Ignore("The desktop lease is already in use.");
                    Assert.That(await fixture.StandardOutput.ReadLineAsync(), Is.EqualTo("ready"));
                    fixture.StandardInput.WriteLine("acquire");
                    Assert.That(await fixture.StandardOutput.ReadLineAsync(), Is.EqualTo("False"));
                    lease.Dispose();
                    fixture.StandardInput.WriteLine("acquire");
                    Assert.That(await fixture.StandardOutput.ReadLineAsync(), Is.EqualTo("True"));
                }
                finally { StopFixture(fixture); }
            }
        }

        [Test]
        public void RuntimeResolverFollowsTheOwnedCmdWrapperWithoutSearchingPath()
        {
            var nativePath = Path.Combine(root, "codex.exe");
            File.Copy(fixturePath, nativePath);
            var launch = new AppServerLaunchInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
                "/d /s /c \"\"" + nativePath + "\" --child\"");
            using (var wrapper = Process.Start(new ProcessStartInfo(launch.FileName, launch.Arguments)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            }))
            {
                Process child = null;
                try
                {
                    var pidFile = Path.Combine(root, "child-pid.txt");
                    Assert.That(SpinWait.SpinUntil(() => File.Exists(pidFile), 5000), Is.True);
                    child = Process.GetProcessById(int.Parse(File.ReadAllText(pidFile)));
                    Assert.That(ComputerUseRuntimeExecutableResolver.Resolve(launch, wrapper), Is.EqualTo(nativePath));
                    Assert.That(ComputerUseRuntimeExecutableResolver.Resolve(new AppServerLaunchInfo(nativePath, "--child"), child), Is.EqualTo(nativePath));
                }
                finally
                {
                    if (child != null) { StopFixture(child); child.Dispose(); }
                    StopFixture(wrapper);
                }
            }
        }

        [Test]
        public async Task JobTerminationWaitsForOwnedParentAndChild()
        {
            using (var fixture = StartFixture("host"))
            using (var job = new ComputerUseProcessJob(fixture))
            {
                Process child = null;
                try
                {
                    await fixture.StandardOutput.ReadLineAsync();
                    fixture.StandardInput.WriteLine("{\"id\":\"1\",\"command\":\"spawn\"}");
                    var response = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await fixture.StandardOutput.ReadLineAsync());
                    child = Process.GetProcessById(Convert.ToInt32(response["result"]));
                    Assert.That(job.TerminateAndWait(), Is.True);
                    Assert.That(fixture.WaitForExit(5000), Is.True);
                    Assert.That(child.WaitForExit(5000), Is.True);
                }
                finally
                {
                    StopFixture(fixture);
                    if (child != null) { StopFixture(child); child.Dispose(); }
                }
            }
        }

        [Test]
        public async Task JobDisposalKillsDescendantsEvenAfterParentExits()
        {
            using (var fixture = StartFixture("host"))
            using (var job = new ComputerUseProcessJob(fixture))
            {
                Process child = null;
                try
                {
                    await fixture.StandardOutput.ReadLineAsync();
                    fixture.StandardInput.WriteLine("{\"id\":\"1\",\"command\":\"spawn\"}");
                    var response = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(await fixture.StandardOutput.ReadLineAsync());
                    child = Process.GetProcessById(Convert.ToInt32(response["result"]));
                    fixture.StandardInput.WriteLine("{\"id\":\"2\",\"command\":\"exit\"}");
                    Assert.That(fixture.WaitForExit(5000), Is.True);
                    job.Dispose();
                    Assert.That(child.WaitForExit(5000), Is.True);
                }
                finally
                {
                    StopFixture(fixture);
                    if (child != null) { StopFixture(child); child.Dispose(); }
                }
            }
        }

        [Test]
        public async Task HostControlTimeoutStopsTheHostInsteadOfRetrying()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "host");
            using (var host = CreateHost())
            {
                await host.WaitReadyAsync(CancellationToken.None);
                Assert.ThrowsAsync<InvalidOperationException>(async () => await host.RequestAsync("wait"));
                Assert.That(host.IsAlive, Is.False);
                Assert.ThrowsAsync<InvalidOperationException>(async () => await host.RequestAsync("echo"));
            }
        }

        [Test]
        public async Task HostEofFailsPendingRequestAndStopsHost()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "host");
            using (var host = CreateHost())
            {
                await host.WaitReadyAsync(CancellationToken.None);
                Assert.ThrowsAsync<InvalidOperationException>(async () => await host.RequestAsync("exit"));
                Assert.That(host.IsAlive, Is.False);
            }
        }

        [Test]
        public async Task HostFailureEvent_WritesSafeDiagnosticsToSessionTrace()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "host");
            using (var output = new StringWriter())
            using (var listener = new TextWriterTraceListener(output))
            {
                Trace.Listeners.Add(listener);
                try
                {
                    using (var host = CreateHost())
                    {
                        await host.WaitReadyAsync(CancellationToken.None);
                        await host.RequestAsync("diagnostic");
                        listener.Flush();
                        Assert.That(output.ToString(), Does.Contain("[ComputerUse] Event=fault; Code=CUA_EXECUTION_FAILED"));
                        Assert.That(output.ToString(), Does.Contain("Stage=executor_request; Method=launch_app; Approval=accept"));
                        Assert.That(output.ToString(), Does.Contain("Category=request_timeout"));
                        Assert.That(output.ToString(), Does.Not.Contain("private-diagnostic-data"));
                    }
                }
                finally { Trace.Listeners.Remove(listener); }
            }
        }

        [Test]
        public async Task CancellingControlTerminatesTheOwnedDescendants()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "host");
            using (var host = CreateHost())
            using (var cancellation = new CancellationTokenSource())
            {
                await host.WaitReadyAsync(CancellationToken.None);
                using (var child = Process.GetProcessById(Convert.ToInt32(await host.RequestAsync("spawn"))))
                {
                    try
                    {
                        cancellation.CancelAfter(100);
                        Assert.CatchAsync<OperationCanceledException>(async () => await host.RequestAsync("wait", token: cancellation.Token));
                        Assert.That(child.WaitForExit(5000), Is.True);
                        Assert.That(host.IsAlive, Is.False);
                    }
                    finally { StopFixture(child); }
                }
            }
        }

        [Test]
        public void HostStartupCancellationStopsTheOwnedProcess()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "silent");
            using (var host = CreateHost())
            using (var cancellation = new CancellationTokenSource(200))
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () => await host.WaitReadyAsync(cancellation.Token));
                Assert.That(host.IsAlive, Is.False);
            }
        }

        [Test]
        public void UnsupportedComponentContract_IsReportedBeforeHostReadiness()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "incompatible");
            using (var host = CreateHost())
            {
                var failure = Assert.ThrowsAsync<InvalidOperationException>(async () => await host.WaitReadyAsync(CancellationToken.None));
                Assert.That(failure.Message, Is.EqualTo("CUA_COMPONENT_CONTRACT"));
            }
        }

        [Test]
        public async Task PipeAclContainsOnlyCurrentUserAndSystem()
        {
            var name = "mycodex-cua-test-" + Guid.NewGuid().ToString("N");
            using (var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
            {
                var connected = pipe.WaitForConnectionAsync();
                ComputerUsePipeSecurity.Restrict(@"\\.\pipe\" + name);
                await connected;
                var security = pipe.GetAccessControl();
                var allowed = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>()
                    .Where(rule => rule.AccessControlType == AccessControlType.Allow).Select(rule => rule.IdentityReference.Value).Distinct().ToArray();
                Assert.That(allowed, Is.EquivalentTo(new[] { WindowsIdentity.GetCurrent().User.Value, "S-1-5-18" }));
            }
        }

        [Test]
        public async Task PolicyWriterCarriesRestrictionsIntoIsolatedHomeWithoutStartingAThread()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "policy");
            var policy = new Dictionary<string, object> { { "default_app_access", "deny" }, { "allow_persistent_authorization", false } };
            await ComputerUsePolicyWriter.WriteAsync(fixturePath, root, policy, CancellationToken.None);
            var capture = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(root, "policy-capture.json")));
            Assert.That(capture["home"], Is.EqualTo(root));
            Assert.That(capture["methods"], Is.EqualTo("initialize,initialized,config/batchWrite"));
            var request = (Dictionary<string, object>)capture["request"];
            var parameters = (Dictionary<string, object>)request["params"];
            var edit = (Dictionary<string, object>)((System.Collections.IList)parameters["edits"])[0];
            Assert.That(edit["keyPath"], Is.EqualTo("computer_use"));
            Assert.That(edit["mergeStrategy"], Is.EqualTo("replace"));
            Assert.That(((Dictionary<string, object>)edit["value"])["default_app_access"], Is.EqualTo("deny"));
        }

        [Test]
        public void PolicyWriterEofFailsInsteadOfClaimingPoliciesWereApplied()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "policy-exit");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await ComputerUsePolicyWriter.WriteAsync(
                fixturePath, root, new Dictionary<string, object>(), CancellationToken.None));
            Assert.That(File.Exists(Path.Combine(root, "policy-capture.json")), Is.False);
        }

        [Test]
        public void PolicyWriterCancellationDoesNotWaitForRequestTimeout()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "policy-wait");
            using (var cancellation = new CancellationTokenSource(200))
            {
                Assert.CatchAsync<OperationCanceledException>(async () => await ComputerUsePolicyWriter.WriteAsync(
                    fixturePath, root, new Dictionary<string, object>(), cancellation.Token));
            }
            Assert.That(File.Exists(Path.Combine(root, "policy-capture.json")), Is.False);
        }

        [Test]
        public void LegacyHostProtocol_CannotBypassExecutionPreparation()
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "host-v1");
            using (var host = CreateHost())
            {
                Assert.ThrowsAsync<InvalidOperationException>(() => host.WaitReadyAsync(CancellationToken.None));
                Assert.That(host.IsAlive, Is.False);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task HostPreparation_AcknowledgesOnlyAfterCallbackCompletes(bool allowed)
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), "host");
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var host = CreateHost(async (thread, turn, token) =>
            {
                Assert.That(thread, Is.EqualTo("thread"));
                Assert.That(turn, Is.EqualTo("turn"));
                entered.TrySetResult(true);
                await release.Task;
                if (!allowed) throw new InvalidOperationException("denied");
            }))
            {
                await host.WaitReadyAsync(CancellationToken.None);
                await host.RequestAsync("prepare");
                await entered.Task;
                Assert.That(await host.RequestAsync("prepared"), Is.Null);
                release.TrySetResult(true);
                object result = null;
                for (var i = 0; i < 100 && result == null; i++)
                {
                    await Task.Delay(10);
                    result = await host.RequestAsync("prepared");
                }
                Assert.That(result, Is.EqualTo(allowed));
            }
        }

        private ComputerUseHostProcess CreateHost(Func<string, string, CancellationToken, Task> prepare = null)
        {
            return new ComputerUseHostProcess(new ComputerUseComponents
            {
                NodePath = fixturePath, RuntimeRoot = root, SelectedCodexPath = fixturePath
            }, root, (code, thread) => { }, prepare);
        }

        private Process StartFixture(string mode)
        {
            File.WriteAllText(Path.Combine(root, "fixture-mode.txt"), mode);
            var process = Process.Start(new ProcessStartInfo(fixturePath)
            {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            });
            process.StandardInput.AutoFlush = true;
            return process;
        }

        private static void StopFixture(Process process)
        {
            try { process.StandardInput.Close(); } catch { }
            try { if (!process.WaitForExit(2000)) process.Kill(); } catch { }
            try { process.WaitForExit(2000); } catch { }
        }

        private const string FixtureSource = @"
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Web.Script.Serialization;
class OwnedCuaFixture {
    static void Main(string[] args) {
        if (args.Length > 0 && args[0] == ""--child"") { File.WriteAllText(""child-pid.txt"", Process.GetCurrentProcess().Id.ToString()); Thread.Sleep(60000); return; }
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        string mode = File.ReadAllText(""fixture-mode.txt"");
        if (mode == ""mutex"") {
            using (var mutex = new Mutex(false, @""Local\MyCodex.ComputerUse."" + WindowsIdentity.GetCurrent().User.Value + ""."" + Process.GetCurrentProcess().SessionId)) {
                bool held = false; Console.WriteLine(""ready"");
                try { while (Console.ReadLine() != null) { held = mutex.WaitOne(0); Console.WriteLine(held); } }
                finally { if (held) mutex.ReleaseMutex(); }
            }
            return;
        }
        var serializer = new JavaScriptSerializer();
        if (mode == ""incompatible"") { Console.WriteLine(serializer.Serialize(new { @event = ""fatal"", code = ""CUA_COMPONENT_CONTRACT"" })); return; }
        var methods = new List<string>();
        if (mode == ""host"" || mode == ""host-v1"") Console.WriteLine(serializer.Serialize(new { ready = true, protocolVersion = mode == ""host"" ? 2 : 1 }));
        object prepared = null;
        string line;
        while ((line = Console.ReadLine()) != null) {
            var request = serializer.Deserialize<Dictionary<string, object>>(line);
            object id; request.TryGetValue(""id"", out id);
            object result = true;
            if (mode.StartsWith(""policy"")) {
                if (mode == ""policy-exit"") return;
                if (mode == ""policy-wait"") continue;
                string method = (string)request[""method""]; methods.Add(method);
                if (method == ""config/batchWrite"") File.WriteAllText(""policy-capture.json"", serializer.Serialize(new { home = Environment.GetEnvironmentVariable(""CODEX_HOME""), methods = string.Join("","", methods), request }));
                result = new Dictionary<string, object>();
                if (id == null) continue;
            } else {
                string command = (string)request[""command""];
                if (command == ""prepare"") Console.WriteLine(serializer.Serialize(new { @event = ""prepare"", id = ""preparation"", thread = ""thread"", turn = ""turn"" }));
                if (command == ""prepareResult"") prepared = request[""prepared""];
                if (command == ""diagnostic"") Console.WriteLine(serializer.Serialize(new {
                    @event = ""fault"", code = ""CUA_EXECUTION_FAILED"", thread = ""private-diagnostic-data"",
                    message = ""private-diagnostic-data"", diagnostic = new {
                        stage = ""executor_request"", method = ""launch_app"", approval = ""accept"",
                        category = ""request_timeout"", errorType = ""Error"", elapsedMs = 15001,
                        message = ""private-diagnostic-data"", stack = ""private-diagnostic-data""
                    }
                }));
                if (command == ""prepared"") result = prepared;
                if (command == ""wait"") continue;
                if (command == ""exit"") return;
                if (command == ""spawn"") {
                    var child = Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName, ""--child"") { UseShellExecute = false, CreateNoWindow = true });
                    result = child.Id;
                }
            }
            Console.WriteLine(serializer.Serialize(new { id, result }));
        }
    }
}";
    }
}
