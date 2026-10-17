using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.CodingRemote
{
    public sealed partial class RemoteCommandDispatcher
    {
        private readonly ICodingRemoteService _codingRemoteService;
        private readonly ICodingRemoteService _officeRemoteService;
        private readonly RemoteWriteCoordinator _writeCoordinator;
        private readonly IRemoteHostMutationSink _mutationSink;
        private readonly ICodingRemotePlanExecutionResponder _planExecutionResponder;
        private readonly IHostSettingsOperator _hostSettingsOperator;
        private readonly IAppServerAuthenticationClient _accountClient;
        private readonly Func<IReadOnlyList<RemoteRelayEvent>> _pendingInteractions;

        public RemoteCommandDispatcher(ICodingRemoteService codingRemoteService, RemoteWriteCoordinator writeCoordinator)
            : this(codingRemoteService, writeCoordinator, null)
        {
        }

        public RemoteCommandDispatcher(ICodingRemoteService codingRemoteService, RemoteWriteCoordinator writeCoordinator, IRemoteHostMutationSink mutationSink)
            : this(codingRemoteService, writeCoordinator, mutationSink, null)
        {
        }

        public RemoteCommandDispatcher(
            ICodingRemoteService codingRemoteService,
            RemoteWriteCoordinator writeCoordinator,
            IRemoteHostMutationSink mutationSink,
            ICodingRemotePlanExecutionResponder planExecutionResponder)
            : this(codingRemoteService, null, writeCoordinator, mutationSink, planExecutionResponder, null)
        {
        }

        public RemoteCommandDispatcher(
            ICodingRemoteService codingRemoteService,
            ICodingRemoteService officeRemoteService,
            RemoteWriteCoordinator writeCoordinator,
            IRemoteHostMutationSink mutationSink,
            ICodingRemotePlanExecutionResponder planExecutionResponder,
            IHostSettingsOperator hostSettingsOperator = null,
            IAppServerAuthenticationClient accountClient = null,
            Func<IReadOnlyList<RemoteRelayEvent>> pendingInteractions = null)
        {
            _codingRemoteService = codingRemoteService ?? throw new ArgumentNullException(nameof(codingRemoteService));
            _officeRemoteService = officeRemoteService;
            _writeCoordinator = writeCoordinator ?? throw new ArgumentNullException(nameof(writeCoordinator));
            _mutationSink = mutationSink;
            _planExecutionResponder = planExecutionResponder;
            _hostSettingsOperator = hostSettingsOperator;
            _accountClient = accountClient;
            _pendingInteractions = pendingInteractions;
        }

        private async Task<RemoteRelayEvent> DispatchCoreAsync(RemoteRelayCommand command, CancellationToken cancellationToken)
        {
            if (command == null)
            {
                return CreateError(null, null, null, RemoteErrorCodes.InvalidCommand, "Command is required.");
            }

            try
            {
                switch (command.CommandType)
                {
                    case RemoteCommandTypes.InteractionPendingList:
                        return DispatchPendingInteractions(command);
                    case RemoteCommandTypes.CodingWorkspacesList:
                        return await DispatchWorkspaceListAsync(command, _codingRemoteService, RemoteEventTypes.CodingWorkspacesListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingWorkspaceArchive:
                        return await DispatchWorkspaceOperationAsync(command, CodingRemoteWorkspaceActions.Archive, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingWorkspaceDelete:
                        return await DispatchWorkspaceOperationAsync(command, CodingRemoteWorkspaceActions.Delete, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeWorkspacesList:
                        return await DispatchWorkspaceListAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeWorkspacesListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionsList:
                        return await DispatchSessionListAsync(command, _codingRemoteService, RemoteEventTypes.CodingSessionsListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionCreate:
                        return await DispatchSessionCreateAsync(command, _codingRemoteService, RemoteEventTypes.CodingSessionCreated, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionArchive:
                        return await DispatchSessionOperationAsync(command, CodingRemoteSessionActions.Archive, _codingRemoteService, RemoteEventTypes.CodingSessionOperationResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionDelete:
                        return await DispatchSessionOperationAsync(command, CodingRemoteSessionActions.Delete, _codingRemoteService, RemoteEventTypes.CodingSessionOperationResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionRename:
                        return await DispatchSessionOperationAsync(command, CodingRemoteSessionActions.Rename, _codingRemoteService, RemoteEventTypes.CodingSessionOperationResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionsList:
                        return await DispatchSessionListAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionsListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionCreate:
                        return await DispatchSessionCreateAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionCreated, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionArchive:
                        return await DispatchSessionOperationAsync(command, CodingRemoteSessionActions.Archive, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionOperationResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionDelete:
                        return await DispatchSessionOperationAsync(command, CodingRemoteSessionActions.Delete, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionOperationResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionRename:
                        return await DispatchSessionOperationAsync(command, CodingRemoteSessionActions.Rename, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionOperationResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionFork:
                        return await DispatchSessionThreadOperationAsync(command, CodingRemoteSessionThreadActions.Fork, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionCompact:
                        return await DispatchSessionThreadOperationAsync(command, CodingRemoteSessionThreadActions.Compact, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionSettingsGet:
                        return await DispatchSessionSettingsGetAsync(command, _codingRemoteService, RemoteEventTypes.CodingSessionSettingsResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionSettingsUpdate:
                        return await DispatchSessionSettingsUpdateAsync(command, _codingRemoteService, RemoteEventTypes.CodingSessionSettingsUpdateResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionSettingsGet:
                        return await DispatchSessionSettingsGetAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionSettingsResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionSettingsUpdate:
                        return await DispatchSessionSettingsUpdateAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionSettingsUpdateResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingMessagesList:
                        return await DispatchMessageListAsync(command, _codingRemoteService, RemoteEventTypes.CodingMessagesListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingMessageSend:
                        return await DispatchMessageSendAsync(command, _codingRemoteService, RemoteEventTypes.CodingMessageAccepted, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeMessagesList:
                        return await DispatchMessageListAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeMessagesListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingMessageContentRead:
                        return await DispatchMessageContentAsync(command, _codingRemoteService, RemoteEventTypes.CodingMessageContentResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeMessageContentRead:
                        return await DispatchMessageContentAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeMessageContentResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.HostCapabilitiesGet:
                        return CreateEvent(command, RemoteEventTypes.HostCapabilitiesResult, new Dictionary<string, object>
                        {
                            { "capabilities", new[] { "conversation.messages.pagination.v1", "conversation.message.contentChunks.v1" } }
                        });
                    case RemoteCommandTypes.OfficeMessageSend:
                        return await DispatchMessageSendAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeMessageAccepted, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingAttachmentsPluginsList:
                        return await DispatchPluginAttachmentCatalogAsync(command, _codingRemoteService, RemoteEventTypes.CodingAttachmentsPluginsListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingAttachmentsSkillsList:
                        return await DispatchSkillAttachmentCatalogAsync(command, _codingRemoteService, RemoteEventTypes.CodingAttachmentsSkillsListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeAttachmentsPluginsList:
                        return await DispatchPluginAttachmentCatalogAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeAttachmentsPluginsListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeAttachmentsSkillsList:
                        return await DispatchSkillAttachmentCatalogAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeAttachmentsSkillsListResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.CodingSessionStop:
                        return await DispatchSessionStopAsync(command, _codingRemoteService, RemoteEventTypes.CodingSessionStopResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.OfficeSessionStop:
                        return await DispatchSessionStopAsync(command, GetOfficeRemoteService(), RemoteEventTypes.OfficeSessionStopResult, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.ApprovalRespond:
                        return await DispatchApprovalRespondAsync(command, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.ToolInputRespond:
                        return await DispatchToolInputRespondAsync(command, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.ToolInputCancel:
                        return await DispatchToolInputCancelAsync(command, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.PlanExecutionRespond:
                        return await DispatchPlanExecutionRespondAsync(command, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.HostSettingsGet:
                        return await DispatchHostSettingsGetAsync(command, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.HostSettingsUpdate:
                        return await DispatchHostSettingsUpdateAsync(command, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.HostAccountGet:
                        return await DispatchHostAccountGetAsync(command, cancellationToken).ConfigureAwait(false);
                    case RemoteCommandTypes.WriteLockAcquire:
                        return DispatchWriteLockAcquire(command);
                    case RemoteCommandTypes.WriteLockRelease:
                        return DispatchWriteLockRelease(command);
                    case RemoteCommandTypes.WriteLockHeartbeat:
                        return DispatchWriteLockHeartbeat(command);
                    default:
                        return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.UnsupportedCommand, "Unsupported remote command.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CodingRemoteOperationException ex)
            {
                return CreateError(
                    command.RequestId,
                    command.OperationId,
                    command.DeviceId,
                    string.IsNullOrWhiteSpace(ex.ErrorCode) ? RemoteErrorCodes.OperationFailed : ex.ErrorCode,
                    ex.Message);
            }
            catch (Exception ex)
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.OperationFailed, ex.Message);
            }
        }

        private ICodingRemoteService GetOfficeRemoteService()
        {
            return _officeRemoteService ?? _codingRemoteService;
        }

        private async Task<RemoteRelayEvent> DispatchWorkspaceListAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var workspaces = await remoteService.ListWorkspacesAsync(cancellationToken).ConfigureAwait(false);
            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "workspaces", MapWorkspaces(workspaces) }
            });
        }

        private async Task<RemoteRelayEvent> DispatchWorkspaceOperationAsync(RemoteRelayCommand command, string action, CancellationToken cancellationToken)
        {
            var workspaceId = GetString(command.Payload, "workspaceId");
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Workspace ID is required.");
            }

            var deleteLocalHistory = GetBool(command.Payload, "deleteLocalHistory");
            var result = await _codingRemoteService.OperateWorkspaceAsync(
                new CodingRemoteWorkspaceOperationRequest
                {
                    WorkspaceId = workspaceId,
                    Action = action,
                    DeleteLocalHistory = deleteLocalHistory
                },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteWorkspaceOperationResult
            {
                WorkspaceId = workspaceId,
                Action = action,
                DeleteLocalHistory = deleteLocalHistory,
                Removed = true
            };

            await PublishWorkspaceOperationMutationAsync(result, cancellationToken).ConfigureAwait(false);

            return CreateEvent(command, RemoteEventTypes.CodingWorkspaceOperationResult, new Dictionary<string, object>
            {
                { "workspaceId", result.WorkspaceId },
                { "action", result.Action },
                { "runtimeProfileId", result.RuntimeProfileId },
                { "deleteLocalHistory", result.DeleteLocalHistory },
                { "processedCount", result.ProcessedCount },
                { "removed", result.Removed },
                { "affectedSessionIds", result.AffectedSessionIds },
                { "statusText", result.StatusText }
            });
        }

        private async Task PublishWorkspaceOperationMutationAsync(CodingRemoteWorkspaceOperationResult result, CancellationToken cancellationToken)
        {
            if (_mutationSink == null || result == null || !result.Removed)
            {
                return;
            }

            var mutationType = string.Equals(result.Action, CodingRemoteWorkspaceActions.Archive, StringComparison.OrdinalIgnoreCase)
                ? RemoteHostMutationTypes.WorkspaceArchived
                : string.Equals(result.Action, CodingRemoteWorkspaceActions.Delete, StringComparison.OrdinalIgnoreCase)
                    ? RemoteHostMutationTypes.WorkspaceDeleted
                    : null;
            if (mutationType == null)
            {
                return;
            }

            try
            {
                await _mutationSink.PublishAsync(
                    new RemoteHostMutation
                    {
                        MutationType = mutationType,
                        RuntimeProfileId = result.RuntimeProfileId,
                        WorkspaceId = result.WorkspaceId,
                        AffectedSessionIds = result.AffectedSessionIds ?? new List<string>(),
                        StatusText = result.StatusText
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The remote operation already succeeded. A local UI refresh failure must not
                // turn a successful mobile command into a failed command response.
            }
        }

        private async Task<RemoteRelayEvent> DispatchSessionListAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var workspaceId = GetString(command.Payload, "workspaceId");
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Workspace ID is required.");
            }

            var sessions = await remoteService.ListSessionsAsync(workspaceId, cancellationToken).ConfigureAwait(false);
            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "workspaceId", workspaceId },
                { "sessions", MapSessions(sessions) }
            });
        }

        private async Task<RemoteRelayEvent> DispatchSessionCreateAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string createdEventType,
            CancellationToken cancellationToken)
        {
            var workspaceId = GetString(command.Payload, "workspaceId");
            if (string.IsNullOrWhiteSpace(workspaceId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Workspace ID is required.");
            }

            var session = await remoteService.CreateSessionAsync(
                new CodingRemoteSessionCreateRequest
                {
                    WorkspaceId = workspaceId,
                    Title = GetString(command.Payload, "title")
                },
                cancellationToken).ConfigureAwait(false);
            session = session ?? new CodingRemoteSessionSummary
            {
                WorkspaceId = workspaceId,
                State = CodingRemoteSessionStates.Idle,
                BranchKind = CodingRemoteSessionBranchKinds.Mainline
            };

            await PublishSessionCreatedMutationAsync(session, cancellationToken).ConfigureAwait(false);

            return CreateEvent(command, createdEventType, new Dictionary<string, object>
            {
                { "workspaceId", session.WorkspaceId },
                { "session", MapSession(session) }
            });
        }

        private async Task PublishSessionCreatedMutationAsync(CodingRemoteSessionSummary session, CancellationToken cancellationToken)
        {
            if (_mutationSink == null || session == null)
            {
                return;
            }

            try
            {
                await _mutationSink.PublishAsync(
                    new RemoteHostMutation
                    {
                        MutationType = RemoteHostMutationTypes.SessionCreated,
                        WorkspaceId = session.WorkspaceId,
                        SessionId = session.SessionId,
                        StatusText = "Session created."
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The remote operation already succeeded. A local UI refresh failure must not
                // turn a successful mobile command into a failed command response.
            }
        }

        private async Task<RemoteRelayEvent> DispatchSessionOperationAsync(
            RemoteRelayCommand command,
            string action,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var deleteLocalHistory = GetBool(command.Payload, "deleteLocalHistory");
            var title = string.Equals(action, CodingRemoteSessionActions.Rename, StringComparison.OrdinalIgnoreCase)
                ? (GetString(command.Payload, "title") ?? string.Empty).Trim()
                : null;
            if (string.Equals(action, CodingRemoteSessionActions.Rename, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(title))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session title is required.");
            }

            var result = await remoteService.OperateSessionAsync(
                new CodingRemoteSessionOperationRequest
                {
                    SessionId = sessionId,
                    Action = action,
                    DeleteLocalHistory = deleteLocalHistory,
                    Title = title
                },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteSessionOperationResult
            {
                SessionId = sessionId,
                Action = action,
                DeleteLocalHistory = deleteLocalHistory,
                Removed = !string.Equals(action, CodingRemoteSessionActions.Rename, StringComparison.OrdinalIgnoreCase),
                Updated = string.Equals(action, CodingRemoteSessionActions.Rename, StringComparison.OrdinalIgnoreCase),
                Title = title
            };

            await PublishSessionOperationMutationAsync(result, cancellationToken).ConfigureAwait(false);

            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "sessionId", result.SessionId },
                { "workspaceId", result.WorkspaceId },
                { "action", result.Action },
                { "runtimeProfileId", result.RuntimeProfileId },
                { "deleteLocalHistory", result.DeleteLocalHistory },
                { "processedCount", result.ProcessedCount },
                { "removed", result.Removed },
                { "updated", result.Updated },
                { "title", result.Title },
                { "statusText", result.StatusText }
            });
        }

        private async Task PublishSessionOperationMutationAsync(CodingRemoteSessionOperationResult result, CancellationToken cancellationToken)
        {
            if (_mutationSink == null || result == null)
            {
                return;
            }

            var mutationType = ResolveSessionOperationMutationType(result);
            if (mutationType == null)
            {
                return;
            }

            try
            {
                await _mutationSink.PublishAsync(
                    new RemoteHostMutation
                    {
                        MutationType = mutationType,
                        RuntimeProfileId = result.RuntimeProfileId,
                        WorkspaceId = result.WorkspaceId,
                        SessionId = result.SessionId,
                        StatusText = result.StatusText
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The remote operation already succeeded. A local UI refresh failure must not
                // turn a successful mobile command into a failed command response.
            }
        }

        private static string ResolveSessionOperationMutationType(CodingRemoteSessionOperationResult result)
        {
            if (result == null)
            {
                return null;
            }

            if (string.Equals(result.Action, CodingRemoteSessionActions.Archive, StringComparison.OrdinalIgnoreCase) && result.Removed)
            {
                return RemoteHostMutationTypes.SessionArchived;
            }

            if (string.Equals(result.Action, CodingRemoteSessionActions.Delete, StringComparison.OrdinalIgnoreCase) && result.Removed)
            {
                return RemoteHostMutationTypes.SessionDeleted;
            }

            if (string.Equals(result.Action, CodingRemoteSessionActions.Rename, StringComparison.OrdinalIgnoreCase) && result.Updated)
            {
                return RemoteHostMutationTypes.SessionRenamed;
            }

            return null;
        }

        private async Task<RemoteRelayEvent> DispatchSessionThreadOperationAsync(
            RemoteRelayCommand command,
            string action,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var mode = GetString(command.Payload, "mode");
            if (string.IsNullOrWhiteSpace(mode))
            {
                mode = CodingRemoteSessionThreadOperationModes.Full;
            }

            var turnId = GetString(command.Payload, "turnId");
            if (string.Equals(mode, CodingRemoteSessionThreadOperationModes.FromTurn, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(turnId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Turn ID is required.");
            }

            if (string.IsNullOrWhiteSpace(command.DeviceId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Device ID is required.");
            }

            var operationId = string.IsNullOrWhiteSpace(command.OperationId)
                ? command.RequestId
                : command.OperationId;
            return await DispatchSessionMutationOnceAsync(command, sessionId, operationId, async () =>
            {
                var result = await _codingRemoteService.OperateSessionThreadAsync(
                    new CodingRemoteSessionThreadOperationRequest
                    {
                        SessionId = sessionId,
                        Action = action,
                        Mode = mode,
                        TurnId = turnId,
                        Title = GetString(command.Payload, "title"),
                        SwitchToCreatedSession = GetBool(command.Payload, "switchToCreatedSession")
                    },
                    cancellationToken).ConfigureAwait(false);

                result = result ?? new CodingRemoteSessionThreadOperationResult
                {
                    SessionId = sessionId,
                    Action = action,
                    Mode = mode
                };
                await PublishSessionThreadOperationMutationAsync(result, cancellationToken).ConfigureAwait(false);
                return CreateEvent(command, RemoteEventTypes.CodingSessionThreadOperationResult, MapSessionThreadOperationResult(result));
            }).ConfigureAwait(false);
        }

        private async Task PublishSessionThreadOperationMutationAsync(CodingRemoteSessionThreadOperationResult result, CancellationToken cancellationToken)
        {
            if (_mutationSink == null || result == null)
            {
                return;
            }

            var mutationType = ResolveSessionThreadOperationMutationType(result);
            if (mutationType == null)
            {
                return;
            }

            try
            {
                await _mutationSink.PublishAsync(
                    new RemoteHostMutation
                    {
                        MutationType = mutationType,
                        WorkspaceId = ResolveSessionThreadOperationWorkspaceId(result),
                        SessionId = ResolveSessionThreadOperationSessionId(result),
                        StatusText = result.StatusText
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The remote operation already succeeded. A local UI refresh failure must not
                // turn a successful mobile command into a failed command response.
            }
        }

        private static string ResolveSessionThreadOperationMutationType(CodingRemoteSessionThreadOperationResult result)
        {
            if (string.Equals(result.Action, CodingRemoteSessionThreadActions.Fork, StringComparison.OrdinalIgnoreCase))
            {
                return RemoteHostMutationTypes.SessionForkCreated;
            }

            if (string.Equals(result.Action, CodingRemoteSessionThreadActions.Compact, StringComparison.OrdinalIgnoreCase))
            {
                return RemoteHostMutationTypes.SessionCompactRequested;
            }

            return null;
        }

        private static string ResolveSessionThreadOperationWorkspaceId(CodingRemoteSessionThreadOperationResult result)
        {
            return result.CreatedSession == null || string.IsNullOrWhiteSpace(result.CreatedSession.WorkspaceId)
                ? result.WorkspaceId
                : result.CreatedSession.WorkspaceId;
        }

        private static string ResolveSessionThreadOperationSessionId(CodingRemoteSessionThreadOperationResult result)
        {
            return result.CreatedSession == null || string.IsNullOrWhiteSpace(result.CreatedSession.SessionId)
                ? result.SessionId
                : result.CreatedSession.SessionId;
        }

        private async Task<RemoteRelayEvent> DispatchSessionSettingsGetAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var settings = await remoteService.GetSessionSettingsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            settings = settings ?? new CodingRemoteSessionSettings { SessionId = sessionId };
            return CreateEvent(command, resultEventType, MapSessionSettings(settings));
        }

        private async Task<RemoteRelayEvent> DispatchSessionSettingsUpdateAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var result = await remoteService.UpdateSessionSettingsAsync(
                new CodingRemoteSessionSettingsUpdateRequest
                {
                    SessionId = sessionId,
                    HasModelId = HasPayloadKey(command.Payload, "modelId"),
                    ModelId = GetString(command.Payload, "modelId"),
                    HasReasoningEffort = HasPayloadKey(command.Payload, "reasoningEffort"),
                    ReasoningEffort = GetString(command.Payload, "reasoningEffort"),
                    HasApprovalMode = HasPayloadKey(command.Payload, "approvalMode"),
                    ApprovalMode = GetString(command.Payload, "approvalMode"),
                    IsPlanModeEnabled = GetNullableBool(command.Payload, "isPlanModeEnabled")
                },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteSessionSettingsUpdateResult
            {
                SessionId = sessionId,
                Updated = true
            };

            await PublishSessionSettingsMutationAsync(result, cancellationToken).ConfigureAwait(false);

            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "sessionId", result.SessionId },
                { "workspaceId", result.WorkspaceId },
                { "updated", result.Updated },
                { "isPlanModeEnabled", result.IsPlanModeEnabled },
                { "statusText", result.StatusText }
            });
        }

        private async Task PublishSessionSettingsMutationAsync(CodingRemoteSessionSettingsUpdateResult result, CancellationToken cancellationToken)
        {
            if (_mutationSink == null || result == null || !result.Updated)
            {
                return;
            }

            try
            {
                await _mutationSink.PublishAsync(
                    new RemoteHostMutation
                    {
                        MutationType = RemoteHostMutationTypes.SessionSettingsUpdated,
                        WorkspaceId = result.WorkspaceId,
                        SessionId = result.SessionId,
                        StatusText = result.StatusText
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The remote operation already succeeded. A local UI refresh failure must not
                // turn a successful mobile command into a failed command response.
            }
        }

        private async Task<RemoteRelayEvent> DispatchMessageListAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var hasBefore = !string.IsNullOrWhiteSpace(GetString(command.Payload, "beforeCursor"));
            var hasAfter = !string.IsNullOrWhiteSpace(GetString(command.Payload, "afterCursor"));
            var hasAnchor = !string.IsNullOrWhiteSpace(GetString(command.Payload, "anchorMessageId"));
            if ((hasBefore ? 1 : 0) + (hasAfter ? 1 : 0) + (hasAnchor ? 1 : 0) > 1)
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Only one paging target is allowed.");
            }

            var pagingVersion = GetInt(command.Payload, "pagingVersion", 0);
            var requestedLimit = GetInt(command.Payload, "limit", pagingVersion > 0 ? 100 : 50);
            var page = await remoteService.ListMessagesAsync(
                new CodingRemoteMessagesListRequest
                {
                    SessionId = sessionId,
                    BeforeCursor = GetString(command.Payload, "beforeCursor"),
                    AfterCursor = GetString(command.Payload, "afterCursor"),
                    AnchorMessageId = GetString(command.Payload, "anchorMessageId"),
                    KnownHistoryRevision = GetString(command.Payload, "knownHistoryRevision"),
                    PagingVersion = pagingVersion,
                    Limit = requestedLimit
                },
                cancellationToken).ConfigureAwait(false);
            page = page ?? new CodingRemoteMessagePage { SessionId = sessionId };

            var result = CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "sessionId", page.SessionId },
                { "messages", MapMessages(page.Messages) },
                { "nextCursor", page.NextCursor },
                { "olderCursor", page.OlderCursor },
                { "newerCursor", page.NewerCursor },
                { "hasOlder", page.HasOlder },
                { "hasNewer", page.HasNewer },
                { "tailConnected", page.TailConnected },
                { "tailMessageId", page.TailMessageId },
                { "latestAssistantMessageId", page.LatestAssistantMessageId },
                { "historyRevision", page.HistoryRevision },
                { "anchorFound", page.AnchorFound },
                { "resolvedAnchorMessageId", page.ResolvedAnchorMessageId },
                { "generating", page.IsGenerating }
            });
            if (!FitsResponseBudget(result))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId,
                    pagingVersion > 0 ? RemoteErrorCodes.ResponseTooLarge : RemoteErrorCodes.MobileUpgradeRequired,
                    "The requested conversation response is too large for this device version.");
            }

            return result;
        }

        private async Task<RemoteRelayEvent> DispatchMessageContentAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            var messageId = GetString(command.Payload, "messageId");
            if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(messageId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID and message ID are required.");
            }

            var page = await remoteService.ReadMessageContentAsync(new CodingRemoteMessageContentRequest
            {
                SessionId = sessionId,
                MessageId = messageId,
                ContentVersion = GetString(command.Payload, "contentVersion"),
                Cursor = GetString(command.Payload, "cursor")
            }, cancellationToken).ConfigureAwait(false);
            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "sessionId", page.SessionId }, { "messageId", page.MessageId }, { "contentVersion", page.ContentVersion },
                { "text", page.Text }, { "nextCursor", page.NextCursor }, { "loadedUtf8Bytes", page.LoadedUtf8Bytes },
                { "totalUtf8Bytes", page.TotalUtf8Bytes }, { "complete", page.Complete }
            });
        }

        private static bool FitsResponseBudget(RemoteRelayEvent response)
        {
            try
            {
                return new UTF8Encoding(false, true).GetByteCount(RemoteRelayPayloadJsonSerializer.SerializeResponse(response)) <= 4 * 1024 * 1024;
            }
            catch
            {
                return false;
            }
        }

        private async Task<RemoteRelayEvent> DispatchMessageSendAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string acceptedEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var text = GetString(command.Payload, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                text = text.Trim();
            }

            var inputItems = BuildInputItems(command.Payload, out var inputItemsError);
            if (!string.IsNullOrWhiteSpace(inputItemsError))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, inputItemsError);
            }

            if (string.IsNullOrWhiteSpace(text) && inputItems.Count == 0)
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Message text or input items are required.");
            }

            if (string.IsNullOrWhiteSpace(command.DeviceId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Device ID is required.");
            }

            var operationId = string.IsNullOrWhiteSpace(command.OperationId)
                ? command.RequestId
                : command.OperationId;
            return await DispatchSessionMutationOnceAsync(command, sessionId, operationId, async () =>
            {
                var result = await remoteService.SendMessageAsync(
                    new CodingRemoteMessageSendRequest
                    {
                        SessionId = sessionId,
                        Text = text,
                        DeviceId = command.DeviceId,
                        OperationId = operationId,
                        InputItems = inputItems
                    },
                    cancellationToken).ConfigureAwait(false);
                result = result ?? new CodingRemoteMessageSendResult
                {
                    SessionId = sessionId,
                    OperationId = operationId,
                    Accepted = true
                };

                return CreateEvent(command, acceptedEventType, new Dictionary<string, object>
                {
                    { "sessionId", result.SessionId },
                    { "operationId", result.OperationId },
                    { "accepted", result.Accepted },
                    { "recreatedThread", result.RecreatedThread },
                    { "dispatchMode", string.IsNullOrWhiteSpace(result.DispatchMode) ? "start" : result.DispatchMode }
                });
            }).ConfigureAwait(false);
        }

        private async Task<RemoteRelayEvent> DispatchSessionMutationOnceAsync(
            RemoteRelayCommand command,
            string sessionId,
            string operationId,
            Func<Task<RemoteRelayEvent>> mutation)
        {
            var lockResult = _writeCoordinator.Acquire(command.DeviceId, operationId, DateTime.UtcNow);
            if (!lockResult.IsGranted)
            {
                return CreateError(command.RequestId, operationId, command.DeviceId, RemoteErrorCodes.WriteLockDenied, lockResult.Reason);
            }

            var lease = _writeCoordinator.BeginSessionOperation(command.DeviceId, sessionId, operationId);
            if (lease.State == RemoteSessionOperationState.Conflict)
            {
                _writeCoordinator.Release(command.DeviceId);
                return CreateError(command.RequestId, operationId, command.DeviceId, RemoteErrorCodes.SessionBusy, "This session is already processing a request.");
            }

            try
            {
                if (lease.State != RemoteSessionOperationState.First)
                {
                    return await lease.Completion.ConfigureAwait(false);
                }

                try
                {
                    var result = await mutation().ConfigureAwait(false);
                    lease.Complete(result);
                    return result;
                }
                catch (Exception ex)
                {
                    lease.Fail(ex);
                    return await lease.Completion.ConfigureAwait(false);
                }
            }
            finally
            {
                _writeCoordinator.Release(command.DeviceId);
            }
        }

        private async Task<RemoteRelayEvent> DispatchSessionStopAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var result = await remoteService.StopSessionAsync(
                new CodingRemoteSessionStopRequest
                {
                    SessionId = sessionId
                },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteSessionStopResult
            {
                SessionId = sessionId,
                Stopped = true
            };

            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "sessionId", result.SessionId },
                { "workspaceId", result.WorkspaceId },
                { "stopped", result.Stopped },
                { "statusText", result.StatusText }
            });
        }

        private async Task<RemoteRelayEvent> DispatchPluginAttachmentCatalogAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var result = await remoteService.ListPluginAttachmentsAsync(sessionId, cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteAttachmentCatalogResult { SessionId = sessionId };
            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "sessionId", result.SessionId },
                { "plugins", MapAttachmentCatalogItems(result.Items) }
            });
        }

        private async Task<RemoteRelayEvent> DispatchSkillAttachmentCatalogAsync(
            RemoteRelayCommand command,
            ICodingRemoteService remoteService,
            string resultEventType,
            CancellationToken cancellationToken)
        {
            var sessionId = GetString(command.Payload, "sessionId");
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Session ID is required.");
            }

            var result = await remoteService.ListSkillAttachmentsAsync(sessionId, GetBool(command.Payload, "forceReload"), cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteAttachmentCatalogResult { SessionId = sessionId };
            return CreateEvent(command, resultEventType, new Dictionary<string, object>
            {
                { "sessionId", result.SessionId },
                { "workspaceId", result.WorkspaceId },
                { "skills", MapAttachmentCatalogItems(result.Items) }
            });
        }

        private async Task<RemoteRelayEvent> DispatchApprovalRespondAsync(
            RemoteRelayCommand command,
            CancellationToken cancellationToken)
        {
            var requestId = GetString(command.Payload, "requestId");
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Approval request ID is required.");
            }

            var decision = GetString(command.Payload, "decision");
            if (string.IsNullOrWhiteSpace(decision))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Approval decision is required.");
            }

            var result = await _codingRemoteService.RespondApprovalAsync(
                new CodingRemoteApprovalResponseRequest
                {
                    RequestId = requestId,
                    Decision = decision
                },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteApprovalResponseResult
            {
                RequestId = requestId,
                Decision = decision,
                Completed = true
            };

            return CreateEvent(command, RemoteEventTypes.ApprovalResponseResult, new Dictionary<string, object>
            {
                { "requestId", result.RequestId },
                { "decision", result.Decision },
                { "completed", result.Completed },
                { "statusText", result.StatusText }
            });
        }

        private async Task<RemoteRelayEvent> DispatchToolInputRespondAsync(RemoteRelayCommand command, CancellationToken cancellationToken)
        {
            var requestId = GetString(command.Payload, "requestId");
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Tool input request ID is required.");
            }

            var answers = GetStringArrayMap(command.Payload, "answers");
            if (answers == null)
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Tool input answers are required.");
            }

            var result = await _codingRemoteService.RespondToolInputAsync(
                new CodingRemoteToolInputRespondRequest
                {
                    RequestId = requestId,
                    Answers = answers
                },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteToolInputResponseResult
            {
                RequestId = requestId,
                Completed = true
            };

            return CreateToolInputResultEvent(command, result);
        }

        private async Task<RemoteRelayEvent> DispatchToolInputCancelAsync(RemoteRelayCommand command, CancellationToken cancellationToken)
        {
            var requestId = GetString(command.Payload, "requestId");
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Tool input request ID is required.");
            }

            var result = await _codingRemoteService.CancelToolInputAsync(
                new CodingRemoteToolInputCancelRequest { RequestId = requestId },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemoteToolInputResponseResult
            {
                RequestId = requestId,
                Completed = true,
                Canceled = true
            };

            return CreateToolInputResultEvent(command, result);
        }

        private async Task<RemoteRelayEvent> DispatchPlanExecutionRespondAsync(RemoteRelayCommand command, CancellationToken cancellationToken)
        {
            if (_planExecutionResponder == null)
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.OperationNotAvailable, "Remote plan execution response is not available.");
            }

            var requestId = GetString(command.Payload, "requestId");
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Plan execution request ID is required.");
            }

            var action = GetString(command.Payload, "action");
            if (!IsPlanExecutionAction(action))
            {
                return CreateError(command.RequestId, command.OperationId, command.DeviceId, RemoteErrorCodes.InvalidCommand, "Plan execution action is invalid.");
            }

            var result = await _planExecutionResponder.RespondPlanExecutionAsync(
                new CodingRemotePlanExecutionResponseRequest
                {
                    RequestId = requestId,
                    ThreadId = GetString(command.Payload, "threadId"),
                    Action = action
                },
                cancellationToken).ConfigureAwait(false);
            result = result ?? new CodingRemotePlanExecutionResponseResult
            {
                RequestId = requestId,
                Action = action,
                Completed = true
            };

            return CreateEvent(command, RemoteEventTypes.PlanExecutionResponseResult, new Dictionary<string, object>
            {
                { "requestId", result.RequestId },
                { "threadId", result.ThreadId },
                { "action", result.Action },
                { "completed", result.Completed },
                { "statusText", result.StatusText }
            });
        }

        private static RemoteRelayEvent CreateToolInputResultEvent(RemoteRelayCommand command, CodingRemoteToolInputResponseResult result)
        {
            return CreateEvent(command, RemoteEventTypes.ToolInputResponseResult, new Dictionary<string, object>
            {
                { "requestId", result.RequestId },
                { "completed", result.Completed },
                { "canceled", result.Canceled },
                { "statusText", result.StatusText }
            });
        }

        private static bool IsPlanExecutionAction(string action)
        {
            return string.Equals(action, "execute", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(action, "continue", StringComparison.OrdinalIgnoreCase);
        }

        private RemoteRelayEvent DispatchWriteLockAcquire(RemoteRelayCommand command)
        {
            var result = _writeCoordinator.Acquire(command.DeviceId, command.OperationId, DateTime.UtcNow);
            return CreateEvent(command, result.IsGranted ? RemoteEventTypes.WriteLockGranted : RemoteEventTypes.WriteLockDenied, new Dictionary<string, object>
            {
                { "holderDeviceId", result.HolderDeviceId },
                { "operationId", result.OperationId },
                { "expiresAtUtc", result.ExpiresAtUtc },
                { "reason", result.Reason }
            });
        }

        private RemoteRelayEvent DispatchWriteLockRelease(RemoteRelayCommand command)
        {
            var released = _writeCoordinator.Release(command.DeviceId);
            return CreateEvent(command, released ? RemoteEventTypes.WriteLockGranted : RemoteEventTypes.WriteLockDenied, new Dictionary<string, object>
            {
                { "released", released }
            });
        }

        private RemoteRelayEvent DispatchWriteLockHeartbeat(RemoteRelayCommand command)
        {
            var renewed = _writeCoordinator.Heartbeat(command.DeviceId, DateTime.UtcNow);
            return CreateEvent(command, renewed ? RemoteEventTypes.WriteLockGranted : RemoteEventTypes.WriteLockDenied, new Dictionary<string, object>
            {
                { "renewed", renewed }
            });
        }

        private static RemoteRelayEvent CreateEvent(RemoteRelayCommand command, string eventType, Dictionary<string, object> payload)
        {
            return new RemoteRelayEvent
            {
                RequestId = command.RequestId,
                OperationId = command.OperationId,
                EventType = eventType,
                Payload = payload ?? new Dictionary<string, object>()
            };
        }

        private static RemoteRelayEvent CreateError(string requestId, string operationId, string deviceId, string code, string message)
        {
            return new RemoteRelayEvent
            {
                RequestId = requestId,
                OperationId = operationId,
                EventType = RemoteEventTypes.RemoteError,
                Payload = new Dictionary<string, object>
                {
                    { "code", code },
                    { "message", message ?? string.Empty },
                    { "requestId", requestId },
                    { "operationId", operationId },
                    { "deviceId", deviceId }
                }
            };
        }

        private static IReadOnlyList<Dictionary<string, object>> MapWorkspaces(IReadOnlyList<CodingRemoteWorkspaceSummary> workspaces)
        {
            var result = new List<Dictionary<string, object>>();
            if (workspaces == null)
            {
                return result;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace == null)
                {
                    continue;
                }

                result.Add(MapWorkspace(workspace));
            }

            return result;
        }

        private static Dictionary<string, object> MapWorkspace(CodingRemoteWorkspaceSummary workspace)
        {
            return new Dictionary<string, object>
            {
                { "id", workspace.WorkspaceId },
                { "name", workspace.DisplayName },
                { "path", workspace.Path },
                { "updatedAt", FormatUtc(workspace.LastUsedAt) },
                { "kind", workspace.Kind }
            };
        }

        private static IReadOnlyList<Dictionary<string, object>> MapSessions(IReadOnlyList<CodingRemoteSessionSummary> sessions)
        {
            var result = new List<Dictionary<string, object>>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session == null)
                {
                    continue;
                }

                result.Add(MapSession(session));
            }

            return result;
        }

        private static Dictionary<string, object> MapSession(CodingRemoteSessionSummary session)
        {
            return new Dictionary<string, object>
            {
                { "id", session.SessionId },
                { "workspaceId", session.WorkspaceId },
                { "title", session.Title },
                { "createdAt", FormatUtc(session.CreatedAt) },
                { "updatedAt", FormatUtc(session.UpdatedAt) },
                { "branchKind", session.BranchKind },
                { "subAgentCount", session.SubAgentCount },
                { "subAgents", MapSessions(session.SubAgents) },
                { "subAgentName", session.SubAgentName },
                { "subAgentRole", session.SubAgentRole },
                { "preview", session.LastMessagePreview },
                { "state", session.State }
            };
        }

        private static Dictionary<string, object> MapSessionThreadOperationResult(CodingRemoteSessionThreadOperationResult result)
        {
            return new Dictionary<string, object>
            {
                { "sessionId", result.SessionId },
                { "workspaceId", result.WorkspaceId },
                { "action", result.Action },
                { "mode", result.Mode },
                { "turnId", result.TurnId },
                { "accepted", result.Accepted },
                { "createdSession", result.CreatedSession == null ? null : MapSession(result.CreatedSession) },
                { "switchToCreatedSession", result.SwitchToCreatedSession },
                { "statusText", result.StatusText }
            };
        }

        private static Dictionary<string, object> MapSessionSettings(CodingRemoteSessionSettings settings)
        {
            return new Dictionary<string, object>
            {
                { "sessionId", settings.SessionId },
                { "workspaceId", settings.WorkspaceId },
                { "modelId", settings.ModelId },
                { "reasoningEffort", settings.ReasoningEffort },
                { "approvalMode", settings.ApprovalMode },
                { "isPlanModeEnabled", settings.IsPlanModeEnabled },
                { "effectiveModelId", settings.EffectiveModelId },
                { "effectiveReasoningEffort", settings.EffectiveReasoningEffort },
                { "effectiveApprovalMode", settings.EffectiveApprovalMode },
                { "defaultModelId", settings.DefaultModelId },
                { "defaultReasoningEffort", settings.DefaultReasoningEffort },
                { "defaultApprovalMode", settings.DefaultApprovalMode },
                { "availableModels", MapModelOptions(settings.AvailableModels) }
            };
        }

        private static IReadOnlyList<Dictionary<string, object>> MapModelOptions(IReadOnlyList<CodingRemoteModelOption> models)
        {
            var result = new List<Dictionary<string, object>>();
            if (models == null)
            {
                return result;
            }

            foreach (var model in models)
            {
                if (model == null)
                {
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "modelId", model.ModelId },
                    { "displayName", model.DisplayName },
                    { "defaultReasoningEffort", model.DefaultReasoningEffort },
                    { "reasoningEfforts", model.ReasoningEfforts ?? new List<string>() }
                });
            }

            return result;
        }

        private static IReadOnlyList<Dictionary<string, object>> MapMessages(IReadOnlyList<CodingRemoteMessageItem> messages)
        {
            var result = new List<Dictionary<string, object>>();
            if (messages == null)
            {
                return result;
            }

            foreach (var message in messages)
            {
                if (message == null)
                {
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "id", message.MessageId },
                    { "sessionId", message.SessionId },
                    { "role", message.Role },
                    { "content", message.Text },
                    { "contentTruncated", message.ContentTruncated },
                    { "contentUtf8ByteCount", message.ContentUtf8ByteCount },
                    { "contentVersion", message.ContentVersion },
                    { "createdAt", FormatUtc(message.CreatedAt) },
                    { "status", message.Status },
                    { "threadId", message.ThreadId },
                    { "turnId", message.TurnId },
                    { "turnIndex", message.TurnIndex },
                    { "turnCount", message.TurnCount },
                    { "turnStartedAt", FormatUtc(message.TurnStartedAt) },
                    { "turnPreview", message.TurnPreview },
                    { "actions", MapMessageActions(message.Actions) },
                    { "operationPreview", MapMessageOperationPreview(message.OperationPreview) }
                });
            }

            return result;
        }

        private static IReadOnlyList<Dictionary<string, object>> MapAttachmentCatalogItems(IReadOnlyList<CodingRemoteAttachmentCatalogItem> items)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var item in items ?? new List<CodingRemoteAttachmentCatalogItem>())
            {
                if (item == null)
                {
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "kind", item.Kind },
                    { "type", item.Type },
                    { "name", item.Name },
                    { "displayName", item.DisplayName },
                    { "path", item.Path },
                    { "enabled", item.Enabled },
                    { "installed", item.Installed },
                    { "sourceText", item.SourceText },
                    { "scope", item.Scope },
                    { "summary", item.Summary }
                });
            }

            return result;
        }

        private static Dictionary<string, object> MapMessageActions(CodingRemoteMessageActions actions)
        {
            actions = actions ?? new CodingRemoteMessageActions();
            return new Dictionary<string, object>
            {
                { "copy", actions.Copy },
                { "forkFromTurn", actions.ForkFromTurn },
            };
        }

        private static Dictionary<string, object> MapMessageOperationPreview(CodingRemoteMessageOperationPreview preview)
        {
            preview = preview ?? new CodingRemoteMessageOperationPreview();
            return new Dictionary<string, object>
            {
                { "trimTurnsAfterTurn", preview.TrimTurnsAfterTurn },
            };
        }

        private static string FormatUtc(DateTime value)
        {
            return value == default(DateTime)
                ? null
                : value.ToUniversalTime().ToString("o");
        }

        private static string GetString(IDictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            return value as string ?? Convert.ToString(value);
        }

        private static List<CodingRemoteInputItem> BuildInputItems(IDictionary<string, object> payload, out string error)
        {
            error = null;
            var result = new List<CodingRemoteInputItem>();
            object raw;
            if (payload == null || !payload.TryGetValue("inputItems", out raw) || raw == null)
            {
                return result;
            }

            var values = raw as IEnumerable<object>;
            if (values == null)
            {
                error = "Input items must be an array.";
                return result;
            }

            foreach (var value in values)
            {
                var itemPayload = value as IDictionary<string, object>;
                if (itemPayload == null)
                {
                    error = "Input item must be an object.";
                    return result;
                }

                var type = (GetString(itemPayload, "type") ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(type))
                {
                    error = "Input item type is required.";
                    return result;
                }

                if (type != "mention" && type != "skill" && type != "text")
                {
                    error = "Unsupported input item type.";
                    return result;
                }

                var path = GetString(itemPayload, "path");
                var itemText = GetString(itemPayload, "text");
                if ((type == "mention" || type == "skill") && string.IsNullOrWhiteSpace(path))
                {
                    error = "Input item path is required.";
                    return result;
                }

                if (type == "text" && string.IsNullOrWhiteSpace(itemText))
                {
                    error = "Text input item text is required.";
                    return result;
                }

                result.Add(new CodingRemoteInputItem
                {
                    Type = type,
                    Name = GetString(itemPayload, "name"),
                    DisplayName = GetString(itemPayload, "displayName"),
                    Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
                    Text = string.IsNullOrWhiteSpace(itemText) ? null : itemText.Trim()
                });
            }

            return result;
        }

        private static bool HasPayloadKey(IDictionary<string, object> payload, string key)
        {
            return payload != null && payload.ContainsKey(key);
        }

        private static bool? GetNullableBool(IDictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            var text = value as string ?? Convert.ToString(value);
            if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "no", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return null;
        }

        private static int GetInt(IDictionary<string, object> payload, string key, int defaultValue)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return defaultValue;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch (FormatException)
            {
                return defaultValue;
            }
            catch (InvalidCastException)
            {
                return defaultValue;
            }
        }

        private static bool GetBool(IDictionary<string, object> payload, string key)
        {
            object value;
            if (payload == null || !payload.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            var text = value as string ?? Convert.ToString(value);
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "1", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private static Dictionary<string, string[]> GetStringArrayMap(IDictionary<string, object> payload, string key)
        {
            object raw;
            if (payload == null || !payload.TryGetValue(key, out raw))
            {
                return null;
            }

            var source = raw as IDictionary<string, object>;
            if (source == null)
            {
                return null;
            }

            var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                if (pair.Value is string)
                {
                    result[pair.Key] = new[] { (string)pair.Value };
                    continue;
                }

                var enumerable = pair.Value as System.Collections.IEnumerable;
                if (enumerable == null)
                {
                    result[pair.Key] = pair.Value == null ? new string[0] : new[] { Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) };
                    continue;
                }

                var values = new List<string>();
                foreach (var item in enumerable)
                {
                    if (item != null)
                    {
                        values.Add(Convert.ToString(item, System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                result[pair.Key] = values.ToArray();
            }

            return result;
        }
    }
}
