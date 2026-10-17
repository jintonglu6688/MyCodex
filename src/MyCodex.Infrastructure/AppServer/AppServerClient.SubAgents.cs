using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient
    {
        private string _approvalRuntimeKey;

        private void BindApprovalRuntime(string runtimeKey)
        {
            lock (_syncRoot)
            {
                if (!string.Equals(_approvalRuntimeKey, runtimeKey, StringComparison.Ordinal)) _threadApprovalModes.Clear();
                _approvalRuntimeKey = runtimeKey;
            }
        }

        public void RememberSubAgentParent(string threadId, string parentThreadId)
        {
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(parentThreadId)) return;
            lock (_syncRoot)
            {
                ApprovalMode parentMode;
                if (!_threadApprovalModes.ContainsKey(threadId) && _threadApprovalModes.TryGetValue(parentThreadId, out parentMode))
                    _threadApprovalModes[threadId] = parentMode;
            }
        }

        public async Task SendSubAgentMessageAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var epoch = GetConnectionEpoch();
            var details = await ReadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            AppServerSubAgentOperations.ValidateIdentity(threadId, details);
            var inputs = BuildUserInputs(inputItems);
            if (inputs.Count == 0) throw new ArgumentException(L("AppServerClient.Error.ValidInputRequired", "At least one valid input item is required."));
            var request = AppServerSubAgentOperations.BuildInputRequest(details, inputs.ToArray());
            cancellationToken.ThrowIfCancellationRequested();
            if (epoch != GetConnectionEpoch()) throw new OperationCanceledException();
            RememberSubAgentParent(threadId, details.ParentThreadId);
            await SendRequestAsync(request.Key, request.Value, cancellationToken, LongRunningRequestTimeoutMs).ConfigureAwait(false);
            // No automatic retry: a lost response does not prove the message was rejected.
        }

        public async Task InterruptSubAgentTurnAsync(string threadId, CancellationToken cancellationToken)
        {
            EnsureConnected();
            var epoch = GetConnectionEpoch();
            var details = await ReadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            AppServerSubAgentOperations.ValidateIdentity(threadId, details);
            var turnId = AppServerSubAgentOperations.GetActiveTurnId(details);
            if (string.IsNullOrWhiteSpace(turnId)) return;
            cancellationToken.ThrowIfCancellationRequested();
            if (epoch != GetConnectionEpoch()) throw new OperationCanceledException();
            await SendRequestAsync("turn/interrupt", new Dictionary<string, object>
            {
                { "threadId", threadId }, { "turnId", turnId }
            }, cancellationToken, DefaultRequestTimeoutMs).ConfigureAwait(false);
        }
    }
}
