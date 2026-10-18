using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnixRemoteGitServicesTests
    {
        private const string KeyListing =
            "DIR\t/home/demo/.ssh\n" +
            "KEY\tid_ed25519.pub\t/home/demo/.ssh/id_ed25519.pub\t/home/demo/.ssh/id_ed25519\tyes\t256 SHA256:abc demo@host (ED25519)\n" +
            "PUB\tssh-ed25519 AAAAC3Nza demo@host\n";

        [TestCase("wsl", "mycodex-wsl-ssh-keys")]
        [TestCase("remote", "mycodex-remote-ssh-keys")]
        public void ReadKeysAsync_UsesTargetOperationAndParsesKeys(string target, string expectedOperation)
        {
            var recorder = new CommandRecorder { ExitCode = 0, StandardOutput = KeyListing };
            var service = CreateSshKeyService(target, recorder);

            var snapshot = service.ReadKeysAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(recorder.OperationName, Is.EqualTo(expectedOperation));
            Assert.That(recorder.TimeoutMilliseconds, Is.EqualTo(10000));
            Assert.That(recorder.Script, Does.Contain("dir=\"$HOME/.ssh\""));
            Assert.That(recorder.Arguments, Is.Empty);
            Assert.That(snapshot.SshDirectory, Is.EqualTo("/home/demo/.ssh"));
            Assert.That(snapshot.Keys, Has.Count.EqualTo(1));
            Assert.That(snapshot.Keys[0].Name, Is.EqualTo("id_ed25519.pub"));
            Assert.That(snapshot.Keys[0].PrivateKeyPath, Is.EqualTo("/home/demo/.ssh/id_ed25519"));
            Assert.That(snapshot.Keys[0].HasPrivateKey, Is.True);
            Assert.That(snapshot.Keys[0].KeyType, Is.EqualTo("ssh-ed25519"));
            Assert.That(snapshot.Keys[0].Comment, Is.EqualTo("demo@host"));
        }

        [TestCase("wsl", "mycodex-wsl-github-ssh")]
        [TestCase("remote", "mycodex-remote-github-ssh")]
        public void TestGitHubSshAsync_UsesTargetOperationAndReadsAuthenticationOutput(string target, string expectedOperation)
        {
            var recorder = new CommandRecorder
            {
                ExitCode = 1,
                StandardError = "Hi demo! You've successfully authenticated, but GitHub does not provide shell access."
            };
            var service = CreateSshKeyService(target, recorder);

            var result = service.TestGitHubSshAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(recorder.OperationName, Is.EqualTo(expectedOperation));
            Assert.That(recorder.TimeoutMilliseconds, Is.EqualTo(12000));
            Assert.That(recorder.Script, Does.Contain("-T git@github.com"));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ExitCode, Is.EqualTo(1));
            Assert.That(result.Output, Does.Contain("successfully authenticated"));
            Assert.That(result.Details, Does.StartWith(">> ssh -T git@github.com"));
        }

        [TestCase("wsl", "mycodex-wsl-git-clone", 30 * 60 * 1000)]
        [TestCase("remote", "mycodex-remote-git-clone", 0)]
        public void CloneAsync_UsesTargetOperationTimeoutAndPositionalArguments(string target, string expectedOperation, int expectedTimeout)
        {
            var recorder = new CommandRecorder { ExitCode = 0 };
            var service = CreateCloneService(target, recorder);

            var result = service.CloneAsync(
                new GitCloneRequest
                {
                    RepositoryUrl = "https://github.com/demo/repo.git",
                    TargetParentDirectory = "/home/demo/src",
                    TargetFolderName = "repo",
                    RecurseSubmodules = true
                },
                null,
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(recorder.OperationName, Is.EqualTo(expectedOperation));
            Assert.That(recorder.TimeoutMilliseconds, Is.EqualTo(expectedTimeout));
            Assert.That(recorder.Script, Is.EqualTo(UnixGitCloneScript.CloneScript));
            Assert.That(recorder.Arguments, Is.EqualTo(new[] { "https://github.com/demo/repo.git", "/home/demo/src", "repo", "true" }));
            Assert.That(result.Success, Is.True);
            Assert.That(result.TargetPath, Is.EqualTo("/home/demo/src/repo"));
        }

        [TestCase("wsl", 15000, "set -eu\n")]
        [TestCase("remote", 18000, "emulate -L sh 2>/dev/null || true\nset -eu\n")]
        public void RuntimeFileOperations_UseTargetTimeoutAndShellPrefix(string target, int expectedTimeout, string expectedScriptStart)
        {
            var recorder = new CommandRecorder { ExitCode = 0 };
            var service = CreateRuntimeFileSystemOperations(target, recorder);

            var result = service.CreateFileAsync("/home/demo", "notes.md").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(recorder.TimeoutMilliseconds, Is.EqualTo(expectedTimeout));
            Assert.That(recorder.Script, Does.StartWith(expectedScriptStart));
        }

        [TestCase("wsl")]
        [TestCase("remote")]
        public void RuntimeFileOperations_ReportExecutorFailureText(string target)
        {
            var recorder = new CommandRecorder { ExitCode = 1, StandardError = "  Permission denied  " };
            var service = CreateRuntimeFileSystemOperations(target, recorder);

            var result = service.CreateDirectoryAsync("/home/demo", "docs").GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Message, Is.EqualTo("Permission denied"));
        }

        [Test]
        public void Constructors_RejectMissingExecutor()
        {
            Assert.Throws<ArgumentNullException>(() => new WslGitSshKeyService(new WslAppServerConfigSnapshot(), null));
            Assert.Throws<ArgumentNullException>(() => new RemoteMacGitSshKeyService(new RemoteMacSshConfigSnapshot(), null));
            Assert.Throws<ArgumentNullException>(() => new WslGitCloneService(new WslAppServerConfigSnapshot(), null));
            Assert.Throws<ArgumentNullException>(() => new RemoteMacGitCloneService(new RemoteMacSshConfigSnapshot(), null));
            Assert.Throws<ArgumentNullException>(() => new WslRuntimeFileSystemOperations(new WslAppServerConfigSnapshot(), null));
            Assert.Throws<ArgumentNullException>(() => new RemoteMacRuntimeFileSystemOperations(new RemoteMacSshConfigSnapshot(), null));
        }

        private static IGitSshKeyService CreateSshKeyService(string target, CommandRecorder recorder)
        {
            return target == "wsl"
                ? (IGitSshKeyService)new WslGitSshKeyService(null, new FakeWslCommandExecutor(recorder))
                : new RemoteMacGitSshKeyService(null, new FakeRemoteSshCommandExecutor(recorder));
        }

        private static IGitCloneService CreateCloneService(string target, CommandRecorder recorder)
        {
            return target == "wsl"
                ? (IGitCloneService)new WslGitCloneService(null, new FakeWslCommandExecutor(recorder))
                : new RemoteMacGitCloneService(null, new FakeRemoteSshCommandExecutor(recorder));
        }

        private static IRuntimeFileSystemOperations CreateRuntimeFileSystemOperations(string target, CommandRecorder recorder)
        {
            return target == "wsl"
                ? (IRuntimeFileSystemOperations)new WslRuntimeFileSystemOperations(null, new FakeWslCommandExecutor(recorder))
                : new RemoteMacRuntimeFileSystemOperations(null, new FakeRemoteSshCommandExecutor(recorder));
        }

        private sealed class CommandRecorder
        {
            public int ExitCode { get; set; }

            public string StandardOutput { get; set; }

            public string StandardError { get; set; }

            public string Script { get; private set; }

            public string OperationName { get; private set; }

            public int TimeoutMilliseconds { get; private set; }

            public string[] Arguments { get; private set; }

            public void Record(object config, string script, string operationName, int timeoutMilliseconds, string[] arguments)
            {
                Assert.That(config, Is.Not.Null);
                Script = script;
                OperationName = operationName;
                TimeoutMilliseconds = timeoutMilliseconds;
                Arguments = arguments;
            }
        }

        private sealed class FakeWslCommandExecutor : IWslCommandExecutor
        {
            private readonly CommandRecorder _recorder;

            public FakeWslCommandExecutor(CommandRecorder recorder)
            {
                _recorder = recorder;
            }

            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                string standardInput,
                params string[] arguments)
            {
                throw new NotSupportedException("Unexpected input command in this test.");
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                _recorder.Record(config, script, operationName, timeoutMilliseconds, arguments);
                return Task.FromResult(new WslCommandExecutionResult
                {
                    ExitCode = _recorder.ExitCode,
                    StandardOutput = _recorder.StandardOutput,
                    StandardError = _recorder.StandardError
                });
            }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                throw new NotSupportedException("Unexpected cancellable command in this test.");
            }
        }

        private sealed class FakeRemoteSshCommandExecutor : IRemoteSshCommandExecutor
        {
            private readonly CommandRecorder _recorder;

            public FakeRemoteSshCommandExecutor(CommandRecorder recorder)
            {
                _recorder = recorder;
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                string standardInput,
                params string[] arguments)
            {
                throw new NotSupportedException("Unexpected input command in this test.");
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                _recorder.Record(config, script, operationName, timeoutMilliseconds, arguments);
                return Task.FromResult(new RemoteSshCommandExecutionResult
                {
                    ExitCode = _recorder.ExitCode,
                    StandardOutput = _recorder.StandardOutput,
                    StandardError = _recorder.StandardError
                });
            }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                CancellationToken cancellationToken,
                params string[] arguments)
            {
                throw new NotSupportedException("Unexpected cancellable command in this test.");
            }
        }
    }
}
