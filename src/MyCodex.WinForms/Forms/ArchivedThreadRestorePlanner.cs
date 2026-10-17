using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ArchivedThreadRestoreResolution
    {
        public ArchivedThreadRestoreResolution(
            IEnumerable<AppServerThreadSummary> restorableThreads,
            IDictionary<string, string> failureMessages)
        {
            RestorableThreads = new List<AppServerThreadSummary>();
            if (restorableThreads != null)
            {
                foreach (var thread in restorableThreads)
                {
                    if (thread != null)
                    {
                        RestorableThreads.Add(thread);
                    }
                }
            }

            FailureMessages = failureMessages == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(failureMessages, StringComparer.OrdinalIgnoreCase);
        }

        public List<AppServerThreadSummary> RestorableThreads { get; }

        public Dictionary<string, string> FailureMessages { get; }
    }

    internal sealed class ArchivedThreadRestoreCompletionPlan
    {
        public ArchivedThreadRestoreCompletionPlan(
            int restoredThreadCount,
            int failureCount,
            string summaryText,
            string statusText,
            string emptyRestoreMessage,
            string failureDialogMessage)
        {
            RestoredThreadCount = restoredThreadCount;
            FailureCount = failureCount;
            SummaryText = summaryText ?? string.Empty;
            StatusText = statusText ?? string.Empty;
            EmptyRestoreMessage = emptyRestoreMessage ?? string.Empty;
            FailureDialogMessage = failureDialogMessage ?? string.Empty;
        }

        public int RestoredThreadCount { get; }

        public int FailureCount { get; }

        public string SummaryText { get; }

        public string StatusText { get; }

        public string EmptyRestoreMessage { get; }

        public string FailureDialogMessage { get; }

        public bool HasRestoredThreads
        {
            get { return RestoredThreadCount > 0; }
        }

        public bool HasFailureDialogMessage
        {
            get { return !string.IsNullOrWhiteSpace(FailureDialogMessage); }
        }
    }

    internal static class ArchivedThreadRestorePlanner
    {
        internal static HashSet<string> BuildSelectedThreadIdSet(IEnumerable<AppServerThreadSummary> selectedThreads)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedThreads == null)
            {
                return result;
            }

            foreach (var thread in selectedThreads)
            {
                if (thread != null && !string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    result.Add(thread.ThreadId);
                }
            }

            return result;
        }

        internal static string BuildFailureLine(
            AppServerThreadSummary thread,
            string errorMessage,
            Func<AppServerThreadSummary, string> titleBuilder)
        {
            var displayTitle = titleBuilder == null
                ? UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session")
                : titleBuilder(thread);
            return string.Format(
                UiLocalization.Text("MainForm.RestoreArchivedThreads.FailureLine.Template", "{0}: {1}"),
                displayTitle,
                errorMessage ?? string.Empty);
        }

        internal static ArchivedThreadRestoreResolution ResolveAfterActiveRefresh(
            IEnumerable<AppServerThreadSummary> activeThreads,
            ISet<string> selectedThreadIds,
            IDictionary<string, string> failureMessages)
        {
            var remainingFailures = failureMessages == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(failureMessages, StringComparer.OrdinalIgnoreCase);
            var restorableThreads = new List<AppServerThreadSummary>();
            if (activeThreads == null || selectedThreadIds == null)
            {
                return new ArchivedThreadRestoreResolution(restorableThreads, remainingFailures);
            }

            foreach (var thread in activeThreads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId) || !selectedThreadIds.Contains(thread.ThreadId))
                {
                    continue;
                }

                restorableThreads.Add(thread);
                remainingFailures.Remove(thread.ThreadId);
            }

            return new ArchivedThreadRestoreResolution(restorableThreads, remainingFailures);
        }

        internal static ArchivedThreadRestoreResolution ResolveFromSuccessfulSelection(
            IEnumerable<AppServerThreadSummary> selectedThreads,
            ISet<string> successfulThreadIds,
            IDictionary<string, string> failureMessages)
        {
            var restorableThreads = new List<AppServerThreadSummary>();
            if (selectedThreads != null && successfulThreadIds != null)
            {
                foreach (var thread in selectedThreads)
                {
                    if (thread != null &&
                        !string.IsNullOrWhiteSpace(thread.ThreadId) &&
                        successfulThreadIds.Contains(thread.ThreadId))
                    {
                        restorableThreads.Add(thread);
                    }
                }
            }

            return new ArchivedThreadRestoreResolution(restorableThreads, failureMessages);
        }

        internal static ArchivedThreadRestoreCompletionPlan BuildCompletionPlan(
            int restoredThreadCount,
            IDictionary<string, string> failureMessages,
            DateTime now)
        {
            var failureText = BuildFailureText(failureMessages);
            var failureCount = failureMessages == null ? 0 : failureMessages.Count;
            if (restoredThreadCount <= 0)
            {
                var emptyRestoreMessage = failureCount == 0
                    ? UiLocalization.Text("MainForm.Message.NoArchivedSessionsRestored", "No archived sessions were restored successfully.")
                    : UiLocalization.Text("MainForm.Message.NoArchivedSessionsRestored", "No archived sessions were restored successfully.")
                      + Environment.NewLine
                      + Environment.NewLine
                      + failureText;
                return new ArchivedThreadRestoreCompletionPlan(
                    0,
                    failureCount,
                    string.Empty,
                    UiLocalization.Text("MainForm.Status.RestoreArchivedSessionsFailed", "Restore archived sessions failed"),
                    emptyRestoreMessage,
                    string.Empty);
            }

            var statusText = failureCount == 0
                ? string.Format(
                    UiLocalization.Text("MainForm.Status.ArchivedSessionsRestored.Template", "Restored {0} archived session(s)"),
                    restoredThreadCount)
                : string.Format(
                    UiLocalization.Text("MainForm.Status.ArchivedSessionsRestoredWithFailures.Template", "Restored {0} archived session(s), {1} failed"),
                    restoredThreadCount,
                    failureCount);
            var failureDialogMessage = failureCount == 0
                ? string.Empty
                : UiLocalization.Text("MainForm.Message.ArchivedSessionRestoreFailuresPrefix", "The following sessions failed to restore:")
                  + Environment.NewLine
                  + Environment.NewLine
                  + failureText;

            return new ArchivedThreadRestoreCompletionPlan(
                restoredThreadCount,
                failureCount,
                string.Format(
                    UiLocalization.Text("MainForm.ThreadSyncSummary.RestoredArchived.Template", "{0:HH:mm} Restored {1} archived session(s)"),
                    now,
                    restoredThreadCount),
                statusText,
                string.Empty,
                failureDialogMessage);
        }

        private static string BuildFailureText(IDictionary<string, string> failureMessages)
        {
            if (failureMessages == null || failureMessages.Count == 0)
            {
                return string.Empty;
            }

            return string.Join(Environment.NewLine, new List<string>(failureMessages.Values).ToArray());
        }
    }
}
