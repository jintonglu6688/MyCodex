using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetUpgradeMatrixTests
    {
        [TestCase("Wsl", "wsl|ubuntu")]
        [TestCase("RemoteUnixSsh", "remote-unix|ssh-two")]
        public void MixedLegacyTargets_RetainCodingOfficeAndAutomationDataWithoutCrossTargetCopies(string source, string activeScope)
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-upgrade-matrix", Guid.NewGuid().ToString("N"));
            var legacy = new AppConfigService(root);
            var scopes = new[] { "windows", "wsl|ubuntu", "wsl|debian", "remote-unix|ssh-one", "remote-unix|ssh-two" };
            Write(legacy.GetConfigFilePath(), new AppConfigSnapshot
            {
                CodexRuntimeSource = source,
                WslAppServer = new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" },
                RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                {
                    SshProfile("ssh-one", "mac.example"), SshProfile("ssh-two", "unix.example")
                },
                SelectedRemoteUnixSshProfileId = "ssh-two"
            });
            var originalState = WriteMixedState(legacy, scopes);
            var originalBytes = File.ReadAllBytes(legacy.GetAppStateFilePath());

            var migration = new TargetDataMigration(legacy);
            var catalog = migration.Run();

            Assert.That(catalog.Targets.Select(t => t.RuntimeScopeId), Is.EquivalentTo(scopes));
            Assert.That(catalog.Targets.Single(t => t.Id == catalog.LastOpenedTargetId).RuntimeScopeId, Is.EqualTo(activeScope));
            foreach (var target in catalog.Targets)
            {
                var paths = new AppConfigService(Path.Combine(root, "targets", target.Id), root, "MyCodex", root);
                var retained = Read<AppStateSnapshot>(paths.GetAppStateFilePath());
                Assert.That(retained.Sessions.Select(s => s.Id), Is.EquivalentTo(new[]
                {
                    Session(target.RuntimeScopeId, AppInteractionMode.Coding).Id,
                    Session(target.RuntimeScopeId, AppInteractionMode.Office).Id
                }));
                Assert.That(retained.RecentWorkspaces.Select(w => w.RootPath), Is.EquivalentTo(
                    originalState.RecentWorkspaces.Where(w => retained.Sessions.Any(s => s.WorkspaceId == w.Id)).Select(w => w.RootPath)));
                Assert.That(retained.RuntimeProfiles.Single().CurrentSessionId, Is.EqualTo(Session(target.RuntimeScopeId, AppInteractionMode.Coding).Id));
                Assert.That(retained.OfficeRuntimeStates.Single().CurrentSessionId, Is.EqualTo(Session(target.RuntimeScopeId, AppInteractionMode.Office).Id));
                var jobs = Read<AutomationStateSnapshot>(paths.GetAutomationsFilePath());
                Assert.That(jobs.Definitions.Select(d => d.Id), Is.EquivalentTo(new[]
                {
                    target.RuntimeScopeId + "-Coding", target.RuntimeScopeId + "-Office"
                }));
                Assert.That(jobs.Definitions.All(d => d.Enabled), Is.True);
                Assert.That(jobs.Runs.Select(r => r.AutomationId), Is.EquivalentTo(jobs.Definitions.Select(d => d.Id)));
            }
            Assert.That(Read<AppStateSnapshot>(Path.Combine(root, "targets", "pending-state.json")).Sessions, Is.Empty);
            Assert.That(Read<AutomationStateSnapshot>(Path.Combine(root, "targets", "pending-automations.json")).Definitions, Is.Empty);
            Assert.That(File.ReadAllBytes(legacy.GetAppStateFilePath()), Is.EqualTo(originalBytes));
            Assert.That(File.ReadAllBytes(Path.Combine(root, "migration-backup", "config", "app-state.json")), Is.EqualTo(originalBytes));
            Assert.That(migration.Run().Targets.Select(t => t.Id), Is.EqualTo(catalog.Targets.Select(t => t.Id)));
        }

        private static AppStateSnapshot WriteMixedState(AppConfigService paths, IEnumerable<string> scopes)
        {
            var state = new AppStateSnapshot();
            var jobs = new AutomationStateSnapshot();
            foreach (var scope in scopes)
            {
                var coding = Session(scope, AppInteractionMode.Coding);
                var office = Session(scope, AppInteractionMode.Office);
                state.Sessions.AddRange(new[] { coding, office });
                var codingWorkspace = Workspace(scope, coding.WorkspaceId);
                var officeWorkspace = Workspace(scope, office.WorkspaceId);
                state.RecentWorkspaces.AddRange(new[] { codingWorkspace, officeWorkspace });
                state.RuntimeProfiles.Add(new RuntimeProfileStateSnapshot
                {
                    RuntimeProfileId = scope, CurrentWorkspaceId = coding.WorkspaceId, CurrentSessionId = coding.Id,
                    RecentWorkspaces = new List<WorkspaceInfo> { codingWorkspace }
                });
                state.OfficeRuntimeStates.Add(new OfficeRuntimeStateSnapshot
                {
                    RuntimeScopeId = scope, CurrentWorkspaceId = office.WorkspaceId, CurrentSessionId = office.Id,
                    RecentWorkspaces = new List<WorkspaceInfo> { officeWorkspace }
                });
                foreach (var mode in new[] { AppInteractionMode.Coding, AppInteractionMode.Office })
                {
                    var jobId = scope + "-" + mode;
                    jobs.Definitions.Add(new AutomationDefinitionSnapshot
                    {
                        Id = jobId, Enabled = true,
                        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                        UpdatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
                        Context = new AutomationContextSnapshot
                        {
                            InteractionMode = mode, RuntimeProfileId = scope, OfficeRuntimeScopeId = scope
                        }
                    });
                    jobs.Runs.Add(new AutomationRunSnapshot { Id = jobId + "-run", AutomationId = jobId });
                }
            }
            Write(paths.GetAppStateFilePath(), state);
            Write(paths.GetAutomationsFilePath(), jobs);
            return state;
        }

        private static RemoteUnixSshProfileConfigSnapshot SshProfile(string id, string host)
        {
            return new RemoteUnixSshProfileConfigSnapshot
            {
                Id = id, Name = id, Ssh = new RemoteMacSshConfigSnapshot { HostName = host, UserName = "dev" }
            };
        }

        private static ChatSession Session(string scope, AppInteractionMode mode)
        {
            var id = scope + "-" + mode;
            return new ChatSession
            {
                Id = id, Title = id, WorkspaceId = id + "-workspace", Mode = mode,
                RuntimeProfileId = scope, OfficeRuntimeScopeId = mode == AppInteractionMode.Office ? scope : null,
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastActiveAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private static WorkspaceInfo Workspace(string scope, string id)
        {
            return new WorkspaceInfo
            {
                Id = id, RootPath = scope == "windows" ? @"E:\fixture\" + id : "/home/dev/" + id,
                LastOpenedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private static void Write<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using (var stream = File.Create(path)) new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
        }

        private static T Read<T>(string path)
        {
            using (var stream = File.OpenRead(path)) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
    }
}
