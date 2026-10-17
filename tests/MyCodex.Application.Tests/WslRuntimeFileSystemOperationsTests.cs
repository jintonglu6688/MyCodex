using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WslRuntimeFileSystemOperationsTests
    {
        [Test]
        public void CreateDirectoryAsync_PassesParentAndNameSeparately()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0 }
            };
            var service = new WslRuntimeFileSystemOperations(new WslAppServerConfigSnapshot(), executor);

            var result = service.CreateDirectoryAsync("/home/demo", "docs").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-wsl-create-directory"));
            Assert.That(executor.Script, Does.StartWith("set -eu\n"));
            Assert.That(executor.Script, Does.Not.Contain("eval"));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/home/demo", "docs" }));
        }

        [Test]
        public void EnsureDirectoryAsync_UsesRecursiveIdempotentScriptAndPositionalPath()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0 }
            };
            var service = new WslRuntimeFileSystemOperations(new WslAppServerConfigSnapshot(), executor);

            var result = service.EnsureDirectoryAsync("/home/demo/Office Docs").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-wsl-ensure-directory"));
            Assert.That(executor.Script, Does.Contain("mkdir -p -- \"$1\""));
            Assert.That(executor.Script, Does.Not.Contain("eval"));
            Assert.That(executor.Arguments, Is.EqualTo(new[] { "/home/demo/Office Docs" }));
        }

        [Test]
        public void RenameAsync_PassesPathAndNameSeparately()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0 }
            };
            var service = new WslRuntimeFileSystemOperations(new WslAppServerConfigSnapshot(), executor);

            var result = service.RenameAsync("/home/demo/old.txt", "new.txt").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-wsl-rename"));
            Assert.That(executor.Script, Does.Not.Contain("eval"));
            Assert.That(executor.Script, Does.Contain("[ ! -e \"$parent/$name\" ]"));
            Assert.That(executor.Script, Does.Contain("parent=$(dirname -- \"$target\")"));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/home/demo/old.txt", "new.txt" }));
        }

        [Test]
        public void DeleteAsync_PassesPathAndRecursiveFlagSeparately()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0 }
            };
            var service = new WslRuntimeFileSystemOperations(new WslAppServerConfigSnapshot(), executor);

            var result = service.DeleteAsync("/home/demo/docs", true).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-wsl-delete"));
            Assert.That(executor.Script, Does.Not.Contain("eval"));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/home/demo/docs", "recursive" }));
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
                ArgumentsByCall = new List<string[]>();
            }

            public WslCommandExecutionResult Result { get; set; }

            public string OperationName { get; private set; }

            public string Script { get; private set; }

            public string[] Arguments { get; private set; }

            public List<string[]> ArgumentsByCall { get; private set; }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                OperationName = operationName;
                Script = script;
                Arguments = arguments;
                ArgumentsByCall.Add(arguments);
                return Task.FromResult(Result ?? new WslCommandExecutionResult());
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
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
