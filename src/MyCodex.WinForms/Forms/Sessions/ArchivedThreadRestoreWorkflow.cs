using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ArchivedThreadRestoreWorkflowResult
    {
        public ArchivedThreadRestoreWorkflowResult(
            List<AppServerThreadSummary> restorableThreads,
            ArchivedThreadRestoreCompletionPlan completionPlan)
        {
            RestorableThreads = restorableThreads ?? new List<AppServerThreadSummary>();
            CompletionPlan = completionPlan;
        }

        public List<AppServerThreadSummary> RestorableThreads { get; }

        public ArchivedThreadRestoreCompletionPlan CompletionPlan { get; }

        public bool HasRestorableThreads
        {
            get { return RestorableThreads.Count > 0; }
        }
    }

    internal sealed class ArchivedThreadRestoreWorkflow
    {
        private readonly Func<string, Task> _unarchiveThreadAsync;
        private readonly Func<Task<List<AppServerThreadSummary>>> _listThreadsAsync;

        internal ArchivedThreadRestoreWorkflow(IAppServerThreadClient appServerClient)
            : this(
                appServerClient != null ? appServerClient.UnarchiveThreadAsync : throw new ArgumentNullException(nameof(appServerClient)),
                appServerClient.ListThreadsAsync)
        {
        }

        internal ArchivedThreadRestoreWorkflow(
            Func<string, Task> unarchiveThreadAsync,
            Func<Task<List<AppServerThreadSummary>>> listThreadsAsync)
        {
            _unarchiveThreadAsync = unarchiveThreadAsync ?? throw new ArgumentNullException(nameof(unarchiveThreadAsync));
            _listThreadsAsync = listThreadsAsync ?? throw new ArgumentNullException(nameof(listThreadsAsync));
        }

        internal async Task<ArchivedThreadRestoreWorkflowResult> RestoreAsync(
            List<AppServerThreadSummary> selectedThreads,
            Func<AppServerThreadSummary, string> buildThreadDisplayTitle)
        {
            if (selectedThreads == null || selectedThreads.Count == 0)
            {
                return new ArchivedThreadRestoreWorkflowResult(
                    new List<AppServerThreadSummary>(),
                    ArchivedThreadRestorePlanner.BuildCompletionPlan(0, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), DateTime.Now));
            }

            var selectedThreadIds = ArchivedThreadRestorePlanner.BuildSelectedThreadIdSet(selectedThreads);
            var successfulThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failureMessages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var thread in selectedThreads)
            {
                if (thread == null || string.IsNullOrWhiteSpace(thread.ThreadId))
                {
                    continue;
                }

                try
                {
                    await _unarchiveThreadAsync(thread.ThreadId);
                    successfulThreadIds.Add(thread.ThreadId);
                }
                catch (Exception ex)
                {
                    failureMessages[thread.ThreadId] = ArchivedThreadRestorePlanner.BuildFailureLine(
                        thread,
                        ex.Message,
                        buildThreadDisplayTitle);
                }
            }

            List<AppServerThreadSummary> restorableThreads;
            try
            {
                var activeThreads = await _listThreadsAsync();
                var resolution = ArchivedThreadRestorePlanner.ResolveAfterActiveRefresh(activeThreads, selectedThreadIds, failureMessages);
                restorableThreads = resolution.RestorableThreads;
                failureMessages = resolution.FailureMessages;
            }
            catch
            {
                var resolution = ArchivedThreadRestorePlanner.ResolveFromSuccessfulSelection(selectedThreads, successfulThreadIds, failureMessages);
                restorableThreads = resolution.RestorableThreads;
                failureMessages = resolution.FailureMessages;
            }

            return new ArchivedThreadRestoreWorkflowResult(
                restorableThreads,
                ArchivedThreadRestorePlanner.BuildCompletionPlan(restorableThreads.Count, failureMessages, DateTime.Now));
        }
    }
}
