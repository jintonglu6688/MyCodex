using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerClientStartupDisposalTests
    {
        [Test]
        public async Task DisposalDoesNotWaitForStartupAndPreventsLateConnection()
        {
            using (var config = new PendingConfigStore())
            using (var client = new AppServerClient(new CodexRuntimeManager(new AppConfigService(
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "startup-dispose-" + Guid.NewGuid().ToString("N")))), config))
            {
                var connecting = client.ConnectAsync();
                try
                {
                    Assert.That(config.Entered.Wait(3000), Is.True);
                    var elapsed = Stopwatch.StartNew();
                    client.Dispose();
                    Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(1)));
                    Assert.That(connecting.IsCompleted, Is.False, "The blocked load has not returned yet.");
                }
                finally { config.Release.Set(); }
                var error = Assert.ThrowsAsync<ObjectDisposedException>(async () => await connecting);
                Assert.That(error.ObjectName, Is.EqualTo(nameof(AppServerClient)));
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.ConnectAsync());
                Assert.ThrowsAsync<ObjectDisposedException>(async () => await client.RestartAsync());
                Assert.That(config.LoadCalls, Is.EqualTo(1), "Disposed clients must not start resolving another runtime.");
                await Task.CompletedTask;
            }
        }

        private sealed class PendingConfigStore : IAppConfigStore, IDisposable
        {
            internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
            internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
            internal int LoadCalls;
            public AppConfigSnapshot Load()
            {
                Interlocked.Increment(ref LoadCalls);
                Entered.Set();
                if (!Release.Wait(5000)) throw new TimeoutException("The test did not release startup.");
                return new AppConfigSnapshot();
            }
            public void Save(AppConfigSnapshot snapshot) => throw new NotSupportedException();
            public void Dispose() { Entered.Dispose(); Release.Dispose(); }
        }
    }
}
