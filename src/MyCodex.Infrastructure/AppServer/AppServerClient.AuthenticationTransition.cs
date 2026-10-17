using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient
    {
        private bool _authenticationLoginPending;
        private string _authenticationPendingLoginId;
        private readonly HashSet<string> _authenticationEarlyLoginCompletions = new HashSet<string>(StringComparer.Ordinal);

        public bool HasAuthenticationTransitionWork
        {
            get
            {
                lock (_syncRoot)
                {
                    return _activeTurnIds.Count != 0 || _localCommandCancellationSources.Count != 0 || _authenticationLoginPending;
                }
            }
        }

        private async Task<AppServerLoginResult> StartAuthenticationLoginWithGateAsync(Func<Task<AppServerLoginResult>> start)
        {
            using (RuntimeOperationGate.Shared.EnterOperation())
            {
                lock (_syncRoot)
                {
                    if (_authenticationLoginPending)
                    {
                        throw new MyCodex.Application.Interfaces.AuthenticationCenterException("runtime_busy");
                    }
                    _authenticationLoginPending = true;
                    _authenticationPendingLoginId = null;
                    _authenticationEarlyLoginCompletions.Clear();
                }
                // Keep an uncertain login pending until completion, cancellation,
                // or a process restart; its response alone does not end OAuth.
                var result = await start().ConfigureAwait(false);
                lock (_syncRoot)
                {
                    _authenticationPendingLoginId = result?.LoginId;
                    if (_authenticationPendingLoginId != null && _authenticationEarlyLoginCompletions.Contains(_authenticationPendingLoginId))
                    {
                        _authenticationLoginPending = false;
                    }
                    _authenticationEarlyLoginCompletions.Clear();
                }
                return result;
            }
        }

        private void ObserveAuthenticationLoginCompletion(string loginId)
        {
            if (string.IsNullOrWhiteSpace(loginId))
            {
                return;
            }
            lock (_syncRoot)
            {
                if (!_authenticationLoginPending)
                {
                    return;
                }
                if (_authenticationPendingLoginId == null)
                {
                    _authenticationEarlyLoginCompletions.Add(loginId);
                }
                else if (string.Equals(_authenticationPendingLoginId, loginId, StringComparison.Ordinal))
                {
                    _authenticationLoginPending = false;
                    _authenticationPendingLoginId = null;
                }
            }
        }

        internal static bool RequiresAuthenticationGate(string method)
        {
            return !string.Equals(method, "initialize", StringComparison.Ordinal) &&
                !string.Equals(method, "getConversationSummary", StringComparison.Ordinal) &&
                !string.Equals(method, "fs/watch", StringComparison.Ordinal) &&
                !string.Equals(method, "fs/unwatch", StringComparison.Ordinal) &&
                !(method ?? string.Empty).EndsWith("/read", StringComparison.Ordinal) &&
                !(method ?? string.Empty).EndsWith("/list", StringComparison.Ordinal) &&
                !(method ?? string.Empty).EndsWith("/get", StringComparison.Ordinal);
        }

        public async Task SendUserMessageAsync(string threadId, string workspacePath,
            IReadOnlyCollection<AppServerUserInputItem> inputItems, ApprovalMode? approvalMode,
            string modelId, string reasoningEffort, AppServerCollaborationMode collaborationMode = null)
        {
            using (RuntimeOperationGate.Shared.EnterOperation())
            {
                await SendUserMessageForAuthenticationGateAsync(threadId, workspacePath, inputItems,
                    approvalMode, modelId, reasoningEffort, collaborationMode).ConfigureAwait(false);
            }
        }

        public async Task<AppServerCommandExecResult> ExecuteCommandAsync(string commandText,
            string workspacePath, ApprovalMode approvalMode, string processId, long timeoutMs,
            Action<string, string> outputReceived)
        {
            using (RuntimeOperationGate.Shared.EnterOperation())
            {
                return await ExecuteCommandForAuthenticationGateAsync(commandText, workspacePath,
                    approvalMode, processId, timeoutMs, outputReceived).ConfigureAwait(false);
            }
        }

        public async Task SubmitCompactThreadRequestAsync(string threadId, string workspacePath,
            ApprovalMode? approvalMode, string modelId)
        {
            using (RuntimeOperationGate.Shared.EnterOperation())
            {
                await SubmitCompactThreadForAuthenticationGateAsync(threadId, workspacePath,
                    approvalMode, modelId).ConfigureAwait(false);
            }
        }
    }
}
