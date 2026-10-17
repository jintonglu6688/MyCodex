using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationHostShutdownTests
    {
        [TestCase(CodexRuntimeSources.Bundled)]
        [TestCase(CodexRuntimeSources.Wsl)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh)]
        public async Task UnusedHostShutdownDoesNotCreateDirectoriesOrStartAndCannotRestart(string runtimeSource)
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "unused-auth-" + Guid.NewGuid().ToString("N"));
            var host = new LocalAuthenticationHost(runtimeSource,
                Path.Combine(root, "missing.exe"), Path.Combine(root, "home"), Path.Combine(root, "store"));
            await host.ShutdownAsync(CancellationToken.None);
            Assert.That(Directory.Exists(root), Is.False);
            Assert.ThrowsAsync<OperationCanceledException>(() => host.RequestAsync("status", new Dictionary<string, object>(), CancellationToken.None));
        }

        [Test]
        public async Task ExitCancelsOnlyOwnedLoginsAndPreservesAnExternalActiveLogin()
        {
            var canceled = new List<string>();
            var calls = new List<string>();
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> request = (method, args, token) =>
            {
                calls.Add(method);
                if (method == "gui/account/login/cancel")
                {
                    canceled.Add((string)args["loginId"]);
                    throw new MyCodex.Application.Interfaces.AuthenticationCenterException("login_not_found");
                }
                Assert.That(args["forUpdate"], Is.True);
                throw new MyCodex.Application.Interfaces.AuthenticationCenterException("login_in_progress");
            };
            await LocalAuthenticationHost.CancelOwnedLoginsAsync(request, new[] { "our-login" }, CancellationToken.None);
            Assert.That(await LocalAuthenticationHost.StopBackendAsync(request, CancellationToken.None), Is.False);
            Assert.That(canceled, Is.EqualTo(new[] { "our-login" }));
            Assert.That(calls, Is.EqualTo(new[] { "gui/account/login/cancel", "backend/shutdown" }));
        }

        [Test]
        public async Task LastUserShutdownWaitsForActiveRouteAndPreservesTheRecoveryFlag()
        {
            var calls = 0;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
            {
                Assert.That(await LocalAuthenticationHost.StopBackendAsync((method, args, token) =>
                {
                    Assert.That(token, Is.EqualTo(timeout.Token));
                    Assert.That(method, Is.EqualTo("backend/shutdown"));
                    Assert.That(args["forUpdate"], Is.True);
                    Assert.That(args["detach"], Is.True);
                    if (++calls == 1) throw new MyCodex.Application.Interfaces.AuthenticationCenterException("route_requests_active");
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "status", "shutting_down" } });
                }, timeout.Token), Is.True);
            }
            Assert.That(calls, Is.EqualTo(2));
        }
    }

    public sealed partial class AuthenticationCoreIntegrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        [Explicit("Requires MYCODEX_AUTH_CORE_TEST_PATH; uses isolated synthetic provider data and no upstream requests.")]
        public async Task ApplicationExitStopsResidentAndNextStartPreservesProvider(bool conversion)
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-exit-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            var store = Path.Combine(root, "private");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(store);
            var start = new ProcessStartInfo(executable,
                "serve --data-dir " + WindowsCommandLineArgumentEscaper.Quote(store) +
                " --codex-home " + WindowsCommandLineArgumentEscaper.Quote(home))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var resident = Process.Start(start))
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                try
                {
                    Assert.That(await resident.StandardOutput.ReadLineAsync(), Does.Contain("ready"));
                    var host = NewHost(executable, home, store);
                    await new AuthenticationCenterClient("Test", host.RequestAsync).SaveAsync(
                        Edit("test", "Test", "test-model", conversion ? "chat_completions" : "responses"), timeout.Token);
                    var configPath = Path.Combine(home, "config.toml");
                    var config = File.ReadAllText(configPath);
                    await host.ShutdownAsync(timeout.Token);
                    Assert.That(resident.WaitForExit(3000), Is.True, "The resident backend must exit before shutdown completes.");
                    Assert.That(File.ReadAllText(configPath), Is.EqualTo(config), "Exit must preserve the selected provider and conversion recovery state.");
                    await host.ShutdownAsync(timeout.Token);
                    Assert.ThrowsAsync<OperationCanceledException>(() => Call(host, "status"));

                    var reopened = NewHost(executable, home, store);
                    try
                    {
                        var status = await Call(reopened, "status");
                        var route = (IDictionary<string, object>)status["route"];
                        Assert.That(route["running"], Is.EqualTo(conversion));
                        if (conversion) Assert.That(route["accepting"], Is.EqualTo(true));
                        var providers = await new AuthenticationCenterClient("Test", reopened.RequestAsync).ReadAsync(timeout.Token);
                        Assert.That(providers.CurrentProviderId, Is.EqualTo("test"));
                    }
                    finally { await reopened.ShutdownAsync(timeout.Token); }
                }
                finally
                {
                    // Only this test's explicitly created, isolated child can be terminated on failure.
                    if (!resident.HasExited) { resident.Kill(); resident.WaitForExit(); }
                }
            }
        }
    }
}
