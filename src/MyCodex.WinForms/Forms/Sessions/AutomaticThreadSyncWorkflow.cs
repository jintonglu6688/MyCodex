using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AutomaticThreadSyncResult
    {
        public AutomaticThreadSyncResult(
            List<AppServerThreadSummary> threadSummaries,
            List<AppServerThreadSummary> newThreads,
            ThreadImportCompletionPlan completionPlan,
            AutomaticThreadSyncFailurePlan failurePlan,
            bool completedCheck,
            bool shouldReportNoNewThreads)
        {
            ThreadSummaries = threadSummaries ?? new List<AppServerThreadSummary>();
            NewThreads = newThreads ?? new List<AppServerThreadSummary>();
            CompletionPlan = completionPlan;
            FailurePlan = failurePlan;
            CompletedCheck = completedCheck;
            ShouldReportNoNewThreads = shouldReportNoNewThreads;
        }

        public List<AppServerThreadSummary> ThreadSummaries { get; }

        public List<AppServerThreadSummary> NewThreads { get; }

        public ThreadImportCompletionPlan CompletionPlan { get; }

        public AutomaticThreadSyncFailurePlan FailurePlan { get; }

        public bool CompletedCheck { get; }

        public bool ShouldReportNoNewThreads { get; }

        public bool HasNewThreads
        {
            get { return NewThreads.Count > 0; }
        }

        public bool HasFailure
        {
            get { return FailurePlan != null; }
        }
    }

    internal sealed class AutomaticThreadSyncWorkflow
    {
        private readonly Func<Task<List<string>>> _listLoadedThreadIdsAsync;
        private readonly Func<Task<List<AppServerThreadSummary>>> _listThreadsAsync;

        internal AutomaticThreadSyncWorkflow(IAppServerThreadClient appServerClient)
            : this(
                appServerClient != null
                    ? new Func<Task<List<string>>>(() => appServerClient.ListLoadedThreadIdsAsync())
                    : throw new ArgumentNullException(nameof(appServerClient)),
                appServerClient.ListThreadsAsync)
        {
        }

        internal AutomaticThreadSyncWorkflow(
            Func<Task<List<string>>> listLoadedThreadIdsAsync,
            Func<Task<List<AppServerThreadSummary>>> listThreadsAsync)
        {
            _listLoadedThreadIdsAsync = listLoadedThreadIdsAsync ?? throw new ArgumentNullException(nameof(listLoadedThreadIdsAsync));
            _listThreadsAsync = listThreadsAsync ?? throw new ArgumentNullException(nameof(listThreadsAsync));
        }

        internal Task<AutomaticThreadSyncResult> RunAsync(
            ISet<string> knownThreadIds,
            bool restrictToLoadedThreadsWhenLoadedIdsAvailable,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace,
            Func<Exception, bool> isConnectivityFailure,
            bool isConnectedStatus,
            DateTime now)
        {
            return RunAsync(
                knownThreadIds,
                restrictToLoadedThreadsWhenLoadedIdsAvailable,
                isWorkspacePathAvailable,
                belongsToKnownWorkspace,
                isConnectivityFailure,
                isConnectedStatus,
                now,
                false,
                null);
        }

        internal async Task<AutomaticThreadSyncResult> RunAsync(
            ISet<string> knownThreadIds,
            bool restrictToLoadedThreadsWhenLoadedIdsAvailable,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace,
            Func<Exception, bool> isConnectivityFailure,
            bool isConnectedStatus,
            DateTime now,
            bool allowUnknownWorkspaces)
        {
            return await RunAsync(
                knownThreadIds,
                restrictToLoadedThreadsWhenLoadedIdsAvailable,
                isWorkspacePathAvailable,
                belongsToKnownWorkspace,
                isConnectivityFailure,
                isConnectedStatus,
                now,
                allowUnknownWorkspaces,
                null).ConfigureAwait(false);
        }

        internal async Task<AutomaticThreadSyncResult> RunAsync(
            ISet<string> knownThreadIds,
            bool restrictToLoadedThreadsWhenLoadedIdsAvailable,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace,
            Func<Exception, bool> isConnectivityFailure,
            bool isConnectedStatus,
            DateTime now,
            bool allowUnknownWorkspaces,
            ISet<string> ignoredThreadIds)
        {
            return await RunAsync(
                knownThreadIds,
                restrictToLoadedThreadsWhenLoadedIdsAvailable,
                isWorkspacePathAvailable,
                belongsToKnownWorkspace,
                isConnectivityFailure,
                isConnectedStatus,
                now,
                allowUnknownWorkspaces,
                ignoredThreadIds,
                AppInteractionMode.Coding,
                null).ConfigureAwait(false);
        }

        internal async Task<AutomaticThreadSyncResult> RunAsync(
            ISet<string> knownThreadIds,
            bool restrictToLoadedThreadsWhenLoadedIdsAvailable,
            Func<string, bool> isWorkspacePathAvailable,
            Func<string, bool> belongsToKnownWorkspace,
            Func<Exception, bool> isConnectivityFailure,
            bool isConnectedStatus,
            DateTime now,
            bool allowUnknownWorkspaces,
            ISet<string> ignoredThreadIds,
            AppInteractionMode interactionMode,
            string officeRootPath)
        {
            try
            {
                HashSet<string> loadedThreadIds = null;
                var loadedThreadIdsAvailable = false;
                try
                {
                    loadedThreadIds = new HashSet<string>(
                        await _listLoadedThreadIdsAsync(),
                        StringComparer.OrdinalIgnoreCase);
                    loadedThreadIdsAvailable = true;
                }
                catch
                {
                    loadedThreadIds = null;
                }

                var threads = await _listThreadsAsync();
                var restrictToLoadedThreads = loadedThreadIdsAvailable && restrictToLoadedThreadsWhenLoadedIdsAvailable;
                var newThreads = ThreadImportWorkflowPlanner.FilterAutomaticSyncThreads(
                    threads,
                    knownThreadIds,
                    loadedThreadIds,
                    restrictToLoadedThreads,
                    isWorkspacePathAvailable,
                    belongsToKnownWorkspace,
                    allowUnknownWorkspaces,
                    ignoredThreadIds,
                    interactionMode,
                    officeRootPath);

                return new AutomaticThreadSyncResult(
                    threads,
                    newThreads,
                    newThreads.Count > 0 ? ThreadImportWorkflowPlanner.BuildAutomaticSynced(newThreads.Count, now) : null,
                    null,
                    true,
                    true);
            }
            catch (Exception ex)
            {
                var failurePlan = isConnectivityFailure != null && isConnectivityFailure(ex)
                    ? ThreadImportWorkflowPlanner.BuildAutomaticConnectivityPause(ex.Message, !isConnectedStatus, now)
                    : ThreadImportWorkflowPlanner.BuildAutomaticCheckFailed(ex.Message, now);
                return new AutomaticThreadSyncResult(
                    new List<AppServerThreadSummary>(),
                    new List<AppServerThreadSummary>(),
                    null,
                    failurePlan,
                    false,
                    false);
            }
        }
    }
}
