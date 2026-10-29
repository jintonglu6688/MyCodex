using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    [Timeout(30000)]
    [Explicit("Windows integration: real owned Node host and synthetic authorization pipe; no browser, desktop action, account, or model request.")]
    public sealed class BrowserUseHostProcessIntegrationTests
    {
        [Test]
        public async Task BlockingPolicyCallbackCannotBlockReadingTheNativeEndReply()
        {
            var working = Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser-host-reader-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(working);
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                try
                {
                    var bundle = ComputerUseBundle.Load();
                    var runtime = Path.Combine(bundle.RuntimeDirectory, "cua_node");
                    var codex = Directory.GetFiles(RepositorySource.GetPath("runtime", "codex", "0.164.0"),
                        "codex.exe", SearchOption.AllDirectories).Single();
                    var components = new ComputerUseComponents
                    {
                        NodePath = Path.Combine(runtime, "bin", "node.exe"), RuntimeRoot = runtime,
                        SelectedCodexPath = codex
                    };
                    using (var host = new ComputerUseHostProcess(components, working, (_, __) => { },
                        prepareBrowser: (thread, turn, executor, token) =>
                        {
                            entered.Set();
                            if (!release.Wait(10000)) throw new TimeoutException("Policy test barrier was not released.");
                            return Task.FromResult(true);
                        }))
                    {
                        Task<object> ending = null;
                        try
                        {
                            await host.WaitReadyAsync(CancellationToken.None);
                            var native = await host.RequestAsync("allocate", new Dictionary<string, object>
                            {
                                { "helperHome", Path.Combine(working, "helper") }
                            }) as IDictionary<string, object>;
                            var slot = (string)native["slot"];
                            await host.RequestAsync("publish", Slot(slot));
                            var access = await host.RequestAsync("allocateBrowser", Slot(slot)) as IDictionary<string, object>;
                            var pipePath = (string)access["pipePath"];
                            ComputerUsePipeSecurity.Restrict(pipePath);
                            await host.RequestAsync("publishBrowser", Slot(slot));
                            await host.RequestAsync("bind", new Dictionary<string, object> { { "slot", slot }, { "thread", "thread-1" } });
                            await host.RequestAsync("begin", new Dictionary<string, object> { { "slot", slot }, { "turn", "turn-1" } });
                            Assert.That(pipePath.StartsWith(@"\\.\pipe\mycodex-browser-access-", StringComparison.Ordinal), Is.True);
                            using (var client = new NamedPipeClientStream(".", pipePath.Substring(9), PipeDirection.InOut, PipeOptions.Asynchronous))
                            {
                                await client.ConnectAsync(3000);
                                var request = new Dictionary<string, object>
                                {
                                    { "jsonrpc", "2.0" }, { "id", 1 }, { "method", "browser/access" },
                                    { "params", new Dictionary<string, object>
                                        {
                                            { "executorId", access["executorId"] }, { "sessionId", "thread-1" },
                                            { "turnId", "turn-1" }, { "nonce", new string('a', 64) }
                                        }
                                    }
                                };
                                var bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(request));
                                var header = BitConverter.GetBytes(bytes.Length);
                                await client.WriteAsync(header, 0, header.Length);
                                await client.WriteAsync(bytes, 0, bytes.Length);
                                await client.FlushAsync();
                                Assert.That(await Task.Run(() => entered.Wait(3000)), Is.True,
                                    "The real pipe must reach the parent policy callback before testing its reader.");
                                ending = host.RequestAsync("end", new Dictionary<string, object> { { "slot", slot }, { "turn", "turn-1" } });
                                var completed = await Task.WhenAny(ending, Task.Delay(2000));
                                Assert.That(completed, Is.SameAs(ending),
                                    "A blocked policy callback must not block the stdout reader from consuming the end reply.");
                                Assert.That(await ending, Is.EqualTo(true));
                                Assert.That(host.IsAlive, Is.True);
                            }
                        }
                        finally
                        {
                            release.Set();
                            if (ending != null) await ending;
                        }
                    }
                }
                finally
                {
                    release.Set();
                    Assert.That(SpinWait.SpinUntil(() =>
                    {
                        try { if (Directory.Exists(working)) Directory.Delete(working, true); return true; }
                        catch (IOException) { return false; }
                        catch (UnauthorizedAccessException) { return false; }
                    }, 3000), Is.True, "Owned host fixture files remained locked after cleanup.");
                }
            }
        }

        private static Dictionary<string, object> Slot(string slot)
        {
            return new Dictionary<string, object> { { "slot", slot } };
        }
    }
}
