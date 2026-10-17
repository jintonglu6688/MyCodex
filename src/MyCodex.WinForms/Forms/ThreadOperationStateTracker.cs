using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ThreadOperationStateTracker
    {
        private readonly HashSet<string> _pendingCompactionThreadIds;
        private readonly Dictionary<string, string> _activeCompactionTurnIds;
        private readonly RecentStringRegistry _recentCompactionCompletedTurnIds;
        private readonly RecentStringRegistry _recentApprovalRequestIds;
        private readonly Dictionary<string, DateTime> _lastTurnCompletedNotificationAtByKey;
        private readonly TimeSpan _turnCompletedNotificationThrottle;

        public ThreadOperationStateTracker(int recentNotificationCapacity, TimeSpan turnCompletedNotificationThrottle)
        {
            _pendingCompactionThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _activeCompactionTurnIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _recentCompactionCompletedTurnIds = new RecentStringRegistry(recentNotificationCapacity);
            _recentApprovalRequestIds = new RecentStringRegistry(recentNotificationCapacity);
            _lastTurnCompletedNotificationAtByKey = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            _turnCompletedNotificationThrottle = turnCompletedNotificationThrottle;
        }

        internal void ClearAllInFlightStates()
        {
            _pendingCompactionThreadIds.Clear();
            _activeCompactionTurnIds.Clear();
        }

        internal bool HasActiveCompaction
        {
            get
            {
                return _pendingCompactionThreadIds.Count != 0 || _activeCompactionTurnIds.Count != 0;
            }
        }

        internal bool TryBeginCompaction(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId) || IsThreadCompacting(threadId))
            {
                return false;
            }

            _pendingCompactionThreadIds.Add(threadId);
            return true;
        }

        internal bool TryMarkCompactionStarted(string threadId, string turnId)
        {
            if (string.IsNullOrWhiteSpace(threadId) || !IsThreadPendingCompaction(threadId))
            {
                return false;
            }

            _pendingCompactionThreadIds.Remove(threadId);
            _activeCompactionTurnIds[threadId] = turnId ?? string.Empty;
            return true;
        }

        internal void ClearThreadCompactionState(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _pendingCompactionThreadIds.Remove(threadId);
            _activeCompactionTurnIds.Remove(threadId);
        }

        internal bool IsThreadCompacting(string threadId)
        {
            return IsThreadPendingCompaction(threadId) || IsThreadActiveCompaction(threadId, null);
        }

        internal bool IsThreadPendingCompaction(string threadId)
        {
            return !string.IsNullOrWhiteSpace(threadId) &&
                   _pendingCompactionThreadIds.Contains(threadId);
        }

        internal bool IsThreadActiveCompaction(string threadId, string turnId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return false;
            }

            string activeTurnId;
            if (!_activeCompactionTurnIds.TryGetValue(threadId, out activeTurnId))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(turnId) ||
                   string.IsNullOrWhiteSpace(activeTurnId) ||
                   string.Equals(activeTurnId, turnId, StringComparison.OrdinalIgnoreCase);
        }

        internal bool TryGetActiveCompactionTurnId(string threadId, out string activeTurnId)
        {
            return _activeCompactionTurnIds.TryGetValue(threadId ?? string.Empty, out activeTurnId);
        }

        internal bool TryRegisterApprovalRequest(string requestId)
        {
            return _recentApprovalRequestIds.TryRegister(requestId);
        }

        internal bool TryRegisterCompactionCompletion(string turnId)
        {
            return _recentCompactionCompletedTurnIds.TryRegister(turnId);
        }

        internal bool HasRecentCompactionCompletionTurn(string turnId)
        {
            return _recentCompactionCompletedTurnIds.Contains(turnId);
        }

        internal bool CanPlayTurnCompletedPrompt(DateTime now)
        {
            return CanPlayTurnCompletedPrompt(string.Empty, now);
        }

        internal bool CanPlayTurnCompletedPrompt(string notificationKey, DateTime now)
        {
            var key = notificationKey ?? string.Empty;
            DateTime lastNotificationAt;
            if (_lastTurnCompletedNotificationAtByKey.TryGetValue(key, out lastNotificationAt) &&
                (now - lastNotificationAt) < _turnCompletedNotificationThrottle)
            {
                return false;
            }

            _lastTurnCompletedNotificationAtByKey[key] = now;
            return true;
        }
    }

    internal sealed class RecentStringRegistry
    {
        private readonly int _capacity;
        private readonly HashSet<string> _knownIds;
        private readonly Queue<string> _order;

        public RecentStringRegistry(int capacity)
        {
            _capacity = Math.Max(1, capacity);
            _knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _order = new Queue<string>();
        }

        internal bool TryRegister(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return true;
            }

            if (_knownIds.Contains(id))
            {
                return false;
            }

            _knownIds.Add(id);
            _order.Enqueue(id);

            while (_order.Count > _capacity)
            {
                _knownIds.Remove(_order.Dequeue());
            }

            return true;
        }

        internal bool Contains(string id)
        {
            return !string.IsNullOrWhiteSpace(id) && _knownIds.Contains(id);
        }
    }
}
