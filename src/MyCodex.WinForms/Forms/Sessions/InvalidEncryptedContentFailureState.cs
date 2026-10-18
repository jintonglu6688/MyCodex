using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;

namespace MyCodex.WinForms.Forms
{
    internal sealed class InvalidEncryptedContentFailureState
    {
        private readonly int _maxRecentEventKeys;
        private readonly Queue<string> _recentEventKeys;
        private readonly HashSet<string> _knownEventKeys;
        private readonly HashSet<string> _repairStartedEventKeys;
        private readonly HashSet<string> _completionHandledEventKeys;

        public InvalidEncryptedContentFailureState(int maxRecentEventKeys)
        {
            _maxRecentEventKeys = Math.Max(1, maxRecentEventKeys);
            _recentEventKeys = new Queue<string>();
            _knownEventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _repairStartedEventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _completionHandledEventKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public InvalidEncryptedContentFailureDecision Evaluate(AppServerEventRoute route, AppServerEventArgs e)
        {
            if (!AppServerEventRouting.IsErrorDisplayEvent(route) ||
                e == null ||
                !LooksLikeInvalidEncryptedContentError(e.Content))
            {
                return InvalidEncryptedContentFailureDecision.Ignore;
            }

            var eventKey = BuildEventKey(e);
            RememberEventKey(eventKey);
            var shouldStartRepair = route == AppServerEventRoute.TurnFailed &&
                _repairStartedEventKeys.Add(eventKey);
            var shouldCompleteRepairAndRetry = route == AppServerEventRoute.TurnFailed &&
                _completionHandledEventKeys.Add(eventKey);

            return new InvalidEncryptedContentFailureDecision(
                true,
                shouldStartRepair,
                shouldCompleteRepairAndRetry,
                eventKey);
        }

        private void RememberEventKey(string eventKey)
        {
            if (!_knownEventKeys.Add(eventKey))
            {
                return;
            }

            _recentEventKeys.Enqueue(eventKey);
            while (_recentEventKeys.Count > _maxRecentEventKeys)
            {
                var removed = _recentEventKeys.Dequeue();
                _knownEventKeys.Remove(removed);
                _repairStartedEventKeys.Remove(removed);
                _completionHandledEventKeys.Remove(removed);
            }
        }

        private static string BuildEventKey(AppServerEventArgs e)
        {
            return string.Join(
                "|",
                new[]
                {
                    e.ThreadId ?? string.Empty,
                    e.TurnId ?? string.Empty,
                    NormalizeErrorContent(e.Content)
                });
        }

        private static string NormalizeErrorContent(string content)
        {
            return string.IsNullOrWhiteSpace(content) ? string.Empty : content.Trim();
        }

        private static bool LooksLikeInvalidEncryptedContentError(string message)
        {
            return ThreadHistoryRejectionClassifier.IsRepairableRejection(message);
        }
    }

    internal sealed class InvalidEncryptedContentFailureDecision
    {
        internal static readonly InvalidEncryptedContentFailureDecision Ignore =
            new InvalidEncryptedContentFailureDecision(false, false, false, null);

        public InvalidEncryptedContentFailureDecision(
            bool shouldSuppressDisplay,
            bool shouldStartRepair,
            bool shouldCompleteRepairAndRetry,
            string eventKey)
        {
            ShouldSuppressDisplay = shouldSuppressDisplay;
            ShouldStartRepair = shouldStartRepair;
            ShouldCompleteRepairAndRetry = shouldCompleteRepairAndRetry;
            EventKey = eventKey;
        }

        public bool ShouldSuppressDisplay { get; }

        public bool ShouldStartRepair { get; }

        public bool ShouldCompleteRepairAndRetry { get; }

        public string EventKey { get; }
    }
}
