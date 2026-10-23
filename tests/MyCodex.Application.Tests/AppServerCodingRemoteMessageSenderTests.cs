using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteMessageSenderTests
    {
        [Test]
        public async Task SendMessageAsync_UsesSessionThreadWorkspaceAndTextInput()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var sender = CreateSender(store, client);

            var result = await sender.SendMessageAsync(
                new CodingRemoteMessageSendRequest
                {
                    SessionId = "session-a",
                    OperationId = "operation-a",
                    Text = "hello from android"
                },
                CancellationToken.None);

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.OperationId, Is.EqualTo("operation-a"));
            Assert.That(result.DispatchMode, Is.EqualTo("start"));
            Assert.That(client.SentThreadId, Is.EqualTo("thread-a"));
            Assert.That(client.SentWorkspacePath, Is.EqualTo(@"E:\Repos\A"));
            Assert.That(client.SentInputs.Single().Type, Is.EqualTo("text"));
            Assert.That(client.SentInputs.Single().Text, Is.EqualTo("hello from android"));
            Assert.That(client.SentApprovalMode, Is.EqualTo(ApprovalMode.Auto));
            Assert.That(client.SentModelId, Is.EqualTo("gpt-test"));
            Assert.That(client.SentReasoningEffort, Is.EqualTo("medium"));
        }

        [Test]
        public async Task SendMessageAsync_ActiveTurnDispatcherCanHandleMessageWithoutStartingTurn()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            CodingRemoteActiveTurnDispatchContext dispatchedContext = null;
            var sender = CreateSender(
                store,
                client,
                null,
                AppInteractionMode.Coding,
                (session, workspace, model, effort) => null,
                (context, cancellationToken) =>
                {
                    dispatchedContext = context;
                    return Task.FromResult(new CodingRemoteMessageSendResult
                    {
                        SessionId = context.SessionId,
                        OperationId = context.OperationId,
                        Accepted = true,
                        DispatchMode = "queue"
                    });
                });

            var result = await sender.SendMessageAsync(
                new CodingRemoteMessageSendRequest
                {
                    SessionId = "session-a",
                    OperationId = "operation-a",
                    Text = "follow up"
                },
                CancellationToken.None);

            Assert.That(result.DispatchMode, Is.EqualTo("queue"));
            Assert.That(dispatchedContext, Is.Not.Null);
            Assert.That(dispatchedContext.SessionId, Is.EqualTo("session-a"));
            Assert.That(dispatchedContext.InputItems.Single().Text, Is.EqualTo("follow up"));
            Assert.That(dispatchedContext.ModelId, Is.EqualTo("gpt-test"));
            Assert.That(client.SentThreadId, Is.Null);
        }

        [Test]
        public async Task SendMessageAsync_UsesStructuredInputItemsWhenProvided()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var sender = CreateSender(store, client);

            await sender.SendMessageAsync(
                new CodingRemoteMessageSendRequest
                {
                    SessionId = "session-a",
                    OperationId = "operation-a",
                    Text = "run prompt",
                    InputItems = new List<CodingRemoteInputItem>
                    {
                        new CodingRemoteInputItem
                        {
                            Type = "mention",
                            Name = "mycodex-office",
                            DisplayName = "MyCodex Office",
                            Path = @"E:\Plugins\mycodex-office"
                        },
                        new CodingRemoteInputItem
                        {
                            Type = "skill",
                            Name = "writing-plans",
                            DisplayName = "Writing Plans",
                            Path = @"E:\Skills\writing-plans"
                        }
                    }
                },
                CancellationToken.None);

            Assert.That(client.SentInputs.Select(item => item.Type).ToArray(), Is.EqualTo(new[] { "mention", "skill", "text" }));
            Assert.That(client.SentInputs.ElementAt(0).Name, Is.EqualTo("mycodex-office"));
            Assert.That(client.SentInputs.ElementAt(0).Path, Is.EqualTo(@"E:\Plugins\mycodex-office"));
            Assert.That(client.SentInputs.ElementAt(1).Name, Is.EqualTo("writing-plans"));
            Assert.That(client.SentInputs.ElementAt(1).Path, Is.EqualTo(@"E:\Skills\writing-plans"));
            Assert.That(client.SentInputs.ElementAt(2).Text, Is.EqualTo("run prompt"));
        }

        [Test]
        public async Task SendMessageAsync_AllowsAttachmentOnlyInputsAndBuildsPreview()
        {
            var store = CreateStore();
            var client = new RecordingThreadClient();
            var sender = CreateSender(store, client);

            await sender.SendMessageAsync(
                new CodingRemoteMessageSendRequest
                {
                    SessionId = "session-a",
                    OperationId = "operation-a",
                    InputItems = new List<CodingRemoteInputItem>
                    {
                        new CodingRemoteInputItem
                        {
                            Type = "skill",
                            Name = "writing-plans",
                            DisplayName = "Writing Plans",
                            Path = @"E:\Skills\writing-plans"
                        }
                    }
                },
                CancellationToken.None);

            Assert.That(client.SentInputs.Single().Type, Is.EqualTo("skill"));
            Assert.That(store.Snapshot.Sessions[0].Preview, Is.EqualTo("[技能: Writing Plans]"));
        }

        [Test]
        public async Task SendMessageAsync_RecreatesMissingThreadBeforeSend()
        {
            var store = CreateStore();
            store.Snapshot.Sessions[0].ThreadId = null;
            var client = new RecordingThreadClient
            {
                CreatedThreadId = "thread-created"
            };
            var sender = CreateSender(store, client);

            await sender.SendMessageAsync(
                new CodingRemoteMessageSendRequest
                {
                    SessionId = "session-a",
                    OperationId = "operation-a",
                    Text = "start"
                },
                CancellationToken.None);

            Assert.That(client.CreatedWorkspacePath, Is.EqualTo(@"E:\Repos\A"));
            Assert.That(client.SentThreadId, Is.EqualTo("thread-created"));
            Assert.That(store.Snapshot.Sessions[0].ThreadId, Is.EqualTo("thread-created"));
        }

        [Test]
        public async Task SendMessageAsync_RecreatedThreadPublishesSessionThreadMutation()
        {
            var store = CreateStore();
            store.Snapshot.Sessions[0].ThreadId = null;
            var client = new RecordingThreadClient
            {
                CreatedThreadId = "thread-created"
            };
            var mutationSink = new RecordingRemoteHostMutationSink();
            var sender = CreateSender(store, client, mutationSink);

            await sender.SendMessageAsync(
                new CodingRemoteMessageSendRequest
                {
                    SessionId = "session-a",
                    OperationId = "operation-a",
                    Text = "start"
                },
                CancellationToken.None);

            Assert.That(mutationSink.Mutations, Has.Count.EqualTo(1));
            Assert.That(mutationSink.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.SessionThreadUpdated));
            Assert.That(mutationSink.Mutations[0].SessionId, Is.EqualTo("session-a"));
            Assert.That(mutationSink.Mutations[0].WorkspaceId, Is.EqualTo("workspace-a"));
        }

        [Test]
        public async Task SendMessageAsync_OfficeModeUsesOfficeWorkspaceAndCollaborationMode()
        {
            var store = CreateOfficeStore();
            var client = new RecordingThreadClient();
            var sender = CreateSender(
                store,
                client,
                null,
                AppInteractionMode.Office,
                (session, workspace, modelId, reasoningEffort) => new AppServerCollaborationMode
                {
                    Mode = "default",
                    ModelId = modelId,
                    ReasoningEffort = reasoningEffort,
                    DeveloperInstructions = "Office instructions for " + workspace.RootPath
                });

            await sender.SendMessageAsync(
                new CodingRemoteMessageSendRequest
                {
                    SessionId = "office-session",
                    OperationId = "operation-office",
                    Text = "draft report"
                },
                CancellationToken.None);

            Assert.That(client.SentWorkspacePath, Is.EqualTo(@"E:\Documents\MyCodex-office\Reports"));
            Assert.That(client.SentInputs.Single().Text, Is.EqualTo("draft report"));
            Assert.That(client.SentCollaborationMode, Is.Not.Null);
            Assert.That(client.SentCollaborationMode.Mode, Is.EqualTo("default"));
            Assert.That(client.SentCollaborationMode.DeveloperInstructions, Does.Contain(@"E:\Documents\MyCodex-office\Reports"));
            Assert.That(store.Snapshot.Sessions.Single().Preview, Is.EqualTo("draft report"));
        }

        private static AppServerCodingRemoteMessageSender CreateSender(FakeAppStateStore store, RecordingThreadClient client)
        {
            return CreateSender(store, client, null);
        }

        private static AppServerCodingRemoteMessageSender CreateSender(FakeAppStateStore store, RecordingThreadClient client, IRemoteHostMutationSink mutationSink)
        {
            return CreateSender(store, client, mutationSink, AppInteractionMode.Coding, (session, workspace, model, effort) => null);
        }

        private static AppServerCodingRemoteMessageSender CreateSender(
            FakeAppStateStore store,
            RecordingThreadClient client,
            IRemoteHostMutationSink mutationSink,
            AppInteractionMode interactionMode,
            Func<ChatSession, WorkspaceInfo, string, string, AppServerCollaborationMode> collaborationModeResolver,
            Func<CodingRemoteActiveTurnDispatchContext, CancellationToken, Task<CodingRemoteMessageSendResult>> activeTurnDispatcher = null)
        {
            return new AppServerCodingRemoteMessageSender(
                store,
                new SessionThreadService(client, new WorkspaceService(), _ => null),
                _ => ApprovalMode.Auto,
                _ => "gpt-test",
                _ => "medium",
                collaborationModeResolver,
                (session, workspace) => null,
                mutationSink,
                interactionMode,
                activeTurnDispatcher);
        }

        private static FakeAppStateStore CreateStore()
        {
            return new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding
                        }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            WorkspaceId = "workspace-a",
                            ThreadId = "thread-a",
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
                    RecentWorkspaces =
                    {
                        new WorkspaceInfo
                        {
                            Id = "office:reports",
                            RootPath = @"E:\Documents\MyCodex-office\Reports",
                            Kind = WorkspaceKind.Office
                        },
                        new WorkspaceInfo
                        {
                            Id = "workspace-a",
                            RootPath = @"E:\Repos\A",
                            Kind = WorkspaceKind.Coding
                        }
                    },
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "office-session",
                            WorkspaceId = "office:reports",
                            ThreadId = "thread-office",
                            Mode = AppInteractionMode.Office
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
                SentInputs = new List<AppServerUserInputItem>();
                CreatedThreadId = "thread-created";
            }

            public string CreatedThreadId { get; set; }

            public string CreatedWorkspacePath { get; private set; }

            public string SentThreadId { get; private set; }

            public string SentWorkspacePath { get; private set; }

            public IReadOnlyCollection<AppServerUserInputItem> SentInputs { get; private set; }

            public ApprovalMode? SentApprovalMode { get; private set; }

            public string SentModelId { get; private set; }

            public string SentReasoningEffort { get; private set; }

            public AppServerCollaborationMode SentCollaborationMode { get; private set; }

            public Task<string> CreateThreadAsync(string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerThreadToolContext toolContext = null)
            {
                CreatedWorkspacePath = workspacePath;
                return Task.FromResult(CreatedThreadId);
            }

            public Task SendUserMessageAsync(string threadId, string workspacePath, IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode, string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null)
            {
                SentThreadId = threadId;
                SentWorkspacePath = workspacePath;
                SentInputs = inputItems;
                SentApprovalMode = approvalMode;
                SentModelId = modelId;
                SentReasoningEffort = reasoningEffort;
                SentCollaborationMode = collaborationMode;
                return Task.FromResult(0);
            }

            public Task<List<AppServerThreadSummary>> ListThreadsAsync() { throw new System.NotSupportedException(); }
            public Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken) { throw new System.NotSupportedException(); }
            public Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null) { throw new System.NotSupportedException(); }
            public Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken) { throw new System.NotSupportedException(); }
            public Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath) { throw new System.NotSupportedException(); }
            public Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.FromResult<AppServerConversationSummary>(null); }
            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken) { throw new System.NotSupportedException(); }
            public Task ArchiveThreadAsync(string threadId) { throw new System.NotSupportedException(); }
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
            public Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken)) { return Task.CompletedTask; }
            public Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null) { throw new System.NotSupportedException(); }
            public Task DeleteThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task UnsubscribeThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task DeleteThreadFileAsync(string sessionPath) { throw new System.NotSupportedException(); }
            public Task SetThreadNameAsync(string threadId, string name) { return Task.FromResult(0); }
            public Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo) { return Task.FromResult(0); }
        }

        private sealed class RecordingRemoteHostMutationSink : IRemoteHostMutationSink
        {
            public RecordingRemoteHostMutationSink()
            {
                Mutations = new List<RemoteHostMutation>();
            }

            public List<RemoteHostMutation> Mutations { get; }

            public Task PublishAsync(RemoteHostMutation mutation, CancellationToken cancellationToken)
            {
                Mutations.Add(mutation);
                return Task.CompletedTask;
            }
        }
    }
}
