using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class JsonAppStateStoreTests
    {
        [TestCase(CodexRuntimeSources.Bundled, "windows", "C:\\code\\retained")]
        [TestCase(CodexRuntimeSources.Wsl, "wsl|ubuntu", "/home/me/retained")]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, "remote-unix|server", "/Users/me/retained")]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, "remote-unix|server", "/srv/retained")]
        public async Task Load_LegacyBranchMetadataPreservesSessionAndContinuesExistingThread(
            string runtimeSource, string profileId, string workspacePath)
        {
            var root = CreateTempDirectory();
            try
            {
                var config = new AppConfigSnapshot
                {
                    CodexRuntimeSource = runtimeSource,
                    SelectedRemoteUnixSshProfileId = "server",
                    RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                    {
                        new RemoteUnixSshProfileConfigSnapshot { Id = "server", Ssh = new RemoteMacSshConfigSnapshot() }
                    }
                };
                config.WslAppServer.DistributionName = "Ubuntu";
                var runtimeStore = new FakeRuntimeScopedConfigFileStore { UseRuntimeEndpoint = profileId != "windows" };
                var configService = new AppConfigService(root);
                var store = new JsonAppStateStore(configService, null, runtimeStore);
                var session = CreateSession("retained", "workspace", profileId, workspacePath + "/history.jsonl");
                session.ThreadId = "existing-thread";
                session.Title = "My existing title";
                session.Preview = "Existing history preview";
                var snapshot = new AppStateSnapshot
                {
                    CurrentSessionId = session.Id,
                    CurrentWorkspaceId = session.WorkspaceId,
                    Sessions = new List<ChatSession> { session },
                    RecentWorkspaces = new List<WorkspaceInfo> { CreateWorkspace("workspace", workspacePath) },
                    RuntimeProfiles = new List<RuntimeProfileStateSnapshot>
                    {
                        CreateRuntimeProfile(profileId, "workspace", session.Id, workspacePath)
                    }
                };
                store.Save(snapshot, config);
                string json;
                if (runtimeStore.UseRuntimeEndpoint)
                {
                    runtimeStore.ReadTextFile(config, "runtime-app-state.json", out json);
                }
                else
                {
                    json = File.ReadAllText(configService.GetAppStateFilePath());
                }
                var oldFields = "\"IsDerived\":true,\"DerivedSourceThreadId\":\"source\",\"DerivedSourceTitle\":\"Source title\","
                    + "\"DerivedSourceWorkspaceName\":\"Source\",\"DerivedSourceWorkspacePath\":\"/source\","
                    + "\"DerivedMode\":\"Full\",\"DerivedIndex\":2,";
                Assert.That(json, Does.Contain("\"ThreadId\":\"existing-thread\""));
                json = json.Replace("\"ThreadId\":\"existing-thread\"", oldFields + "\"ThreadId\":\"existing-thread\"");
                if (runtimeStore.UseRuntimeEndpoint)
                {
                    runtimeStore.WriteTextFile(config, "runtime-app-state.json", json);
                }
                else
                {
                    File.WriteAllText(configService.GetAppStateFilePath(), json);
                }

                var loaded = store.Load(config);
                var restored = loaded.Sessions.Single();
                Assert.That(restored.Title, Is.EqualTo(session.Title));
                Assert.That(restored.ThreadId, Is.EqualTo(session.ThreadId));
                Assert.That(restored.SessionPath, Is.EqualTo(session.SessionPath));
                Assert.That(restored.Preview, Is.EqualTo(session.Preview));
                Assert.That(restored.RuntimeProfileId, Is.EqualTo(profileId));
                Assert.That(restored.WorkspaceId, Is.EqualTo("workspace"));
                Assert.That(restored.IsFork || restored.IsReview || restored.IsSubAgent, Is.False);
                var client = new RetainedThreadClient();
                var threadService = new SessionThreadService(client, new WorkspaceService(), path => null);
                await threadService.SendUserMessageAsync(restored, loaded.RecentWorkspaces.Single(),
                    new[] { new AppServerUserInputItem { Type = "text", Text = "Continue" } },
                    ApprovalMode.ReadOnly, null, null);
                Assert.That(client.ThreadId, Is.EqualTo(session.ThreadId));
                Assert.That(client.WorkspacePath, Is.EqualTo(workspacePath));
                store.Save(loaded, config);
                Assert.That(store.Load(config).Sessions.Single().ThreadId, Is.EqualTo(session.ThreadId));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private sealed class RetainedThreadClient : UnsupportedAppServerThreadClient
        {
            public string ThreadId { get; private set; }
            public string WorkspacePath { get; private set; }

            public override Task SendUserMessageAsync(string threadId, string workspacePath,
                IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode,
                string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null)
            {
                ThreadId = threadId;
                WorkspacePath = workspacePath;
                return Task.CompletedTask;
            }
        }

        [Test]
        public void SaveAndLoad_LocalStateRoundTrips()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAppStateStore(service, null, new FakeRuntimeScopedConfigFileStore());
                store.Save(new AppStateSnapshot
                {
                    HasCompletedThreadImportPrompt = true,
                    CurrentWorkspaceId = "workspace-local",
                    CurrentSessionId = "session-local",
                    RecentWorkspaces = new List<WorkspaceInfo>
                    {
                        CreateWorkspace("workspace-local", @"C:\code\local")
                    },
                    Sessions = new List<ChatSession>
                    {
                        CreateSession("session-local", "workspace-local", "windows")
                    },
                    RuntimeProfiles = new List<RuntimeProfileStateSnapshot>
                    {
                        CreateRuntimeProfile("windows", "workspace-local", "session-local", @"C:\code\local")
                    },
                    OfficeRuntimeStates = new List<OfficeRuntimeStateSnapshot>
                    {
                        CreateOfficeState("windows", "office-local", @"C:\office\local")
                    }
                });

                var reloaded = new JsonAppStateStore(
                    service,
                    null,
                    new FakeRuntimeScopedConfigFileStore()).Load();

                Assert.That(reloaded.HasCompletedThreadImportPrompt, Is.True);
                Assert.That(reloaded.CurrentWorkspaceId, Is.EqualTo("workspace-local"));
                Assert.That(reloaded.CurrentSessionId, Is.EqualTo("session-local"));
                Assert.That(reloaded.RecentWorkspaces.Select(item => item.Id), Is.EqualTo(new[] { "workspace-local" }));
                Assert.That(reloaded.Sessions.Select(item => item.Id), Is.EqualTo(new[] { "session-local" }));
                Assert.That(reloaded.RuntimeProfiles.Select(item => item.RuntimeProfileId), Is.EqualTo(new[] { "windows" }));
                Assert.That(reloaded.OfficeRuntimeStates.Select(item => item.RuntimeScopeId), Is.EqualTo(new[] { "windows" }));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Save_UnchangedLocalStateDoesNotRewriteFile()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAppStateStore(service, null, new FakeRuntimeScopedConfigFileStore());
                var snapshot = CreateSnapshotWithWorkspace("workspace-local", @"C:\code\local");
                store.Save(snapshot);
                var path = service.GetAppStateFilePath();
                var earlier = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, earlier);

                store.Save(snapshot);
                Assert.That(File.GetLastWriteTimeUtc(path), Is.EqualTo(earlier));

                store.Save(CreateSnapshotWithWorkspace("workspace-other", @"C:\code\other"));
                Assert.That(File.GetLastWriteTimeUtc(path), Is.GreaterThan(earlier));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_CorruptPrimaryAndValidBackup_RestoresBackup()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAppStateStore(service, null, new FakeRuntimeScopedConfigFileStore());
                store.Save(CreateSnapshotWithWorkspace("backup-workspace", @"C:\code\backup"));
                store.Save(CreateSnapshotWithWorkspace("primary-workspace", @"C:\code\primary"));
                File.WriteAllText(service.GetAppStateFilePath(), "{ invalid json");

                var recovered = store.Load();

                Assert.That(recovered.RecentWorkspaces.Select(item => item.Id), Is.EqualTo(new[] { "backup-workspace" }));
                Assert.That(new JsonAppStateStore(
                    service,
                    null,
                    new FakeRuntimeScopedConfigFileStore()).Load().RecentWorkspaces[0].Id,
                    Is.EqualTo("backup-workspace"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_RuntimeCommandFailureReportsReadFailureAndPreventsOverwrite()
        {
            var root = CreateTempDirectory();
            try
            {
                var readError = new InvalidOperationException("bash: line 1: zsh: command not found");
                var runtimeStore = new FakeRuntimeScopedConfigFileStore
                {
                    UseRuntimeEndpoint = true,
                    ReadError = readError
                };
                var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh };
                var store = new JsonAppStateStore(new AppConfigService(root), null, runtimeStore);

                var error = Assert.Throws<RuntimeStateReadException>(() => store.Load(config));
                Assert.That(error.StatePath, Is.EqualTo("runtime-app-state.json"));
                Assert.That(error.FailureReason, Is.EqualTo(readError.Message));
                Assert.That(error.InnerException, Is.SameAs(readError));
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Save(new AppStateSnapshot(), config));
                Assert.That(runtimeStore.WrittenPath, Is.Null);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Load_RuntimeInvalidJsonStillRequiresRecoveryAndPreventsOverwrite()
        {
            var root = CreateTempDirectory();
            try
            {
                var runtimeStore = new FakeRuntimeScopedConfigFileStore { UseRuntimeEndpoint = true };
                var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh };
                runtimeStore.WriteTextFile(config, "runtime-app-state.json", "{ invalid json");
                var store = new JsonAppStateStore(new AppConfigService(root), null, runtimeStore);

                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Load(config));
                Assert.Throws<PersistenceRecoveryRequiredException>(() => store.Save(new AppStateSnapshot(), config));
                runtimeStore.ReadTextFile(config, "runtime-app-state.json", out var content);
                Assert.That(content, Is.EqualTo("{ invalid json"));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void Save_LocalStateFiltersEndpointProjectState()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var store = new JsonAppStateStore(service, null, new FakeRuntimeScopedConfigFileStore());
                store.Save(new AppStateSnapshot
                {
                    CurrentWorkspaceId = "endpoint-workspace",
                    CurrentSessionId = "endpoint-session",
                    RecentWorkspaces = new List<WorkspaceInfo>
                    {
                        CreateWorkspace("local-workspace", @"C:\code\local"),
                        CreateWorkspace("endpoint-workspace", "/home/me/endpoint")
                    },
                    Sessions = new List<ChatSession>
                    {
                        CreateSession("local-session", "local-workspace", "windows"),
                        CreateSession("endpoint-session", "endpoint-workspace", "wsl|ubuntu", "/home/me/.codex/session.jsonl")
                    },
                    RuntimeProfiles = new List<RuntimeProfileStateSnapshot>
                    {
                        CreateRuntimeProfile("windows", "local-workspace", "local-session", @"C:\code\local"),
                        CreateRuntimeProfile("wsl|ubuntu", "endpoint-workspace", "endpoint-session", "/home/me/endpoint")
                    },
                    OfficeRuntimeStates = new List<OfficeRuntimeStateSnapshot>
                    {
                        CreateOfficeState("windows", "office-local", @"C:\office\local"),
                        CreateOfficeState("remote-unix|server", "office-endpoint", "/home/me/office")
                    }
                });

                var reloaded = store.Load();

                Assert.That(reloaded.RecentWorkspaces.Select(item => item.Id), Is.EqualTo(new[] { "local-workspace" }));
                Assert.That(reloaded.Sessions.Select(item => item.Id), Is.EqualTo(new[] { "local-session" }));
                Assert.That(reloaded.RuntimeProfiles.Select(item => item.RuntimeProfileId), Is.EqualTo(new[] { "windows" }));
                Assert.That(reloaded.OfficeRuntimeStates.Select(item => item.RuntimeScopeId), Is.EqualTo(new[] { "windows" }));
                Assert.That(reloaded.CurrentWorkspaceId, Is.Null);
                Assert.That(reloaded.CurrentSessionId, Is.Null);
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void SaveAndLoad_RuntimeEndpointIsolatesActiveProfileAndOfficeScope()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(root);
                var runtimeStore = new FakeRuntimeScopedConfigFileStore { UseRuntimeEndpoint = true };
                var store = new JsonAppStateStore(service, null, runtimeStore);
                var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Wsl };
                config.WslAppServer.DistributionName = "Ubuntu";
                store.Save(new AppStateSnapshot
                {
                    CurrentWorkspaceId = "active-workspace",
                    CurrentSessionId = "active-session",
                    RecentWorkspaces = new List<WorkspaceInfo>
                    {
                        CreateWorkspace("active-workspace", "/home/me/active"),
                        CreateWorkspace("local-workspace", @"C:\code\local")
                    },
                    Sessions = new List<ChatSession>
                    {
                        CreateSession("active-session", "active-workspace", "wsl|ubuntu", "/home/me/.codex/active.jsonl"),
                        CreateSession("other-runtime-session", "other-workspace", "wsl|debian", "/home/me/.codex/other.jsonl"),
                        CreateSession("local-session", "local-workspace", "windows", @"C:\sessions\local.jsonl"),
                        CreateOfficeSession("active-office-session", "wsl|ubuntu"),
                        CreateOfficeSession("other-office-session", "remote-unix|server")
                    },
                    RuntimeProfiles = new List<RuntimeProfileStateSnapshot>
                    {
                        CreateRuntimeProfile("wsl|ubuntu", "active-workspace", "active-session", "/home/me/active"),
                        CreateRuntimeProfile("wsl|debian", "other-workspace", "other-runtime-session", "/home/me/other")
                    },
                    OfficeRuntimeStates = new List<OfficeRuntimeStateSnapshot>
                    {
                        CreateOfficeState("wsl|ubuntu", "active-office-workspace", "/home/me/office"),
                        CreateOfficeState("remote-unix|server", "other-office-workspace", "/srv/office")
                    }
                }, config);

                var reloaded = store.Load(config);

                Assert.That(runtimeStore.WrittenPath, Is.EqualTo("runtime-app-state.json"));
                Assert.That(reloaded.RecentWorkspaces.Select(item => item.Id), Is.EqualTo(new[] { "active-workspace" }));
                Assert.That(reloaded.Sessions.Select(item => item.Id), Is.EqualTo(new[] { "active-session", "active-office-session" }));
                Assert.That(reloaded.RuntimeProfiles, Has.Count.EqualTo(1));
                Assert.That(reloaded.RuntimeProfiles[0].RuntimeProfileId, Is.EqualTo("wsl|ubuntu"));
                Assert.That(reloaded.RuntimeProfiles[0].CurrentWorkspaceId, Is.EqualTo("active-workspace"));
                Assert.That(reloaded.RuntimeProfiles[0].CurrentSessionId, Is.EqualTo("active-session"));
                Assert.That(reloaded.OfficeRuntimeStates.Select(item => item.RuntimeScopeId), Is.EqualTo(new[] { "wsl|ubuntu" }));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static AppStateSnapshot CreateSnapshotWithWorkspace(string workspaceId, string rootPath)
        {
            return new AppStateSnapshot
            {
                CurrentWorkspaceId = workspaceId,
                RecentWorkspaces = new List<WorkspaceInfo> { CreateWorkspace(workspaceId, rootPath) }
            };
        }

        private static WorkspaceInfo CreateWorkspace(string id, string rootPath)
        {
            return new WorkspaceInfo
            {
                Id = id,
                Name = id,
                RootPath = rootPath,
                Kind = WorkspaceKind.Coding
            };
        }

        private static ChatSession CreateSession(
            string id,
            string workspaceId,
            string runtimeProfileId,
            string sessionPath = null)
        {
            return new ChatSession
            {
                Id = id,
                WorkspaceId = workspaceId,
                RuntimeProfileId = runtimeProfileId,
                SessionPath = sessionPath,
                Title = id
            };
        }

        private static ChatSession CreateOfficeSession(string id, string runtimeScopeId)
        {
            return new ChatSession
            {
                Id = id,
                WorkspaceId = "office:" + runtimeScopeId,
                Mode = AppInteractionMode.Office,
                OfficeRuntimeScopeId = runtimeScopeId,
                Title = id
            };
        }

        private static RuntimeProfileStateSnapshot CreateRuntimeProfile(
            string profileId,
            string workspaceId,
            string sessionId,
            string rootPath)
        {
            return new RuntimeProfileStateSnapshot
            {
                RuntimeProfileId = profileId,
                CurrentWorkspaceId = workspaceId,
                CurrentSessionId = sessionId,
                RecentWorkspaces = new List<WorkspaceInfo> { CreateWorkspace(workspaceId, rootPath) }
            };
        }

        private static OfficeRuntimeStateSnapshot CreateOfficeState(
            string scopeId,
            string workspaceId,
            string rootPath)
        {
            return new OfficeRuntimeStateSnapshot
            {
                RuntimeScopeId = scopeId,
                CurrentWorkspaceId = workspaceId,
                CurrentSessionId = "session:" + scopeId,
                RecentWorkspaces = new List<WorkspaceInfo> { CreateWorkspace(workspaceId, rootPath) }
            };
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "JsonAppStateStoreTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class FakeRuntimeScopedConfigFileStore : IRuntimeScopedConfigFileStore
        {
            private string _content;

            public bool UseRuntimeEndpoint { get; set; }

            public Exception ReadError { get; set; }

            public string WrittenPath { get; private set; }

            public bool TryGetRuntimeAppStateFilePathForAsyncAccess(AppConfigSnapshot config, out string path)
            {
                path = UseRuntimeEndpoint ? "runtime-app-state.json" : null;
                return UseRuntimeEndpoint;
            }

            public bool TryGetRuntimeModeSettingsFilePathForAsyncAccess(AppConfigSnapshot config, out string path)
            {
                path = null;
                return false;
            }

            public bool ReadTextFile(AppConfigSnapshot config, string path, out string content)
            {
                if (ReadError != null)
                {
                    throw ReadError;
                }

                content = _content;
                return _content != null;
            }

            public Task<Tuple<bool, string>> ReadTextFileAsync(AppConfigSnapshot config, string path)
            {
                return Task.FromResult(Tuple.Create(_content != null, _content));
            }

            public void WriteTextFile(AppConfigSnapshot config, string path, string content)
            {
                WrittenPath = path;
                _content = content;
            }

            public Task WriteTextFileAsync(AppConfigSnapshot config, string path, string content)
            {
                WriteTextFile(config, path, content);
                return Task.CompletedTask;
            }
        }
    }
}
