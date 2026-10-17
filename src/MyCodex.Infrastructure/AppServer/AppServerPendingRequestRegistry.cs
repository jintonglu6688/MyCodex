using System;
using System.Collections.Generic;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerPendingRequestRegistry
    {
        private readonly Dictionary<string, PendingRequest<AppServerPendingApprovalRequest>> _approvalRequests =
            new Dictionary<string, PendingRequest<AppServerPendingApprovalRequest>>(StringComparer.Ordinal);

        private readonly Dictionary<string, PendingRequest<AppServerPendingToolUserInputRequest>> _toolInputRequests =
            new Dictionary<string, PendingRequest<AppServerPendingToolUserInputRequest>>(StringComparer.Ordinal);

        public void RegisterApproval(string requestId, AppServerPendingApprovalRequest request)
        {
            RegisterApproval(requestId, request, 0L);
        }

        public void RegisterApproval(string requestId, AppServerPendingApprovalRequest request, long connectionEpoch)
        {
            if (string.IsNullOrWhiteSpace(requestId) || request == null)
            {
                return;
            }

            _approvalRequests[requestId] = new PendingRequest<AppServerPendingApprovalRequest>(request, connectionEpoch);
        }

        public bool TryGetApproval(string requestId, out AppServerPendingApprovalRequest request)
        {
            PendingRequest<AppServerPendingApprovalRequest> pendingRequest;
            if (_approvalRequests.TryGetValue(requestId ?? string.Empty, out pendingRequest))
            {
                request = pendingRequest.Request;
                return true;
            }

            request = null;
            return false;
        }

        public bool RemoveApproval(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return false;
            }

            return _approvalRequests.Remove(requestId);
        }

        public bool TryTakeApproval(string requestId, out AppServerPendingApprovalRequest request)
        {
            long connectionEpoch;
            return TryTakeApproval(requestId, out request, out connectionEpoch);
        }

        public bool TryTakeApproval(string requestId, out AppServerPendingApprovalRequest request, out long connectionEpoch)
        {
            PendingRequest<AppServerPendingApprovalRequest> pendingRequest;
            if (!_approvalRequests.TryGetValue(requestId ?? string.Empty, out pendingRequest))
            {
                request = null;
                connectionEpoch = 0L;
                return false;
            }

            _approvalRequests.Remove(requestId);
            request = pendingRequest.Request;
            connectionEpoch = pendingRequest.ConnectionEpoch;
            return true;
        }

        public void RegisterToolInput(string requestId, AppServerPendingToolUserInputRequest request)
        {
            RegisterToolInput(requestId, request, 0L);
        }

        public void RegisterToolInput(string requestId, AppServerPendingToolUserInputRequest request, long connectionEpoch)
        {
            if (string.IsNullOrWhiteSpace(requestId) || request == null)
            {
                return;
            }

            _toolInputRequests[requestId] = new PendingRequest<AppServerPendingToolUserInputRequest>(request, connectionEpoch);
        }

        public bool TryGetToolInput(string requestId, out AppServerPendingToolUserInputRequest request)
        {
            PendingRequest<AppServerPendingToolUserInputRequest> pendingRequest;
            if (_toolInputRequests.TryGetValue(requestId ?? string.Empty, out pendingRequest))
            {
                request = pendingRequest.Request;
                return true;
            }

            request = null;
            return false;
        }

        public bool RemoveToolInput(string requestId)
        {
            if (string.IsNullOrWhiteSpace(requestId))
            {
                return false;
            }

            return _toolInputRequests.Remove(requestId);
        }

        public bool TryTakeToolInput(string requestId, out AppServerPendingToolUserInputRequest request)
        {
            long connectionEpoch;
            return TryTakeToolInput(requestId, out request, out connectionEpoch);
        }

        public bool TryTakeToolInput(string requestId, out AppServerPendingToolUserInputRequest request, out long connectionEpoch)
        {
            PendingRequest<AppServerPendingToolUserInputRequest> pendingRequest;
            if (!_toolInputRequests.TryGetValue(requestId ?? string.Empty, out pendingRequest))
            {
                request = null;
                connectionEpoch = 0L;
                return false;
            }

            _toolInputRequests.Remove(requestId);
            request = pendingRequest.Request;
            connectionEpoch = pendingRequest.ConnectionEpoch;
            return true;
        }

        public void Clear()
        {
            _approvalRequests.Clear();
            _toolInputRequests.Clear();
        }

        private sealed class PendingRequest<T>
        {
            public PendingRequest(T request, long connectionEpoch)
            {
                Request = request;
                ConnectionEpoch = connectionEpoch;
            }

            public T Request { get; private set; }

            public long ConnectionEpoch { get; private set; }
        }
    }
}
