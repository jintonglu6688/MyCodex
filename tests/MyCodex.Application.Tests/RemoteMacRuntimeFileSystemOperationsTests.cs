using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteMacRuntimeFileSystemOperationsTests
    {
        [Test]
        public void EnsureDirectoryAsync_UsesRecursiveIdempotentScriptAndPositionalPath()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0 }
            };
            var service = new RemoteMacRuntimeFileSystemOperations(new RemoteMacSshConfigSnapshot(), executor);

            var result = service.EnsureDirectoryAsync("/Users/demo/Office Docs").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-remote-ensure-directory"));
            Assert.That(executor.Script, Does.Contain("mkdir -p -- \"$1\""));
            Assert.That(executor.Script, Does.Not.Contain("eval"));
            Assert.That(executor.Arguments, Is.EqualTo(new[] { "/Users/demo/Office Docs" }));
        }

        private sealed class FakeRemoteSshCommandExecutor : IRemoteSshCommandExecutor
        {
            public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
                RemoteMacSshConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, System.Threading.CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                throw new System.NotSupportedException("Unexpected input command in this test.");
            }

            public FakeRemoteSshCommandExecutor()
            {
                ArgumentsByCall = new List<string[]>();
            }

            public RemoteSshCommandExecutionResult Result { get; set; }

            public string OperationName { get; private set; }

            public string Script { get; private set; }

            public string[] Arguments { get; private set; }

            public List<string[]> ArgumentsByCall { get; private set; }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                OperationName = operationName;
                Script = script;
                Arguments = arguments;
                ArgumentsByCall.Add(arguments);
                return Task.FromResult(Result ?? new RemoteSshCommandExecutionResult());
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                return ExecuteAsync(config, script, operationName, timeoutMilliseconds, arguments);
            }
        }
    }
}
