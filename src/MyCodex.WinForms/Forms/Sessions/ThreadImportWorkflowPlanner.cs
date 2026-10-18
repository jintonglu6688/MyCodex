using System;
using System.Collections.Generic;
using System.IO;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ThreadImportCompletionPlan
    {
        public ThreadImportCompletionPlan(string summaryText, string statusText)
        {
            SummaryText = summaryText ?? string.Empty;
            StatusText = statusText ?? string.Empty;
        }

        public string SummaryText { get; }

        public string StatusText { get; }

        public bool HasSummaryText
        {
            get { return !string.IsNullOrWhiteSpace(SummaryText); }
        }

        public bool HasStatusText
        {
            get { return !string.IsNullOrWhiteSpace(StatusText); }
        }
    }

    internal sealed class AutomaticThreadSyncFailurePlan
    {
        public AutomaticThreadSyncFailurePlan(string summaryText, string logText, string statusText)
        {
            SummaryText = summaryText ?? string.Empty;
            LogText = logText ?? string.Empty;
            StatusText = statusText ?? string.Empty;
        }

        public string SummaryText { get; }

        public string LogText { get; }

        public string StatusText { get; }

        public bool HasStatusText
        {
            get { return !string.IsNullOrWhiteSpace(StatusText); }
        }
    }

    internal static class ThreadImportWorkflowPlanner
    {
        internal static bool ShouldRunAutomaticSync(bool isRunning, bool hasImportedSessions, bool isConnected)
        {
            return !isRunning && hasImportedSessions && isConnected;
        }

        internal static bool ShouldRunAutomaticSync(
            bool isRunning,
            bool hasImportedSessions,
            bool isConnected,
            bool allowEmptyStateBootstrap)
        {
            return !isRunning && isConnected && (hasImportedSessions || allowEmptyStateBootstrap);
        }

        internal static List<AppServerThreadSummary> FilterAutomaticSyncThreads(
            IEnumerable<AppServerThreadSummary> threads,
            ISet<string> knownThreadIds,
            ISet<string> loadedThreadIds,
            bool restrictToLoadedThreads,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace)
        {
            return FilterAutomaticSyncThreads(
                threads,
                knownThreadIds,
                loadedThreadIds,
                restrictToLoadedThreads,
                isWorkspacePathAvailable,
                belongsToKnownWorkspace,
                false,
                null);
        }

        internal static List<AppServerThreadSummary> FilterAutomaticSyncThreads(
            IEnumerable<AppServerThreadSummary> threads,
            ISet<string> knownThreadIds,
            ISet<string> loadedThreadIds,
            bool restrictToLoadedThreads,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace,
            bool allowUnknownWorkspaces)
        {
            return FilterAutomaticSyncThreads(
                threads,
                knownThreadIds,
                loadedThreadIds,
                restrictToLoadedThreads,
                isWorkspacePathAvailable,
                belongsToKnownWorkspace,
                allowUnknownWorkspaces,
                null,
                AppInteractionMode.Coding,
                null);
        }

        internal static List<AppServerThreadSummary> FilterAutomaticSyncThreads(
            IEnumerable<AppServerThreadSummary> threads,
            ISet<string> knownThreadIds,
            ISet<string> loadedThreadIds,
            bool restrictToLoadedThreads,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace,
            bool allowUnknownWorkspaces,
            ISet<string> ignoredThreadIds)
        {
            return FilterAutomaticSyncThreads(
                threads,
                knownThreadIds,
                loadedThreadIds,
                restrictToLoadedThreads,
                isWorkspacePathAvailable,
                belongsToKnownWorkspace,
                allowUnknownWorkspaces,
                ignoredThreadIds,
                AppInteractionMode.Coding,
                null);
        }

        internal static List<AppServerThreadSummary> FilterAutomaticSyncThreads(
            IEnumerable<AppServerThreadSummary> threads,
            ISet<string> knownThreadIds,
            ISet<string> loadedThreadIds,
            bool restrictToLoadedThreads,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace,
            bool allowUnknownWorkspaces,
            ISet<string> ignoredThreadIds,
            AppInteractionMode interactionMode,
            string officeRootPath)
        {
            var result = new List<AppServerThreadSummary>();
            if (threads == null)
            {
                return result;
            }

            foreach (var thread in threads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId) || string.IsNullOrWhiteSpace(thread.WorkingDirectory))
                {
                    continue;
                }

                if (isWorkspacePathAvailable == null || !isWorkspacePathAvailable(thread.WorkingDirectory))
                {
                    continue;
                }

                if (!MatchesInteractionMode(thread.WorkingDirectory, interactionMode, officeRootPath))
                {
                    continue;
                }

                if (knownThreadIds != null && knownThreadIds.Contains(thread.ThreadId))
                {
                    continue;
                }

                if (ignoredThreadIds != null && ignoredThreadIds.Contains(thread.ThreadId))
                {
                    continue;
                }

                var isKnownParentSubAgent = IsKnownParentSubAgent(thread, knownThreadIds);
                if (restrictToLoadedThreads &&
                    (loadedThreadIds == null || !loadedThreadIds.Contains(thread.ThreadId)) &&
                    !isKnownParentSubAgent)
                {
                    continue;
                }

                if (!allowUnknownWorkspaces &&
                    !isKnownParentSubAgent &&
                    (belongsToKnownWorkspace == null || !belongsToKnownWorkspace(thread.WorkingDirectory)))
                {
                    continue;
                }

                result.Add(thread);
            }

            return result;
        }

        internal static List<AppServerThreadSummary> FilterThreadsForInteractionMode(
            IEnumerable<AppServerThreadSummary> threads,
            AppInteractionMode interactionMode,
            string officeRootPath)
        {
            var result = new List<AppServerThreadSummary>();
            if (threads == null)
            {
                return result;
            }

            foreach (var thread in threads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.WorkingDirectory))
                {
                    continue;
                }

                if (MatchesInteractionMode(thread.WorkingDirectory, interactionMode, officeRootPath))
                {
                    result.Add(thread);
                }
            }

            return result;
        }

        private static bool MatchesInteractionMode(string workingDirectory, AppInteractionMode interactionMode, string officeRootPath)
        {
            var isOfficePath = IsSameOrChildPath(workingDirectory, officeRootPath);
            return interactionMode == AppInteractionMode.Office
                ? isOfficePath
                : !isOfficePath;
        }

        private static bool IsSameOrChildPath(string candidatePath, string rootPath)
        {
            if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            var candidate = NormalizeComparablePath(candidatePath);
            var root = NormalizeComparablePath(rootPath);
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(root))
            {
                return false;
            }

            if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return candidate.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) ||
                   candidate.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeComparablePath(string path)
        {
            try
            {
                return Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.Trim()
                    .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static bool IsKnownParentSubAgent(AppServerThreadSummary thread, ISet<string> knownThreadIds)
        {
            if (thread == null ||
                knownThreadIds == null ||
                string.IsNullOrWhiteSpace(thread.ParentThreadId))
            {
                return false;
            }

            return (string.Equals(thread.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase) ||
                    !string.IsNullOrWhiteSpace(thread.ParentThreadId)) &&
                   knownThreadIds.Contains(thread.ParentThreadId);
        }

        internal static ThreadImportCompletionPlan BuildManualNoSelection(bool isInitialImport)
        {
            return new ThreadImportCompletionPlan(
                string.Empty,
                isInitialImport
                    ? UiLocalization.Text("MainForm.Status.NoHistorySessionsSynced", "No history sessions were synced")
                    : UiLocalization.Text("MainForm.Status.NoNewHistorySessionsToSync", "No new history sessions to sync"));
        }

        internal static ThreadImportCompletionPlan BuildManualSuccess(int importedThreadCount, DateTime now)
        {
            return new ThreadImportCompletionPlan(
                string.Format(
                    UiLocalization.Text("MainForm.ThreadSyncSummary.ManualSync.Template", "{0:HH:mm} Manual sync {1} history session(s)"),
                    now,
                    importedThreadCount),
                string.Format(
                    UiLocalization.Text("MainForm.Status.HistorySessionsSynced.Template", "Synced {0} history session(s)"),
                    importedThreadCount));
        }

        internal static ThreadImportCompletionPlan BuildAutomaticSynced(int importedThreadCount, DateTime now)
        {
            return new ThreadImportCompletionPlan(
                string.Format(
                    UiLocalization.Text("MainForm.ThreadSyncSummary.AutoSyncedNew.Template", "{0:HH:mm} Auto-synced {1} new history session(s)"),
                    now,
                    importedThreadCount),
                string.Format(
                    UiLocalization.Text("MainForm.Status.AutoSyncedNewThreads.Template", "Automatically synced {0} new thread(s)"),
                    importedThreadCount));
        }

        internal static ThreadImportCompletionPlan BuildAutomaticNoNew(DateTime now)
        {
            return new ThreadImportCompletionPlan(
                string.Format(
                    UiLocalization.Text("MainForm.ThreadSyncSummary.AutoCheckNoNew.Template", "{0:HH:mm} Auto check completed, no new sessions"),
                    now),
                string.Empty);
        }

        internal static AutomaticThreadSyncFailurePlan BuildAutomaticConnectivityPause(
            string errorMessage,
            bool shouldSetDisconnectedStatus,
            DateTime now)
        {
            return new AutomaticThreadSyncFailurePlan(
                string.Format(
                    UiLocalization.Text("MainForm.ThreadSyncSummary.AutoSyncPausedWaitingConnection.Template", "{0:HH:mm} Auto sync paused, waiting for connection recovery"),
                    now),
                UiLocalization.Text("MainForm.Log.AutoSyncPausedPrefix", "Auto sync paused: ") + (errorMessage ?? string.Empty),
                shouldSetDisconnectedStatus
                    ? UiLocalization.Text("MainForm.Status.AutoSyncPausedDisconnected", "App Server is not connected. Automatic sync paused.")
                    : string.Empty);
        }

        internal static AutomaticThreadSyncFailurePlan BuildAutomaticCheckFailed(string errorMessage, DateTime now)
        {
            return new AutomaticThreadSyncFailurePlan(
                string.Format(
                    UiLocalization.Text("MainForm.ThreadSyncSummary.AutoCheckFailed.Template", "{0:HH:mm} Auto check failed"),
                    now),
                UiLocalization.Text("MainForm.Log.AutoSyncCheckFailedPrefix", "Auto sync check failed: ") + (errorMessage ?? string.Empty),
                string.Empty);
        }
    }
}
