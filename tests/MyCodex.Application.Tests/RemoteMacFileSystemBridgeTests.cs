using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteMacFileSystemBridgeTests
    {
        [Test]
        public void DirectoryExistsAsync_ReturnsTrueForZeroExitCodeAndPassesPathAsArgument()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new RemoteMacFileSystemBridge(executor);

            var exists = bridge.DirectoryExistsAsync(new RemoteMacSshConfigSnapshot(), "/Users/tester/repo").GetAwaiter().GetResult();

            Assert.That(exists, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-test-remote-directory"));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/Users/tester/repo" }));
        }

        [Test]
        public void FileExistsAsync_ReturnsFalseForMissingFileAndPassesPathAsArgument()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 1 }
            };
            var bridge = new RemoteMacFileSystemBridge(executor);

            var exists = bridge.FileExistsAsync(new RemoteMacSshConfigSnapshot(), "/Users/tester/repo/notes.md").GetAwaiter().GetResult();

            Assert.That(exists, Is.False);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-test-remote-file"));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/Users/tester/repo/notes.md" }));
        }

        [Test]
        public void FileExistsAsync_ReturnsTrueForZeroExitCode()
        {
            var bridge = new RemoteMacFileSystemBridge(new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0 }
            });

            var exists = bridge.FileExistsAsync(new RemoteMacSshConfigSnapshot(), "/Users/tester/repo/notes.md").GetAwaiter().GetResult();

            Assert.That(exists, Is.True);
        }

        [Test]
        public void ResolveHomeDirectoryAsync_UsesSharedHomeScriptAndReturnsTrimmedAbsolutePath()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = " /Users/alice \n"
                }
            };
            var bridge = new RemoteMacFileSystemBridge(executor);

            var home = bridge.ResolveHomeDirectoryAsync(new RemoteMacSshConfigSnapshot()).GetAwaiter().GetResult();

            Assert.That(home, Is.EqualTo("/Users/alice"));
            Assert.That(executor.Script, Is.EqualTo(UnixFileSystemBridgeScripts.ResolveHomeDirectoryScript));
            Assert.That(executor.Script, Is.EqualTo("printf '%s\\n' \"$HOME\""));
            Assert.That(executor.Script, Does.Not.Contain("USER"));
            Assert.That(executor.Arguments, Is.Empty);
        }

        [Test]
        public void ResolveHomeDirectoryAsync_PropagatesRemoteAvailabilityFailure()
        {
            var bridge = new RemoteMacFileSystemBridge(new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 255,
                    StandardError = "SSH target unavailable."
                }
            });

            var exception = Assert.Throws<System.InvalidOperationException>(() =>
                bridge.ResolveHomeDirectoryAsync(new RemoteMacSshConfigSnapshot()).GetAwaiter().GetResult());

            Assert.That(exception.Message, Does.Contain("unavailable"));
        }

        [TestCase("")]
        [TestCase("Users/alice")]
        [TestCase("/Users/alice\nunexpected")]
        public void ResolveHomeDirectoryAsync_RejectsMissingOrNonAbsoluteOutput(string output)
        {
            var bridge = new RemoteMacFileSystemBridge(new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0, StandardOutput = output }
            });

            Assert.Throws<System.InvalidOperationException>(() =>
                bridge.ResolveHomeDirectoryAsync(new RemoteMacSshConfigSnapshot()).GetAwaiter().GetResult());
        }

        [Test]
        public void ListDirectoryAsync_UsesSingleProcessPerlListingScript()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new RemoteMacFileSystemBridge(executor);

            bridge.ListDirectoryAsync(new RemoteMacSshConfigSnapshot(), "/Users/tester").GetAwaiter().GetResult();

            Assert.That(executor.OperationName, Is.EqualTo("mycodex-list-remote-directory"));
            Assert.That(executor.Script, Does.Contain("perl -MMIME::Base64=encode_base64"));
            Assert.That(executor.Script, Does.Not.Contain("| base64"));
            Assert.That(executor.Script, Does.Not.Contain("stat -f %z"));
        }

        [Test]
        public void ReadGitInfoAsync_ParsesGitOutput()
        {
            var bridge = new RemoteMacFileSystemBridge(new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "branch=main\nsha=abcdef1234567890\norigin=git@example.com:repo.git\n"
                }
            });

            var snapshot = bridge.ReadGitInfoAsync(new RemoteMacSshConfigSnapshot(), "/Users/tester/repo").GetAwaiter().GetResult();

            Assert.That(snapshot.IsGitRepository, Is.True);
            Assert.That(snapshot.Branch, Is.EqualTo("main"));
            Assert.That(snapshot.Sha, Is.EqualTo("abcdef1234567890"));
            Assert.That(snapshot.OriginUrl, Is.EqualTo("git@example.com:repo.git"));
        }

        [Test]
        public void ReadGitInfoAsync_DoesNotOverwriteZshPathVariable()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new RemoteMacFileSystemBridge(executor);

            bridge.ReadGitInfoAsync(new RemoteMacSshConfigSnapshot(), "/Users/tester/repo").GetAwaiter().GetResult();

            Assert.That(executor.Script, Does.Contain("workspace_path=${1:-}"));
            Assert.That(executor.Script, Does.Not.Match("(?m)^path="));
        }

        [Test]
        public void ReadGitInfoAsync_UsesSharedWorkspaceScript()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new RemoteMacFileSystemBridge(executor);

            bridge.ReadGitInfoAsync(new RemoteMacSshConfigSnapshot(), "/Users/demo/repo").GetAwaiter().GetResult();

            Assert.That(executor.Script, Does.Contain("workspace_path=${1:-}"));
            Assert.That(executor.Script, Does.Contain("git -C \"$workspace_path\" rev-parse --is-inside-work-tree"));
        }

        [Test]
        public void ReadTextFileAsync_ReturnsRemoteOutput()
        {
            var bridge = new RemoteMacFileSystemBridge(new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = "hello\nworld"
                }
            });

            var text = bridge.ReadTextFileAsync(new RemoteMacSshConfigSnapshot(), "/Users/tester/.codex/skills/demo/SKILL.md").GetAwaiter().GetResult();

            Assert.That(text, Is.EqualTo("hello\nworld"));
        }

        [Test]
        public void DeleteCodexSessionPathAsync_PassesRemoteSessionPath()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0 }
            };
            var bridge = new RemoteMacFileSystemBridge(executor);

            bridge.DeleteCodexSessionPathAsync(
                new RemoteMacSshConfigSnapshot(),
                "/Users/tester/.codex/sessions/demo.jsonl").GetAwaiter().GetResult();

            Assert.That(executor.OperationName, Is.EqualTo("mycodex-delete-remote-codex-session"));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/Users/tester/.codex/sessions/demo.jsonl" }));
        }

        [Test]
        public void DeleteCodexSessionPathAsync_MapsUnsupportedPathExitCode()
        {
            var bridge = new RemoteMacFileSystemBridge(new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 73, StandardError = "blocked" }
            });

            var ex = Assert.Throws<System.InvalidOperationException>(() =>
                bridge.DeleteCodexSessionPathAsync(new RemoteMacSshConfigSnapshot(), "/tmp/demo.jsonl").GetAwaiter().GetResult());

            Assert.That(ex.Message, Does.Contain("Codex"));
        }

        [Test]
        public void RemoteMacGitCommandRunner_PassesWorkspaceAndGitArgumentsSeparately()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult { ExitCode = 0, StandardOutput = "ok" }
            };
            var runner = new RemoteMacGitCommandRunner(new RemoteMacSshConfigSnapshot(), executor);

            var result = runner.Run("/Users/tester/repo", new[] { "commit", "-m", "update app" }, 20000);

            Assert.That(result.Success, Is.True);
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-remote-git"));
            Assert.That(executor.Script, Does.Contain("git -C \"$workdir\" \"$@\""));
            Assert.That(executor.Arguments, Is.EquivalentTo(new[] { "/Users/tester/repo", "commit", "-m", "update app" }));
        }

        [Test]
        public void RemoteMacGitCommandRunner_RedactsSensitiveOutput()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 1,
                    StandardOutput = "https://user:pass@example.com/repo.git",
                    StandardError = "ghp_SECRET"
                }
            };
            var runner = new RemoteMacGitCommandRunner(new RemoteMacSshConfigSnapshot(), executor);

            var result = runner.Run("/Users/tester/repo", new[] { "remote", "-v" }, 12000);

            Assert.That(result.StandardOutput, Is.EqualTo("https://***:***@example.com/repo.git"));
            Assert.That(result.StandardError, Is.EqualTo("***"));
        }

        [Test]
        public void RemoteMacGitCommandRunner_BatchesStatusFollowUpCommands()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput =
                        BatchRecord("status", 0, "## main...origin/main [ahead 1]\0 M src/app.cs\0", string.Empty) +
                        BatchRecord("diff-numstat", 0, "1\t0\tsrc/app.cs\0", string.Empty) +
                        BatchRecord("head-sha", 0, "abc123\n", string.Empty)
                }
            };
            var runner = new RemoteMacGitCommandRunner(new RemoteMacSshConfigSnapshot(), executor);

            var status = runner.Run("/Users/tester/repo", new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all", "--no-renames" }, 15000);
            var stats = runner.Run("/Users/tester/repo", new[] { "diff", "--numstat", "--no-renames", "-z", "--no-ext-diff" }, 15000);
            var sha = runner.Run("/Users/tester/repo", new[] { "rev-parse", "--short", "HEAD" }, 15000);

            Assert.That(executor.ArgumentsByCall, Has.Count.EqualTo(1));
            Assert.That(executor.OperationName, Is.EqualTo("mycodex-remote-git-status-batch"));
            Assert.That(status.StandardOutput, Does.Contain("main"));
            Assert.That(stats.StandardOutput, Does.Contain("src/app.cs"));
            Assert.That(sha.StandardOutput, Is.EqualTo("abc123\n"));
        }

        [Test]
        public void RemoteMacGitEnvironmentService_ReadStatusUsesSingleBatchCommandWithoutEval()
        {
            var executor = new FakeRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput =
                        "git-version\t0\t" + Encode("git version 2.45.0") + "\n" +
                        "git-path\t0\t" + Encode("/usr/bin/git") + "\n" +
                        "user-name\t0\t" + Encode("Demo User") + "\n" +
                        "user-email\t0\t" + Encode("demo@example.com") + "\n" +
                        "credential-helper\t0\t" + Encode("osxkeychain") + "\n" +
                        "gcm\t127\t" + Encode(string.Empty) + "\n" +
                        "gcm-legacy\t0\t" + Encode("Git Credential Manager 2.6.0") + "\n" +
                        "ssh-keys\t0\t" + Encode("id_ed25519.pub") + "\n" +
                        "github-ssh\t1\t" + Encode("Hi demo! You've successfully authenticated.") + "\n"
                }
            };
            var service = new RemoteMacGitEnvironmentService(new RemoteMacSshConfigSnapshot(), executor);

            var snapshot = service.ReadStatusAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(executor.ArgumentsByCall, Has.Count.EqualTo(1));
            Assert.That(executor.Script, Does.Not.Contain("eval"));
            Assert.That(snapshot.GitVersion, Is.EqualTo("git version 2.45.0"));
            Assert.That(snapshot.GitPath, Is.EqualTo("/usr/bin/git"));
            Assert.That(snapshot.GlobalUserName, Is.EqualTo("Demo User"));
            Assert.That(snapshot.GlobalUserEmail, Is.EqualTo("demo@example.com"));
            Assert.That(snapshot.CredentialHelper, Is.EqualTo("osxkeychain"));
            Assert.That(snapshot.GitCredentialManager, Is.EqualTo("Git Credential Manager 2.6.0"));
            Assert.That(snapshot.SshKeys, Is.EqualTo("id_ed25519.pub"));
            Assert.That(snapshot.GitHubSsh, Does.Contain("successfully authenticated"));
        }

        private static string Encode(string value)
        {
            return System.Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static string BatchRecord(string key, int exitCode, string stdout, string stderr)
        {
            return key + "\t" + exitCode + "\t" + Encode(stdout) + "\t" + Encode(stderr) + "\n";
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
                Results = new Queue<RemoteSshCommandExecutionResult>();
                ArgumentsByCall = new List<string[]>();
            }

            public RemoteSshCommandExecutionResult Result { get; set; }

            public Queue<RemoteSshCommandExecutionResult> Results { get; private set; }

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
                return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : (Result ?? new RemoteSshCommandExecutionResult()));
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
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
