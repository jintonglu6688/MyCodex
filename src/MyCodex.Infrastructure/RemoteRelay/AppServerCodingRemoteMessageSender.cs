using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteMessageSender : ICodingRemoteMessageSender
    {
        private readonly IAppStateStore _appStateStore;
        private readonly SessionThreadService _sessionThreadService;
        private readonly Func<ChatSession, ApprovalMode> _approvalModeResolver;
        private readonly Func<ChatSession, string> _modelIdResolver;
        private readonly Func<ChatSession, string> _reasoningEffortResolver;
        private readonly Func<ChatSession, WorkspaceInfo, string, string, AppServerCollaborationMode> _collaborationModeResolver;
        private readonly Func<ChatSession, WorkspaceInfo, AppServerThreadToolContext> _toolContextResolver;
        private readonly IRemoteHostMutationSink _mutationSink;
        private readonly AppInteractionMode _interactionMode;
        private readonly WorkspaceKind _workspaceKind;
        private readonly Func<CodingRemoteActiveTurnDispatchContext, CancellationToken, Task<CodingRemoteMessageSendResult>> _activeTurnDispatcher;

        public AppServerCodingRemoteMessageSender(
            IAppStateStore appStateStore,
            SessionThreadService sessionThreadService,
            Func<ChatSession, ApprovalMode> approvalModeResolver,
            Func<ChatSession, string> modelIdResolver,
            Func<ChatSession, string> reasoningEffortResolver,
            Func<ChatSession, WorkspaceInfo, string, string, AppServerCollaborationMode> collaborationModeResolver,
            Func<ChatSession, WorkspaceInfo, AppServerThreadToolContext> toolContextResolver)
            : this(
                appStateStore,
                sessionThreadService,
                approvalModeResolver,
                modelIdResolver,
                reasoningEffortResolver,
                collaborationModeResolver,
                toolContextResolver,
                null)
        {
        }

        public AppServerCodingRemoteMessageSender(
            IAppStateStore appStateStore,
            SessionThreadService sessionThreadService,
            Func<ChatSession, ApprovalMode> approvalModeResolver,
            Func<ChatSession, string> modelIdResolver,
            Func<ChatSession, string> reasoningEffortResolver,
            Func<ChatSession, WorkspaceInfo, string, string, AppServerCollaborationMode> collaborationModeResolver,
            Func<ChatSession, WorkspaceInfo, AppServerThreadToolContext> toolContextResolver,
            IRemoteHostMutationSink mutationSink)
            : this(
                appStateStore,
                sessionThreadService,
                approvalModeResolver,
                modelIdResolver,
                reasoningEffortResolver,
                collaborationModeResolver,
                toolContextResolver,
                mutationSink,
                AppInteractionMode.Coding)
        {
        }

        public AppServerCodingRemoteMessageSender(
            IAppStateStore appStateStore,
            SessionThreadService sessionThreadService,
            Func<ChatSession, ApprovalMode> approvalModeResolver,
            Func<ChatSession, string> modelIdResolver,
            Func<ChatSession, string> reasoningEffortResolver,
            Func<ChatSession, WorkspaceInfo, string, string, AppServerCollaborationMode> collaborationModeResolver,
            Func<ChatSession, WorkspaceInfo, AppServerThreadToolContext> toolContextResolver,
            IRemoteHostMutationSink mutationSink,
            AppInteractionMode interactionMode,
            Func<CodingRemoteActiveTurnDispatchContext, CancellationToken, Task<CodingRemoteMessageSendResult>> activeTurnDispatcher = null)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _sessionThreadService = sessionThreadService ?? throw new ArgumentNullException(nameof(sessionThreadService));
            _approvalModeResolver = approvalModeResolver ?? throw new ArgumentNullException(nameof(approvalModeResolver));
            _modelIdResolver = modelIdResolver ?? throw new ArgumentNullException(nameof(modelIdResolver));
            _reasoningEffortResolver = reasoningEffortResolver ?? throw new ArgumentNullException(nameof(reasoningEffortResolver));
            _collaborationModeResolver = collaborationModeResolver;
            _toolContextResolver = toolContextResolver;
            _mutationSink = mutationSink;
            _interactionMode = interactionMode;
            _workspaceKind = interactionMode == AppInteractionMode.Office ? WorkspaceKind.Office : WorkspaceKind.Coding;
            _activeTurnDispatcher = activeTurnDispatcher;
        }

        public async Task<CodingRemoteMessageSendResult> SendMessageAsync(CodingRemoteMessageSendRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteMessageSendRequest();
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                throw new InvalidOperationException("Session ID is required.");
            }

            var text = (request.Text ?? string.Empty).Trim();
            var inputItems = BuildInputItems(request, text);
            if (inputItems.Count == 0)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Message text or input items are required.");
            }

            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            var session = FindSession(snapshot, request.SessionId);
            if (session == null)
            {
                throw new InvalidOperationException("Session was not found.");
            }

            var workspace = FindWorkspace(snapshot, session.WorkspaceId);
            if (workspace == null)
            {
                throw new InvalidOperationException("Workspace was not found.");
            }

            var approvalMode = _approvalModeResolver(session);
            var modelId = _modelIdResolver(session);
            var reasoningEffort = _reasoningEffortResolver(session);
            var toolContext = _toolContextResolver == null ? null : _toolContextResolver(session, workspace);
            var recreatedThread = false;

            if (string.IsNullOrWhiteSpace(session.ThreadId))
            {
                await _sessionThreadService.RecreateThreadAsync(session, workspace, approvalMode, modelId, reasoningEffort, toolContext).ConfigureAwait(false);
                recreatedThread = true;
            }

            var collaborationMode = _collaborationModeResolver == null
                ? null
                : _collaborationModeResolver(session, workspace, modelId, reasoningEffort);

            if (_activeTurnDispatcher != null)
            {
                var activeTurnResult = await _activeTurnDispatcher(
                    new CodingRemoteActiveTurnDispatchContext
                    {
                        SessionId = session.Id,
                        OperationId = request.OperationId,
                        InputItems = inputItems,
                        ApprovalMode = approvalMode,
                        ModelId = modelId,
                        ReasoningEffort = reasoningEffort,
                        CollaborationMode = collaborationMode,
                        ToolContext = toolContext,
                        DispatchText = text,
                        PreviewText = BuildPreview(text, request.InputItems)
                    },
                    cancellationToken).ConfigureAwait(false);
                if (activeTurnResult != null)
                {
                    return activeTurnResult;
                }
            }

            var sendResult = await _sessionThreadService.SendUserMessageAsync(
                session,
                workspace,
                inputItems,
                approvalMode,
                modelId,
                reasoningEffort,
                collaborationMode,
                toolContext).ConfigureAwait(false);

            session.Preview = BuildPreview(text, request.InputItems);
            session.LastActiveAt = DateTime.Now;
            _appStateStore.Save(snapshot);
            if (recreatedThread || (sendResult != null && sendResult.RecreatedThreadAfterMissingThread))
            {
                await PublishSessionThreadUpdatedMutationAsync(session, cancellationToken).ConfigureAwait(false);
            }

            return new CodingRemoteMessageSendResult
            {
                SessionId = session.Id,
                OperationId = request.OperationId,
                Accepted = true,
                RecreatedThread = recreatedThread || (sendResult != null && sendResult.RecreatedThreadAfterMissingThread),
                DispatchMode = "start"
            };
        }

        private static List<AppServerUserInputItem> BuildInputItems(CodingRemoteMessageSendRequest request, string text)
        {
            var result = new List<AppServerUserInputItem>();
            foreach (var item in request.InputItems ?? new List<CodingRemoteInputItem>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Type))
                {
                    continue;
                }

                var type = item.Type.Trim().ToLowerInvariant();
                if (type == "mention" || type == "skill")
                {
                    if (string.IsNullOrWhiteSpace(item.Path))
                    {
                        throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Input item path is required.");
                    }

                    result.Add(new AppServerUserInputItem
                    {
                        Type = type,
                        Name = string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim(),
                        Path = item.Path.Trim()
                    });
                    continue;
                }

                if (type == "text")
                {
                    var itemText = (item.Text ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(itemText))
                    {
                        result.Add(new AppServerUserInputItem
                        {
                            Type = "text",
                            Text = itemText
                        });
                    }

                    continue;
                }

                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, "Unsupported input item type.");
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                result.Add(new AppServerUserInputItem
                {
                    Type = "text",
                    Text = text
                });
            }

            return result;
        }

        private static string BuildPreview(string text, IReadOnlyCollection<CodingRemoteInputItem> inputItems)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var parts = new List<string>();
            foreach (var item in inputItems ?? new List<CodingRemoteInputItem>())
            {
                if (item == null)
                {
                    continue;
                }

                var label = GetPreviewLabel(item.Type);
                if (label == null)
                {
                    continue;
                }

                var name = string.IsNullOrWhiteSpace(item.DisplayName) ? item.Name : item.DisplayName;
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = item.Path;
                }

                if (!string.IsNullOrWhiteSpace(name))
                {
                    parts.Add("[" + label + ": " + name.Trim() + "]");
                }
            }

            return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
        }

        private static string GetPreviewLabel(string type)
        {
            if (string.Equals(type, "mention", StringComparison.OrdinalIgnoreCase))
            {
                return "插件";
            }

            if (string.Equals(type, "skill", StringComparison.OrdinalIgnoreCase))
            {
                return "技能";
            }

            return null;
        }

        private async Task PublishSessionThreadUpdatedMutationAsync(ChatSession session, CancellationToken cancellationToken)
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
                        MutationType = RemoteHostMutationTypes.SessionThreadUpdated,
                        WorkspaceId = session.WorkspaceId,
                        SessionId = session.Id,
                        StatusText = "Session thread updated."
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The remote message was already accepted. A local UI refresh failure must not
                // turn a successful mobile send into a failed command response.
            }
        }

        private ChatSession FindSession(AppStateSnapshot snapshot, string sessionId)
        {
            return (snapshot.Sessions ?? new List<ChatSession>())
                .FirstOrDefault(session =>
                    session != null &&
                    session.Mode == _interactionMode &&
                    string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        private WorkspaceInfo FindWorkspace(AppStateSnapshot snapshot, string workspaceId)
        {
            return (snapshot.RecentWorkspaces ?? new List<WorkspaceInfo>())
                .FirstOrDefault(workspace =>
                    workspace != null &&
                    workspace.Kind == _workspaceKind &&
                    string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(workspace.RootPath));
        }
    }
}
