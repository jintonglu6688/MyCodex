using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WslAuthenticationHostResolveTests
    {
        [Test]
        public void UnverifiedHost_IsNeverExecuted()
        {
            var executor = new RecordingWslExecutor();
            var host = new WslAuthenticationHost(new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" }, executor);

            Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                host.RequestAsync("status", new Dictionary<string, object>(), CancellationToken.None));

            Assert.That(executor.Operations, Is.EqualTo(new[] { "mycodex-auth-package" }));
        }

        private sealed class RecordingWslExecutor : IWslCommandExecutor
        {
            public List<string> Operations { get; } = new List<string>();

            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(WslAppServerConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, CancellationToken cancellationToken, string standardInput,
                params string[] arguments)
            {
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, cancellationToken, arguments);
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(WslAppServerConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, params string[] arguments)
            {
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, CancellationToken.None, arguments);
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(WslAppServerConfigSnapshot config, string script,
                string operationName, int timeoutMilliseconds, CancellationToken cancellationToken, params string[] arguments)
            {
                Operations.Add(operationName);
                // A tampered install: its manifest does not describe a known release.
                return Task.FromResult(new WslCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "x86_64\n" + new string('a', 64) + "\n{\"schemaVersion\":1}",
                    StandardError = string.Empty
                });
            }
        }
    }
}
