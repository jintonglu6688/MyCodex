using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed partial class RemoteCommandDispatcherTests
    {
        [Test]
        public async Task DispatchAsync_PendingInteractions_ReturnsExistingEventPayloadsWithoutMutation()
        {
            var pending = new RemoteRelayEvent
            {
                EventType = RemoteEventTypes.ApprovalRequested, RequestId = "approval-a", OperationId = "turn-a",
                Payload = new Dictionary<string, object> { { "sessionId", "session-a" }, { "requestId", "approval-a" } }
            };
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), null, new RemoteWriteCoordinator(),
                null, null, pendingInteractions: () => new[] { pending });

            var result = await dispatcher.DispatchAsync(new RemoteRelayCommand
            {
                CommandType = RemoteCommandTypes.InteractionPendingList, RequestId = "read-a", DeviceId = "device-a"
            }, CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.InteractionPendingListResult));
            Assert.That(result.RequestId, Is.EqualTo("read-a"));
            var events = (Dictionary<string, object>[])result.Payload["events"];
            Assert.That(events, Has.Length.EqualTo(1));
            Assert.That(events[0]["eventType"], Is.EqualTo(RemoteEventTypes.ApprovalRequested));
            Assert.That(events[0]["requestId"], Is.EqualTo("approval-a"));
            Assert.That(events[0]["operationId"], Is.EqualTo("turn-a"));
            Assert.That(events[0]["payload"], Is.SameAs(pending.Payload));
        }

        [Test]
        public async Task DispatchAsync_HostAccountGet_ReturnsReadOnlyQuotaSnapshot()
        {
            var account = new FakeAccountClient
            {
                Authentication = new AppServerAuthenticationState
                {
                    IsAuthenticated = true,
                    AuthMode = "chatgpt",
                    Email = "user@example.com",
                    PlanType = "plus"
                }
            };
            account.RateLimits.RateLimitResetCreditsAvailableCount = 2;
            account.RateLimits.ResetCredits = new List<AppServerRateLimitResetCredit>
            {
                new AppServerRateLimitResetCredit
                {
                    Status = "available",
                    ExpiresAtLocalTime = new DateTime(2026, 8, 10, 18, 30, 0)
                }
            };
            account.RateLimits.Buckets.Add(new AppServerRateLimitBucket
            {
                LimitId = "codex",
                LimitName = "Codex",
                Primary = new AppServerRateLimitWindowInfo
                {
                    UsedPercent = 35,
                    WindowDurationMinutes = 300
                }
            });
            account.TokenUsage.LifetimeTokens = 3993200000L;
            account.TokenUsage.PeakDailyTokens = 2059000000L;
            account.TokenUsage.CurrentStreakDays = 66;
            account.TokenUsage.LongestStreakDays = 66;
            account.TokenUsage.DailyBuckets.Add(new AppServerAccountTokenUsageDailyBucket
            {
                StartDate = "2026-08-03",
                Tokens = 1204000000L
            });
            var dispatcher = new RemoteCommandDispatcher(
                new FakeCodingRemoteService(),
                null,
                new RemoteWriteCoordinator(),
                null,
                null,
                null,
                account);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-account",
                    CommandType = RemoteCommandTypes.HostAccountGet,
                    DeviceId = "device-a"
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.HostAccountResult));
            Assert.That(result.Payload["available"], Is.True);
            Assert.That(result.Payload["email"], Is.EqualTo("user@example.com"));
            Assert.That(result.Payload["planType"], Is.EqualTo("plus"));
            Assert.That(result.Payload["resetCreditsAvailableCount"], Is.EqualTo(2));
            Assert.That(account.LastRefreshToken, Is.False);
            var buckets = (IReadOnlyList<Dictionary<string, object>>)result.Payload["buckets"];
            Assert.That(buckets, Has.Count.EqualTo(1));
            Assert.That(buckets[0]["name"], Is.EqualTo("Codex"));
            var resetCredits = (IReadOnlyList<Dictionary<string, object>>)result.Payload["resetCredits"];
            Assert.That(resetCredits, Has.Count.EqualTo(1));
            Assert.That(resetCredits[0]["status"], Is.EqualTo("available"));
            var tokenUsage = (Dictionary<string, object>)result.Payload["tokenUsage"];
            Assert.That(tokenUsage["lifetimeTokens"], Is.EqualTo(3993200000L));
            var dailyBuckets = (IReadOnlyList<Dictionary<string, object>>)tokenUsage["dailyBuckets"];
            Assert.That(dailyBuckets[0]["tokens"], Is.EqualTo(1204000000L));
        }

        [Test]
        public async Task DispatchAsync_WorkspaceList_ReturnsCorrelatedResultEvent()
        {
            var service = new FakeCodingRemoteService();
            service.Workspaces.Add(new CodingRemoteWorkspaceSummary
            {
                WorkspaceId = "workspace-a",
                DisplayName = "Repo A",
                Path = @"E:\Repos\A",
                Kind = CodingRemoteKinds.Coding
            });
            var mutationSink = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutationSink);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-1",
                    CommandType = RemoteCommandTypes.CodingWorkspacesList,
                    DeviceId = "device-a"
                },
                CancellationToken.None);

            Assert.That(result.PayloadType, Is.EqualTo(RemoteRelayPayloadTypes.Event));
            Assert.That(result.RequestId, Is.EqualTo("request-1"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingWorkspacesListResult));
            var workspaces = (IReadOnlyList<Dictionary<string, object>>)result.Payload["workspaces"];
            Assert.That(workspaces, Has.Count.EqualTo(1));
            Assert.That(workspaces[0]["id"], Is.EqualTo("workspace-a"));
        }

        [Test]
        public async Task DispatchAsync_OfficeWorkspaceList_UsesOfficeServiceAndReturnsOfficeEvent()
        {
            var codingService = new FakeCodingRemoteService();
            var officeService = new FakeCodingRemoteService();
            officeService.Workspaces.Add(new CodingRemoteWorkspaceSummary
            {
                WorkspaceId = "office:reports",
                DisplayName = "Reports",
                Path = @"E:\Documents\MyCodex-office\Reports",
                Kind = CodingRemoteKinds.Office
            });
            var dispatcher = new RemoteCommandDispatcher(
                codingService,
                officeService,
                new RemoteWriteCoordinator(),
                null,
                null);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-office-workspaces",
                    CommandType = RemoteCommandTypes.OfficeWorkspacesList,
                    DeviceId = "device-a"
                },
                CancellationToken.None);

            Assert.That(codingService.WorkspaceListCalls, Is.EqualTo(0));
            Assert.That(officeService.WorkspaceListCalls, Is.EqualTo(1));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.OfficeWorkspacesListResult));
            var workspaces = (IReadOnlyList<Dictionary<string, object>>)result.Payload["workspaces"];
            Assert.That(workspaces, Has.Count.EqualTo(1));
            Assert.That(workspaces[0]["id"], Is.EqualTo("office:reports"));
            Assert.That(workspaces[0]["kind"], Is.EqualTo(CodingRemoteKinds.Office));
        }

        [Test]
        public async Task DispatchAsync_SessionList_PassesWorkspaceIdToService()
        {
            var service = new FakeCodingRemoteService();
            service.Sessions.Add(new CodingRemoteSessionSummary
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                Title = "Session A",
                State = CodingRemoteSessionStates.Idle
            });
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-2",
                    CommandType = RemoteCommandTypes.CodingSessionsList,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "workspaceId", "workspace-a" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastWorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionsListResult));
            var sessions = (IReadOnlyList<Dictionary<string, object>>)result.Payload["sessions"];
            Assert.That(sessions, Has.Count.EqualTo(1));
            Assert.That(sessions[0]["id"], Is.EqualTo("session-a"));
        }

        [Test]
        public async Task DispatchAsync_SessionCreate_PassesWorkspaceIdReturnsSessionAndPublishesMutation()
        {
            var service = new FakeCodingRemoteService();
            service.CreatedSession = new CodingRemoteSessionSummary
            {
                SessionId = "session-new",
                WorkspaceId = "workspace-a",
                Title = "Repo A",
                CreatedAt = new System.DateTime(2026, 6, 8, 1, 2, 3, System.DateTimeKind.Utc),
                UpdatedAt = new System.DateTime(2026, 6, 8, 1, 2, 3, System.DateTimeKind.Utc),
                BranchKind = CodingRemoteSessionBranchKinds.Mainline,
                State = CodingRemoteSessionStates.Idle
            };
            var mutationSink = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutationSink);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-create",
                    CommandType = RemoteCommandTypes.CodingSessionCreate,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "workspaceId", "workspace-a" },
                        { "title", "Custom title" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionCreateRequest.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(service.LastSessionCreateRequest.Title, Is.EqualTo("Custom title"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionCreated));
            Assert.That(result.Payload["workspaceId"], Is.EqualTo("workspace-a"));
            var session = (Dictionary<string, object>)result.Payload["session"];
            Assert.That(session["id"], Is.EqualTo("session-new"));
            Assert.That(session["title"], Is.EqualTo("Repo A"));
            Assert.That(mutationSink.Mutations, Has.Count.EqualTo(1));
            Assert.That(mutationSink.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.SessionCreated));
            Assert.That(mutationSink.Mutations[0].WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(mutationSink.Mutations[0].SessionId, Is.EqualTo("session-new"));
        }

        [Test]
        public async Task DispatchAsync_OfficeSessionCreate_UsesOfficeServiceAndReturnsOfficeEvent()
        {
            var codingService = new FakeCodingRemoteService();
            var officeService = new FakeCodingRemoteService
            {
                CreatedSession = new CodingRemoteSessionSummary
                {
                    SessionId = "office-session",
                    WorkspaceId = "office:reports",
                    Title = "Report draft",
                    BranchKind = CodingRemoteSessionBranchKinds.Mainline,
                    State = CodingRemoteSessionStates.Idle
                }
            };
            var dispatcher = new RemoteCommandDispatcher(
                codingService,
                officeService,
                new RemoteWriteCoordinator(),
                null,
                null);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-office-session-create",
                    CommandType = RemoteCommandTypes.OfficeSessionCreate,
                    Payload = new Dictionary<string, object>
                    {
                        { "workspaceId", "office:reports" },
                        { "title", "Report draft" }
                    }
                },
                CancellationToken.None);

            Assert.That(codingService.LastSessionCreateRequest, Is.Null);
            Assert.That(officeService.LastSessionCreateRequest.WorkspaceId, Is.EqualTo("office:reports"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.OfficeSessionCreated));
            var session = (Dictionary<string, object>)result.Payload["session"];
            Assert.That(session["id"], Is.EqualTo("office-session"));
        }

        [Test]
        public async Task DispatchAsync_MessageList_BuildsRequestWithLimitAndCursor()
        {
            var service = new FakeCodingRemoteService();
            service.MessagePage = new CodingRemoteMessagePage
            {
                SessionId = "session-a",
                NextCursor = "older-1",
                IsGenerating = true
            };
            service.MessagePage.Messages.Add(new CodingRemoteMessageItem
            {
                MessageId = "message-a",
                SessionId = "session-a",
                Role = CodingRemoteMessageRoles.User,
                Text = "hello",
                Status = CodingRemoteMessageStatuses.Complete
            });
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-3",
                    CommandType = RemoteCommandTypes.CodingMessagesList,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "beforeCursor", "newer" },
                        { "limit", 25 }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastMessageRequest.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastMessageRequest.BeforeCursor, Is.EqualTo("newer"));
            Assert.That(service.LastMessageRequest.Limit, Is.EqualTo(25));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingMessagesListResult));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["nextCursor"], Is.EqualTo("older-1"));
            Assert.That(result.Payload["generating"], Is.True);
            var messages = (IReadOnlyList<Dictionary<string, object>>)result.Payload["messages"];
            Assert.That(messages, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task DispatchAsync_OfficeMessageList_UsesOfficeServiceAndReturnsOfficeEvent()
        {
            var codingService = new FakeCodingRemoteService();
            var officeService = new FakeCodingRemoteService();
            officeService.MessagePage = new CodingRemoteMessagePage { SessionId = "office-session" };
            officeService.MessagePage.Messages.Add(new CodingRemoteMessageItem
            {
                MessageId = "message-a",
                SessionId = "office-session",
                Role = CodingRemoteMessageRoles.Assistant,
                Text = "Done",
                Status = CodingRemoteMessageStatuses.Complete
            });
            var dispatcher = new RemoteCommandDispatcher(
                codingService,
                officeService,
                new RemoteWriteCoordinator(),
                null,
                null);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-office-messages",
                    CommandType = RemoteCommandTypes.OfficeMessagesList,
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "office-session" },
                        { "limit", 0 }
                    }
                },
                CancellationToken.None);

            Assert.That(codingService.LastMessageRequest, Is.Null);
            Assert.That(officeService.LastMessageRequest.SessionId, Is.EqualTo("office-session"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.OfficeMessagesListResult));
            var messages = (IReadOnlyList<Dictionary<string, object>>)result.Payload["messages"];
            Assert.That(messages[0]["content"], Is.EqualTo("Done"));
        }

        [Test]
        public async Task DispatchAsync_MessageList_IncludesTurnActionPayload()
        {
            var service = new FakeCodingRemoteService();
            service.MessagePage.Messages.Add(new CodingRemoteMessageItem
            {
                MessageId = "message-1",
                SessionId = "session-1",
                Role = CodingRemoteMessageRoles.Assistant,
                Text = "assistant reply",
                Status = CodingRemoteMessageStatuses.Complete,
                ThreadId = "thread-1",
                TurnId = "turn-2",
                TurnIndex = 2,
                TurnCount = 3,
                Actions = new CodingRemoteMessageActions
                {
                    Copy = true,
                    ForkFromTurn = true,
                },
                OperationPreview = new CodingRemoteMessageOperationPreview
                {
                    TrimTurnsAfterTurn = 1,
                }
            });
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    CommandType = RemoteCommandTypes.CodingMessagesList,
                    RequestId = "request-1",
                    Payload = new Dictionary<string, object> { { "sessionId", "session-1" }, { "limit", 0 } }
                },
                CancellationToken.None);

            var messages = (IReadOnlyList<Dictionary<string, object>>)result.Payload["messages"];
            var message = messages[0];
            Assert.That(message["threadId"], Is.EqualTo("thread-1"));
            Assert.That(message["turnId"], Is.EqualTo("turn-2"));
            Assert.That(message["turnIndex"], Is.EqualTo(2));
            Assert.That(message["turnCount"], Is.EqualTo(3));
            var actions = (Dictionary<string, object>)message["actions"];
            Assert.That(actions["copy"], Is.EqualTo(true));
            Assert.That(actions["forkFromTurn"], Is.EqualTo(true));
            Assert.That(actions.ContainsKey("deriveFromTurn"), Is.False);
            Assert.That(actions.ContainsKey("rollbackBeforeTurn"), Is.False);
            var preview = (Dictionary<string, object>)message["operationPreview"];
            Assert.That(preview.ContainsKey("rollbackAffectedTurns"), Is.False);
            Assert.That(preview["trimTurnsAfterTurn"], Is.EqualTo(1));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_PassesTextToServiceAndReturnsAcceptedEvent()
        {
            var service = new FakeCodingRemoteService();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-send",
                    OperationId = "operation-send",
                    CommandType = RemoteCommandTypes.CodingMessageSend,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "text", "hello from android" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSendRequest.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSendRequest.Text, Is.EqualTo("hello from android"));
            Assert.That(service.LastSendRequest.DeviceId, Is.EqualTo("device-a"));
            Assert.That(service.LastSendRequest.OperationId, Is.EqualTo("operation-send"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingMessageAccepted));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["accepted"], Is.True);
            Assert.That(result.Payload["dispatchMode"], Is.EqualTo("start"));
        }

        [Test]
        public async Task DispatchAsync_OfficeMessageSend_UsesOfficeServiceAndReturnsOfficeAcceptedEvent()
        {
            var codingService = new FakeCodingRemoteService();
            var officeService = new FakeCodingRemoteService();
            var dispatcher = new RemoteCommandDispatcher(
                codingService,
                officeService,
                new RemoteWriteCoordinator(),
                null,
                null);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-office-send",
                    OperationId = "operation-office-send",
                    CommandType = RemoteCommandTypes.OfficeMessageSend,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "office-session" },
                        { "text", "draft report" }
                    }
                },
                CancellationToken.None);

            Assert.That(codingService.LastSendRequest, Is.Null);
            Assert.That(officeService.LastSendRequest.SessionId, Is.EqualTo("office-session"));
            Assert.That(officeService.LastSendRequest.Text, Is.EqualTo("draft report"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.OfficeMessageAccepted));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_DuplicateInFlightOperationExecutesOnceAndReturnsSameEvent()
        {
            var entered = NewSignal();
            var release = NewSignal();
            var service = new FakeCodingRemoteService
            {
                SendMessageHandler = async request =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return Accepted(request);
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var first = dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-a"), CancellationToken.None);
            await entered.Task;
            var duplicate = dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-a"), CancellationToken.None);
            release.TrySetResult(true);
            var results = await Task.WhenAll(first, duplicate);

            Assert.That(service.SendCount, Is.EqualTo(1));
            Assert.That(results[1], Is.SameAs(results[0]));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_CompletedOperationReplaysSameEventWithoutExecutingAgain()
        {
            var service = new FakeCodingRemoteService();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var first = await dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-a"), CancellationToken.None);
            var replay = await dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-a"), CancellationToken.None);

            Assert.That(service.SendCount, Is.EqualTo(1));
            Assert.That(replay, Is.SameAs(first));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_DifferentOperationInSameSessionReturnsSessionBusy()
        {
            var entered = NewSignal();
            var release = NewSignal();
            var service = new FakeCodingRemoteService
            {
                SendMessageHandler = async request =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return Accepted(request);
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var first = dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-a"), CancellationToken.None);
            await entered.Task;
            var conflict = await dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-b"), CancellationToken.None);
            release.TrySetResult(true);
            await first;

            Assert.That(conflict.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(conflict.Payload["code"], Is.EqualTo(RemoteErrorCodes.SessionBusy));
            Assert.That(service.SendCount, Is.EqualTo(1));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_DifferentSessionsEnterServiceConcurrently()
        {
            var firstEntered = NewSignal();
            var secondEntered = NewSignal();
            var release = NewSignal();
            var service = new FakeCodingRemoteService
            {
                SendMessageHandler = async request =>
                {
                    (request.SessionId == "session-a" ? firstEntered : secondEntered).TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return Accepted(request);
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var first = dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-a"), CancellationToken.None);
            await firstEntered.Task;
            var second = dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-b", "operation-b"), CancellationToken.None);
            await secondEntered.Task;
            release.TrySetResult(true);
            await Task.WhenAll(first, second);

            Assert.That(service.SendCount, Is.EqualTo(2));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_CompletingOneSessionDoesNotExposeAnotherActiveSession()
        {
            var firstEntered = NewSignal();
            var secondEntered = NewSignal();
            var releaseFirst = NewSignal();
            var releaseSecond = NewSignal();
            var service = new FakeCodingRemoteService
            {
                SendMessageHandler = async request =>
                {
                    if (request.SessionId == "session-a")
                    {
                        firstEntered.TrySetResult(true);
                        await releaseFirst.Task.ConfigureAwait(false);
                    }
                    else if (request.SessionId == "session-b")
                    {
                        secondEntered.TrySetResult(true);
                        await releaseSecond.Task.ConfigureAwait(false);
                    }

                    return Accepted(request);
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());
            var first = dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-a", "operation-a"), CancellationToken.None);
            await firstEntered.Task;
            var second = dispatcher.DispatchAsync(CreateMessageSend("device-a", "session-b", "operation-b"), CancellationToken.None);
            await secondEntered.Task;
            releaseFirst.TrySetResult(true);
            await first;

            var otherDevice = await dispatcher.DispatchAsync(CreateMessageSend("device-b", "session-c", "operation-c"), CancellationToken.None);
            releaseSecond.TrySetResult(true);
            await second;

            Assert.That(otherDevice.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(otherDevice.Payload["code"], Is.EqualTo(RemoteErrorCodes.WriteLockDenied));
            Assert.That(service.SendCount, Is.EqualTo(2));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_FailedOperationReleasesDuplicatesAndAllowsRetry()
        {
            var entered = NewSignal();
            var fail = NewSignal();
            var service = new FakeCodingRemoteService
            {
                SendMessageHandler = async request =>
                {
                    entered.TrySetResult(true);
                    await fail.Task.ConfigureAwait(false);
                    throw new System.InvalidOperationException("send failed");
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());
            var command = CreateMessageSend("device-a", "session-a", "operation-a");

            var first = dispatcher.DispatchAsync(command, CancellationToken.None);
            await entered.Task;
            var duplicate = dispatcher.DispatchAsync(command, CancellationToken.None);
            fail.TrySetResult(true);
            var failures = await Task.WhenAll(first, duplicate);
            service.SendMessageHandler = request => Task.FromResult(Accepted(request));
            var retry = await dispatcher.DispatchAsync(command, CancellationToken.None);

            Assert.That(failures[0].Payload["code"], Is.EqualTo(RemoteErrorCodes.OperationFailed));
            Assert.That(failures[1].Payload["code"], Is.EqualTo(RemoteErrorCodes.OperationFailed));
            Assert.That(retry.EventType, Is.EqualTo(RemoteEventTypes.CodingMessageAccepted));
            Assert.That(service.SendCount, Is.EqualTo(2));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_CanceledOperationCancelsDuplicatesAndAllowsRetry()
        {
            var entered = NewSignal();
            var cancel = NewSignal();
            var service = new FakeCodingRemoteService
            {
                SendMessageHandler = async request =>
                {
                    entered.TrySetResult(true);
                    await cancel.Task.ConfigureAwait(false);
                    throw new System.OperationCanceledException();
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());
            var command = CreateMessageSend("device-a", "session-a", "operation-a");

            var first = dispatcher.DispatchAsync(command, CancellationToken.None);
            await entered.Task;
            var duplicate = dispatcher.DispatchAsync(command, CancellationToken.None);
            cancel.TrySetResult(true);

            Assert.That(async () => await first, Throws.InstanceOf<System.OperationCanceledException>());
            Assert.That(async () => await duplicate, Throws.InstanceOf<System.OperationCanceledException>());
            service.SendMessageHandler = request => Task.FromResult(Accepted(request));
            var retry = await dispatcher.DispatchAsync(command, CancellationToken.None);
            Assert.That(retry.EventType, Is.EqualTo(RemoteEventTypes.CodingMessageAccepted));
            Assert.That(service.SendCount, Is.EqualTo(2));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_PassesPluginSkillAndTextInputItems()
        {
            var service = new FakeCodingRemoteService();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-send-items",
                    OperationId = "operation-send-items",
                    CommandType = RemoteCommandTypes.CodingMessageSend,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "text", "  run this with context  " },
                        {
                            "inputItems",
                            new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "type", "mention" },
                                    { "name", "mycodex-office" },
                                    { "displayName", "MyCodex Office" },
                                    { "path", @"E:\Plugins\mycodex-office" }
                                },
                                new Dictionary<string, object>
                                {
                                    { "type", "skill" },
                                    { "name", "writing-plans" },
                                    { "displayName", "Writing Plans" },
                                    { "path", @"E:\Skills\writing-plans" }
                                }
                            }
                        }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingMessageAccepted));
            Assert.That(service.LastSendRequest.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSendRequest.Text, Is.EqualTo("run this with context"));
            Assert.That(service.LastSendRequest.InputItems, Has.Count.EqualTo(2));
            Assert.That(service.LastSendRequest.InputItems[0].Type, Is.EqualTo("mention"));
            Assert.That(service.LastSendRequest.InputItems[0].Name, Is.EqualTo("mycodex-office"));
            Assert.That(service.LastSendRequest.InputItems[0].DisplayName, Is.EqualTo("MyCodex Office"));
            Assert.That(service.LastSendRequest.InputItems[0].Path, Is.EqualTo(@"E:\Plugins\mycodex-office"));
            Assert.That(service.LastSendRequest.InputItems[1].Type, Is.EqualTo("skill"));
            Assert.That(service.LastSendRequest.InputItems[1].Name, Is.EqualTo("writing-plans"));
            Assert.That(service.LastSendRequest.InputItems[1].DisplayName, Is.EqualTo("Writing Plans"));
            Assert.That(service.LastSendRequest.InputItems[1].Path, Is.EqualTo(@"E:\Skills\writing-plans"));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_AllowsAttachmentOnlySend()
        {
            var service = new FakeCodingRemoteService();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-send-attachment-only",
                    OperationId = "operation-send-attachment-only",
                    CommandType = RemoteCommandTypes.CodingMessageSend,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        {
                            "inputItems",
                            new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "type", "skill" },
                                    { "name", "writing-plans" },
                                    { "path", @"E:\Skills\writing-plans" }
                                }
                            }
                        }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingMessageAccepted));
            Assert.That(service.LastSendRequest.Text, Is.Null);
            Assert.That(service.LastSendRequest.InputItems.Single().Type, Is.EqualTo("skill"));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_RejectsStructuredInputWithoutPath()
        {
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-send-invalid-item",
                    OperationId = "operation-send-invalid-item",
                    CommandType = RemoteCommandTypes.CodingMessageSend,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        {
                            "inputItems",
                            new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "type", "mention" },
                                    { "name", "broken-plugin" }
                                }
                            }
                        }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.InvalidCommand));
        }

        [Test]
        public async Task DispatchAsync_MessageSend_DeniesWhenAnotherDeviceHoldsWriteLock()
        {
            var coordinator = new RemoteWriteCoordinator();
            coordinator.Acquire("device-a", "operation-a", System.DateTime.UtcNow);
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), coordinator);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-send",
                    OperationId = "operation-send",
                    CommandType = RemoteCommandTypes.CodingMessageSend,
                    DeviceId = "device-b",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "text", "blocked" }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.WriteLockDenied));
        }

        [Test]
        public async Task DispatchAsync_PluginAttachmentCatalog_ReturnsPlugins()
        {
            var service = new FakeCodingRemoteService();
            service.PluginAttachmentCatalog.Items.Add(new CodingRemoteAttachmentCatalogItem
            {
                Kind = "plugin",
                Type = "mention",
                Name = "mycodex-office",
                DisplayName = "MyCodex Office",
                Path = @"E:\Plugins\mycodex-office",
                Enabled = true,
                Installed = true,
                SourceText = "personal",
                Summary = "Office tools"
            });
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-plugin-catalog",
                    CommandType = RemoteCommandTypes.CodingAttachmentsPluginsList,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingAttachmentsPluginsListResult));
            Assert.That(service.LastPluginAttachmentSessionId, Is.EqualTo("session-a"));
            var plugins = (IReadOnlyList<Dictionary<string, object>>)result.Payload["plugins"];
            Assert.That(plugins.Single()["type"], Is.EqualTo("mention"));
            Assert.That(plugins.Single()["displayName"], Is.EqualTo("MyCodex Office"));
            Assert.That(plugins.Single()["path"], Is.EqualTo(@"E:\Plugins\mycodex-office"));
        }

        [Test]
        public async Task DispatchAsync_SkillAttachmentCatalog_ReturnsSkillsForSession()
        {
            var service = new FakeCodingRemoteService();
            service.SkillAttachmentCatalog.WorkspaceId = "workspace-a";
            service.SkillAttachmentCatalog.Items.Add(new CodingRemoteAttachmentCatalogItem
            {
                Kind = "skill",
                Type = "skill",
                Name = "writing-plans",
                DisplayName = "Writing Plans",
                Path = @"E:\Skills\writing-plans",
                Enabled = true,
                SourceText = "plugin",
                Scope = "plugin",
                Summary = "Plan work"
            });
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-skill-catalog",
                    CommandType = RemoteCommandTypes.CodingAttachmentsSkillsList,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "forceReload", true }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingAttachmentsSkillsListResult));
            Assert.That(service.LastSkillAttachmentSessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSkillAttachmentForceReload, Is.True);
            Assert.That(result.Payload["workspaceId"], Is.EqualTo("workspace-a"));
            var skills = (IReadOnlyList<Dictionary<string, object>>)result.Payload["skills"];
            Assert.That(skills.Single()["type"], Is.EqualTo("skill"));
            Assert.That(skills.Single()["displayName"], Is.EqualTo("Writing Plans"));
            Assert.That(skills.Single()["path"], Is.EqualTo(@"E:\Skills\writing-plans"));
            Assert.That(skills.Single()["scope"], Is.EqualTo("plugin"));
        }

        [Test]
        public async Task DispatchAsync_ForkFromTurn_MapsRequestResultAndPublishesMutation()
        {
            var service = new FakeCodingRemoteService();
            service.ThreadOperationResult = new CodingRemoteSessionThreadOperationResult
            {
                SessionId = "session-1",
                WorkspaceId = "workspace-1",
                CreatedSession = new CodingRemoteSessionSummary
                {
                    SessionId = "session-2",
                    WorkspaceId = "workspace-1",
                    Title = "New fork",
                    BranchKind = CodingRemoteSessionBranchKinds.Fork,
                    State = CodingRemoteSessionStates.Idle
                },
                Action = CodingRemoteSessionThreadActions.Fork,
                Mode = CodingRemoteSessionThreadOperationModes.FromTurn,
                TurnId = "turn-2",
                StatusText = "Fork created."
            };
            var mutations = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutations);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    CommandType = RemoteCommandTypes.CodingSessionFork,
                    RequestId = "request-1",
                    OperationId = "operation-fork",
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-1" },
                        { "mode", "fromTurn" },
                        { "turnId", "turn-2" },
                        { "title", "New fork" },
                        { "switchToCreatedSession", true }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionThreadOperationResult));
            Assert.That(service.LastThreadOperationRequest.Action, Is.EqualTo(CodingRemoteSessionThreadActions.Fork));
            Assert.That(service.LastThreadOperationRequest.Mode, Is.EqualTo(CodingRemoteSessionThreadOperationModes.FromTurn));
            Assert.That(service.LastThreadOperationRequest.TurnId, Is.EqualTo("turn-2"));
            Assert.That(service.LastThreadOperationRequest.SwitchToCreatedSession, Is.True);
            Assert.That(mutations.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.SessionForkCreated));
        }

        [Test]
        public async Task DispatchAsync_Compact_MapsRequestResultAndPublishesMutation()
        {
            var service = new FakeCodingRemoteService();
            service.ThreadOperationResult = new CodingRemoteSessionThreadOperationResult
            {
                SessionId = "session-1",
                WorkspaceId = "workspace-1",
                Action = CodingRemoteSessionThreadActions.Compact,
                Mode = CodingRemoteSessionThreadOperationModes.Full,
                Accepted = true,
                StatusText = "Context compaction started."
            };
            var mutations = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutations);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    CommandType = RemoteCommandTypes.CodingSessionCompact,
                    RequestId = "request-1",
                    OperationId = "operation-compact",
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object> { { "sessionId", "session-1" } }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionThreadOperationResult));
            Assert.That(service.LastThreadOperationRequest.Action, Is.EqualTo(CodingRemoteSessionThreadActions.Compact));
            Assert.That(mutations.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.SessionCompactRequested));
        }

        [Test]
        public async Task DispatchAsync_Compact_DuplicateInFlightOperationExecutesAndPublishesOnce()
        {
            var entered = NewSignal();
            var release = NewSignal();
            var service = new FakeCodingRemoteService
            {
                ThreadOperationHandler = async request =>
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    return new CodingRemoteSessionThreadOperationResult
                    {
                        SessionId = request.SessionId,
                        Action = request.Action,
                        Mode = request.Mode,
                        Accepted = true
                    };
                }
            };
            var mutations = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutations);
            var command = new RemoteRelayCommand
            {
                CommandType = RemoteCommandTypes.CodingSessionCompact,
                RequestId = "request-compact",
                OperationId = "operation-compact",
                DeviceId = "device-a",
                Payload = new Dictionary<string, object> { { "sessionId", "session-a" } }
            };

            var first = dispatcher.DispatchAsync(command, CancellationToken.None);
            await entered.Task;
            var duplicate = dispatcher.DispatchAsync(command, CancellationToken.None);
            release.TrySetResult(true);
            var results = await Task.WhenAll(first, duplicate);

            Assert.That(service.ThreadOperationCount, Is.EqualTo(1));
            Assert.That(mutations.Mutations, Has.Count.EqualTo(1));
            Assert.That(results[1], Is.SameAs(results[0]));
        }

        [Test]
        public async Task DispatchAsync_SessionThreadOperation_DeniesWhenAnotherDeviceHoldsWriteLock()
        {
            var coordinator = new RemoteWriteCoordinator();
            coordinator.Acquire("device-a", "operation-a", System.DateTime.UtcNow);
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), coordinator);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-fork",
                    CommandType = RemoteCommandTypes.CodingSessionFork,
                    OperationId = "operation-fork",
                    DeviceId = "device-b",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "turnId", "turn-2" }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.WriteLockDenied));
        }

        [Test]
        public async Task DispatchAsync_WorkspaceArchive_PassesWorkspaceIdAndReturnsOperationResult()
        {
            var service = new FakeCodingRemoteService();
            service.WorkspaceOperationResult = new CodingRemoteWorkspaceOperationResult
            {
                WorkspaceId = "workspace-a",
                Action = CodingRemoteWorkspaceActions.Archive,
                ProcessedCount = 2,
                Removed = true,
                StatusText = "Archived 2 sessions."
            };
            var mutationSink = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutationSink);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-archive",
                    CommandType = RemoteCommandTypes.CodingWorkspaceArchive,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "workspaceId", "workspace-a" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastWorkspaceOperationRequest.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(service.LastWorkspaceOperationRequest.Action, Is.EqualTo(CodingRemoteWorkspaceActions.Archive));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingWorkspaceOperationResult));
            Assert.That(result.Payload["workspaceId"], Is.EqualTo("workspace-a"));
            Assert.That(result.Payload["action"], Is.EqualTo(CodingRemoteWorkspaceActions.Archive));
            Assert.That(result.Payload["processedCount"], Is.EqualTo(2));
            Assert.That(result.Payload["removed"], Is.True);
        }

        [Test]
        public async Task DispatchAsync_WorkspaceDelete_PassesDeleteLocalHistoryFlag()
        {
            var service = new FakeCodingRemoteService();
            service.WorkspaceOperationResult = new CodingRemoteWorkspaceOperationResult
            {
                WorkspaceId = "workspace-a",
                Action = CodingRemoteWorkspaceActions.Delete,
                DeleteLocalHistory = true,
                ProcessedCount = 3,
                Removed = true,
                StatusText = "Deleted 3 session files."
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-delete",
                    CommandType = RemoteCommandTypes.CodingWorkspaceDelete,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "workspaceId", "workspace-a" },
                        { "deleteLocalHistory", true }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastWorkspaceOperationRequest.WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(service.LastWorkspaceOperationRequest.Action, Is.EqualTo(CodingRemoteWorkspaceActions.Delete));
            Assert.That(service.LastWorkspaceOperationRequest.DeleteLocalHistory, Is.True);
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingWorkspaceOperationResult));
            Assert.That(result.Payload["deleteLocalHistory"], Is.True);
            Assert.That(result.Payload["processedCount"], Is.EqualTo(3));
        }

        [Test]
        public async Task DispatchAsync_WorkspaceOperation_PublishesHostMutationAfterSuccess()
        {
            var service = new FakeCodingRemoteService();
            service.WorkspaceOperationResult = new CodingRemoteWorkspaceOperationResult
            {
                WorkspaceId = "workspace-a",
                Action = CodingRemoteWorkspaceActions.Delete,
                DeleteLocalHistory = false,
                ProcessedCount = 2,
                Removed = true,
                RuntimeProfileId = "windows",
                AffectedSessionIds = new List<string> { "session-a", "session-b" },
                StatusText = "Removed workspace."
            };
            var mutationSink = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutationSink);

            await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-delete",
                    CommandType = RemoteCommandTypes.CodingWorkspaceDelete,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "workspaceId", "workspace-a" },
                        { "deleteLocalHistory", false }
                    }
                },
                CancellationToken.None);

            Assert.That(mutationSink.Mutations, Has.Count.EqualTo(1));
            Assert.That(mutationSink.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.WorkspaceDeleted));
            Assert.That(mutationSink.Mutations[0].WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(mutationSink.Mutations[0].RuntimeProfileId, Is.EqualTo("windows"));
            Assert.That(mutationSink.Mutations[0].AffectedSessionIds.ToArray(), Is.EqualTo(new[] { "session-a", "session-b" }));
            Assert.That(mutationSink.Mutations[0].StatusText, Is.EqualTo("Removed workspace."));
        }

        [Test]
        public async Task DispatchAsync_WorkspaceOperation_RequiresWorkspaceId()
        {
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-delete",
                    CommandType = RemoteCommandTypes.CodingWorkspaceDelete,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>()
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.InvalidCommand));
        }

        [Test]
        public async Task DispatchAsync_WorkspaceOperation_MapsTypedOperationErrorCode()
        {
            var service = new FakeCodingRemoteService
            {
                WorkspaceOperationException = new CodingRemoteOperationException(RemoteErrorCodes.WorkspaceNotFound, "Workspace was not found.")
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-delete",
                    CommandType = RemoteCommandTypes.CodingWorkspaceDelete,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "workspaceId", "workspace-a" }
                    }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.WorkspaceNotFound));
        }

        [Test]
        public async Task DispatchAsync_SessionArchive_PassesSessionIdAndReturnsOperationResult()
        {
            var service = new FakeCodingRemoteService();
            service.SessionOperationResult = new CodingRemoteSessionOperationResult
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                Action = CodingRemoteSessionActions.Archive,
                ProcessedCount = 1,
                Removed = true,
                StatusText = "Archived session."
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-session-archive",
                    CommandType = RemoteCommandTypes.CodingSessionArchive,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionOperationRequest.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSessionOperationRequest.Action, Is.EqualTo(CodingRemoteSessionActions.Archive));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionOperationResult));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["action"], Is.EqualTo(CodingRemoteSessionActions.Archive));
            Assert.That(result.Payload["processedCount"], Is.EqualTo(1));
            Assert.That(result.Payload["removed"], Is.True);
        }

        [Test]
        public async Task DispatchAsync_SessionDelete_PassesDeleteLocalHistoryFlagAndPublishesMutation()
        {
            var service = new FakeCodingRemoteService();
            service.SessionOperationResult = new CodingRemoteSessionOperationResult
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                RuntimeProfileId = "windows",
                Action = CodingRemoteSessionActions.Delete,
                DeleteLocalHistory = true,
                ProcessedCount = 1,
                Removed = true,
                StatusText = "Deleted session."
            };
            var mutationSink = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutationSink);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-session-delete",
                    CommandType = RemoteCommandTypes.CodingSessionDelete,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "deleteLocalHistory", true }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionOperationRequest.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSessionOperationRequest.Action, Is.EqualTo(CodingRemoteSessionActions.Delete));
            Assert.That(service.LastSessionOperationRequest.DeleteLocalHistory, Is.True);
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionOperationResult));
            Assert.That(result.Payload["deleteLocalHistory"], Is.True);
            Assert.That(mutationSink.Mutations, Has.Count.EqualTo(1));
            Assert.That(mutationSink.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.SessionDeleted));
            Assert.That(mutationSink.Mutations[0].SessionId, Is.EqualTo("session-a"));
            Assert.That(mutationSink.Mutations[0].WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(mutationSink.Mutations[0].RuntimeProfileId, Is.EqualTo("windows"));
        }

        [Test]
        public async Task DispatchAsync_SessionRename_PassesTitleAndPublishesMutation()
        {
            var service = new FakeCodingRemoteService();
            service.SessionOperationResult = new CodingRemoteSessionOperationResult
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                RuntimeProfileId = "windows",
                Action = CodingRemoteSessionActions.Rename,
                Title = "Renamed session",
                ProcessedCount = 1,
                Updated = true,
                StatusText = "Renamed session."
            };
            var mutationSink = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutationSink);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-session-rename",
                    CommandType = RemoteCommandTypes.CodingSessionRename,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "title", "  Renamed session  " }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionOperationRequest.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSessionOperationRequest.Action, Is.EqualTo(CodingRemoteSessionActions.Rename));
            Assert.That(service.LastSessionOperationRequest.Title, Is.EqualTo("Renamed session"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionOperationResult));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["action"], Is.EqualTo(CodingRemoteSessionActions.Rename));
            Assert.That(result.Payload["title"], Is.EqualTo("Renamed session"));
            Assert.That(result.Payload["updated"], Is.True);
            Assert.That(mutationSink.Mutations, Has.Count.EqualTo(1));
            Assert.That(mutationSink.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.SessionRenamed));
            Assert.That(mutationSink.Mutations[0].SessionId, Is.EqualTo("session-a"));
            Assert.That(mutationSink.Mutations[0].WorkspaceId, Is.EqualTo("workspace-a"));
            Assert.That(mutationSink.Mutations[0].StatusText, Is.EqualTo("Renamed session."));
        }

        [Test]
        public async Task DispatchAsync_SessionOperation_RequiresSessionId()
        {
            var dispatcher = new RemoteCommandDispatcher(new FakeCodingRemoteService(), new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-session-delete",
                    CommandType = RemoteCommandTypes.CodingSessionDelete,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>()
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.InvalidCommand));
        }

        [Test]
        public async Task DispatchAsync_SessionSettingsGet_ReturnsCurrentAndEffectiveSettings()
        {
            var service = new FakeCodingRemoteService();
            service.SessionSettings = new CodingRemoteSessionSettings
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                ModelId = "gpt-custom",
                ReasoningEffort = "high",
                ApprovalMode = "auto",
                EffectiveModelId = "gpt-custom",
                EffectiveReasoningEffort = "high",
                EffectiveApprovalMode = "auto",
                DefaultModelId = "gpt-default",
                DefaultReasoningEffort = "medium",
                DefaultApprovalMode = "read-only",
                IsPlanModeEnabled = true
            };
            service.SessionSettings.AvailableModels.Add(new CodingRemoteModelOption
            {
                ModelId = "gpt-custom",
                DisplayName = "GPT Custom",
                DefaultReasoningEffort = "medium",
                ReasoningEfforts = { "low", "medium", "high" }
            });
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-settings",
                    CommandType = RemoteCommandTypes.CodingSessionSettingsGet,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionSettingsId, Is.EqualTo("session-a"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionSettingsResult));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["modelId"], Is.EqualTo("gpt-custom"));
            Assert.That(result.Payload["reasoningEffort"], Is.EqualTo("high"));
            Assert.That(result.Payload["approvalMode"], Is.EqualTo("auto"));
            Assert.That(result.Payload["effectiveModelId"], Is.EqualTo("gpt-custom"));
            Assert.That(result.Payload["defaultApprovalMode"], Is.EqualTo("read-only"));
            Assert.That(result.Payload["isPlanModeEnabled"], Is.True);
            var models = (IReadOnlyList<Dictionary<string, object>>)result.Payload["availableModels"];
            Assert.That(models, Has.Count.EqualTo(1));
            Assert.That(models[0]["modelId"], Is.EqualTo("gpt-custom"));
            Assert.That(((IReadOnlyList<string>)models[0]["reasoningEfforts"]).ToArray(), Is.EqualTo(new[] { "low", "medium", "high" }));
        }

        [Test]
        public async Task DispatchAsync_SessionSettingsUpdate_PassesNullableOverrides()
        {
            var service = new FakeCodingRemoteService();
            service.SessionSettingsUpdateResult = new CodingRemoteSessionSettingsUpdateResult
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                Updated = true,
                StatusText = "Session settings updated."
            };
            var mutationSink = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator(), mutationSink);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-settings-update",
                    CommandType = RemoteCommandTypes.CodingSessionSettingsUpdate,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "modelId", "gpt-custom" },
                        { "reasoningEffort", "medium" },
                        { "approvalMode", "full-access" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionSettingsUpdate.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSessionSettingsUpdate.ModelId, Is.EqualTo("gpt-custom"));
            Assert.That(service.LastSessionSettingsUpdate.ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(service.LastSessionSettingsUpdate.ApprovalMode, Is.EqualTo("full-access"));
            Assert.That(service.LastSessionSettingsUpdate.HasModelId, Is.True);
            Assert.That(service.LastSessionSettingsUpdate.HasReasoningEffort, Is.True);
            Assert.That(service.LastSessionSettingsUpdate.HasApprovalMode, Is.True);
            Assert.That(service.LastSessionSettingsUpdate.IsPlanModeEnabled, Is.Null);
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionSettingsUpdateResult));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["updated"], Is.True);
            Assert.That(result.Payload["statusText"], Is.EqualTo("Session settings updated."));
            Assert.That(mutationSink.Mutations, Has.Count.EqualTo(1));
            Assert.That(mutationSink.Mutations[0].MutationType, Is.EqualTo(RemoteHostMutationTypes.SessionSettingsUpdated));
            Assert.That(mutationSink.Mutations[0].SessionId, Is.EqualTo("session-a"));
            Assert.That(mutationSink.Mutations[0].WorkspaceId, Is.EqualTo("workspace-a"));
        }

        [Test]
        public async Task DispatchAsync_SessionSettingsUpdate_PassesPlanModeWithoutModelOverrides()
        {
            var service = new FakeCodingRemoteService();
            service.SessionSettingsUpdateResult = new CodingRemoteSessionSettingsUpdateResult
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                Updated = true,
                StatusText = "Session settings updated.",
                IsPlanModeEnabled = true
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-settings-update-plan",
                    CommandType = RemoteCommandTypes.CodingSessionSettingsUpdate,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" },
                        { "isPlanModeEnabled", true }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionSettingsUpdate.SessionId, Is.EqualTo("session-a"));
            Assert.That(service.LastSessionSettingsUpdate.HasModelId, Is.False);
            Assert.That(service.LastSessionSettingsUpdate.ModelId, Is.Null);
            Assert.That(service.LastSessionSettingsUpdate.HasReasoningEffort, Is.False);
            Assert.That(service.LastSessionSettingsUpdate.ReasoningEffort, Is.Null);
            Assert.That(service.LastSessionSettingsUpdate.HasApprovalMode, Is.False);
            Assert.That(service.LastSessionSettingsUpdate.ApprovalMode, Is.Null);
            Assert.That(service.LastSessionSettingsUpdate.IsPlanModeEnabled, Is.True);
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionSettingsUpdateResult));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["updated"], Is.True);
            Assert.That(result.Payload["isPlanModeEnabled"], Is.True);
        }

        [Test]
        public async Task DispatchAsync_SessionStop_PassesSessionIdAndReturnsStopResult()
        {
            var service = new FakeCodingRemoteService();
            service.SessionStopResult = new CodingRemoteSessionStopResult
            {
                SessionId = "session-a",
                WorkspaceId = "workspace-a",
                Stopped = true,
                StatusText = "Stop requested."
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-stop",
                    CommandType = RemoteCommandTypes.CodingSessionStop,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "sessionId", "session-a" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastSessionStopRequest.SessionId, Is.EqualTo("session-a"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.CodingSessionStopResult));
            Assert.That(result.Payload["sessionId"], Is.EqualTo("session-a"));
            Assert.That(result.Payload["workspaceId"], Is.EqualTo("workspace-a"));
            Assert.That(result.Payload["stopped"], Is.True);
            Assert.That(result.Payload["statusText"], Is.EqualTo("Stop requested."));
        }

        [Test]
        public async Task DispatchAsync_ApprovalRespond_PassesDecisionAndReturnsResult()
        {
            var service = new FakeCodingRemoteService();
            service.ApprovalResponseResult = new CodingRemoteApprovalResponseResult
            {
                RequestId = "approval-1",
                Decision = "allow_once",
                Completed = true,
                StatusText = "Approval response submitted."
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-approval-response",
                    CommandType = RemoteCommandTypes.ApprovalRespond,
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object>
                    {
                        { "requestId", "approval-1" },
                        { "decision", "allow_once" }
                    }
                },
                CancellationToken.None);

            Assert.That(service.LastApprovalResponseRequest.RequestId, Is.EqualTo("approval-1"));
            Assert.That(service.LastApprovalResponseRequest.Decision, Is.EqualTo("allow_once"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.ApprovalResponseResult));
            Assert.That(result.Payload["requestId"], Is.EqualTo("approval-1"));
            Assert.That(result.Payload["decision"], Is.EqualTo("allow_once"));
            Assert.That(result.Payload["completed"], Is.True);
        }

        [Test]
        public async Task DispatchAsync_ToolInputRespond_PassesAnswersAndReturnsResult()
        {
            var service = new FakeCodingRemoteService
            {
                ToolInputResponseResult = new CodingRemoteToolInputResponseResult
                {
                    RequestId = "input-1",
                    Completed = true,
                    Canceled = false,
                    StatusText = "ok"
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "cmd-1",
                    CommandType = RemoteCommandTypes.ToolInputRespond,
                    Payload = new Dictionary<string, object>
                    {
                        { "requestId", "input-1" },
                        { "answers", new Dictionary<string, object> { { "choice", new object[] { "continue" } } } }
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(service.LastToolInputRespondRequest.RequestId, Is.EqualTo("input-1"));
            Assert.That(service.LastToolInputRespondRequest.Answers["choice"], Is.EqualTo(new[] { "continue" }));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.ToolInputResponseResult));
            Assert.That(result.Payload["completed"], Is.EqualTo(true));
            Assert.That(result.Payload["canceled"], Is.EqualTo(false));
        }

        [Test]
        public async Task DispatchAsync_ToolInputCancel_PassesRequestIdAndReturnsResult()
        {
            var service = new FakeCodingRemoteService
            {
                ToolInputResponseResult = new CodingRemoteToolInputResponseResult
                {
                    RequestId = "input-1",
                    Completed = true,
                    Canceled = true
                }
            };
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "cmd-1",
                    CommandType = RemoteCommandTypes.ToolInputCancel,
                    Payload = new Dictionary<string, object> { { "requestId", "input-1" } }
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(service.LastToolInputCancelRequest.RequestId, Is.EqualTo("input-1"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.ToolInputResponseResult));
            Assert.That(result.Payload["canceled"], Is.EqualTo(true));
        }

        [Test]
        public async Task DispatchAsync_AsyncToolInput_RoutesAnswersAndIgnoreToAsyncResponder()
        {
            var service = new FakeCodingRemoteService();
            var responder = new RecordingAsyncInputResponder();
            var dispatcher = new RemoteCommandDispatcher(
                service,
                null,
                new RemoteWriteCoordinator(),
                null,
                null,
                asyncInputResponder: responder);

            var respond = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "cmd-1",
                    CommandType = RemoteCommandTypes.ToolInputRespond,
                    Payload = new Dictionary<string, object>
                    {
                        { "requestId", "async:thread-1:item-1" },
                        { "answers", new Dictionary<string, object> { { "async-1", new object[] { "A" } } } }
                    }
                },
                CancellationToken.None).ConfigureAwait(false);
            var ignore = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "cmd-2",
                    CommandType = RemoteCommandTypes.ToolInputCancel,
                    Payload = new Dictionary<string, object> { { "requestId", "async:thread-1:item-1" } }
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(service.LastToolInputRespondRequest, Is.Null);
            Assert.That(service.LastToolInputCancelRequest, Is.Null);
            Assert.That(responder.LastRespondRequest.Answers["async-1"], Is.EqualTo(new[] { "A" }));
            Assert.That(responder.LastIgnoreRequest.RequestId, Is.EqualTo("async:thread-1:item-1"));
            Assert.That(respond.EventType, Is.EqualTo(RemoteEventTypes.ToolInputResponseResult));
            Assert.That(respond.Payload["completed"], Is.EqualTo(true));
            Assert.That(ignore.Payload["canceled"], Is.EqualTo(true));
        }

        [Test]
        public async Task DispatchAsync_AsyncToolInputWithoutResponder_ReturnsOperationNotAvailable()
        {
            var service = new FakeCodingRemoteService();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "cmd-1",
                    CommandType = RemoteCommandTypes.ToolInputCancel,
                    Payload = new Dictionary<string, object> { { "requestId", "async:thread-1:item-1" } }
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(service.LastToolInputCancelRequest, Is.Null);
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.OperationNotAvailable));
        }

        [Test]
        public async Task DispatchAsync_PlanExecutionRespond_PassesActionAndReturnsResult()
        {
            var responder = new RecordingPlanExecutionResponder();
            var dispatcher = new RemoteCommandDispatcher(
                new FakeCodingRemoteService(),
                new RemoteWriteCoordinator(),
                null,
                responder);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "cmd-1",
                    CommandType = RemoteCommandTypes.PlanExecutionRespond,
                    Payload = new Dictionary<string, object>
                    {
                        { "requestId", "plan-1" },
                        { "threadId", "thread-1" },
                        { "action", "execute" }
                    }
                },
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(responder.LastRequest.RequestId, Is.EqualTo("plan-1"));
            Assert.That(responder.LastRequest.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(responder.LastRequest.Action, Is.EqualTo("execute"));
            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.PlanExecutionResponseResult));
            Assert.That(result.Payload["completed"], Is.EqualTo(true));
            Assert.That(result.Payload["action"], Is.EqualTo("execute"));
        }

        [TestCase("coding.unknown")]
        [TestCase("coding.session.derive")]
        [TestCase("coding.session.rollback")]
        public async Task DispatchAsync_UnknownCommand_ReturnsStructuredRemoteError(string commandType)
        {
            var service = new FakeCodingRemoteService();
            var dispatcher = new RemoteCommandDispatcher(service, new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-4",
                    CommandType = commandType,
                    DeviceId = "device-a"
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.UnsupportedCommand));
            Assert.That(result.Payload["requestId"], Is.EqualTo("request-4"));
            Assert.That(service.LastThreadOperationRequest, Is.Null);
            Assert.That(service.LastSessionCreateRequest, Is.Null);
            Assert.That(service.LastWorkspaceOperationRequest, Is.Null);
        }

        [Test]
        public async Task DispatchAsync_OfficeCategoryCreate_IsUnsupported()
        {
            var dispatcher = new RemoteCommandDispatcher(
                new FakeCodingRemoteService(),
                new FakeCodingRemoteService(),
                new RemoteWriteCoordinator(),
                null,
                null);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "request-office-category",
                    CommandType = "office.category.create",
                    DeviceId = "device-a",
                    Payload = new Dictionary<string, object> { { "name", "Reports" } }
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.UnsupportedCommand));
        }

        private static RemoteRelayCommand CreateMessageSend(string deviceId, string sessionId, string operationId)
        {
            return new RemoteRelayCommand
            {
                RequestId = "request-" + operationId,
                OperationId = operationId,
                CommandType = RemoteCommandTypes.CodingMessageSend,
                DeviceId = deviceId,
                Payload = new Dictionary<string, object>
                {
                    { "sessionId", sessionId },
                    { "text", "hello" }
                }
            };
        }

        private static CodingRemoteMessageSendResult Accepted(CodingRemoteMessageSendRequest request)
        {
            return new CodingRemoteMessageSendResult
            {
                SessionId = request.SessionId,
                OperationId = request.OperationId,
                Accepted = true
            };
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        [Test]
        public async Task DispatchAsync_HostSettingsGet_MapsAggregateResult()
        {
            var host = new FakeHostSettingsOperator
            {
                Snapshot = CreateHostSettingsSnapshot()
            };
            var dispatcher = new RemoteCommandDispatcher(
                new FakeCodingRemoteService(),
                null,
                new RemoteWriteCoordinator(),
                null,
                null,
                host);

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "host-get",
                    CommandType = RemoteCommandTypes.HostSettingsGet,
                    DeviceId = "phone"
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.HostSettingsResult));
            var scope = (Dictionary<string, object>)result.Payload["scope"];
            Assert.That(scope["runtimeSource"], Is.EqualTo("Wsl"));
            Assert.That(((IReadOnlyList<Dictionary<string, object>>)result.Payload["models"])[0]["modelId"], Is.EqualTo("gpt-test"));
        }

        [Test]
        public async Task DispatchAsync_HostSettingsUpdate_ParsesPresentSectionsAndPublishesMutation()
        {
            var host = new FakeHostSettingsOperator
            {
                Snapshot = CreateHostSettingsSnapshot(),
                UpdateResult = new HostSettingsUpdateResult
                {
                    Updated = true,
                    Settings = CreateHostSettingsSnapshot()
                }
            };
            var mutations = new RecordingRemoteHostMutationSink();
            var dispatcher = new RemoteCommandDispatcher(
                new FakeCodingRemoteService(),
                null,
                new RemoteWriteCoordinator(),
                mutations,
                null,
                host);
            var command = new RemoteRelayCommand
            {
                RequestId = "host-update",
                OperationId = "host-op",
                CommandType = RemoteCommandTypes.HostSettingsUpdate,
                DeviceId = "phone"
            };
            command.Payload["defaults"] = new Dictionary<string, object>
            {
                { "modelId", "gpt-test" },
                { "reasoningEffort", "high" },
                { "approvalMode", "auto" }
            };

            var result = await dispatcher.DispatchAsync(command, CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.HostSettingsUpdateResult));
            Assert.That(result.Payload["updated"], Is.True);
            Assert.That(host.LastRequest.Defaults.ModelId, Is.EqualTo("gpt-test"));
            Assert.That(host.LastRequest.CustomApproval, Is.Null);
            Assert.That(mutations.Mutations.Single().MutationType, Is.EqualTo(RemoteHostMutationTypes.HostSettingsUpdated));
        }

        [Test]
        public async Task DispatchAsync_HostSettingsUpdate_RejectsMissingFieldsAndReleasesLock()
        {
            var host = new FakeHostSettingsOperator { Snapshot = CreateHostSettingsSnapshot() };
            var coordinator = new RemoteWriteCoordinator();
            var dispatcher = new RemoteCommandDispatcher(
                new FakeCodingRemoteService(),
                null,
                coordinator,
                null,
                null,
                host);
            var invalid = new RemoteRelayCommand
            {
                RequestId = "bad",
                CommandType = RemoteCommandTypes.HostSettingsUpdate,
                DeviceId = "phone"
            };
            invalid.Payload["defaults"] = new Dictionary<string, object>
            {
                { "modelId", "gpt-test" }
            };

            var result = await dispatcher.DispatchAsync(invalid, CancellationToken.None);
            var acquiredAfterFailure = coordinator.Acquire("other-phone", "next", System.DateTime.UtcNow);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.InvalidCommand));
            Assert.That(host.LastRequest, Is.Null);
            Assert.That(acquiredAfterFailure.IsGranted, Is.True);
        }

        [Test]
        public async Task DispatchAsync_HostSettingsCommands_ReturnUnavailableWithoutOperator()
        {
            var dispatcher = new RemoteCommandDispatcher(
                new FakeCodingRemoteService(),
                new RemoteWriteCoordinator());

            var result = await dispatcher.DispatchAsync(
                new RemoteRelayCommand
                {
                    RequestId = "host-get",
                    CommandType = RemoteCommandTypes.HostSettingsGet,
                    DeviceId = "phone"
                },
                CancellationToken.None);

            Assert.That(result.EventType, Is.EqualTo(RemoteEventTypes.RemoteError));
            Assert.That(result.Payload["code"], Is.EqualTo(RemoteErrorCodes.OperationNotAvailable));
        }

        private static HostSettingsSnapshot CreateHostSettingsSnapshot()
        {
            return new HostSettingsSnapshot
            {
                Scope = new HostSettingsScope
                {
                    HostName = "PC",
                    RuntimeSource = "Wsl",
                    RuntimeDisplayName = "Ubuntu",
                    AuthenticationDisplayName = "ChatGPT"
                },
                Defaults = new HostSettingsDefaults
                {
                    ModelId = "gpt-test",
                    ReasoningEffort = "high",
                    ApprovalMode = "auto"
                },
                Models = new List<CodingRemoteModelOption>
                {
                    new CodingRemoteModelOption
                    {
                        ModelId = "gpt-test",
                        DisplayName = "GPT Test",
                        DefaultReasoningEffort = "high",
                        ReasoningEfforts = new List<string> { "high" }
                    }
                },
                CustomApproval = new HostSettingsCustomApproval
                {
                    AvailablePermissions = new List<HostSettingsPermissionOption>
                    {
                        new HostSettingsPermissionOption { Id = "GitInspect", DisplayName = "Git inspect" }
                    },
                    EnabledPermissions = new List<string> { "GitInspect" },
                    CommandPrefixes = new List<string> { "git status" }
                }
            };
        }

        private sealed class FakeHostSettingsOperator : IHostSettingsOperator
        {
            public HostSettingsSnapshot Snapshot { get; set; }

            public HostSettingsUpdateRequest LastRequest { get; private set; }

            public HostSettingsUpdateResult UpdateResult { get; set; }

            public Task<HostSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(Snapshot);
            }

            public Task<HostSettingsUpdateResult> UpdateAsync(
                HostSettingsUpdateRequest request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(UpdateResult ?? new HostSettingsUpdateResult
                {
                    Settings = Snapshot
                });
            }
        }

        private sealed class FakeCodingRemoteService : ICodingRemoteService
        {
            private int _sendCount;
            private int _threadOperationCount;

            public FakeCodingRemoteService()
            {
                Workspaces = new List<CodingRemoteWorkspaceSummary>();
                Sessions = new List<CodingRemoteSessionSummary>();
                MessagePage = new CodingRemoteMessagePage();
                PluginAttachmentCatalog = new CodingRemoteAttachmentCatalogResult();
                SkillAttachmentCatalog = new CodingRemoteAttachmentCatalogResult();
            }

            public List<CodingRemoteWorkspaceSummary> Workspaces { get; }

            public List<CodingRemoteSessionSummary> Sessions { get; }

            public CodingRemoteSessionSummary CreatedSession { get; set; }

            public CodingRemoteWorkspaceSummary CreatedWorkspace { get; set; }

            public CodingRemoteMessagePage MessagePage { get; set; }

            public int WorkspaceListCalls { get; private set; }

            public string LastWorkspaceCreateName { get; private set; }

            public string LastWorkspaceId { get; private set; }

            public CodingRemoteSessionCreateRequest LastSessionCreateRequest { get; private set; }

            public CodingRemoteMessagesListRequest LastMessageRequest { get; private set; }

            public CodingRemoteMessageSendRequest LastSendRequest { get; private set; }

            public System.Func<CodingRemoteMessageSendRequest, Task<CodingRemoteMessageSendResult>> SendMessageHandler { get; set; }

            public int SendCount => Volatile.Read(ref _sendCount);

            public CodingRemoteSessionStopRequest LastSessionStopRequest { get; private set; }

            public CodingRemoteSessionStopResult SessionStopResult { get; set; }

            public CodingRemoteWorkspaceOperationRequest LastWorkspaceOperationRequest { get; private set; }

            public CodingRemoteWorkspaceOperationResult WorkspaceOperationResult { get; set; }

            public CodingRemoteOperationException WorkspaceOperationException { get; set; }

            public CodingRemoteSessionOperationRequest LastSessionOperationRequest { get; private set; }

            public CodingRemoteSessionOperationResult SessionOperationResult { get; set; }

            public CodingRemoteSessionThreadOperationRequest LastThreadOperationRequest { get; private set; }

            public CodingRemoteSessionThreadOperationResult ThreadOperationResult { get; set; }

            public System.Func<CodingRemoteSessionThreadOperationRequest, Task<CodingRemoteSessionThreadOperationResult>> ThreadOperationHandler { get; set; }

            public int ThreadOperationCount => Volatile.Read(ref _threadOperationCount);

            public string LastSessionSettingsId { get; private set; }

            public CodingRemoteSessionSettings SessionSettings { get; set; }

            public CodingRemoteSessionSettingsUpdateRequest LastSessionSettingsUpdate { get; private set; }

            public CodingRemoteSessionSettingsUpdateResult SessionSettingsUpdateResult { get; set; }

            public CodingRemoteAttachmentCatalogResult PluginAttachmentCatalog { get; }

            public CodingRemoteAttachmentCatalogResult SkillAttachmentCatalog { get; }

            public string LastPluginAttachmentSessionId { get; private set; }

            public string LastSkillAttachmentSessionId { get; private set; }

            public bool LastSkillAttachmentForceReload { get; private set; }

            public CodingRemoteApprovalResponseRequest LastApprovalResponseRequest { get; private set; }

            public CodingRemoteApprovalResponseResult ApprovalResponseResult { get; set; }

            public CodingRemoteToolInputRespondRequest LastToolInputRespondRequest { get; private set; }

            public CodingRemoteToolInputCancelRequest LastToolInputCancelRequest { get; private set; }

            public CodingRemoteToolInputResponseResult ToolInputResponseResult { get; set; }

            public Task<IReadOnlyList<CodingRemoteWorkspaceSummary>> ListWorkspacesAsync(CancellationToken cancellationToken)
            {
                WorkspaceListCalls++;
                return Task.FromResult((IReadOnlyList<CodingRemoteWorkspaceSummary>)Workspaces);
            }

            public Task<IReadOnlyList<CodingRemoteSessionSummary>> ListSessionsAsync(string workspaceId, CancellationToken cancellationToken)
            {
                LastWorkspaceId = workspaceId;
                return Task.FromResult((IReadOnlyList<CodingRemoteSessionSummary>)Sessions);
            }

            public Task<CodingRemoteSessionSummary> CreateSessionAsync(CodingRemoteSessionCreateRequest request, CancellationToken cancellationToken)
            {
                LastSessionCreateRequest = request;
                return Task.FromResult(CreatedSession);
            }

            public Task<CodingRemoteMessagePage> ListMessagesAsync(CodingRemoteMessagesListRequest request, CancellationToken cancellationToken)
            {
                LastMessageRequest = request;
                return Task.FromResult(MessagePage);
            }

            public Task<CodingRemoteMessageContentPage> ReadMessageContentAsync(CodingRemoteMessageContentRequest request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new CodingRemoteMessageContentPage
                {
                    SessionId = request.SessionId,
                    MessageId = request.MessageId,
                    ContentVersion = request.ContentVersion,
                    Complete = true
                });
            }

            public Task<CodingRemoteMessageSendResult> SendMessageAsync(CodingRemoteMessageSendRequest request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _sendCount);
                LastSendRequest = request;
                return SendMessageHandler != null
                    ? SendMessageHandler(request)
                    : Task.FromResult(Accepted(request));
            }

            public Task<CodingRemoteAttachmentCatalogResult> ListPluginAttachmentsAsync(string sessionId, CancellationToken cancellationToken)
            {
                LastPluginAttachmentSessionId = sessionId;
                PluginAttachmentCatalog.SessionId = sessionId;
                return Task.FromResult(PluginAttachmentCatalog);
            }

            public Task<CodingRemoteAttachmentCatalogResult> ListSkillAttachmentsAsync(string sessionId, bool forceReload, CancellationToken cancellationToken)
            {
                LastSkillAttachmentSessionId = sessionId;
                LastSkillAttachmentForceReload = forceReload;
                SkillAttachmentCatalog.SessionId = sessionId;
                return Task.FromResult(SkillAttachmentCatalog);
            }

            public Task<CodingRemoteSessionStopResult> StopSessionAsync(CodingRemoteSessionStopRequest request, CancellationToken cancellationToken)
            {
                LastSessionStopRequest = request;
                return Task.FromResult(SessionStopResult ?? new CodingRemoteSessionStopResult
                {
                    SessionId = request.SessionId,
                    Stopped = true
                });
            }

            public Task<CodingRemoteWorkspaceOperationResult> OperateWorkspaceAsync(CodingRemoteWorkspaceOperationRequest request, CancellationToken cancellationToken)
            {
                LastWorkspaceOperationRequest = request;
                if (WorkspaceOperationException != null)
                {
                    throw WorkspaceOperationException;
                }

                return Task.FromResult(WorkspaceOperationResult ?? new CodingRemoteWorkspaceOperationResult
                {
                    WorkspaceId = request.WorkspaceId,
                    Action = request.Action,
                    DeleteLocalHistory = request.DeleteLocalHistory,
                    Removed = true
                });
            }

            public Task<CodingRemoteSessionOperationResult> OperateSessionAsync(CodingRemoteSessionOperationRequest request, CancellationToken cancellationToken)
            {
                LastSessionOperationRequest = request;
                return Task.FromResult(SessionOperationResult ?? new CodingRemoteSessionOperationResult
                {
                    SessionId = request.SessionId,
                    Action = request.Action,
                    DeleteLocalHistory = request.DeleteLocalHistory,
                    Removed = true
                });
            }

            public Task<CodingRemoteSessionThreadOperationResult> OperateSessionThreadAsync(CodingRemoteSessionThreadOperationRequest request, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref _threadOperationCount);
                LastThreadOperationRequest = request;
                return ThreadOperationHandler != null
                    ? ThreadOperationHandler(request)
                    : Task.FromResult(ThreadOperationResult);
            }

            public Task<CodingRemoteSessionSettings> GetSessionSettingsAsync(string sessionId, CancellationToken cancellationToken)
            {
                LastSessionSettingsId = sessionId;
                return Task.FromResult(SessionSettings);
            }

            public Task<CodingRemoteSessionSettingsUpdateResult> UpdateSessionSettingsAsync(CodingRemoteSessionSettingsUpdateRequest request, CancellationToken cancellationToken)
            {
                LastSessionSettingsUpdate = request;
                return Task.FromResult(SessionSettingsUpdateResult);
            }

            public Task<CodingRemoteApprovalResponseResult> RespondApprovalAsync(
                CodingRemoteApprovalResponseRequest request,
                CancellationToken cancellationToken)
            {
                LastApprovalResponseRequest = request;
                return Task.FromResult(ApprovalResponseResult ?? new CodingRemoteApprovalResponseResult
                {
                    RequestId = request.RequestId,
                    Decision = request.Decision,
                    Completed = true
                });
            }

            public Task<CodingRemoteToolInputResponseResult> RespondToolInputAsync(
                CodingRemoteToolInputRespondRequest request,
                CancellationToken cancellationToken)
            {
                LastToolInputRespondRequest = request;
                return Task.FromResult(ToolInputResponseResult ?? new CodingRemoteToolInputResponseResult
                {
                    RequestId = request.RequestId,
                    Completed = true
                });
            }

            public Task<CodingRemoteToolInputResponseResult> CancelToolInputAsync(
                CodingRemoteToolInputCancelRequest request,
                CancellationToken cancellationToken)
            {
                LastToolInputCancelRequest = request;
                return Task.FromResult(ToolInputResponseResult ?? new CodingRemoteToolInputResponseResult
                {
                    RequestId = request.RequestId,
                    Completed = true,
                    Canceled = true
                });
            }
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

        private sealed class FakeAccountClient : IAppServerAuthenticationClient
        {
            public FakeAccountClient()
            {
                Authentication = new AppServerAuthenticationState();
                RateLimits = new AppServerRateLimitsSnapshot();
                TokenUsage = new AppServerAccountTokenUsageSnapshot();
            }

            public AppServerAuthenticationState Authentication { get; set; }

            public AppServerRateLimitsSnapshot RateLimits { get; private set; }

            public AppServerAccountTokenUsageSnapshot TokenUsage { get; private set; }

            public bool? LastRefreshToken { get; private set; }

            public Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
            {
                LastRefreshToken = refreshToken;
                return Task.FromResult(Authentication);
            }

            public Task<AppServerRateLimitsSnapshot> GetRateLimitsAsync()
            {
                return Task.FromResult(RateLimits);
            }

            public Task<AppServerLoginResult> StartChatGptLoginAsync()
            {
                return Task.FromResult(new AppServerLoginResult());
            }

            public Task<AppServerLoginResult> StartChatGptDeviceCodeLoginAsync()
            {
                return Task.FromResult(new AppServerLoginResult());
            }

            public Task<AppServerLoginResult> StartOpenAiApiKeyLoginAsync(string apiKey)
            {
                return Task.FromResult(new AppServerLoginResult());
            }

            public Task<AppServerLoginCancelResult> CancelLoginAsync(string loginId)
            {
                return Task.FromResult(new AppServerLoginCancelResult());
            }

            public Task<AppServerAccountTokenUsageSnapshot> GetAccountTokenUsageAsync(
                CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(TokenUsage);
            }

            public Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
                string idempotencyKey,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new AppServerRateLimitResetConsumeResult());
            }

            public Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
                string idempotencyKey,
                string creditId,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new AppServerRateLimitResetConsumeResult());
            }
        }

        private sealed class RecordingAsyncInputResponder : ICodingRemoteAsyncInputResponder
        {
            public CodingRemoteToolInputRespondRequest LastRespondRequest { get; private set; }

            public CodingRemoteToolInputCancelRequest LastIgnoreRequest { get; private set; }

            public Task<CodingRemoteToolInputResponseResult> RespondAsyncInputAsync(
                CodingRemoteToolInputRespondRequest request,
                CancellationToken cancellationToken)
            {
                LastRespondRequest = request;
                return Task.FromResult(new CodingRemoteToolInputResponseResult { RequestId = request.RequestId, Completed = true });
            }

            public Task<CodingRemoteToolInputResponseResult> IgnoreAsyncInputAsync(
                CodingRemoteToolInputCancelRequest request,
                CancellationToken cancellationToken)
            {
                LastIgnoreRequest = request;
                return Task.FromResult(new CodingRemoteToolInputResponseResult { RequestId = request.RequestId, Completed = true, Canceled = true });
            }
        }

        private sealed class RecordingPlanExecutionResponder : ICodingRemotePlanExecutionResponder
        {
            public CodingRemotePlanExecutionResponseRequest LastRequest { get; private set; }

            public Task<CodingRemotePlanExecutionResponseResult> RespondPlanExecutionAsync(
                CodingRemotePlanExecutionResponseRequest request,
                CancellationToken cancellationToken)
            {
                LastRequest = request;
                return Task.FromResult(new CodingRemotePlanExecutionResponseResult
                {
                    RequestId = request.RequestId,
                    ThreadId = request.ThreadId,
                    Action = request.Action,
                    Completed = true
                });
            }
        }
    }
}
