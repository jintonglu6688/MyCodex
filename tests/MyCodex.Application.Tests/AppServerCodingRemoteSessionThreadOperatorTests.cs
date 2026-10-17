using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.Shared.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteSessionThreadOperatorTests
    {
        [Test]
        public async Task OperateSessionThreadAsync_ForkFromTurn_UsesAtomicForkWhenSupported()
        {
            var store = new FakeAppStateStore { Snapshot = BuildSnapshot() };
            var client = new CapabilityRecordingThreadClient(true);
            client.Details.ThreadId = "thread-1";
            client.Details.TurnStatuses["turn-2"] = "completed";
            client.Details.TurnCount = 4;
            client.Details.Messages.Add(new AppServerThreadMessage { ThreadId = "thread-1", TurnId = "turn-2", TurnIndex = 2, Sender = "assistant", Content = "reply" });
            client.ForkSummary = new AppServerThreadSummary { ThreadId = "thread-2", WorkingDirectory = @"E:\MyCodex" };
            var op = new AppServerCodingRemoteSessionThreadOperator(store, client);

            var result = await op.OperateSessionThreadAsync(
                new CodingRemoteSessionThreadOperationRequest
                {
                    SessionId = "session-1",
                    Action = CodingRemoteSessionThreadActions.Fork,
                    Mode = CodingRemoteSessionThreadOperationModes.FromTurn,
                    TurnId = "turn-2",
                    Title = "Fork title"
                },
                CancellationToken.None);

            Assert.That(client.ForkedThreadId, Is.EqualTo("thread-1"));
            Assert.That(client.ForkedLastTurnId, Is.EqualTo("turn-2"));
            Assert.That(client.ForkCallCount, Is.EqualTo(1));
            Assert.That(result.CreatedSession.SessionId, Is.Not.Empty);
            Assert.That(result.CreatedSession.Title, Is.EqualTo("Fork title"));
            Assert.That(result.StatusText, Is.EqualTo(SharedLocalization.Text("CodingRemote.ThreadOperation.ForkCreated", "Fork created.")));
            var created = store.Load().Sessions.FirstOrDefault(session => session.ThreadId == "thread-2");
            Assert.That(created, Is.Not.Null);
            Assert.That(created.IsSubAgent, Is.False);
            Assert.That(created.ThreadSource, Is.Null.Or.Empty);
            Assert.That(created.ParentThreadId, Is.Null.Or.Empty);
        }

        [Test]
        public void OperateSessionThreadAsync_ForkFromTurn_ClientWithoutCapabilityFailsBeforeCreating()
        {
            var store = new FakeAppStateStore { Snapshot = BuildSnapshot() };
            var client = new RecordingThreadClient();
            var op = new AppServerCodingRemoteSessionThreadOperator(store, client);
            Assert.ThrowsAsync<System.NotSupportedException>(async () => await op.OperateSessionThreadAsync(
                new CodingRemoteSessionThreadOperationRequest { SessionId = "session-1", Action = "fork", Mode = "fromTurn", TurnId = "turn-1" }, CancellationToken.None));
            Assert.That(client.ForkCallCount, Is.Zero);
            Assert.That(store.Snapshot.Sessions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task OperateSessionThreadAsync_Compact_UsesCurrentWorkspaceAndSessionSettings()
        {
            var store = new FakeAppStateStore { Snapshot = BuildSnapshot() };
            var client = new RecordingThreadClient();
            var op = new AppServerCodingRemoteSessionThreadOperator(store, client);

            var result = await op.OperateSessionThreadAsync(
                new CodingRemoteSessionThreadOperationRequest
                {
                    SessionId = "session-1",
                    Action = CodingRemoteSessionThreadActions.Compact,
                    Mode = CodingRemoteSessionThreadOperationModes.Full
                },
                CancellationToken.None);

            Assert.That(client.CompactedThreadId, Is.EqualTo("thread-1"));
            Assert.That(client.CompactedWorkspacePath, Is.EqualTo(@"E:\MyCodex"));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.StatusText, Is.EqualTo(SharedLocalization.Text("CodingRemote.ThreadOperation.CompactionStarted", "Context compaction started.")));
        }

        [TestCase("inProgress")]
        [TestCase(null)]
        public void ForkFromTurn_RejectsUnfinishedTurnBeforeAnyMutation(string status)
        {
            var store = new FakeAppStateStore { Snapshot = BuildSnapshot() };
            var client = new CapabilityRecordingThreadClient(true);
            client.Details.ThreadId = "thread-1";
            client.Details.TurnStatuses["turn-1"] = status;
            Assert.ThrowsAsync<System.InvalidOperationException>(async () => await new AppServerCodingRemoteSessionThreadOperator(store, client)
                .OperateSessionThreadAsync(new CodingRemoteSessionThreadOperationRequest
                { SessionId = "session-1", Action = "fork", Mode = "fromTurn", TurnId = "turn-1" }, CancellationToken.None));
            Assert.That(client.ForkCallCount, Is.Zero);
            Assert.That(store.Snapshot.Sessions, Has.Count.EqualTo(1));
        }

        [TestCase(null, null)]
        [TestCase("model-local", "high")]
        public async Task FullFork_PreservesOverrideState(string model, string effort)
        {
            var store = new FakeAppStateStore { Snapshot = BuildSnapshot() };
            store.Snapshot.Sessions[0].ModelId = model;
            store.Snapshot.Sessions[0].ReasoningEffort = effort;
            var client = new RecordingThreadClient();
            var result = await new AppServerCodingRemoteSessionThreadOperator(store, client).OperateSessionThreadAsync(
                new CodingRemoteSessionThreadOperationRequest { SessionId = "session-1", Action = "fork", Mode = "full" }, CancellationToken.None);
            var created = store.Snapshot.Sessions.Single(session => session.Id == result.CreatedSession.SessionId);
            Assert.That(created.ModelId, Is.EqualTo(model));
            Assert.That(created.ReasoningEffort, Is.EqualTo(effort));
            Assert.That(client.ForkedLastTurnId, Is.Null);
        }

        private static AppStateSnapshot BuildSnapshot()
        {
            return new AppStateSnapshot
            {
                CurrentSessionId = "session-1",
                CurrentCodingSessionId = "session-1",
                RecentWorkspaces = new List<WorkspaceInfo>
                {
                    new WorkspaceInfo { Id = "workspace-1", Name = "MyCodex", RootPath = @"E:\MyCodex", Kind = WorkspaceKind.Coding }
                },
                Sessions = new List<ChatSession>
                {
                    new ChatSession
                    {
                        Id = "session-1",
                        WorkspaceId = "workspace-1",
                        ThreadId = "thread-1",
                        Title = "Main",
                        Mode = AppInteractionMode.Coding,
                        ApprovalMode = ApprovalMode.Auto
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

        private class RecordingThreadClient : IAppServerThreadClient
        {
            public RecordingThreadClient()
            {
                Details = new AppServerThreadDetails();
                ForkSummary = new AppServerThreadSummary { ThreadId = "thread-forked" };
            }

            public AppServerThreadDetails Details { get; }

            public AppServerThreadSummary ForkSummary { get; set; }

            public int ForkCallCount { get; private set; }


            public string ForkedThreadId { get; private set; }

            public string ForkedLastTurnId { get; private set; }



            public string CompactedThreadId { get; private set; }

            public string CompactedWorkspacePath { get; private set; }

            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId)
            {
                return Task.FromResult(Details);
            }

            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
            {
                return Task.FromResult(Details);
            }

            public Task<AppServerThreadSummary> ForkThreadAsync(string threadId)
            {
                ForkCallCount++;
                ForkedThreadId = threadId;
                return Task.FromResult(ForkSummary);
            }

            public Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId, string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort)
            {
                return ForkThreadAsync(threadId, lastTurnId);
            }


            public Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId)
            {
                ForkCallCount++;
                ForkedThreadId = threadId;
                ForkedLastTurnId = lastTurnId;
                return Task.FromResult(ForkSummary);
            }



            public Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                CompactedThreadId = threadId;
                CompactedWorkspacePath = workspacePath;
                return Task.CompletedTask;
            }

            public Task SetThreadNameAsync(string threadId, string name)
            {
                return Task.CompletedTask;
            }

            public Task<string> CreateThreadAsync(string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerThreadToolContext toolContext = null) { throw new System.NotSupportedException(); }
            public Task SendUserMessageAsync(string threadId, string workspacePath, IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null) { throw new System.NotSupportedException(); }
            public Task<List<AppServerThreadSummary>> ListThreadsAsync() { throw new System.NotSupportedException(); }
            public Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken) { throw new System.NotSupportedException(); }
            public Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null) { throw new System.NotSupportedException(); }
            public Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken) { throw new System.NotSupportedException(); }
            public Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath) { throw new System.NotSupportedException(); }
            public Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.FromResult<AppServerConversationSummary>(null); }
            public Task ArchiveThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task UnarchiveThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null) { throw new System.NotSupportedException(); }
            public Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId = null) { return Task.FromResult(AppServerTurnSteerResult.NotAttempted(null, "test-double")); }
            public Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId, string expectedTurnId) { return TrySteerTurnAsync(threadId, inputItems, clientUserMessageId); }
            public Task<AppServerThreadInjectItemsResult> InjectThreadItemsAsync(string threadId, IReadOnlyCollection<IDictionary<string, object>> items, CancellationToken cancellationToken = default(CancellationToken)) { return Task.FromResult(new AppServerThreadInjectItemsResult { Succeeded = true }); }
            public Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.CompletedTask; }
            public Task DeleteThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task UnsubscribeThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task DeleteThreadFileAsync(string sessionPath) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo) { throw new System.NotSupportedException(); }
        }

        private sealed class CapabilityRecordingThreadClient : RecordingThreadClient, IAppServerConnectionClient
        {
            public CapabilityRecordingThreadClient(bool supportsTurnBoundedFork)
            {
                ProtocolCapabilities = new AppServerProtocolCapabilities
                {
                    RuntimeVersion = supportsTurnBoundedFork ? "0.144.1" : "0.142.5",
                    SupportsThreadForkLastTurnId = supportsTurnBoundedFork
                };
            }

            public event System.EventHandler<AppServerEventArgs> EventReceived
            {
                add { }
                remove { }
            }

            public AppServerProtocolCapabilities ProtocolCapabilities { get; }

            public Task ConnectAsync()
            {
                return Task.CompletedTask;
            }

            public Task RestartAsync()
            {
                return Task.CompletedTask;
            }
        }
    }
}
