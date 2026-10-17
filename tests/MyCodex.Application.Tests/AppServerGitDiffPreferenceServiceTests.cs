using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerGitDiffPreferenceServiceTests
    {
        [Test]
        public async Task ReadDiffAsync_ForRemoteRuntimeUsesAppServerFirst()
        {
            var appServer = new FakeAppServerGitDiffClient
            {
                Result = new AppServerGitDiffToRemoteResult { Sha = "abc123", Diff = "remote diff" }
            };
            var fallback = new FakeGitDiffReader { ResultOutput = "fallback diff" };
            var service = new AppServerGitDiffPreferenceService(appServer, fallback);

            var result = await service.ReadDiffAsync(
                "/repo",
                new GitChangedFileEntry { FilePath = "a.txt" },
                CodexRuntimeSources.RemoteUnixSsh,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Is.EqualTo("remote diff"));
            Assert.That(result.DisplayMessage, Does.Contain("App Server"));
            Assert.That(appServer.CallCount, Is.EqualTo(1));
            Assert.That(fallback.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ReadDiffAsync_WhenAppServerFailsUsesFallback()
        {
            var appServer = new FakeAppServerGitDiffClient
            {
                Exception = new InvalidOperationException("unsupported")
            };
            var fallback = new FakeGitDiffReader { ResultOutput = "fallback diff" };
            var service = new AppServerGitDiffPreferenceService(appServer, fallback);

            var result = await service.ReadDiffAsync(
                "/repo",
                new GitChangedFileEntry { FilePath = "a.txt" },
                CodexRuntimeSources.RemoteUnixSsh,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Is.EqualTo("fallback diff"));
            Assert.That(appServer.CallCount, Is.EqualTo(1));
            Assert.That(fallback.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadDiffAsync_ForLocalRuntimeUsesFallbackOnly()
        {
            var appServer = new FakeAppServerGitDiffClient
            {
                Result = new AppServerGitDiffToRemoteResult { Sha = "abc123", Diff = "remote diff" }
            };
            var fallback = new FakeGitDiffReader { ResultOutput = "fallback diff" };
            var service = new AppServerGitDiffPreferenceService(appServer, fallback);

            var result = await service.ReadDiffAsync(
                "E:\\repo",
                new GitChangedFileEntry { FilePath = "a.txt" },
                CodexRuntimeSources.Bundled,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Is.EqualTo("fallback diff"));
            Assert.That(appServer.CallCount, Is.EqualTo(0));
            Assert.That(fallback.CallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ReadDiffAsync_ForRemoteRuntimeFiltersAppServerWorkspaceDiffToSelectedFile()
        {
            var appServer = new FakeAppServerGitDiffClient
            {
                Result = new AppServerGitDiffToRemoteResult
                {
                    Sha = "abc123",
                    Diff = string.Join("\n", new[]
                    {
                        "diff --git a/src/a.cs b/src/a.cs",
                        "index 111..222 100644",
                        "--- a/src/a.cs",
                        "+++ b/src/a.cs",
                        "@@ -1 +1 @@",
                        "-old a",
                        "+new a",
                        "diff --git a/src/b.cs b/src/b.cs",
                        "index 333..444 100644",
                        "--- a/src/b.cs",
                        "+++ b/src/b.cs",
                        "@@ -1 +1 @@",
                        "-old b",
                        "+new b"
                    })
                }
            };
            var fallback = new FakeGitDiffReader { ResultOutput = "fallback diff" };
            var service = new AppServerGitDiffPreferenceService(appServer, fallback);

            var result = await service.ReadDiffAsync(
                "/repo",
                new GitChangedFileEntry { FilePath = "src/a.cs" },
                CodexRuntimeSources.RemoteUnixSsh,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Does.Contain("src/a.cs"));
            Assert.That(result.StandardOutput, Does.Contain("+new a"));
            Assert.That(result.StandardOutput, Does.Not.Contain("src/b.cs"));
            Assert.That(fallback.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ReadDiffAsync_WhenAppServerWorkspaceDiffDoesNotContainSelectedFileUsesFallback()
        {
            var appServer = new FakeAppServerGitDiffClient
            {
                Result = new AppServerGitDiffToRemoteResult
                {
                    Sha = "abc123",
                    Diff = "diff --git a/src/b.cs b/src/b.cs\n+new b"
                }
            };
            var fallback = new FakeGitDiffReader { ResultOutput = "fallback diff" };
            var service = new AppServerGitDiffPreferenceService(appServer, fallback);

            var result = await service.ReadDiffAsync(
                "/repo",
                new GitChangedFileEntry { FilePath = "src/a.cs" },
                CodexRuntimeSources.RemoteUnixSsh,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.StandardOutput, Is.EqualTo("fallback diff"));
            Assert.That(appServer.CallCount, Is.EqualTo(1));
            Assert.That(fallback.CallCount, Is.EqualTo(1));
        }

        private sealed class FakeAppServerGitDiffClient : IAppServerGitDiffClient
        {
            public int CallCount { get; private set; }

            public AppServerGitDiffToRemoteResult Result { get; set; }

            public Exception Exception { get; set; }

            public Task<AppServerGitDiffToRemoteResult> GetGitDiffToRemoteAsync(string workspacePath, CancellationToken cancellationToken = default(CancellationToken))
            {
                CallCount++;
                if (Exception != null)
                {
                    throw Exception;
                }

                return Task.FromResult(Result);
            }
        }

        private sealed class FakeGitDiffReader : IGitDiffReader
        {
            public int CallCount { get; private set; }

            public string ResultOutput { get; set; }

            public Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default(CancellationToken))
            {
                CallCount++;
                return Task.FromResult(BuildResult());
            }

            public Task<GitOperationResult> ReadDiffAsync(string workspacePath, GitChangedFileEntry entry, GitDiffKind diffKind, CancellationToken cancellationToken = default(CancellationToken))
            {
                CallCount++;
                return Task.FromResult(BuildResult());
            }

            public Task<GitOperationResult> ReadConflictDetailsAsync(string workspacePath, GitChangedFileEntry entry, CancellationToken cancellationToken = default(CancellationToken))
            {
                CallCount++;
                return Task.FromResult(BuildResult());
            }

            private GitOperationResult BuildResult()
            {
                return new GitOperationResult
                {
                    Success = true,
                    ExitCode = 0,
                    StandardOutput = ResultOutput
                };
            }
        }
    }
}
