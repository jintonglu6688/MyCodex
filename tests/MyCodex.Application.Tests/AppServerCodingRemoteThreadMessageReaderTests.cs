using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerCodingRemoteThreadMessageReaderTests
    {
        [Test]
        public async Task ListMessagesAsync_ReadsThreadMessagesForSessionThreadId()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            ThreadId = "thread-a",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient { HasInterruptibleTurnResult = true };
            threadClient.Details.Messages.Add(new AppServerThreadMessage
            {
                ThreadId = "thread-a",
                TurnId = "turn-1",
                TurnIndex = 0,
                Sender = "User",
                Content = "hello"
            });
            threadClient.Details.Messages.Add(new AppServerThreadMessage
            {
                ThreadId = "thread-a",
                TurnId = "turn-1",
                TurnIndex = 0,
                Sender = "Codex",
                Content = "done"
            });
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest
                {
                    SessionId = "session-a",
                    Limit = 50
                },
                CancellationToken.None);

            Assert.That(threadClient.LastThreadId, Is.EqualTo("thread-a"));
            Assert.That(page.SessionId, Is.EqualTo("session-a"));
            Assert.That(page.Messages, Has.Count.EqualTo(2));
            Assert.That(page.Messages[0].SessionId, Is.EqualTo("session-a"));
            Assert.That(page.Messages[0].Role, Is.EqualTo(CodingRemoteMessageRoles.User));
            Assert.That(page.Messages[0].Text, Is.EqualTo("hello"));
            Assert.That(page.Messages[1].Role, Is.EqualTo(CodingRemoteMessageRoles.Assistant));
            Assert.That(page.Messages[1].Text, Is.EqualTo("done"));
            Assert.That(page.IsGenerating, Is.True);
        }

        [Test]
        public async Task ListMessagesAsync_WithoutThreadId_ReturnsEmptyPageWithoutCallingAppServer()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient();
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(new CodingRemoteMessagesListRequest { SessionId = "session-a" }, CancellationToken.None);

            Assert.That(page.SessionId, Is.EqualTo("session-a"));
            Assert.That(page.Messages, Is.Empty);
            Assert.That(threadClient.LastThreadId, Is.Null);
        }

        [Test]
        public async Task ListMessagesAsync_WithZeroLimitReturnsAllMessages()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            ThreadId = "thread-a",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient();
            for (var i = 0; i < 250; i++)
            {
                threadClient.Details.Messages.Add(new AppServerThreadMessage
                {
                    ThreadId = "thread-a",
                    TurnId = "turn-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    TurnIndex = i,
                    Sender = "Codex",
                    Content = "message-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
            }
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest
                {
                    SessionId = "session-a",
                    Limit = 0
                },
                CancellationToken.None);

            Assert.That(page.Messages, Has.Count.EqualTo(250));
            Assert.That(page.Messages[0].Text, Is.EqualTo("message-0"));
            Assert.That(page.Messages[249].Text, Is.EqualTo("message-249"));
        }

        [Test]
        public async Task ListMessagesAsync_ReturnsTurnMetadataAndActionsForAssistantMessage()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-1",
                            ThreadId = "thread-1",
                            WorkspaceId = "workspace-1",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient();
            threadClient.Details.TurnCount = 3;
            threadClient.Details.Messages.Add(new AppServerThreadMessage
            {
                ThreadId = "thread-1",
                TurnId = "turn-2",
                TurnIndex = 2,
                Sender = "assistant",
                Content = "assistant reply"
            });
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest { SessionId = "session-1", Limit = 0 },
                CancellationToken.None);

            Assert.That(page.Messages, Has.Count.EqualTo(1));
            var message = page.Messages[0];
            Assert.That(message.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(message.TurnId, Is.EqualTo("turn-2"));
            Assert.That(message.TurnIndex, Is.EqualTo(2));
            Assert.That(message.TurnCount, Is.EqualTo(3));
            Assert.That(message.Actions.Copy, Is.True);
            Assert.That(message.Actions.ForkFromTurn, Is.True);
            Assert.That(message.OperationPreview.TrimTurnsAfterTurn, Is.EqualTo(1));
        }

        [Test]
        public async Task ListMessagesAsync_OfficeModeReadsOfficeSessionWithoutTurnOperations()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "office-session",
                            ThreadId = "thread-office",
                            WorkspaceId = "office:reports",
                            Mode = AppInteractionMode.Office
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient();
            threadClient.Details.TurnCount = 3;
            threadClient.Details.Messages.Add(new AppServerThreadMessage
            {
                ThreadId = "thread-office",
                TurnId = "turn-2",
                TurnIndex = 2,
                Sender = "assistant",
                Content = "office reply"
            });
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient, AppInteractionMode.Office);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest { SessionId = "office-session", Limit = 0 },
                CancellationToken.None);

            Assert.That(threadClient.LastThreadId, Is.EqualTo("thread-office"));
            Assert.That(page.Messages, Has.Count.EqualTo(1));
            var message = page.Messages[0];
            Assert.That(message.Text, Is.EqualTo("office reply"));
            Assert.That(message.Actions.Copy, Is.True);
            Assert.That(message.Actions.ForkFromTurn, Is.False);
        }

        [Test]
        public async Task ListMessagesAsync_MessageWithoutValidTurnOnlyExposesCopy()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-1",
                            ThreadId = "thread-1",
                            WorkspaceId = "workspace-1",
                            Mode = AppInteractionMode.Coding
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient();
            threadClient.Details.TurnCount = 3;
            threadClient.Details.Messages.Add(new AppServerThreadMessage
            {
                ThreadId = "thread-1",
                TurnId = "",
                TurnIndex = 0,
                Sender = "system",
                Content = "system note"
            });
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest { SessionId = "session-1", Limit = 0 },
                CancellationToken.None);

            var message = page.Messages[0];
            Assert.That(message.Actions.Copy, Is.True);
            Assert.That(message.Actions.ForkFromTurn, Is.False);
            Assert.That(message.OperationPreview.TrimTurnsAfterTurn, Is.EqualTo(0));
        }

        [Test]
        public async Task ListMessagesAsync_ImportedSessionWithExistingSessionFilePrefersAppServerWhenAvailable()
        {
            var sessionPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "remote-session-file-reader.jsonl");
            File.WriteAllLines(
                sessionPath,
                new[]
                {
                    "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"hello from file\"}]}}",
                    "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"done from file\"}]}}",
                    "{\"type\":\"response_item\",\"payload\":{\"type\":\"function_call\",\"name\":\"ignored\"}}"
                });
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            ThreadId = "thread-a",
                            Mode = AppInteractionMode.Coding,
                            IsImported = true,
                            SessionPath = sessionPath
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient();
            threadClient.Details.TurnCount = 1;
            threadClient.Details.Messages.Add(new AppServerThreadMessage
            {
                ThreadId = "thread-a",
                TurnId = "turn-appserver-1",
                TurnIndex = 1,
                Sender = "Codex",
                Content = "from app server"
            });
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest
                {
                    SessionId = "session-a",
                    Limit = 50
                },
                CancellationToken.None);

            Assert.That(threadClient.LastThreadId, Is.EqualTo("thread-a"));
            Assert.That(page.Messages, Has.Count.EqualTo(1));
            Assert.That(page.Messages[0].Text, Is.EqualTo("from app server"));
            Assert.That(page.Messages[0].TurnId, Is.EqualTo("turn-appserver-1"));
        }

        [Test]
        public async Task ListMessagesAsync_ImportedSessionFileFallbackDoesNotExposeTurnOperations()
        {
            var sessionPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "remote-session-file-reader-fallback.jsonl");
            File.WriteAllLines(
                sessionPath,
                new[]
                {
                    "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"hello from file\"}]}}",
                    "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"done from file\"}]}}"
                });
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            ThreadId = "thread-a",
                            Mode = AppInteractionMode.Coding,
                            IsImported = true,
                            SessionPath = sessionPath
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient { ThrowOnRead = true };
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest
                {
                    SessionId = "session-a",
                    Limit = 50
                },
                CancellationToken.None);

            Assert.That(threadClient.LastThreadId, Is.EqualTo("thread-a"));
            Assert.That(page.Messages, Has.Count.EqualTo(2));
            Assert.That(page.Messages[0].Text, Is.EqualTo("hello from file"));
            Assert.That(page.Messages[0].Actions.Copy, Is.True);
            Assert.That(page.Messages[0].Actions.ForkFromTurn, Is.False);
        }

        [Test]
        public async Task ListMessagesAsync_ImportedSessionWithMissingSessionFileFallsBackToAppServer()
        {
            var store = new FakeAppStateStore
            {
                Snapshot = new AppStateSnapshot
                {
                    Sessions =
                    {
                        new ChatSession
                        {
                            Id = "session-a",
                            ThreadId = "thread-a",
                            Mode = AppInteractionMode.Coding,
                            IsImported = true,
                            SessionPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "missing.jsonl")
                        }
                    }
                }
            };
            var threadClient = new FakeThreadClient();
            threadClient.Details.Messages.Add(new AppServerThreadMessage
            {
                ThreadId = "thread-a",
                TurnId = "turn-1",
                TurnIndex = 0,
                Sender = "Codex",
                Content = "from app server"
            });
            var reader = new AppServerCodingRemoteThreadMessageReader(store, threadClient);

            var page = await reader.ListMessagesAsync(
                new CodingRemoteMessagesListRequest
                {
                    SessionId = "session-a",
                    Limit = 50
                },
                CancellationToken.None);

            Assert.That(threadClient.LastThreadId, Is.EqualTo("thread-a"));
            Assert.That(page.Messages, Has.Count.EqualTo(1));
            Assert.That(page.Messages[0].Text, Is.EqualTo("from app server"));
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

        private sealed class FakeThreadClient : IAppServerThreadClient, IAppServerInteractionClient
        {
            public FakeThreadClient()
            {
                Details = new AppServerThreadDetails
                {
                    ThreadId = "thread-a"
                };
            }

            public AppServerThreadDetails Details { get; }

            public bool ThrowOnRead { get; set; }

            public bool HasInterruptibleTurnResult { get; set; }

            public string LastThreadId { get; private set; }

            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId)
            {
                LastThreadId = threadId;
                if (ThrowOnRead)
                {
                    throw new System.InvalidOperationException("App Server read failed.");
                }

                return Task.FromResult(Details);
            }

            public Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
            {
                LastThreadId = threadId;
                if (ThrowOnRead)
                {
                    throw new System.InvalidOperationException("App Server read failed.");
                }

                return Task.FromResult(Details);
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
            public Task DeleteThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task UnsubscribeThreadAsync(string threadId) { throw new System.NotSupportedException(); }
            public Task DeleteThreadFileAsync(string sessionPath) { throw new System.NotSupportedException(); }
            public Task SetThreadNameAsync(string threadId, string name) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null) { throw new System.NotSupportedException(); }
            public Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId) { throw new System.NotSupportedException(); }
            public Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo) { throw new System.NotSupportedException(); }
            public bool HasInterruptibleTurn(string threadId) { return HasInterruptibleTurnResult; }
            public Task InterruptAsync(string threadId, string turnId = null) { throw new System.NotSupportedException(); }
            public Task ResolveApprovalRequestAsync(string requestId, AppServerApprovalDecision decision) { throw new System.NotSupportedException(); }
            public Task ResolveToolUserInputRequestAsync(string requestId, IDictionary<string, string[]> answers) { throw new System.NotSupportedException(); }
            public Task CancelToolUserInputRequestAsync(string requestId) { throw new System.NotSupportedException(); }
        }
    }
}
