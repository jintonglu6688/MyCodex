using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WslFileSystemBridgeTests
    {
        [Test]
        public void DirectoryExistsAsync_ReturnsTrueForZeroExitCodeAndPassesPathAsArgument()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new WslFileSystemBridge(executor);

            var exists = bridge.DirectoryExistsAsync(new WslAppServerConfigSnapshot(), "/home/tester/repo").GetAwaiter().GetResult();

            Assert.That(exists, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-test-directory"));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/home/tester/repo" }));
        }

        [Test]
        public void DirectoryExistsAsync_ReturnsFalseForMissingDirectory()
        {
            var bridge = new WslFileSystemBridge(new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 1 }
            });

            var exists = bridge.DirectoryExistsAsync(new WslAppServerConfigSnapshot(), "/missing").GetAwaiter().GetResult();

            Assert.That(exists, Is.False);
        }

        [Test]
        public void ResolveHomeDirectoryAsync_UsesSharedHomeScriptAndReturnsTrimmedAbsolutePath()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "  /home/alice  \n"
                }
            };
            var bridge = new WslFileSystemBridge(executor);

            var home = bridge.ResolveHomeDirectoryAsync(new WslAppServerConfigSnapshot()).GetAwaiter().GetResult();

            Assert.That(home, Is.EqualTo("/home/alice"));
            Assert.That(executor.Script, Is.EqualTo(UnixFileSystemBridgeScripts.ResolveHomeDirectoryScript));
            Assert.That(executor.Script, Is.EqualTo("printf '%s\\n' \"$HOME\""));
            Assert.That(executor.Script, Does.Not.Contain("USER"));
            Assert.That(executor.Arguments, Is.Empty);
        }

        [TestCase("")]
        [TestCase("relative/home")]
        [TestCase("/home/alice\nunexpected")]
        public void ResolveHomeDirectoryAsync_RejectsMissingOrNonAbsoluteOutput(string output)
        {
            var bridge = new WslFileSystemBridge(new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0, StandardOutput = output }
            });

            Assert.Throws<System.InvalidOperationException>(() =>
                bridge.ResolveHomeDirectoryAsync(new WslAppServerConfigSnapshot()).GetAwaiter().GetResult());
        }

        [Test]
        public void DeleteCodexSessionPathAsync_MapsUnsupportedPathExitCode()
        {
            var bridge = new WslFileSystemBridge(new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 73, StandardError = "blocked" }
            });

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                bridge.DeleteCodexSessionPathAsync(new WslAppServerConfigSnapshot(), "/tmp/demo.jsonl").GetAwaiter().GetResult());

            Assert.That(ex.Message, Does.Contain("Codex"));
        }

        [Test]
        public void ReadGitInfoAsync_ParsesGitOutput()
        {
            var bridge = new WslFileSystemBridge(new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "branch=main\nsha=1234567890abcdef\norigin=https://example.com/repo.git\n"
                }
            });

            var snapshot = bridge.ReadGitInfoAsync(new WslAppServerConfigSnapshot(), "/home/tester/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.Branch, Is.EqualTo("main"));
            Assert.That(snapshot.Sha, Is.EqualTo("1234567890abcdef"));
            Assert.That(snapshot.OriginUrl, Is.EqualTo("https://example.com/repo.git"));
        }

        [Test]
        public void ReadGitInfoAsync_UsesWorkspacePathVariable()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new WslFileSystemBridge(executor);

            bridge.ReadGitInfoAsync(new WslAppServerConfigSnapshot(), "/home/tester/repo").GetAwaiter().GetResult();

            Assert.That(executor.Script, Does.Contain("workspace_path=${1:-}"));
            Assert.That(executor.Script, Does.Not.Match("(?m)^path="));
        }

        [Test]
        public void ListDirectoryAsync_KeepsWslBase64ListingScript()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new WslFileSystemBridge(executor);

            bridge.ListDirectoryAsync(new WslAppServerConfigSnapshot(), "/home/demo").GetAwaiter().GetResult();

            Assert.That(executor.Script, Does.Contain("enc() { printf '%s' \"$1\" | base64 | tr -d '\\n'; }"));
            Assert.That(executor.Script, Does.Not.Contain("perl -MMIME::Base64=encode_base64"));
        }

        [Test]
        public void ReadUncommittedChangesAsync_ParsesStatusAndReadsDiffs()
        {
            var executor = new FakeWslCommandExecutor();
            executor.Results.Enqueue(new WslCommandExecutionResult
            {
                ExitCode = 0,
                StandardOutput = " M src/app.cs\0?? notes.txt\0"
            });
            executor.Results.Enqueue(new WslCommandExecutionResult { ExitCode = 0, StandardOutput = string.Empty });
            executor.Results.Enqueue(new WslCommandExecutionResult
            {
                ExitCode = 0,
                StandardOutput = "diff --git a/src/app.cs b/src/app.cs\n+added\n-removed\n"
            });
            var bridge = new WslFileSystemBridge(executor);

            var snapshot = bridge.ReadUncommittedChangesAsync(new WslAppServerConfigSnapshot(), "/home/tester/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.Files, Has.Count.EqualTo(2));
            Assert.That(snapshot.Files[0].FilePath, Is.EqualTo("src/app.cs"));
            Assert.That(snapshot.Files[0].ChangeKind, Is.EqualTo("modified"));
            Assert.That(snapshot.Files[0].AddedLineCount, Is.EqualTo(1));
            Assert.That(snapshot.Files[0].DeletedLineCount, Is.EqualTo(1));
            Assert.That(snapshot.Files[1].FilePath, Is.EqualTo("notes.txt"));
            Assert.That(snapshot.Files[1].ChangeKind, Is.EqualTo("untracked"));
            Assert.That(executor.ArgumentsByCall[1], Is.EquivalentTo(new[] { "/home/tester/repo", "src/app.cs" }));
        }

        [Test]
        public void WslGitCommandRunner_PassesWorkspaceAndGitArgumentsSeparately()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0, StandardOutput = "ok" }
            };
            var runner = new WslGitCommandRunner(new WslAppServerConfigSnapshot(), executor);

            var result = runner.Run("/home/tester/repo", new[] { "add", "--", "src/app file.txt" }, 12000);

            Assert.That(result.Success, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-wsl-git"));
            Assert.That(executor.Script, Does.Contain("git -C \"$workdir\" \"$@\""));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/home/tester/repo", "add", "--", "src/app file.txt" }));
        }

        [Test]
        public void WslGitCommandRunner_RedactsSensitiveOutput()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult
                {
                    ExitCode = 1,
                    StandardOutput = "https://user:pass@example.com/repo.git",
                    StandardError = "ghp_SECRET"
                }
            };
            var runner = new WslGitCommandRunner(new WslAppServerConfigSnapshot(), executor);

            var result = runner.Run("/home/tester/repo", new[] { "remote", "-v" }, 12000);

            Assert.That(result.StandardOutput, Is.EqualTo("https://***:***@example.com/repo.git"));
            Assert.That(result.StandardError, Is.EqualTo("***"));
        }

        [Test]
        public void WslGitEnvironmentService_ReadStatusUsesCommandKeysWithoutEval()
        {
            var executor = new FakeWslCommandExecutor
            {
                Result = new WslCommandExecutionResult { ExitCode = 0, StandardOutput = "ok" }
            };
            var service = new WslGitEnvironmentService(new WslAppServerConfigSnapshot(), executor);

            service.ReadStatusAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(executor.Script, Does.Not.Contain("eval"));
            Assert.That(executor.ArgumentsByCall[0], Is.EquivalentTo(new[] { "git-version" }));
            Assert.That(executor.ArgumentsByCall[1], Is.EquivalentTo(new[] { "git-path" }));
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
                ArgumentsByCall = new List<string[]>();
            }

            public WslCommandExecutionResult Result { get; set; }

            public Queue<WslCommandExecutionResult> Results { get; private set; }

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
                return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : (Result ?? new WslCommandExecutionResult()));
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
