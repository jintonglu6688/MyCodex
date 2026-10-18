using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal sealed class GuardianDeniedActionApprovalState
    {
        private readonly Dictionary<string, AppServerEventArgs> _requests =
            new Dictionary<string, AppServerEventArgs>(StringComparer.Ordinal);

        internal bool TryRegister(AppServerEventArgs notification)
        {
            var request = notification?.GuardianApprovalReview?.ApprovalRequest;
            if (request == null || notification.IsHistorical ||
                string.IsNullOrWhiteSpace(notification.RequestId) ||
                !string.Equals(notification.ThreadId, request.ThreadId, StringComparison.Ordinal) ||
                _requests.ContainsKey(notification.RequestId))
            {
                return false;
            }

            _requests.Add(notification.RequestId, notification);
            return true;
        }

        internal bool TryTake(AppServerEventArgs notification)
        {
            AppServerEventArgs registered;
            if (notification == null || string.IsNullOrWhiteSpace(notification.RequestId) ||
                !_requests.TryGetValue(notification.RequestId, out registered) ||
                !ReferenceEquals(registered, notification))
            {
                return false;
            }

            _requests[notification.RequestId] = null;
            return true;
        }

        internal void Clear()
        {
            _requests.Clear();
        }
    }
}
