        using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class WslNetworkReadinessProbeTests
    {
        [Test]
        public void WaitForReadyAsync_ReturnsReadyWhenProbeSucceeds()
        {
            var executor = new FakeWslCommandExecutor();
            executor.Results.Enqueue(new WslCommandExecutionResult { ExitCode = 0 });
            var probe = new WslNetworkReadinessProbe(executor);

            var result = probe.WaitForReadyAsync(new WslAppServerConfigSnapshot(), 1000).GetAwaiter().GetResult();

            Assert.That(result.IsReady, Is.True);
            Assert.That(result.Attempts, Is.EqualTo(1));
            Assert.That(result.Host, Is.EqualTo("chatgpt.com"));
            Assert.That(executor.LastArguments, Is.EqualTo(new[] { "chatgpt.com" }));
        }

        [Test]
        public void WaitForReadyAsync_RetriesUntilProbeSucceeds()
        {
            var executor = new FakeWslCommandExecutor();
            executor.Results.Enqueue(new WslCommandExecutionResult { ExitCode = 1, StandardError = "try again" });
            executor.Results.Enqueue(new WslCommandExecutionResult { ExitCode = 0 });
            var probe = new WslNetworkReadinessProbe(executor);

            var result = probe.WaitForReadyAsync(new WslAppServerConfigSnapshot(), 1200).GetAwaiter().GetResult();

            Assert.That(result.IsReady, Is.True);
            Assert.That(result.Attempts, Is.EqualTo(2));
        }

        [Test]
        public void WaitForReadyAsync_ReturnsFailureAfterTimeout()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 1, StandardError = "dns failed" }
            };
            var probe = new WslNetworkReadinessProbe(executor);

            var result = probe.WaitForReadyAsync(new WslAppServerConfigSnapshot(), 20).GetAwaiter().GetResult();

            Assert.That(result.IsReady, Is.False);
            Assert.That(result.Attempts, Is.GreaterThanOrEqualTo(1));
            Assert.That(result.LastError, Does.Contain("dns failed"));
        }

        private sealed class FakeWslCommandExecutor : IWslCommandExecutor
        {
            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(
                WslAppServerConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, System.Threading.CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                throw new System.NotSupportedException("Unexpected input command in this test.");
            }

            public FakeWslCommandExecutor()
            {
                Results = new Queue<WslCommandExecutionResult>();
            }

            public WslCommandExecutionResult Result { get; set; }

            public Queue<WslCommandExecutionResult> Results { get; private set; }

            public string[] LastArguments { get; private set; }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                LastArguments = arguments;
                return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                System.Threading.CancellationToken cancellationToken,
                params string[] arguments)
            {
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }
        }
    }
}
