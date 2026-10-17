using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.InternalTools;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerDynamicRequestCoordinator
    {
        private readonly object _syncRoot;
        private readonly AppServerPendingRequestRegistry _pendingRequestRegistry;
        private readonly Func<IInternalToolRegistry> _getInternalToolRegistry;
        private readonly Func<string, ApprovalMode> _getThreadApprovalMode;
        private readonly Func<string, InternalToolExecutionContext> _getInternalToolContext;
        private readonly Func<long> _getConnectionEpoch;
        private readonly Func<object, object, long, Task> _sendServerRequestResponseAsync;
        private readonly Func<object, int, string, long, Task> _sendErrorResponseAsync;
        private readonly Action<AppServerEventArgs> _raiseEvent;
        private readonly Func<string, string> _mapServerPathToClient;
        private readonly Func<ApprovalMode, AppServerPendingApprovalRequest, CustomApprovalPolicyEvaluation> _evaluateCustomApproval;

        public AppServerDynamicRequestCoordinator(
            object syncRoot,
            AppServerPendingRequestRegistry pendingRequestRegistry,
            Func<IInternalToolRegistry> getInternalToolRegistry,
            Func<string, ApprovalMode> getThreadApprovalMode,
            Func<string, InternalToolExecutionContext> getInternalToolContext,
            Func<long> getConnectionEpoch,
            Func<object, object, long, Task> sendServerRequestResponseAsync,
            Func<object, int, string, long, Task> sendErrorResponseAsync,
            Action<AppServerEventArgs> raiseEvent,
            Func<string, string> mapServerPathToClient,
            Func<ApprovalMode, AppServerPendingApprovalRequest, CustomApprovalPolicyEvaluation> evaluateCustomApproval = null)
        {
            _syncRoot = syncRoot ?? throw new ArgumentNullException(nameof(syncRoot));
            _pendingRequestRegistry = pendingRequestRegistry ?? throw new ArgumentNullException(nameof(pendingRequestRegistry));
            _getInternalToolRegistry = getInternalToolRegistry ?? throw new ArgumentNullException(nameof(getInternalToolRegistry));
            _getThreadApprovalMode = getThreadApprovalMode ?? throw new ArgumentNullException(nameof(getThreadApprovalMode));
            _getInternalToolContext = getInternalToolContext ?? throw new ArgumentNullException(nameof(getInternalToolContext));
            _getConnectionEpoch = getConnectionEpoch ?? throw new ArgumentNullException(nameof(getConnectionEpoch));
            _sendServerRequestResponseAsync = sendServerRequestResponseAsync ?? throw new ArgumentNullException(nameof(sendServerRequestResponseAsync));
            _sendErrorResponseAsync = sendErrorResponseAsync ?? throw new ArgumentNullException(nameof(sendErrorResponseAsync));
            _raiseEvent = raiseEvent ?? throw new ArgumentNullException(nameof(raiseEvent));
            _mapServerPathToClient = mapServerPathToClient ?? throw new ArgumentNullException(nameof(mapServerPathToClient));
            _evaluateCustomApproval = evaluateCustomApproval ?? ((mode, request) => new CustomApprovalPolicyEvaluation());
        }

        public void HandleServerRequest(IDictionary<string, object> message)
        {
            HandleServerRequest(message, _getConnectionEpoch());
        }

        public void HandleServerRequest(IDictionary<string, object> message, long connectionEpoch)
        {
            if (!TryHandleServerRequest(message, connectionEpoch))
            {
                var method = GetString(message, "method");
                var rawRequestId = GetRawRequestId(message);
                var parameters = GetDictionary(message, "params");
                _sendErrorResponseAsync(
                    rawRequestId,
                    -32601,
                    L("AppServerClient.Error.ServerRequestNotImplemented", "This server request is not implemented in the current version."),
                    connectionEpoch)
                    .Forget(L("AppServerClient.Operation.ReplyUnimplementedServerRequest", "Reply to unimplemented server request"));
                _raiseEvent(AppServerNotificationFactory.CreateUnhandledServerRequest(method, parameters));
            }
        }

        public bool TryHandleServerRequest(IDictionary<string, object> message)
        {
            return TryHandleServerRequest(message, _getConnectionEpoch());
        }

        private bool TryHandleServerRequest(IDictionary<string, object> message, long connectionEpoch)
        {
            var method = GetString(message, "method");
            var rawRequestId = GetRawRequestId(message);
            var requestId = NormalizeRequestId(rawRequestId);
            var parameters = GetDictionary(message, "params");

            switch (method)
            {
                case "item/commandExecution/requestApproval":
                    RegisterApprovalRequest(rawRequestId, requestId, AppServerApprovalRequestKind.Command, parameters, connectionEpoch);
                    return true;
                case "item/fileChange/requestApproval":
                    RegisterApprovalRequest(rawRequestId, requestId, AppServerApprovalRequestKind.FileChange, parameters, connectionEpoch);
                    return true;
                case "item/tool/requestUserInput":
                    RegisterToolUserInputRequest(rawRequestId, requestId, parameters, connectionEpoch);
                    return true;
                case "item/tool/call":
                    HandleDynamicToolCallRequest(rawRequestId, requestId, parameters, connectionEpoch);
                    return true;
                case "mcpServer/elicitation/request":
                    RegisterMcpElicitationRequest(rawRequestId, requestId, parameters, connectionEpoch);
                    return true;
                case "item/permissions/requestApproval":
                    RegisterApprovalRequest(rawRequestId, requestId, AppServerApprovalRequestKind.Permissions, parameters, connectionEpoch);
                    return true;
                default:
                    return false;
            }
        }

        public async Task ResolveApprovalRequestAsync(string requestId, AppServerApprovalDecision decision)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ApprovalRequestIdRequired", "Approval request ID cannot be empty."), nameof(requestId));
            }

            AppServerPendingApprovalRequest pendingRequest;
            long connectionEpoch;
            lock (_syncRoot)
            {
                if (!_pendingRequestRegistry.TryTakeApproval(requestId, out pendingRequest, out connectionEpoch))
                {
                    throw new InvalidOperationException(L("AppServerClient.Error.PendingApprovalRequestNotFound", "Pending approval request was not found."));
                }
            }

            object result;
            switch (pendingRequest.Kind)
            {
                case AppServerApprovalRequestKind.Command:
                    result = new Dictionary<string, object>
                    {
                        { "decision", AppServerInteractionRequestMapper.MapCommandApprovalDecision(decision) }
                    };
                    break;
                case AppServerApprovalRequestKind.FileChange:
                    result = new Dictionary<string, object>
                    {
                        { "decision", AppServerInteractionRequestMapper.MapFileChangeApprovalDecision(decision) }
                    };
                    break;
                case AppServerApprovalRequestKind.Permissions:
                    if (decision == AppServerApprovalDecision.Decline)
                    {
                        await _sendErrorResponseAsync(
                            pendingRequest.RawRequestId,
                            4001,
                            L("AppServerClient.Error.PermissionRequestRejectedByUser", "The user rejected the permission request."),
                            connectionEpoch)
                            .ConfigureAwait(false);
                        _raiseEvent(new AppServerEventArgs(
                            "approval_resolved",
                            L("AppServerClient.Approval.PermissionRequestRejected", "Permission request rejected."),
                            pendingRequest.ThreadId,
                            pendingRequest.TurnId,
                            pendingRequest.ItemId,
                            requestId,
                            reason: pendingRequest.Reason,
                            details: pendingRequest.Details,
                            approvalKind: pendingRequest.Kind,
                            canApproveForSession: pendingRequest.CanApproveForSession,
                            approvalClassification: pendingRequest.Classification));
                        return;
                    }

                    result = AppServerInteractionRequestMapper.BuildPermissionsApprovalResponse(pendingRequest.Parameters, decision);
                    break;
                default:
                    throw new InvalidOperationException(L("AppServerClient.Error.UnsupportedApprovalRequestType", "Unsupported approval request type."));
            }

            await _sendServerRequestResponseAsync(pendingRequest.RawRequestId, result, connectionEpoch).ConfigureAwait(false);
            _raiseEvent(new AppServerEventArgs(
                "approval_resolved",
                AppServerInteractionRequestMapper.BuildApprovalResolvedMessage(pendingRequest, decision),
                pendingRequest.ThreadId,
                pendingRequest.TurnId,
                pendingRequest.ItemId,
                requestId,
                pendingRequest.CommandText,
                pendingRequest.WorkingDirectory,
                pendingRequest.Reason,
                pendingRequest.Details,
                pendingRequest.Kind,
                pendingRequest.CanApproveForSession,
                approvalClassification: pendingRequest.Classification));
        }

        public async Task ResolveToolUserInputRequestAsync(string requestId, IDictionary<string, string[]> answers)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ToolInputRequestIdRequired", "Tool input request ID cannot be empty."), nameof(requestId));
            }

            AppServerPendingToolUserInputRequest pendingRequest;
            long connectionEpoch;
            lock (_syncRoot)
            {
                if (!_pendingRequestRegistry.TryTakeToolInput(requestId, out pendingRequest, out connectionEpoch))
                {
                    throw new InvalidOperationException(L("AppServerClient.Error.PendingToolInputRequestNotFound", "Pending tool input request was not found."));
                }
            }

            await _sendServerRequestResponseAsync(
                pendingRequest.RawRequestId,
                AppServerInteractionRequestMapper.BuildToolUserInputResponse(pendingRequest, answers),
                connectionEpoch)
                .ConfigureAwait(false);

            _raiseEvent(new AppServerEventArgs(
                "tool_user_input_resolved",
                L("AppServerClient.ToolInput.Submitted", "Tool input submitted."),
                pendingRequest.ThreadId,
                pendingRequest.TurnId,
                pendingRequest.ItemId,
                requestId));
        }

        public async Task CancelToolUserInputRequestAsync(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ToolInputRequestIdRequired", "Tool input request ID cannot be empty."), nameof(requestId));
            }

            AppServerPendingToolUserInputRequest pendingRequest;
            long connectionEpoch;
            lock (_syncRoot)
            {
                if (!_pendingRequestRegistry.TryTakeToolInput(requestId, out pendingRequest, out connectionEpoch))
                {
                    throw new InvalidOperationException(L("AppServerClient.Error.PendingToolInputRequestNotFound", "Pending tool input request was not found."));
                }
            }

            if (pendingRequest.IsMcpElicitation)
            {
                await _sendServerRequestResponseAsync(
                    pendingRequest.RawRequestId,
                    AppServerInteractionRequestMapper.BuildMcpElicitationCancelResponse(),
                    connectionEpoch)
                    .ConfigureAwait(false);
            }
            else
            {
                await _sendErrorResponseAsync(
                    pendingRequest.RawRequestId,
                    4002,
                    "user cancelled MCP tool call",
                    connectionEpoch).ConfigureAwait(false);
            }

            _raiseEvent(new AppServerEventArgs(
                "tool_user_input_resolved",
                L("AppServerClient.ToolInput.Canceled", "Tool input canceled."),
                pendingRequest.ThreadId,
                pendingRequest.TurnId,
                pendingRequest.ItemId,
                requestId));
        }

        public void ClearResolvedInteractionRequest(string requestId)
        {
            RemovePendingApprovalRequest(requestId);
            RemovePendingToolUserInputRequest(requestId);
        }

        private void HandleDynamicToolCallRequest(
            object rawRequestId,
            string requestId,
            IDictionary<string, object> parameters,
            long connectionEpoch)
        {
            HandleDynamicToolCallRequestAsync(rawRequestId, parameters, connectionEpoch)
                .Forget(L("AppServerClient.Operation.HandleDynamicToolCall", "Handle internal tool call"));
        }

        private async Task HandleDynamicToolCallRequestAsync(
            object rawRequestId,
            IDictionary<string, object> parameters,
            long connectionEpoch)
        {
            InternalToolResult result;
            try
            {
                result = await ExecuteDynamicToolCallAsync(parameters).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                result = InternalToolResult.FromText(false, ex.Message);
            }

            await _sendServerRequestResponseAsync(
                rawRequestId,
                AppServerDynamicToolResponseMapper.ToWire(result),
                connectionEpoch).ConfigureAwait(false);
        }

        private async Task<InternalToolResult> ExecuteDynamicToolCallAsync(IDictionary<string, object> parameters)
        {
            var request = AppServerDynamicToolCallMapper.FromParams(parameters);
            InternalToolExecutionContext context = null;
            IInternalToolExecutor executor = null;
            lock (_syncRoot)
            {
                if (!string.IsNullOrWhiteSpace(request.ThreadId))
                {
                    context = _getInternalToolContext(request.ThreadId);
                }

                var registry = _getInternalToolRegistry();
                if (registry != null)
                {
                    registry.TryGetExecutor(request.Namespace, request.Name, out executor);
                }
            }

            if (string.IsNullOrWhiteSpace(request.ThreadId) || context == null)
            {
                return InternalToolResult.FromText(false, "Internal tool context is unavailable for this thread.");
            }

            if (executor == null)
            {
                return InternalToolResult.FromText(
                    false,
                    "Internal tool is not registered: " +
                    (request.Namespace ?? string.Empty) +
                    "/" +
                    (request.Name ?? string.Empty));
            }

            try
            {
                _raiseEvent(CreateInternalToolEvent("internal_tool_running", request, null));
                var result = await executor.ExecuteAsync(request, context).ConfigureAwait(false);
                _raiseEvent(CreateInternalToolEvent(null, request, result));
                return result;
            }
            catch (Exception ex)
            {
                var result = InternalToolResult.FromText(false, ex.Message);
                _raiseEvent(CreateInternalToolEvent(null, request, result));
                return result;
            }
        }

        private AppServerEventArgs CreateInternalToolEvent(string eventType, InternalToolRequest request, InternalToolResult result)
        {
            var toolName = BuildInternalToolDisplayName(request);
            string content;
            if (string.Equals(eventType, "internal_tool_running", StringComparison.Ordinal))
            {
                content = "Running internal tool: " + toolName;
            }
            else if (result != null && result.Success)
            {
                content = "Internal tool completed: " + toolName;
                eventType = "internal_tool_completed";
            }
            else
            {
                content = "Internal tool failed: " + toolName;
                eventType = "internal_tool_failed";
            }

            return new AppServerEventArgs(
                eventType,
                content,
                threadId: request != null ? request.ThreadId : null,
                turnId: request != null ? request.TurnId : null,
                requestId: request != null ? request.CallId : null,
                mcpToolName: toolName);
        }

        private static string BuildInternalToolDisplayName(InternalToolRequest request)
        {
            if (request == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(request.Namespace)
                ? request.Name
                : request.Namespace + "/" + request.Name;
        }

        public void HandleCommandApprovalRequest(object rawRequestId, string requestId, IDictionary<string, object> parameters)
        {
            RegisterApprovalRequest(
                rawRequestId,
                requestId,
                AppServerApprovalRequestKind.Command,
                parameters,
                _getConnectionEpoch());
        }

        private void RegisterApprovalRequest(
            object rawRequestId,
            string requestId,
            AppServerApprovalRequestKind kind,
            IDictionary<string, object> parameters,
            long connectionEpoch)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            var pendingRequest = AppServerInteractionRequestMapper.BuildPendingApprovalRequest(
                rawRequestId,
                requestId,
                kind,
                parameters,
                _mapServerPathToClient);

            if (TryAutoApproveFullAccessRequest(requestId, pendingRequest, connectionEpoch) ||
                TryAutoApproveCustomApprovalRequest(requestId, pendingRequest, connectionEpoch))
            {
                return;
            }

            lock (_syncRoot)
            {
                _pendingRequestRegistry.RegisterApproval(requestId, pendingRequest, connectionEpoch);
            }

            _raiseEvent(new AppServerEventArgs(
                "approval_requested",
                pendingRequest.Content,
                pendingRequest.ThreadId,
                pendingRequest.TurnId,
                pendingRequest.ItemId,
                requestId,
                pendingRequest.CommandText,
                pendingRequest.WorkingDirectory,
                pendingRequest.Reason,
                pendingRequest.Details,
                kind,
                pendingRequest.CanApproveForSession,
                approvalClassification: pendingRequest.Classification));
        }

        private bool TryAutoApproveFullAccessRequest(
            string requestId,
            AppServerPendingApprovalRequest request,
            long connectionEpoch)
        {
            var mode = _getThreadApprovalMode(request.ThreadId);
            if (string.IsNullOrWhiteSpace(request.ThreadId) ||
                (mode != ApprovalMode.FullAccess && mode != ApprovalMode.FullAccessAutoApprove))
            {
                return false;
            }

            switch (request.Kind)
            {
                case AppServerApprovalRequestKind.Command:
                case AppServerApprovalRequestKind.FileChange:
                    if (!AppServerInteractionRequestMapper.HasAvailableDecision(request.Parameters, "accept"))
                    {
                        return false;
                    }
                    break;
                case AppServerApprovalRequestKind.Permissions:
                    break;
                default:
                    return false;
            }

            AutoApproveApprovalRequestAsync(requestId, request,
                L("AppServerClient.Approval.Source.FullAccessAutoApprove", "automatically approved by Full Access setting"),
                connectionEpoch).Forget("Auto-approve by Full Access setting");
            return true;
        }

        private bool TryAutoApproveCustomApprovalRequest(
            string requestId,
            AppServerPendingApprovalRequest pendingRequest,
            long connectionEpoch)
        {
            if (pendingRequest == null)
            {
                return false;
            }

            var approvalMode = _getThreadApprovalMode(pendingRequest.ThreadId);
            var evaluation = _evaluateCustomApproval(approvalMode, pendingRequest);
            if (evaluation == null || !evaluation.ShouldAutoApprove)
            {
                return false;
            }

            AutoApproveApprovalRequestAsync(
                requestId,
                pendingRequest,
                ResolveCustomApprovalSource(evaluation),
                connectionEpoch)
                .Forget("Auto-approve by custom approval policy");
            return true;
        }

        private string ResolveCustomApprovalSource(CustomApprovalPolicyEvaluation evaluation)
        {
            if (evaluation != null && evaluation.Source == CustomApprovalPolicySource.CommandPrefix)
            {
                return L("AppServerClient.Approval.Source.CustomCommandPrefix", "custom command prefix");
            }

            return L("AppServerClient.Approval.Source.CustomPermission", "custom permission");
        }

        private async Task AutoApproveApprovalRequestAsync(
            string requestId,
            AppServerPendingApprovalRequest pendingRequest,
            string source,
            long connectionEpoch)
        {
            await _sendServerRequestResponseAsync(
                pendingRequest.RawRequestId,
                AppServerInteractionRequestMapper.BuildApprovalResponse(
                    pendingRequest,
                    AppServerApprovalDecision.AllowOnce),
                connectionEpoch)
                .ConfigureAwait(false);

            _raiseEvent(new AppServerEventArgs(
                "approval_resolved",
                AppServerInteractionRequestMapper.BuildApprovalResolvedMessage(
                    pendingRequest,
                    AppServerApprovalDecision.AllowOnce,
                    source),
                pendingRequest.ThreadId,
                pendingRequest.TurnId,
                pendingRequest.ItemId,
                requestId,
                pendingRequest.CommandText,
                pendingRequest.WorkingDirectory,
                pendingRequest.Reason,
                pendingRequest.Details,
                pendingRequest.Kind,
                pendingRequest.CanApproveForSession,
                approvalClassification: pendingRequest.Classification));
        }

        private void RegisterToolUserInputRequest(
            object rawRequestId,
            string requestId,
            IDictionary<string, object> parameters,
            long connectionEpoch)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            var pendingRequest = AppServerInteractionRequestMapper.BuildPendingToolUserInputRequest(rawRequestId, requestId, parameters);
            lock (_syncRoot)
            {
                _pendingRequestRegistry.RegisterToolInput(requestId, pendingRequest, connectionEpoch);
            }

            _raiseEvent(new AppServerEventArgs(
                "tool_user_input_requested",
                pendingRequest.Content,
                pendingRequest.ThreadId,
                pendingRequest.TurnId,
                pendingRequest.ItemId,
                requestId,
                details: pendingRequest.Details,
                toolQuestions: pendingRequest.Questions));
        }

        private void RegisterMcpElicitationRequest(
            object rawRequestId,
            string requestId,
            IDictionary<string, object> parameters,
            long connectionEpoch)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            var pendingRequest = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(rawRequestId, requestId, parameters);
            var mode = _getThreadApprovalMode(pendingRequest.ThreadId);
            if (!string.IsNullOrWhiteSpace(pendingRequest.ThreadId) &&
                (mode == ApprovalMode.FullAccess || mode == ApprovalMode.FullAccessAutoApprove) &&
                AppServerMcpToolApprovalMapper.IsToolApproval(parameters))
            {
                AutoApproveMcpToolRequestAsync(pendingRequest, connectionEpoch).Forget("Auto-approve MCP tool by Full Access setting");
                return;
            }
            lock (_syncRoot)
            {
                _pendingRequestRegistry.RegisterToolInput(requestId, pendingRequest, connectionEpoch);
            }

            var isUrlMode = string.Equals(pendingRequest.McpElicitationMode, "url", StringComparison.OrdinalIgnoreCase);
            _raiseEvent(new AppServerEventArgs(
                isUrlMode ? "mcp_elicitation_url_requested" : "mcp_elicitation_requested",
                pendingRequest.Content,
                pendingRequest.ThreadId,
                pendingRequest.TurnId,
                pendingRequest.ItemId,
                requestId,
                details: pendingRequest.Details,
                toolQuestions: pendingRequest.Questions,
                mcpServerName: pendingRequest.McpServerName,
                url: pendingRequest.McpUrl,
                isMcpElicitationConfirmation: pendingRequest.IsMcpElicitationConfirmation));
        }

        private async Task AutoApproveMcpToolRequestAsync(AppServerPendingToolUserInputRequest request, long connectionEpoch)
        {
            await _sendServerRequestResponseAsync(request.RawRequestId,
                AppServerMcpToolApprovalMapper.BuildResponse(request.Parameters, new Dictionary<string, string[]>
                {
                    { AppServerMcpToolApprovalMapper.QuestionId, new[] { "once" } }
                }), connectionEpoch).ConfigureAwait(false);
            _raiseEvent(new AppServerEventArgs("approval_resolved",
                request.Content + Environment.NewLine + L("AppServerClient.Approval.Source.FullAccessAutoApprove", "automatically approved by Full Access setting"),
                request.ThreadId, request.TurnId, request.ItemId, request.RequestId,
                mcpServerName: request.McpServerName));
        }

        private void RemovePendingApprovalRequest(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            lock (_syncRoot)
            {
                _pendingRequestRegistry.RemoveApproval(requestId);
            }
        }

        private void RemovePendingToolUserInputRequest(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return;
            }

            lock (_syncRoot)
            {
                _pendingRequestRegistry.RemoveToolInput(requestId);
            }
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetDictionary(source, key);
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetString(source, key);
        }

        private static object GetRawRequestId(IDictionary<string, object> message)
        {
            if (message == null || !message.ContainsKey("id"))
            {
                return null;
            }

            return message["id"];
        }

        private static string NormalizeRequestId(object requestId)
        {
            return requestId == null ? null : Convert.ToString(requestId);
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }
    }
}
