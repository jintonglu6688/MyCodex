using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteSessionOperatorTests
    {
        [Test]
        public async Task OperateSessionAsync_DeleteRecordOnlyRemovesSessionFromAppState()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "session-a",
                    Action = CodingRemoteSessionActions.Delete,
                    DeleteLocalHistory = false
                },
                CancellationToken.None);

            Assert.That(result.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(result.Action, Is.EqualTo(CodingRemoteSessionActions.Delete));
            Assert.That(result.DeleteLocalHistory, Is.False);
            Assert.That(result.ProcessedCount, Is.EqualTo(2));
            Assert.That(result.Removed, Is.True);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "session-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "child-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "session-b"), Is.True);
            Assert.That(store.Snapshot.CurrentSessionId, Is.EqualTo("session-b"));
            Assert.That(store.Snapshot.CurrentCodingSessionId, Is.EqualTo("session-b"));
            Assert.That(client.ArchivedThreadIds, Is.Empty);
            Assert.That(client.DeletedThreadIds, Is.Empty);
            Assert.That(client.DeletedSessionPaths, Is.Empty);
        }

        [Test]
        public async Task OperateSessionAsync_ArchiveArchivesThreadBeforeRemovingSession()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "session-a",
                    Action = CodingRemoteSessionActions.Archive
                },
                CancellationToken.None);

            Assert.That(result.Action, Is.EqualTo(CodingRemoteSessionActions.Archive));
            Assert.That(client.ArchivedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a" }));
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "session-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "child-a"), Is.False);
        }

        [Test]
        public void OperateSessionAsync_PartialArchiveKeepsUnconfirmedChild()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient { CascadeArchive = false };
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            Assert.ThrowsAsync<System.InvalidOperationException>(async () => await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest { SessionId = "session-a", Action = CodingRemoteSessionActions.Archive },
                CancellationToken.None));

            Assert.That(client.ArchivedThreadIds, Is.EqualTo(new[] { "thread-a" }));
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "session-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "child-a"), Is.True);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "session-b"), Is.True);
        }

        [Test]
        public async Task OperateSessionAsync_ArchiveIncludesOutOfOrderGrandchildren()
        {
            var store = CreateStore();
            store.Snapshot.Sessions.Insert(
                1,
                new ChatSession
                {
                    Id = "grandchild-a",
                    WorkspaceId = "workspace-a",
                    Mode = AppInteractionMode.Coding,
                    ThreadId = "grandchild-thread-a",
                    IsSubAgent = true,
                    ParentThreadId = "child-thread-a"
                });
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "session-a",
                    Action = CodingRemoteSessionActions.Archive
                },
                CancellationToken.None);

            Assert.That(result.ProcessedCount, Is.EqualTo(3));
            Assert.That(
                client.ArchivedThreadIds.ToArray(),
                Is.EqualTo(new[] { "thread-a" }));
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "grandchild-a"), Is.False);
        }

        [Test]
        public async Task OperateSessionAsync_DeleteLocalHistoryDeletesThreadBeforeRemovingSession()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "session-a",
                    Action = CodingRemoteSessionActions.Delete,
                    DeleteLocalHistory = true
                },
                CancellationToken.None);

            Assert.That(result.Action, Is.EqualTo(CodingRemoteSessionActions.Delete));
            Assert.That(result.DeleteLocalHistory, Is.True);
            Assert.That(result.ProcessedCount, Is.EqualTo(2));
            Assert.That(client.DeletedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a", "child-thread-a" }));
            Assert.That(client.UnsubscribedThreadIds, Is.Empty);
            Assert.That(client.DeletedSessionPaths, Is.Empty);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "session-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "child-a"), Is.False);
        }

        [Test]
        public async Task OperateSessionAsync_DeleteLocalHistoryFallsBackToSessionFileDeleteWhenThreadDeleteFails()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient { ThrowOnDeleteThread = true };
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "session-a",
                    Action = CodingRemoteSessionActions.Delete,
                    DeleteLocalHistory = true
                },
                CancellationToken.None);

            Assert.That(result.Action, Is.EqualTo(CodingRemoteSessionActions.Delete));
            Assert.That(result.DeleteLocalHistory, Is.True);
            Assert.That(result.ProcessedCount, Is.EqualTo(2));
            Assert.That(client.DeletedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a", "child-thread-a" }));
            Assert.That(client.UnsubscribedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a", "child-thread-a" }));
            Assert.That(client.DeletedSessionPaths.ToArray(), Is.EqualTo(new[] { @"E:\Codex\thread-a.jsonl", @"E:\Codex\child-thread-a.jsonl" }));
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "session-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "child-a"), Is.False);
        }

        [Test]
        public void OperateSessionAsync_MissingSessionThrowsSessionNotFound()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var ex = Assert.ThrowsAsync<CodingRemoteOperationException>(async () =>
                await operatorService.OperateSessionAsync(
                    new CodingRemoteSessionOperationRequest
                    {
                        SessionId = "missing-session",
                        Action = CodingRemoteSessionActions.Delete
                    },
                    CancellationToken.None));

            Assert.That(ex.ErrorCode, Is.EqualTo(RemoteErrorCodes.SessionNotFound));
        }

        [Test]
        public async Task OperateSessionAsync_RenameUpdatesAppStateAndThreadName()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "session-a",
                    Action = CodingRemoteSessionActions.Rename,
                    Title = "  Updated title  "
                },
                CancellationToken.None);

            Assert.That(result.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(result.Action, Is.EqualTo(CodingRemoteSessionActions.Rename));
            Assert.That(result.Title, Is.EqualTo("Updated title"));
            Assert.That(result.Updated, Is.True);
            Assert.That(result.Removed, Is.False);
            Assert.That(result.ProcessedCount, Is.EqualTo(1));
            Assert.That(store.Snapshot.Sessions.Find(session => session.Id == "session-a").Title, Is.EqualTo("Updated title"));
            Assert.That(client.RenamedThreads.ToArray(), Is.EqualTo(new[] { "thread-a=Updated title" }));
        }

        [Test]
        public async Task OperateSessionAsync_RenameKeepsLocalTitleWhenThreadRenameFails()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient { ThrowOnRename = true };
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "session-a",
                    Action = CodingRemoteSessionActions.Rename,
                    Title = "Local title"
                },
                CancellationToken.None);

            Assert.That(result.Updated, Is.True);
            Assert.That(result.Title, Is.EqualTo("Local title"));
            Assert.That(store.Snapshot.Sessions.Find(session => session.Id == "session-a").Title, Is.EqualTo("Local title"));
            Assert.That(result.StatusText, Does.Contain("remote sync failed"));
        }

        [Test]
        public async Task OperateSessionAsync_OfficeModeDeleteClearsOfficeSelectionOnly()
        {
            var store = CreateOfficeStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteSessionOperator(store, client, AppInteractionMode.Office);

            var result = await operatorService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = "office-session",
                    Action = CodingRemoteSessionActions.Delete,
                    DeleteLocalHistory = false
                },
                CancellationToken.None);

            Assert.That(result.SessionId, Is.EqualTo("office-session"));
            Assert.That(result.WorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(store.Snapshot.Sessions.Exists(session => session.Id == "office-session"), Is.False);
            Assert.That(store.Snapshot.CurrentSessionId, Is.Null);
            Assert.That(store.Snapshot.CurrentOfficeSessionId, Is.Null);
            Assert.That(store.Snapshot.CurrentCodingSessionId, Is.EqualTo("coding-session"));
        }

        private static FakeAppStateStore CreateStore()
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    CurrentWorkspaceId = "workspace-a",
                    CurrentSessionId = "session-a",
                    CurrentCodingWorkspaceId = "workspace-a",
                    CurrentCodingSessionId = "session-a",
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo { Id = "workspace-a", Name = "Repo A", RootPath = @"E:\Repos\A", Kind = WorkspaceKind.Coding }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            RuntimeProfileId = "windows",
                            ThreadId = "thread-a",
                            SessionPath = @"E:\Codex\thread-a.jsonl"
                        },
                        new ChatSession
                        {
                            Id = "child-a",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            RuntimeProfileId = "windows",
                            ThreadId = "child-thread-a",
                            SessionPath = @"E:\Codex\child-thread-a.jsonl",
                            IsSubAgent = true,
                            ParentThreadId = "thread-a"
                        },
                        new ChatSession
                        {
                            Id = "session-b",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            ThreadId = "thread-b"
                        }
                    },
                    RuntimeProfiles =
                    {
                        new RuntimeProfileStateSnapshot
                        {
                            RuntimeProfileId = "windows",
                            CurrentWorkspaceId = "workspace-a",
                            CurrentSessionId = "session-a"
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
                    CurrentWorkspaceId = "office:reports",
                    CurrentSessionId = "office-session",
                    CurrentCodingWorkspaceId = "workspace-a",
                    CurrentCodingSessionId = "coding-session",
                    CurrentOfficeWorkspaceId = "office:reports",
                    CurrentOfficeSessionId = "office-session",
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo { Id = "office:reports", Name = "Reports", RootPath = @"E:\Documents\Reports", Kind = WorkspaceKind.Office },
                        new WorkspaceInfo { Id = "workspace-a", Name = "Repo A", RootPath = @"E:\Repos\A", Kind = WorkspaceKind.Coding }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "office-session",
                            WorkspaceId = "office:reports",
                            Mode = AppInteractionMode.Office,
                            ThreadId = "thread-office"
                        },
                        new ChatSession
                        {
                            Id = "coding-session",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            ThreadId = "thread-coding"
                        }
                    }
                }
            };
        }

        private sealed class FakeAppStateStore : IAppStateStore
        {
            public AppStateSnapshot Snapshot { get; set; }

            public AppStateSnapshot Load()
            {
                return Snapshot ?? new AppStateSnapshot();
            }

            public void Save(AppStateSnapshot snapshot)
            {
                Snapshot = snapshot;
            }
        }

        private sealed class RecordingThreadClient : IAppServerThreadClient
        {
            public RecordingThreadClient()
            {
                ArchivedThreadIds = new List<string>();
                DeletedThreadIds = new List<string>();
                UnsubscribedThreadIds = new List<string>();
                DeletedSessionPaths = new List<string>();
                RenamedThreads = new List<string>();
            }

            public List<string> ArchivedThreadIds { get; }

            public List<string> DeletedThreadIds { get; }

            public List<string> UnsubscribedThreadIds { get; }

            public List<string> DeletedSessionPaths { get; }

            public List<string> RenamedThreads { get; }

            public bool ThrowOnDeleteThread { get; set; }

            public bool ThrowOnRename { get; set; }

            public bool CascadeArchive { get; set; } = true;

            public Task ArchiveThreadAsync(string threadId)
            {
                ArchivedThreadIds.Add(threadId);
                return Task.CompletedTask;
            }

            public Task DeleteThreadAsync(string threadId)
            {
                DeletedThreadIds.Add(threadId);
                if (ThrowOnDeleteThread)
                {
                    throw new System.InvalidOperationException("Native thread delete unavailable.");
                }

                return Task.CompletedTask;
            }

            public Task UnsubscribeThreadAsync(string threadId)
            {
                UnsubscribedThreadIds.Add(threadId);
                return Task.CompletedTask;
            }

            public Task DeleteThreadFileAsync(string sessionPath)
            {
                DeletedSessionPaths.Add(sessionPath);
                return Task.CompletedTask;
            }

            public Task<string> CreateThreadAsync(string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerThreadToolContext toolContext = null) { throw new System.NotSupportedException(); }
            public Task SendUserMessageAsync(string threadId, string workspacePath, IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null) { throw new System.NotSupportedException(); }
            public Task<List<AppServerThreadSummary>> ListThreadsAsync() { return Task.FromResult(new List<AppServerThreadSummary>()); }
            public Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken) { return Task.FromResult(new List<AppServerThreadSummary>()); }
            public Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null) { throw new System.NotSupportedException(); }
            public Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken) { throw new System.NotSupportedException(); }
            public Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath)
            {
                var ids = ArchivedThreadIds.ToList();
                if (CascadeArchive && ids.Contains("thread-a")) ids.AddRange(new[] { "child-thread-a", "grandchild-thread-a" });
                return Task.FromResult(ids.Select(id => new AppServerThreadSummary { ThreadId = id }).ToList());
            }
            public Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.FromResult<AppServerConversationSummary>(null); }
            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId) { return Task.FromResult(new AppServerThreadDetails()); }
            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken) { return Task.FromResult(new AppServerThreadDetails()); }
            public Task UnarchiveThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadSummary> ForkThreadAsync(string threadId) { throw new System.NotSupportedException(); }

            public Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId, string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort)
            {
                return ForkThreadAsync(threadId, lastTurnId);
            }

            public Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId) { throw new System.NotSupportedException(); }
            public Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId = null) { return Task.FromResult(AppServerTurnSteerResult.NotAttempted(null, "test-double")); }
            public Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId, string expectedTurnId) { return TrySteerTurnAsync(threadId, inputItems, clientUserMessageId); }
            public Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(string threadId, IReadOnlyCollection<IDictionary<string, object>> items, CancellationToken cancellationToken = default(CancellationToken)) { return Task.FromResult(new AppServerThreadInjectItemsResult { Succeeded = true }); }
            public Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.CompletedTask; }
            public Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null) { throw new System.NotSupportedException(); }
            public Task SetThreadNameAsync(string threadId, string name)
            {
                if (ThrowOnRename)
                {
                    throw new System.InvalidOperationException("Thread rename unavailable.");
                }

                RenamedThreads.Add(threadId + "=" + name);
                return Task.CompletedTask;
            }
            public Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo) { throw new System.NotSupportedException(); }
        }
    }
}
