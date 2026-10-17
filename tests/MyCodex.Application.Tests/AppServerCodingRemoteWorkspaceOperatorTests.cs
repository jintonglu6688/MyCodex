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
    public sealed class AppServerCodingRemoteWorkspaceOperatorTests
    {
        [Test]
        public async Task OperateWorkspaceAsync_DeleteRecordOnlyRemovesWorkspaceAndSessionsFromAppState()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteWorkspaceOperator(store, client);

            var result = await operatorService.OperateWorkspaceAsync(
                new CodingRemoteWorkspaceOperationRequest
                {
                    WorkspaceId = "workspace-a",
                    Action = CodingRemoteWorkspaceActions.Delete,
                    DeleteLocalHistory = false
                },
                CancellationToken.None);

            Assert.That(result.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(result.Action, Is.EqualTo(CodingRemoteWorkspaceActions.Delete));
            Assert.That(result.DeleteLocalHistory, Is.False);
            Assert.That(result.ProcessedCount, Is.EqualTo(2));
            Assert.That(result.Removed, Is.True);
            Assert.That(store.Snapshot.RecentWorkspaces.Exists(workspace => workspace.Id == "workspace-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.WorkspaceId == "workspace-a"), Is.False);
            Assert.That(store.Snapshot.CurrentWorkspaceId, Is.Null);
            Assert.That(store.Snapshot.CurrentSessionId, Is.Null);
            Assert.That(client.ArchivedThreadIds, Is.Empty);
            Assert.That(client.DeletedThreadIds, Is.Empty);
            Assert.That(client.DeletedSessionPaths, Is.Empty);
        }

        [Test]
        public async Task OperateWorkspaceAsync_ArchiveArchivesThreadSessionsBeforeRemovingWorkspace()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteWorkspaceOperator(store, client);

            var result = await operatorService.OperateWorkspaceAsync(
                new CodingRemoteWorkspaceOperationRequest
                {
                    WorkspaceId = "workspace-a",
                    Action = CodingRemoteWorkspaceActions.Archive
                },
                CancellationToken.None);

            Assert.That(result.Action, Is.EqualTo(CodingRemoteWorkspaceActions.Archive));
            Assert.That(result.ProcessedCount, Is.EqualTo(1));
            Assert.That(client.ArchivedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a" }));
            Assert.That(store.Snapshot.RecentWorkspaces.Exists(workspace => workspace.Id == "workspace-a"), Is.True);
            Assert.That(store.Snapshot.Sessions.Where(session => session.WorkspaceId == "workspace-a").Select(session => session.Id), Is.EqualTo(new[] { "session-draft" }));
            Assert.That(result.Removed, Is.False);
        }

        [Test]
        public async Task OperateWorkspaceAsync_DeleteLocalHistoryDeletesThreadsBeforeRemovingWorkspace()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteWorkspaceOperator(store, client);

            var result = await operatorService.OperateWorkspaceAsync(
                new CodingRemoteWorkspaceOperationRequest
                {
                    WorkspaceId = "workspace-a",
                    Action = CodingRemoteWorkspaceActions.Delete,
                    DeleteLocalHistory = true
                },
                CancellationToken.None);

            Assert.That(result.Action, Is.EqualTo(CodingRemoteWorkspaceActions.Delete));
            Assert.That(result.DeleteLocalHistory, Is.True);
            Assert.That(result.ProcessedCount, Is.EqualTo(2));
            Assert.That(client.DeletedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a" }));
            Assert.That(client.UnsubscribedThreadIds, Is.Empty);
            Assert.That(client.DeletedSessionPaths, Is.Empty);
            Assert.That(store.Snapshot.RecentWorkspaces.Exists(workspace => workspace.Id == "workspace-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.WorkspaceId == "workspace-a"), Is.False);
        }

        [Test]
        public async Task OperateWorkspaceAsync_DeleteLocalHistoryFallsBackToSessionFileDeleteWhenThreadDeleteFails()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient { ThrowOnDeleteThread = true };
            var operatorService = new AppServerCodingRemoteWorkspaceOperator(store, client);

            var result = await operatorService.OperateWorkspaceAsync(
                new CodingRemoteWorkspaceOperationRequest
                {
                    WorkspaceId = "workspace-a",
                    Action = CodingRemoteWorkspaceActions.Delete,
                    DeleteLocalHistory = true
                },
                CancellationToken.None);

            Assert.That(result.Action, Is.EqualTo(CodingRemoteWorkspaceActions.Delete));
            Assert.That(result.DeleteLocalHistory, Is.True);
            Assert.That(result.ProcessedCount, Is.EqualTo(2));
            Assert.That(client.DeletedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a" }));
            Assert.That(client.UnsubscribedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-a" }));
            Assert.That(client.DeletedSessionPaths.ToArray(), Is.EqualTo(new[] { @"E:\Codex\thread-a.jsonl" }));
            Assert.That(store.Snapshot.RecentWorkspaces.Exists(workspace => workspace.Id == "workspace-a"), Is.False);
            Assert.That(store.Snapshot.Sessions.Exists(session => session.WorkspaceId == "workspace-a"), Is.False);
        }

        [Test]
        public void OperateWorkspaceAsync_MissingWorkspaceThrowsWorkspaceNotFound()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var operatorService = new AppServerCodingRemoteWorkspaceOperator(store, client);

            var ex = Assert.ThrowsAsync<CodingRemoteOperationException>(async () =>
                await operatorService.OperateWorkspaceAsync(
                    new CodingRemoteWorkspaceOperationRequest
                    {
                        WorkspaceId = "missing-workspace",
                        Action = CodingRemoteWorkspaceActions.Delete,
                        DeleteLocalHistory = false
                    },
                    CancellationToken.None));

            Assert.That(ex.ErrorCode, Is.EqualTo(RemoteErrorCodes.WorkspaceNotFound));
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
                        new WorkspaceInfo { Id = "workspace-a", Name = "Repo A", RootPath = @"E:\Repos\A", Kind = WorkspaceKind.Coding },
                        new WorkspaceInfo { Id = "workspace-b", Name = "Repo B", RootPath = @"E:\Repos\B", Kind = WorkspaceKind.Coding }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding,
                            ThreadId = "thread-a",
                            SessionPath = @"E:\Codex\thread-a.jsonl"
                        },
                        new ChatSession
                        {
                            Id = "session-draft",
                            WorkspaceId = "workspace-a",
                            Mode = AppInteractionMode.Coding
                        },
                        new ChatSession
                        {
                            Id = "session-b",
                            WorkspaceId = "workspace-b",
                            Mode = AppInteractionMode.Coding,
                            ThreadId = "thread-b"
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
            }

            public List<string> ArchivedThreadIds { get; }

            public List<string> DeletedThreadIds { get; }

            public List<string> UnsubscribedThreadIds { get; }

            public List<string> DeletedSessionPaths { get; }

            public bool ThrowOnDeleteThread { get; set; }

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
            public Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath) { var ids = ArchivedThreadIds.ToList(); return Task.FromResult(ids.Select(id => new AppServerThreadSummary { ThreadId = id }).ToList()); }
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
            public Task SetThreadNameAsync(string threadId, string name) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo) { throw new System.NotSupportedException(); }
        }
    }
}
