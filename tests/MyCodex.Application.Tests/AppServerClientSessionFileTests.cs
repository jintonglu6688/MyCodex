using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerClientSessionFileTests
    {
        [Test]
        public void DeleteThreadFileAsync_RejectsWslSessionPath()
        {
            using (var client = CreateClient())
            {
                var ex = Assert.Throws<InvalidOperationException>(() =>
                    client.DeleteThreadFileAsync("/home/tester/.codex/sessions/demo.jsonl"));

                Assert.That(ex.Message, Does.Contain("WSL session files"));
            }
        }

        [Test]
        public void CodexSessionFileStore_DeletesOnlySupportedSessionFiles()
        {
            using (UseCulture("en-US"))
            {
                var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionDirectory = Path.Combine(codexHome, "sessions");
                Directory.CreateDirectory(sessionDirectory);
                var sessionPath = Path.Combine(sessionDirectory, "demo.jsonl");
                File.WriteAllText(sessionPath, "demo");

                var store = CreateSessionFileStore(codexHome);

                store.DeleteThreadFileAsync(sessionPath, new[] { codexHome }).GetAwaiter().GetResult();

                Assert.That(File.Exists(sessionPath), Is.False);

                var outsidePath = Path.Combine(tempRoot, "outside.jsonl");
                File.WriteAllText(outsidePath, "outside");

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    store.DeleteThreadFileAsync(outsidePath).GetAwaiter().GetResult());

                Assert.That(ex.Message, Does.Contain("outside supported Codex state directories"));
                Assert.That(File.Exists(outsidePath), Is.True);

                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void CodexSessionFileStore_DeletesSessionFilesInsideApprovedOfficialHome()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var defaultCodexHome = Path.Combine(tempRoot, ".codex");
                var sessionDirectory = Path.Combine(defaultCodexHome, "sessions");
                Directory.CreateDirectory(sessionDirectory);
                var sessionPath = Path.Combine(sessionDirectory, "demo.jsonl");
                File.WriteAllText(sessionPath, "demo");

                var store = CreateSessionFileStore(defaultCodexHome);

                store.DeleteThreadFileAsync(
                    sessionPath,
                    new[] { defaultCodexHome })
                    .GetAwaiter()
                    .GetResult();

                Assert.That(File.Exists(sessionPath), Is.False);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void CodexSessionFileStore_RejectsPathOutsideApprovedOfficialHome()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var configuredCodexHome = Path.Combine(tempRoot, "configured", ".codex");
                var disguisedSessionDirectory = Path.Combine(tempRoot, "unconfigured", "sessions");
                Directory.CreateDirectory(disguisedSessionDirectory);
                var disguisedSessionPath = Path.Combine(disguisedSessionDirectory, "demo.jsonl");
                File.WriteAllText(disguisedSessionPath, "demo");

                var store = CreateSessionFileStore(configuredCodexHome);

                Assert.Throws<InvalidOperationException>(() =>
                    store.DeleteThreadFileAsync(disguisedSessionPath, new[] { configuredCodexHome }).GetAwaiter().GetResult());
                Assert.That(File.Exists(disguisedSessionPath), Is.True);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void CodexSessionFileStore_RejectsMissingPathOutsideConfiguredRoots()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            var configuredCodexHome = Path.Combine(tempRoot, "configured", ".codex");
            var outsideSessionPath = Path.Combine(tempRoot, "outside", "missing.jsonl");
            var store = CreateSessionFileStore(configuredCodexHome);

            Assert.Throws<InvalidOperationException>(() =>
                store.DeleteThreadFileAsync(outsideSessionPath, new[] { configuredCodexHome }).GetAwaiter().GetResult());
        }

        [Test]
        public void CodexSessionFileStore_AllowsMissingPathInsideConfiguredRoot()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            var configuredCodexHome = Path.Combine(tempRoot, "configured", ".codex");
            var missingSessionPath = Path.Combine(configuredCodexHome, "sessions", "missing.jsonl");
            var store = CreateSessionFileStore(configuredCodexHome);

            Assert.DoesNotThrow(() =>
                store.DeleteThreadFileAsync(missingSessionPath, new[] { configuredCodexHome }).GetAwaiter().GetResult());
        }

        [Test]
        public void CodexSessionFileStore_DeletesWslDefaultSessionPathThroughWslBridge()
        {
            var bridge = new FakeWslFileSystemBridge();
            var store = new CodexSessionFileStore(
                () => AppServerHostPlatform.Wsl,
                () => new AppConfigSnapshot
                {
                    WslAppServer = new WslAppServerConfigSnapshot
                    {
                        CodexHomeStrategy = WslCodexHomeStrategy.WslDefault,
                        DistributionName = "Ubuntu"
                    }
                },
                bridge);

            store.DeleteThreadFileAsync("/home/tester/.codex/sessions/demo.jsonl").GetAwaiter().GetResult();

            Assert.That(bridge.DeletedSessionPath, Is.EqualTo("/home/tester/.codex/sessions/demo.jsonl"));
            Assert.That(bridge.DeleteConfig.DistributionName, Is.EqualTo("Ubuntu"));
        }

        [Test]
        public void CodexSessionFileStore_DeletesRemoteMacSessionPathThroughRemoteBridge()
        {
            var remoteBridge = new FakeRemoteMacFileSystemBridge();
            var store = new CodexSessionFileStore(
                () => AppServerHostPlatform.RemoteMac,
                () => new AppConfigSnapshot
                {
                    RemoteMacSsh = new RemoteMacSshConfigSnapshot
                    {
                        HostName = "192.168.50.2",
                        UserName = "tonglujin"
                    }
                },
                new FakeWslFileSystemBridge(),
                remoteBridge);

            store.DeleteThreadFileAsync("/Users/tonglujin/.codex/sessions/demo.jsonl").GetAwaiter().GetResult();

            Assert.That(remoteBridge.DeletedSessionPath, Is.EqualTo("/Users/tonglujin/.codex/sessions/demo.jsonl"));
            Assert.That(remoteBridge.DeleteConfig.HostName, Is.EqualTo("192.168.50.2"));
            Assert.That(remoteBridge.DeleteConfig.UserName, Is.EqualTo("tonglujin"));
        }

        [Test]
        public void TryResolveLocalCodexHomePathForThreadScan_WslDefaultDoesNotScanWindowsHome()
        {
            string codexHomePath;

            var resolved = LocalSessionCatalog.TryResolveLocalCodexHomePathForThreadScan(
                AppServerHostPlatform.Wsl,
                new AppConfigSnapshot
                {
                    WslAppServer = new WslAppServerConfigSnapshot
                    {
                        CodexHomeStrategy = WslCodexHomeStrategy.WslDefault
                    }
                },
                out codexHomePath);

            Assert.That(resolved, Is.False);
            Assert.That(codexHomePath, Is.Null);
        }

        [Test]
        public void TryResolveLocalCodexHomePathForThreadScan_WindowsScansDefaultCodexHome()
        {
            string codexHomePath;

            var resolved = LocalSessionCatalog.TryResolveLocalCodexHomePathForThreadScan(
                AppServerHostPlatform.Windows,
                new AppConfigSnapshot(),
                out codexHomePath);

            Assert.That(resolved, Is.True);
            Assert.That(codexHomePath, Does.EndWith(@"\.codex"));
        }

        [Test]
        public void CodexSessionFileStore_RejectsSessionPathThroughReparsePoint()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            string linkPath = null;
            try
            {
                var defaultCodexHome = Path.Combine(tempRoot, ".codex");
                var sessionDirectory = Path.Combine(tempRoot, "codex-homes", "profiles", "deepseek", "sessions");
                var outsideDirectory = Path.Combine(tempRoot, "outside");
                Directory.CreateDirectory(sessionDirectory);
                Directory.CreateDirectory(outsideDirectory);
                var outsideSessionPath = Path.Combine(outsideDirectory, "demo.jsonl");
                File.WriteAllText(outsideSessionPath, "demo");

                linkPath = Path.Combine(sessionDirectory, "linked");
                Assume.That(TryCreateJunction(linkPath, outsideDirectory), Is.True);

                var store = CreateSessionFileStore(defaultCodexHome);
                var linkedSessionPath = Path.Combine(linkPath, "demo.jsonl");

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    store.DeleteThreadFileAsync(
                        linkedSessionPath,
                        new[] { defaultCodexHome, Path.GetDirectoryName(sessionDirectory) })
                        .GetAwaiter()
                        .GetResult());

                Assert.That(ex.Message, Does.Contain("reparse point"));
                Assert.That(File.Exists(outsideSessionPath), Is.True);
            }
            finally
            {
                DeleteJunctionIfExists(linkPath);
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void CodexSessionFileStore_RejectsMissingPathThroughBrokenJunction()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            string linkPath = null;
            try
            {
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionDirectory = Path.Combine(codexHome, "sessions");
                var outsideDirectory = Path.Combine(tempRoot, "outside");
                Directory.CreateDirectory(sessionDirectory);
                Directory.CreateDirectory(outsideDirectory);

                linkPath = Path.Combine(sessionDirectory, "linked");
                Assume.That(TryCreateJunction(linkPath, outsideDirectory), Is.True);
                Directory.Delete(outsideDirectory, true);

                var store = CreateSessionFileStore(codexHome);
                var linkedSessionPath = Path.Combine(linkPath, "missing.jsonl");

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    store.DeleteThreadFileAsync(linkedSessionPath, new[] { codexHome }).GetAwaiter().GetResult());

                Assert.That(ex.Message, Does.Contain("reparse point"));
            }
            finally
            {
                DeleteJunctionIfExists(linkPath);
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void CodexSessionFileStore_RejectsJunctionIntroducedAfterInitialValidation()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            string linkPath = null;
            try
            {
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionDirectory = Path.Combine(codexHome, "sessions");
                linkPath = Path.Combine(sessionDirectory, "candidate");
                var outsideDirectory = Path.Combine(tempRoot, "outside");
                Directory.CreateDirectory(linkPath);
                Directory.CreateDirectory(outsideDirectory);
                var sessionPath = Path.Combine(linkPath, "demo.jsonl");
                var outsideSessionPath = Path.Combine(outsideDirectory, "demo.jsonl");
                File.WriteAllText(sessionPath, "inside");
                File.WriteAllText(outsideSessionPath, "outside");

                var store = new CodexSessionFileStore(
                    () => AppServerHostPlatform.Windows,
                    () => new AppConfigSnapshot(),
                    new FakeWslFileSystemBridge(),
                    new FakeRemoteMacFileSystemBridge(),
                    () =>
                    {
                        File.Delete(sessionPath);
                        Directory.Delete(linkPath, false);
                        Assert.That(TryCreateJunction(linkPath, outsideDirectory), Is.True);
                    });

                var ex = Assert.Throws<InvalidOperationException>(() =>
                    store.DeleteThreadFileAsync(sessionPath, new[] { codexHome }).GetAwaiter().GetResult());

                Assert.That(ex.Message, Does.Contain("reparse point"));
                Assert.That(File.ReadAllText(outsideSessionPath), Is.EqualTo("outside"));
            }
            finally
            {
                DeleteJunctionIfExists(linkPath);
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void NormalizeLocalThreadWorkingDirectory_MapsMountedWslPathToWindowsPath()
        {
            var normalized = LocalSessionCatalog.NormalizeLocalThreadWorkingDirectory("/mnt/e/Projects/demo folder");

            Assert.That(normalized, Is.EqualTo(@"E:\Projects\demo folder"));
        }

        [Test]
        public void NormalizeLocalThreadWorkingDirectory_ExtractsEmbeddedWindowsPathFromMixedCwd()
        {
            var normalized = LocalSessionCatalog.NormalizeLocalThreadWorkingDirectory("/mnt/e/MyCodex/MyCodex/Release/E:\\MyCodex");

            Assert.That(normalized, Is.EqualTo(@"E:\MyCodex"));
        }

        [Test]
        public void NormalizeLocalThreadWorkingDirectory_ExtractsTrailingWindowsPathFromMappedMixedCwd()
        {
            var normalized = LocalSessionCatalog.NormalizeLocalThreadWorkingDirectory(@"E:\MyCodex\MyCodex\Release\E:\MyCodex");

            Assert.That(normalized, Is.EqualTo(@"E:\MyCodex"));
        }

        [Test]
        public void NormalizeLocalThreadWorkingDirectory_FixesDriveRelativeWindowsPath()
        {
            var normalized = LocalSessionCatalog.NormalizeLocalThreadWorkingDirectory("E:MyCodex");

            Assert.That(normalized, Is.EqualTo(@"E:\MyCodex"));
        }

        [Test]
        public void NormalizeLocalThreadWorkingDirectory_FixesDriveRelativeWindowsSubPath()
        {
            var normalized = LocalSessionCatalog.NormalizeLocalThreadWorkingDirectory(@"E:MyCodex\src");

            Assert.That(normalized, Is.EqualTo(@"E:\MyCodex\src"));
        }

        [Test]
        public void MergeThreadSummary_ReplacesMalformedWorkingDirectoryWithAbsolutePath()
        {
            var orderedThreads = new List<AppServerThreadSummary>();
            var mergedThreadMap = new Dictionary<string, AppServerThreadSummary>(StringComparer.OrdinalIgnoreCase);
            var malformedSummary = new AppServerThreadSummary
            {
                ThreadId = "thread-1",
                WorkingDirectory = "E:MyCodex"
            };
            var correctedSummary = new AppServerThreadSummary
            {
                ThreadId = "thread-1",
                WorkingDirectory = @"E:\MyCodex"
            };

            LocalSessionCatalog.MergeThreadSummary(orderedThreads, mergedThreadMap, malformedSummary);
            LocalSessionCatalog.MergeThreadSummary(orderedThreads, mergedThreadMap, correctedSummary);

            Assert.That(orderedThreads, Has.Count.EqualTo(1));
            Assert.That(orderedThreads[0].WorkingDirectory, Is.EqualTo(@"E:\MyCodex"));
        }

        [Test]
        public void MergeThreadSummary_PreservesSubAgentMetadataFromLocalSummary()
        {
            var orderedThreads = new List<AppServerThreadSummary>();
            var mergedThreadMap = new Dictionary<string, AppServerThreadSummary>(StringComparer.OrdinalIgnoreCase);
            var appServerSummary = new AppServerThreadSummary
            {
                ThreadId = "child-thread",
                WorkingDirectory = @"E:\BoyRemote"
            };
            var localSummary = new AppServerThreadSummary
            {
                ThreadId = "child-thread",
                WorkingDirectory = @"E:\BoyRemote",
                ThreadSource = "subagent",
                ParentThreadId = "parent-thread",
                SubagentDepth = 1,
                AgentNickname = "Turing",
                AgentRole = "default"
            };

            LocalSessionCatalog.MergeThreadSummary(orderedThreads, mergedThreadMap, appServerSummary);
            LocalSessionCatalog.MergeThreadSummary(orderedThreads, mergedThreadMap, localSummary);

            Assert.That(orderedThreads, Has.Count.EqualTo(1));
            Assert.That(orderedThreads[0].ThreadSource, Is.EqualTo("subagent"));
            Assert.That(orderedThreads[0].ParentThreadId, Is.EqualTo("parent-thread"));
            Assert.That(orderedThreads[0].SubagentDepth, Is.EqualTo(1));
            Assert.That(orderedThreads[0].AgentNickname, Is.EqualTo("Turing"));
            Assert.That(orderedThreads[0].AgentRole, Is.EqualTo("default"));
        }

        [Test]
        public void EnumerateLocalThreadSummaries_StopsAtConfiguredLimit()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionsRoot = Path.Combine(codexHome, "sessions", "2026");
                Directory.CreateDirectory(sessionsRoot);
                for (var i = 0; i < 5; i++)
                {
                    WriteLocalSessionFile(Path.Combine(sessionsRoot, "thread-" + i + ".jsonl"), "thread-" + i);
                }

                var summaries = new List<AppServerThreadSummary>(LocalSessionCatalog.EnumerateLocalThreadSummaries(
                    false,
                    null,
                    codexHome,
                    2,
                    CancellationToken.None));

                Assert.That(summaries, Has.Count.EqualTo(2));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void EnumerateLocalThreadSummaries_EmitsNewestFilesBeforeApplyingLimit()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionsRoot = Path.Combine(codexHome, "sessions", "2026");
                Directory.CreateDirectory(sessionsRoot);
                var oldPath = Path.Combine(sessionsRoot, "000-old-thread.jsonl");
                var newPath = Path.Combine(sessionsRoot, "999-new-thread.jsonl");
                WriteLocalSessionFile(oldPath, "old-thread");
                WriteLocalSessionFile(newPath, "new-thread");
                File.SetLastWriteTimeUtc(oldPath, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                File.SetLastWriteTimeUtc(newPath, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

                var summaries = new List<AppServerThreadSummary>(LocalSessionCatalog.EnumerateLocalThreadSummaries(
                    false,
                    null,
                    codexHome,
                    1,
                    CancellationToken.None));

                Assert.That(summaries.Select(summary => summary.ThreadId).ToArray(), Is.EqualTo(new[] { "new-thread" }));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void EnumerateLocalThreadSummaries_MapsSubAgentSessionMetadata()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionsRoot = Path.Combine(codexHome, "sessions");
                Directory.CreateDirectory(sessionsRoot);
                var sessionPath = Path.Combine(sessionsRoot, "child-thread.jsonl");
                File.WriteAllText(
                    sessionPath,
                    "{\"type\":\"session_meta\",\"payload\":{\"id\":\"child-thread\",\"timestamp\":\"2026-05-16T07:47:26Z\",\"cwd\":\"E:\\\\BoyRemote\",\"thread_source\":\"subagent\",\"agent_nickname\":\"Ampere\",\"agent_role\":\"default\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"parent-thread\",\"depth\":1}}}}}" + Environment.NewLine);

                var summaries = new List<AppServerThreadSummary>(LocalSessionCatalog.EnumerateLocalThreadSummaries(
                    false,
                    @"E:\BoyRemote",
                    codexHome,
                    100,
                    CancellationToken.None));

                Assert.That(summaries, Has.Count.EqualTo(1));
                Assert.That(summaries[0].ThreadId, Is.EqualTo("child-thread"));
                Assert.That(summaries[0].ThreadSource, Is.EqualTo("subagent"));
                Assert.That(summaries[0].ParentThreadId, Is.EqualTo("parent-thread"));
                Assert.That(summaries[0].SubagentDepth, Is.EqualTo(1));
                Assert.That(summaries[0].AgentNickname, Is.EqualTo("Ampere"));
                Assert.That(summaries[0].AgentRole, Is.EqualTo("default"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public async Task RuntimeThreadScanCatalog_RemoteMacMapsSubAgentSessionMetadata()
        {
            var sessionPath = "/Users/demo/.codex/sessions/child-thread.jsonl";
            var firstLine = "{\"type\":\"session_meta\",\"payload\":{\"id\":\"child-thread\",\"timestamp\":\"2026-05-16T07:47:26Z\",\"cwd\":\"/Users/demo/repo\",\"thread_source\":\"subagent\",\"agent_nickname\":\"Ampere\",\"agent_role\":\"default\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"parent-thread\",\"depth\":1}}}}}";
            var executor = new CapturingRemoteSshCommandExecutor
            {
                Result = new RemoteSshCommandExecutionResult
                {
                    ExitCode = 0,
                    StandardOutput = ToBase64(sessionPath) + "\t1770000000\t" + ToBase64(firstLine) + "\n"
                }
            };
            var catalog = new RuntimeThreadScanCatalog(
                new StaticConfigStore(new AppConfigSnapshot()),
                new CapturingWslCommandExecutor(),
                executor);

            var summaries = await catalog.ListThreadSummariesAsync(
                AppServerHostPlatform.RemoteMac,
                50,
                CancellationToken.None);

            Assert.That(executor.Arguments[0], Is.EqualTo(string.Empty));
            Assert.That(executor.Arguments[1], Is.EqualTo("50"));
            Assert.That(executor.Script, Does.Contain("File::Find"));
            Assert.That(summaries, Has.Count.EqualTo(1));
            Assert.That(summaries[0].ThreadId, Is.EqualTo("child-thread"));
            Assert.That(summaries[0].WorkingDirectory, Is.EqualTo("/Users/demo/repo"));
            Assert.That(summaries[0].ThreadSource, Is.EqualTo("subagent"));
            Assert.That(summaries[0].ParentThreadId, Is.EqualTo("parent-thread"));
            Assert.That(summaries[0].SubagentDepth, Is.EqualTo(1));
            Assert.That(summaries[0].AgentNickname, Is.EqualTo("Ampere"));
            Assert.That(summaries[0].AgentRole, Is.EqualTo("default"));
            Assert.That(summaries[0].SessionPath, Is.EqualTo(sessionPath));
        }

        [Test]
        public void EnumerateLocalThreadSummaries_ReadsSessionMetadataWhileFileIsOpenForWriting()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionsRoot = Path.Combine(codexHome, "sessions");
                Directory.CreateDirectory(sessionsRoot);
                var sessionPath = Path.Combine(sessionsRoot, "active-child-thread.jsonl");
                File.WriteAllText(
                    sessionPath,
                    "{\"type\":\"session_meta\",\"payload\":{\"id\":\"active-child-thread\",\"timestamp\":\"2026-05-16T07:47:26Z\",\"cwd\":\"E:\\\\MyCodex\",\"thread_source\":\"subagent\",\"agent_nickname\":\"Turing\",\"source\":{\"subagent\":{\"thread_spawn\":{\"parent_thread_id\":\"parent-thread\",\"depth\":1}}}}}" + Environment.NewLine);

                using (new FileStream(sessionPath, FileMode.Open, FileAccess.Write, FileShare.Read))
                {
                    var summaries = new List<AppServerThreadSummary>(LocalSessionCatalog.EnumerateLocalThreadSummaries(
                        false,
                        null,
                        codexHome,
                        100,
                        CancellationToken.None));

                    Assert.That(summaries, Has.Count.EqualTo(1));
                    Assert.That(summaries[0].ThreadId, Is.EqualTo("active-child-thread"));
                    Assert.That(summaries[0].ParentThreadId, Is.EqualTo("parent-thread"));
                    Assert.That(summaries[0].AgentNickname, Is.EqualTo("Turing"));
                }
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void EnumerateLocalThreadSummaries_ThrowsWhenCancellationIsRequested()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var codexHome = Path.Combine(tempRoot, ".codex");
                var sessionsRoot = Path.Combine(codexHome, "sessions");
                Directory.CreateDirectory(sessionsRoot);
                WriteLocalSessionFile(Path.Combine(sessionsRoot, "thread-1.jsonl"), "thread-1");

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    Assert.Throws<OperationCanceledException>(() =>
                        new List<AppServerThreadSummary>(LocalSessionCatalog.EnumerateLocalThreadSummaries(
                            false,
                            null,
                            codexHome,
                            100,
                            cancellation.Token)));
                }
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static AppServerClient CreateClient()
        {
            var appDataPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            return new AppServerClient(
                new CodexRuntimeManager(new AppConfigService(appDataPath)),
                new StubConfigStore());
        }

        private static CodexSessionFileStore CreateSessionFileStore(string codexHome)
        {
            return new CodexSessionFileStore(
                () => AppServerHostPlatform.Windows,
                () => new AppConfigSnapshot());
        }

        private static void WriteLocalSessionFile(string path, string threadId)
        {
            File.WriteAllText(
                path,
                "{\"type\":\"session_meta\",\"payload\":{\"id\":\"" + threadId + "\",\"timestamp\":\"2026-05-01T00:00:00Z\",\"cwd\":\"E:\\\\MyCodex\"}}" + Environment.NewLine);
        }

        private static string ToBase64(string value)
        {
            return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
        }

        private static bool TryCreateJunction(string linkPath, string targetPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                Arguments = string.Format("/c mklink /J \"{0}\" \"{1}\"", linkPath, targetPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return false;
                }

                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0;
            }
        }

        private static void DeleteJunctionIfExists(string linkPath)
        {
            if (string.IsNullOrWhiteSpace(linkPath) || !Directory.Exists(linkPath))
            {
                return;
            }

            try
            {
                var attributes = File.GetAttributes(linkPath);
                if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    Directory.Delete(linkPath, false);
                }
            }
            catch
            {
            }
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;
            private readonly CultureInfo _originalDefaultCulture;
            private readonly CultureInfo _originalDefaultUiCulture;

            public CultureScope(string cultureName)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
                _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
            }
        }

        private sealed class StubConfigStore : IAppConfigStore
        {
            public AppConfigSnapshot Load()
            {
                return new AppConfigSnapshot();
            }

            public void Save(AppConfigSnapshot snapshot)
            {
            }
        }

        private sealed class StaticConfigStore : IAppConfigStore
        {
            private readonly AppConfigSnapshot _snapshot;

            public StaticConfigStore(AppConfigSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public AppConfigSnapshot Load()
            {
                return _snapshot;
            }

            public void Save(AppConfigSnapshot snapshot)
            {
            }
        }

        private sealed class CapturingWslCommandExecutor : IWslCommandExecutor
        {
            public Task<WslCommandExecutionResult> ExecuteWithInputAsync(
                WslAppServerConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, System.Threading.CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                throw new System.NotSupportedException("Unexpected input command in this test.");
            }

            public string[] Arguments { get; private set; }

            public string Script { get; private set; }

            public WslCommandExecutionResult Result { get; set; }

            public Task<WslCommandExecutionResult> ExecuteAsync(
                WslAppServerConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                Script = script;
                Arguments = arguments;
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
                Script = script;
                Arguments = arguments;
                return Task.FromResult(Result ?? new WslCommandExecutionResult());
            }
        }

        private sealed class CapturingRemoteSshCommandExecutor : IRemoteSshCommandExecutor
        {
            public Task<RemoteSshCommandExecutionResult> ExecuteWithInputAsync(
                RemoteMacSshConfigSnapshot config, string script, string operationName,
                int timeoutMilliseconds, System.Threading.CancellationToken cancellationToken,
                string standardInput, params string[] arguments)
            {
                throw new System.NotSupportedException("Unexpected input command in this test.");
            }

            public string[] Arguments { get; private set; }

            public string Script { get; private set; }

            public RemoteSshCommandExecutionResult Result { get; set; }

            public Task<RemoteSshCommandExecutionResult> ExecuteAsync(
                RemoteMacSshConfigSnapshot config,
                string script,
                string operationName,
                int timeoutMilliseconds,
                params string[] arguments)
            {
                Script = script;
                Arguments = arguments;
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
                Script = script;
                Arguments = arguments;
                return Task.FromResult(Result ?? new RemoteSshCommandExecutionResult());
            }
        }

        private sealed class FakeWslFileSystemBridge : IWslFileSystemBridge
        {
            public WslAppServerConfigSnapshot DeleteConfig { get; private set; }

            public string DeletedSessionPath { get; private set; }

            public Task<string> ResolveHomeDirectoryAsync(WslAppServerConfigSnapshot config)
            {
                return Task.FromResult("/home/test");
            }

            public Task<bool> DirectoryExistsAsync(WslAppServerConfigSnapshot config, string path)
            {
                return Task.FromResult(false);
            }

            public Task<bool> FileExistsAsync(WslAppServerConfigSnapshot config, string path)
            {
                return Task.FromResult(false);
            }

            public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(WslAppServerConfigSnapshot config, string path)
            {
                return Task.FromResult((IReadOnlyList<RuntimeFileSystemEntrySnapshot>)Array.Empty<RuntimeFileSystemEntrySnapshot>());
            }

            public Task DeleteCodexSessionPathAsync(WslAppServerConfigSnapshot config, string sessionPath)
            {
                DeleteConfig = config;
                DeletedSessionPath = sessionPath;
                return Task.CompletedTask;
            }

            public Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(WslAppServerConfigSnapshot config, string workspacePath)
            {
                return Task.FromResult(new WorkspaceGitInfoSnapshot());
            }
        }

        private sealed class FakeRemoteMacFileSystemBridge : IRemoteMacFileSystemBridge
        {
            public RemoteMacSshConfigSnapshot DeleteConfig { get; private set; }

            public string DeletedSessionPath { get; private set; }

            public Task<string> ResolveHomeDirectoryAsync(RemoteMacSshConfigSnapshot config)
            {
                return Task.FromResult("/Users/test");
            }

            public Task<bool> DirectoryExistsAsync(RemoteMacSshConfigSnapshot config, string path)
            {
                return Task.FromResult(false);
            }

            public Task<bool> FileExistsAsync(RemoteMacSshConfigSnapshot config, string path)
            {
                return Task.FromResult(false);
            }

            public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(RemoteMacSshConfigSnapshot config, string path)
            {
                return Task.FromResult((IReadOnlyList<RuntimeFileSystemEntrySnapshot>)Array.Empty<RuntimeFileSystemEntrySnapshot>());
            }

            public Task DeleteCodexSessionPathAsync(RemoteMacSshConfigSnapshot config, string sessionPath)
            {
                DeleteConfig = config;
                DeletedSessionPath = sessionPath;
                return Task.CompletedTask;
            }

            public Task<string> ReadTextFileAsync(RemoteMacSshConfigSnapshot config, string path)
            {
                return Task.FromResult(string.Empty);
            }

            public Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(RemoteMacSshConfigSnapshot config, string workspacePath)
            {
                return Task.FromResult(new WorkspaceGitInfoSnapshot());
            }
        }
    }
}
