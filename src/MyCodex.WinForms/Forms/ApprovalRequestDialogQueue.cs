using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ApprovalRequestDialogQueue
    {
        private readonly Queue<AppServerEventArgs> _pendingRequests = new Queue<AppServerEventArgs>();
        private readonly HashSet<string> _resolvedRequestIds = new HashSet<string>(StringComparer.Ordinal);
        private string _activeRequestId;

        public bool HasPending
        {
            get { return !string.IsNullOrWhiteSpace(_activeRequestId) || _pendingRequests.Count > 0; }
        }

        public bool Enqueue(AppServerEventArgs request, out AppServerEventArgs nextRequest)
        {
            nextRequest = null;
            if (!IsValidRequest(request) || _resolvedRequestIds.Contains(request.RequestId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(_activeRequestId))
            {
                _pendingRequests.Enqueue(request);
                return false;
            }

            _activeRequestId = request.RequestId;
            nextRequest = request;
            return true;
        }

        public void MarkResolved(string requestId)
        {
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                _resolvedRequestIds.Add(requestId);
            }
        }

        public bool CompleteCurrent(out AppServerEventArgs nextRequest)
        {
            if (!string.IsNullOrWhiteSpace(_activeRequestId))
            {
                _resolvedRequestIds.Remove(_activeRequestId);
            }

            _activeRequestId = null;
            nextRequest = DequeueNextUnresolvedRequest();
            if (nextRequest == null)
            {
                return false;
            }

            _activeRequestId = nextRequest.RequestId;
            return true;
        }

        public void Clear()
        {
            _pendingRequests.Clear();
            _resolvedRequestIds.Clear();
            _activeRequestId = null;
        }

        private AppServerEventArgs DequeueNextUnresolvedRequest()
        {
            while (_pendingRequests.Count > 0)
            {
                var request = _pendingRequests.Dequeue();
                if (!IsValidRequest(request))
                {
                    continue;
                }

                if (_resolvedRequestIds.Remove(request.RequestId))
                {
                    continue;
                }

                return request;
            }

            return null;
        }

        private static bool IsValidRequest(AppServerEventArgs request)
        {
            return request != null && !string.IsNullOrWhiteSpace(request.RequestId);
        }
    }
}
