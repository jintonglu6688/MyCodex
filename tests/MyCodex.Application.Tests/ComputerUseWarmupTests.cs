using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.BrowserUse;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseWarmupTests
    {
        private readonly List<string> calls = new List<string>();
        private readonly BrowserUseComponents browser = new BrowserUseComponents();

        [SetUp]
        public void SetUp() { calls.Clear(); }

        [Test]
        public async Task Warmup_VerifiesBrowserBeforeThreadPreparationAndReusesTheResult()
        {
            var loads = 0;
            using (var manager = Manager(_ => { loads++; return browser; }))
            {
                await manager.WarmupAsync(CancellationToken.None);
                Assert.That(CachedBrowser(manager), Is.SameAs(browser), "The first thread must find an already verified browser package.");
                Assert.That(await LoadForThread(manager), Is.SameAs(browser));
                await manager.WarmupAsync(CancellationToken.None);
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(calls, Is.EqualTo(new[] { "config/read" }));
                Assert.That(FormTestDriver.Field<object>(manager, "_host"), Is.Null, "Static verification must not start the control host.");
            }
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task Warmup_DoesNotLoadBrowserWhenSwitchOrPluginIsDisabled(bool enabled, bool pluginEnabled)
        {
            using (var manager = Manager(_ => throw new AssertionException("Disabled browser was loaded."), enabled, pluginEnabled))
            {
                await manager.WarmupAsync(CancellationToken.None);
                Assert.That(CachedBrowser(manager), Is.Null);
                Assert.That(FormTestDriver.Field<object>(manager, "_host"), Is.Null);
            }
        }

        [Test]
        public async Task Warmup_FailurePreservesNativeComponentsAndThreadPreparationCanRetry()
        {
            var loads = 0;
            using (var manager = Manager(_ => ++loads == 1 ? throw new InvalidDataException("Fixture corruption") : browser))
            {
                var native = FormTestDriver.Field<ComputerUseComponents>(manager, "_components");
                await manager.WarmupAsync(CancellationToken.None);
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(CachedBrowser(manager), Is.Null);
                Assert.That(FormTestDriver.Field<ComputerUseComponents>(manager, "_components"), Is.SameAs(native));
                Assert.That(await LoadForThread(manager), Is.SameAs(browser));
                Assert.That(loads, Is.EqualTo(2));
            }
        }

        [Test, Timeout(10000)]
        public async Task Warmup_CancellationDiscardsVerificationResultAndAllowsRetry()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loads = 0;
            using (var cancellation = new CancellationTokenSource())
            using (var manager = Manager(_ =>
            {
                if (Interlocked.Increment(ref loads) == 1)
                {
                    entered.TrySetResult(true);
                    release.Task.GetAwaiter().GetResult();
                }
                return browser;
            }))
            {
                var warmup = manager.WarmupAsync(cancellation.Token);
                try
                {
                    Assert.That(await Task.WhenAny(entered.Task, warmup), Is.SameAs(entered.Task), "Warmup must reach browser verification.");
                    cancellation.Cancel();
                }
                finally { release.TrySetResult(true); }
                await warmup;
                Assert.That(CachedBrowser(manager), Is.Null);
                await manager.WarmupAsync(CancellationToken.None);
                Assert.That(CachedBrowser(manager), Is.SameAs(browser));
                Assert.That(loads, Is.EqualTo(2));
            }
        }

        [Test, Timeout(10000)]
        public async Task Warmup_ConcurrentCallsVerifyOnlyOnce()
        {
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var loads = 0;
            using (var manager = Manager(_ =>
            {
                Interlocked.Increment(ref loads);
                entered.TrySetResult(true);
                release.Task.GetAwaiter().GetResult();
                return browser;
            }))
            {
                var first = manager.WarmupAsync(CancellationToken.None);
                Task second = null;
                try
                {
                    Assert.That(await Task.WhenAny(entered.Task, first), Is.SameAs(entered.Task), "Warmup must reach browser verification.");
                    second = manager.WarmupAsync(CancellationToken.None);
                }
                finally { release.TrySetResult(true); }
                await Task.WhenAll(first, second);
                Assert.That(CachedBrowser(manager), Is.SameAs(browser));
                Assert.That(loads, Is.EqualTo(1));
                Assert.That(calls, Is.EqualTo(new[] { "config/read" }));
            }
        }

        private ComputerUseSessionManager Manager(Func<string, BrowserUseComponents> loader, bool enabled = true, bool pluginEnabled = true)
        {
            var config = new Dictionary<string, object> { { "plugins", new Dictionary<string, object>
            {
                { "computer-use@mycodex-computer-use", new Dictionary<string, object> { { "enabled", false } } },
                { "browser-use@mycodex-browser-use", new Dictionary<string, object> { { "enabled", pluginEnabled } } }
            } } };
            var manager = new ComputerUseSessionManager("unused", "unused", "unused", (method, parameters, token) =>
            {
                calls.Add(method);
                Assert.That(method, Is.EqualTo("config/read"), "Warmup must not install plugins or write execution policy.");
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "config", config } });
            }, null, browserEnabled: () => enabled, browserComponentsLoader: loader);
            FormTestDriver.SetField(manager, "_components", new ComputerUseComponents());
            return manager;
        }

        private static BrowserUseComponents CachedBrowser(ComputerUseSessionManager manager) =>
            FormTestDriver.Field<BrowserUseComponents>(manager, "_browserComponents");

        private static Task<BrowserUseComponents> LoadForThread(ComputerUseSessionManager manager) =>
            FormTestDriver.Invoke<Task<BrowserUseComponents>>(manager, "LoadBrowserComponentsAsync", CancellationToken.None);
    }
}
