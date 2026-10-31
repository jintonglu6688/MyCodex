using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteSessionSettingsOperatorTests
    {
        [Test]
        public async Task GetSessionSettingsAsync_ReturnsOverridesEffectiveDefaultsAndModels()
        {
            var store = CreateStore();
            store.Snapshot.Sessions[0].ModelId = "gpt-custom";
            store.Snapshot.Sessions[0].ReasoningEffort = "high";
            store.Snapshot.Sessions[0].ApprovalMode = ApprovalMode.FullAccess;
            store.Snapshot.Sessions[0].IsPlanModeEnabled = true;
            var operatorUnderTest = CreateOperator(store, supportsReasoning: true);

            var result = await operatorUnderTest.GetSessionSettingsAsync("session-a", CancellationToken.None);

            Assert.That(result.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(result.ModelId, Is.EqualTo("gpt-custom"));
            Assert.That(result.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(result.ApprovalMode, Is.EqualTo("full-access"));
            Assert.That(result.EffectiveModelId, Is.EqualTo("gpt-custom"));
            Assert.That(result.EffectiveReasoningEffort, Is.EqualTo("high"));
            Assert.That(result.EffectiveApprovalMode, Is.EqualTo("full-access"));
            Assert.That(result.IsPlanModeEnabled, Is.True);
            Assert.That(result.DefaultModelId, Is.EqualTo("gpt-default"));
            Assert.That(result.DefaultReasoningEffort, Is.EqualTo("medium"));
            Assert.That(result.DefaultApprovalMode, Is.EqualTo("auto"));
            Assert.That(result.AvailableModels, Has.Count.EqualTo(1));
            Assert.That(result.AvailableModels[0].ModelId, Is.EqualTo("gpt-custom"));
            Assert.That(result.AvailableModels[0].ReasoningEfforts, Is.EqualTo(new[] { "low", "medium", "high" }));
        }

        [Test]
        public async Task GetSessionSettingsAsync_FormatsCustomApprovalMode()
        {
            var store = CreateStoreWithSession(new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1",
                Mode = AppInteractionMode.Coding,
                ApprovalMode = ApprovalMode.Custom
            });
            var service = CreateOperator(store, () => ApprovalMode.Custom);

            var result = await service.GetSessionSettingsAsync("session-1", CancellationToken.None);

            Assert.That(result.ApprovalMode, Is.EqualTo("custom"));
            Assert.That(result.EffectiveApprovalMode, Is.EqualTo("custom"));
            Assert.That(result.DefaultApprovalMode, Is.EqualTo("custom"));
        }

        [Test]
        public async Task UpdateSessionSettingsAsync_SavesSessionOverrides()
        {
            var store = CreateStore();
            var operatorUnderTest = CreateOperator(store, supportsReasoning: true);

            var result = await operatorUnderTest.UpdateSessionSettingsAsync(
                new MyCodex.Application.CodingRemote.CodingRemoteSessionSettingsUpdateRequest
                {
                    SessionId = "session-a",
                    HasModelId = true,
                    ModelId = "gpt-custom",
                    HasReasoningEffort = true,
                    ReasoningEffort = "medium",
                    HasApprovalMode = true,
                    ApprovalMode = "read-only"
                },
                CancellationToken.None);

            Assert.That(result.Updated, Is.True);
            Assert.That(store.SaveCount, Is.EqualTo(1));
            var session = store.Snapshot.Sessions[0];
            Assert.That(session.ModelId, Is.EqualTo("gpt-custom"));
            Assert.That(session.ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(session.AutomationModelSettingsInitialized, Is.True);
            Assert.That(session.ApprovalMode, Is.EqualTo(ApprovalMode.ReadOnly));
        }

        [Test]
        public async Task UpdateSessionSettingsAsync_FollowGlobalPreventsAutomationSettingsFromBeingRestored()
        {
            var store = CreateStore();
            var session = store.Snapshot.Sessions[0];
            session.ModelId = "gpt-5.6-luna";
            session.ReasoningEffort = "high";
            await CreateOperator(store, supportsReasoning: true).UpdateSessionSettingsAsync(
                new MyCodex.Application.CodingRemote.CodingRemoteSessionSettingsUpdateRequest
                {
                    SessionId = session.Id, HasModelId = true, ModelId = null,
                    HasReasoningEffort = true, ReasoningEffort = null
                }, CancellationToken.None);
            Assert.That(session.ModelId, Is.Null);
            Assert.That(session.ReasoningEffort, Is.Null);
            Assert.That(session.AutomationModelSettingsInitialized, Is.True);
        }

        [TestCase("custom", ApprovalMode.Custom)]
        [TestCase("full-access", ApprovalMode.FullAccess)]
        [TestCase("full-access-auto-approve", ApprovalMode.FullAccess)]
        public async Task UpdateSessionSettingsAsync_ParsesCustomApprovalMode(string wire, ApprovalMode expected)
        {
            var session = new ChatSession
            {
                Id = "session-1",
                WorkspaceId = "workspace-1",
                Mode = AppInteractionMode.Coding
            };
            var store = CreateStoreWithSession(session);
            var service = CreateOperator(store, () => ApprovalMode.Auto);

            await service.UpdateSessionSettingsAsync(
                new MyCodex.Application.CodingRemote.CodingRemoteSessionSettingsUpdateRequest
                {
                    SessionId = "session-1",
                    HasApprovalMode = true,
                    ApprovalMode = wire
                },
                CancellationToken.None);

            Assert.That(store.Snapshot.Sessions[0].ApprovalMode, Is.EqualTo(expected));
        }

        [Test]
        public async Task UpdateSessionSettingsAsync_UpdatesPlanModeWithoutClearingOmittedSettings()
        {
            var store = CreateStore();
            store.Snapshot.Sessions[0].ModelId = "gpt-custom";
            store.Snapshot.Sessions[0].ReasoningEffort = "high";
            store.Snapshot.Sessions[0].ApprovalMode = ApprovalMode.FullAccess;
            store.Snapshot.Sessions[0].IsPlanModeEnabled = false;
            var operatorUnderTest = CreateOperator(store, supportsReasoning: true);

            var result = await operatorUnderTest.UpdateSessionSettingsAsync(
                new MyCodex.Application.CodingRemote.CodingRemoteSessionSettingsUpdateRequest
                {
                    SessionId = "session-a",
                    IsPlanModeEnabled = true
                },
                CancellationToken.None);

            Assert.That(result.Updated, Is.True);
            Assert.That(result.IsPlanModeEnabled, Is.True);
            Assert.That(store.SaveCount, Is.EqualTo(1));
            var session = store.Snapshot.Sessions[0];
            Assert.That(session.ModelId, Is.EqualTo("gpt-custom"));
            Assert.That(session.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(session.ApprovalMode, Is.EqualTo(ApprovalMode.FullAccess));
            Assert.That(session.IsPlanModeEnabled, Is.True);
        }

        [Test]
        public async Task UpdateSessionSettingsAsync_OfficeModeUpdatesOfficeSession()
        {
            var store = CreateOfficeStore();
            var operatorUnderTest = CreateOperator(store, supportsReasoning: true, AppInteractionMode.Office);

            var result = await operatorUnderTest.UpdateSessionSettingsAsync(
                new MyCodex.Application.CodingRemote.CodingRemoteSessionSettingsUpdateRequest
                {
                    SessionId = "office-session",
                    IsPlanModeEnabled = true
                },
                CancellationToken.None);

            Assert.That(result.Updated, Is.True);
            Assert.That(result.WorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(store.Snapshot.Sessions[0].IsPlanModeEnabled, Is.True);
        }

        [Test]
        public async Task UpdateSessionSettingsAsync_ClearsReasoningWhenModelDoesNotSupportIt()
        {
            var store = CreateStore();
            var operatorUnderTest = CreateOperator(store, supportsReasoning: false);

            await operatorUnderTest.UpdateSessionSettingsAsync(
                new MyCodex.Application.CodingRemote.CodingRemoteSessionSettingsUpdateRequest
                {
                    SessionId = "session-a",
                    HasModelId = true,
                    ModelId = "plain-model",
                    HasReasoningEffort = true,
                    ReasoningEffort = "high",
                    HasApprovalMode = true,
                    ApprovalMode = null
                },
                CancellationToken.None);

            var session = store.Snapshot.Sessions[0];
            Assert.That(session.ModelId, Is.EqualTo("plain-model"));
            Assert.That(session.ReasoningEffort, Is.Null);
            Assert.That(session.ApprovalMode, Is.Null);
        }

        private static AppServerCodingRemoteSessionSettingsOperator CreateOperator(
            FakeAppStateStore store,
            bool supportsReasoning,
            AppInteractionMode interactionMode = AppInteractionMode.Coding)
        {
            return CreateOperator(store, () => ApprovalMode.Auto, supportsReasoning, interactionMode);
        }

        private static AppServerCodingRemoteSessionSettingsOperator CreateOperator(
            FakeAppStateStore store,
            Func<ApprovalMode> defaultApprovalMode)
        {
            return CreateOperator(store, defaultApprovalMode, true);
        }

        private static AppServerCodingRemoteSessionSettingsOperator CreateOperator(
            FakeAppStateStore store,
            Func<ApprovalMode> defaultApprovalMode,
            bool supportsReasoning,
            AppInteractionMode interactionMode = AppInteractionMode.Coding)
        {
            return new AppServerCodingRemoteSessionSettingsOperator(
                store,
                _ => Task.FromResult((IReadOnlyList<AppServerModelOption>)new List<AppServerModelOption>
                {
                    new AppServerModelOption
                    {
                        ModelId = "gpt-custom",
                        DisplayName = "GPT Custom",
                        DefaultReasoningEffort = "medium",
                        ReasoningEfforts =
                        {
                            new AppServerModelEffortOption { EffortId = "low" },
                            new AppServerModelEffortOption { EffortId = "medium" },
                            new AppServerModelEffortOption { EffortId = "high" }
                        }
                    }
                }),
                () => "gpt-default",
                () => "medium",
                defaultApprovalMode,
                session => string.IsNullOrWhiteSpace(session.ModelId) ? "gpt-default" : session.ModelId,
                session => string.IsNullOrWhiteSpace(session.ReasoningEffort) ? "medium" : session.ReasoningEffort,
                session => session.ApprovalMode ?? defaultApprovalMode(),
                _ => supportsReasoning,
                interactionMode);
        }

        private static FakeAppStateStore CreateStore()
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
        }

        private static FakeAppStateStore CreateOfficeStore()
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "office-session",
                            WorkspaceId = "office:reports",
                            Mode = AppInteractionMode.Office
                        },
                        new ChatSession
                        {
                            Id = "session-a",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
        }

        private static FakeAppStateStore CreateStoreWithSession(ChatSession session)
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        session
                    }
                }
            };
        }

        private sealed class FakeAppStateStore : IAppStateStore
        {
            public AppStateSnapshot Snapshot { get; set; }

            public int SaveCount { get; private set; }

            public AppStateSnapshot Load()
            {
                return Snapshot ?? new AppStateSnapshot();
            }

            public void Save(AppStateSnapshot snapshot)
            {
                Snapshot = snapshot;
                SaveCount++;
            }
        }
    }
}
