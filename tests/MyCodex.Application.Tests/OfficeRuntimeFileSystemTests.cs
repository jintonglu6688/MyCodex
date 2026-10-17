using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeRuntimeFileSystemTests
    {
        [Test]
        public void WindowsOperationsUseLocalFileSystem()
        {
            var path = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "OfficeRuntimeFileSystemTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            try
            {
                File.WriteAllText(Path.Combine(path, "notes.txt"), "notes");
                var wsl = new FakeWslFileSystemBridge();
                var remote = new FakeRemoteFileSystemBridge();
                var fileSystem = new OfficeRuntimeFileSystem(wsl, remote);

                var exists = fileSystem.DirectoryExistsAsync(
                    new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Bundled },
                    path).GetAwaiter().GetResult();
                var entries = fileSystem.ListDirectoryAsync(
                    new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Custom },
                    path).GetAwaiter().GetResult();

                Assert.That(exists, Is.True);
                Assert.That(entries, Has.Count.EqualTo(1));
                Assert.That(entries[0].Name, Is.EqualTo("notes.txt"));
                Assert.That(wsl.TotalCallCount, Is.EqualTo(0));
                Assert.That(remote.TotalCallCount, Is.EqualTo(0));
            }
            finally
            {
                Directory.Delete(path, true);
            }
        }

        [Test]
        public void WslOperationsUseWslBridgeForTheSelectedTarget()
        {
            var wsl = new FakeWslFileSystemBridge
            {
                HomePath = "/home/alice",
                DirectoryExists = true
            };
            var remote = new FakeRemoteFileSystemBridge();
            var fileSystem = new OfficeRuntimeFileSystem(wsl, remote);
            var config = new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.Wsl,
                WslAppServer = new WslAppServerConfigSnapshot
                {
                    DistributionName = "Ubuntu"
                }
            };

            var home = fileSystem.ResolveHomeDirectoryAsync(config).GetAwaiter().GetResult();
            var exists = fileSystem.DirectoryExistsAsync(config, "/home/alice/MyCodex-office").GetAwaiter().GetResult();

            Assert.That(home, Is.EqualTo("/home/alice"));
            Assert.That(exists, Is.True);
            Assert.That(wsl.ResolveHomeCallCount, Is.EqualTo(1));
            Assert.That(wsl.DirectoryExistsCallCount, Is.EqualTo(1));
            Assert.That(remote.TotalCallCount, Is.EqualTo(0));
        }

        [TestCase(CodexRuntimeSources.RemoteMacSsh)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh)]
        public void RemoteRuntimeSourcesUseSelectedRemoteBridge(string runtimeSource)
        {
            var wsl = new FakeWslFileSystemBridge();
            var remote = new FakeRemoteFileSystemBridge
            {
                HomePath = "/srv/alice",
                DirectoryExists = true
            };
            var fileSystem = new OfficeRuntimeFileSystem(wsl, remote);
            var config = new AppConfigSnapshot
            {
                CodexRuntimeSource = runtimeSource,
                SelectedRemoteUnixSshProfileId = "profile-a",
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    new RemoteUnixSshProfileConfigSnapshot
                    {
                        Id = "profile-a",
                        Ssh = new RemoteMacSshConfigSnapshot { HostName = "example.com" }
                    }
                }
            };

            var home = fileSystem.ResolveHomeDirectoryAsync(config).GetAwaiter().GetResult();
            var exists = fileSystem.DirectoryExistsAsync(config, "/srv/alice/office").GetAwaiter().GetResult();

            Assert.That(home, Is.EqualTo("/srv/alice"));
            Assert.That(exists, Is.True);
            Assert.That(remote.LastConfig.HostName, Is.EqualTo("example.com"));
            Assert.That(wsl.TotalCallCount, Is.EqualTo(0));
        }

        [Test]
        public void RemoteAvailabilityFailurePropagatesWithoutWindowsFallback()
        {
            var remote = new FakeRemoteFileSystemBridge
            {
                Failure = new InvalidOperationException("Remote unavailable.")
            };
            var fileSystem = new OfficeRuntimeFileSystem(new FakeWslFileSystemBridge(), remote);
            var config = new AppConfigSnapshot
            {
                CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                RemoteMacSsh = new RemoteMacSshConfigSnapshot
                {
                    HostName = "offline.example",
                    UserName = "alice"
                }
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                fileSystem.ResolveHomeDirectoryAsync(config).GetAwaiter().GetResult());

            Assert.That(exception.Message, Does.Contain("unavailable"));
            Assert.That(remote.ResolveHomeCallCount, Is.EqualTo(1));
        }

        [Test]
        public void ConfiguredAbsoluteUnixRootCanBeCheckedWithoutHomeProbe()
        {
            var wsl = new FakeWslFileSystemBridge { DirectoryExists = true };
            var fileSystem = new OfficeRuntimeFileSystem(wsl, new FakeRemoteFileSystemBridge());
            var workspaceService = new OfficeWorkspaceService(
                () => throw new InvalidOperationException("Windows fallback used."),
                () => throw new InvalidOperationException("Windows fallback used."));
            var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl };

            var root = workspaceService.ResolveOfficeRootPath(
                AppServerHostPlatform.Wsl,
                "/srv/office/./docs",
                null);
            var exists = fileSystem.DirectoryExistsAsync(config, root).GetAwaiter().GetResult();

            Assert.That(root, Is.EqualTo("/srv/office/docs"));
            Assert.That(exists, Is.True);
            Assert.That(wsl.ResolveHomeCallCount, Is.EqualTo(0));
        }

        private sealed class FakeWslFileSystemBridge : IWslFileSystemBridge
        {
            public string HomePath { get; set; }

            public bool DirectoryExists { get; set; }

            public int ResolveHomeCallCount { get; private set; }

            public int DirectoryExistsCallCount { get; private set; }

            public int TotalCallCount => ResolveHomeCallCount + DirectoryExistsCallCount;

            public Task<string> ResolveHomeDirectoryAsync(WslAppServerConfigSnapshot config)
            {
                ResolveHomeCallCount++;
                return Task.FromResult(HomePath);
            }

            public Task<bool> DirectoryExistsAsync(WslAppServerConfigSnapshot config, string path)
            {
                DirectoryExistsCallCount++;
                return Task.FromResult(DirectoryExists);
            }

            public Task<bool> FileExistsAsync(WslAppServerConfigSnapshot config, string path)
            {
                return Task.FromResult(false);
            }

            public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(
                WslAppServerConfigSnapshot config,
                string path)
            {
                return Task.FromResult((IReadOnlyList<RuntimeFileSystemEntrySnapshot>)
                    new List<RuntimeFileSystemEntrySnapshot>());
            }

            public Task DeleteCodexSessionPathAsync(WslAppServerConfigSnapshot config, string sessionPath)
            {
                return Task.CompletedTask;
            }

            public Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(WslAppServerConfigSnapshot config, string workspacePath)
            {
                return Task.FromResult(new WorkspaceGitInfoSnapshot());
            }

            public Task<GitDiffSnapshot> ReadUncommittedChangesAsync(
                WslAppServerConfigSnapshot config,
                string workspacePath,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new GitDiffSnapshot());
            }
        }

        private sealed class FakeRemoteFileSystemBridge : IRemoteMacFileSystemBridge
        {
            public string HomePath { get; set; }

            public bool DirectoryExists { get; set; }

            public Exception Failure { get; set; }

            public int ResolveHomeCallCount { get; private set; }

            public int DirectoryExistsCallCount { get; private set; }

            public RemoteMacSshConfigSnapshot LastConfig { get; private set; }

            public int TotalCallCount => ResolveHomeCallCount + DirectoryExistsCallCount;

            public Task<string> ResolveHomeDirectoryAsync(RemoteMacSshConfigSnapshot config)
            {
                ResolveHomeCallCount++;
                LastConfig = config;
                if (Failure != null)
                {
                    throw Failure;
                }

                return Task.FromResult(HomePath);
            }

            public Task<bool> DirectoryExistsAsync(RemoteMacSshConfigSnapshot config, string path)
            {
                DirectoryExistsCallCount++;
                LastConfig = config;
                return Task.FromResult(DirectoryExists);
            }

            public Task<bool> FileExistsAsync(RemoteMacSshConfigSnapshot config, string path)
            {
                return Task.FromResult(false);
            }

            public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(
                RemoteMacSshConfigSnapshot config,
                string path)
            {
                return Task.FromResult((IReadOnlyList<RuntimeFileSystemEntrySnapshot>)
                    new List<RuntimeFileSystemEntrySnapshot>());
            }

            public Task DeleteCodexSessionPathAsync(RemoteMacSshConfigSnapshot config, string sessionPath)
            {
                return Task.CompletedTask;
            }

            public Task<string> ReadTextFileAsync(RemoteMacSshConfigSnapshot config, string path)
            {
                return Task.FromResult(string.Empty);
            }

            public Task WriteSkillFilesAsync(
                RemoteMacSshConfigSnapshot config,
                string skillDirectoryPath,
                string skillMarkdownText,
                string openAiAgentText,
                RemoteSkillAgentFileAction agentFileAction)
            {
                return Task.CompletedTask;
            }

            public Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(RemoteMacSshConfigSnapshot config, string workspacePath)
            {
                return Task.FromResult(new WorkspaceGitInfoSnapshot());
            }

            public Task<GitDiffSnapshot> ReadUncommittedChangesAsync(
                RemoteMacSshConfigSnapshot config,
                string workspacePath,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new GitDiffSnapshot());
            }
        }
    }
}
