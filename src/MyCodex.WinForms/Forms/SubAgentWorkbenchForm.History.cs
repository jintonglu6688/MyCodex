using System;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;

namespace MyCodex.WinForms.Forms
{
    internal sealed partial class SubAgentWorkbenchForm
    {
        private Task _historyLoadTask;
        private string _historyLoadThreadId;
        private string _displayedHistoryThreadId;
        private int _historyLoadVersion;
        private int _historyRevision;

        private Task LoadActiveSessionHistoryAsync(bool includeSystemHeader)
        {
            if (ActiveSession == null || _operations.HistoryLoadWorkflow == null)
            {
                ShowNoActiveSubAgentMessage();
                return Task.CompletedTask;
            }

            if (_historyLoadTask != null && !_historyLoadTask.IsCompleted &&
                string.Equals(_historyLoadThreadId, ActiveSession.ThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return _historyLoadTask;
            }

            _historyLoadThreadId = ActiveSession.ThreadId;
            _historyLoadTask = LoadHistoryCoreAsync(ActiveSession, includeSystemHeader, ++_historyLoadVersion);
            return _historyLoadTask;
        }

        private async Task LoadHistoryCoreAsync(ChatSession session, bool includeSystemHeader, int version)
        {
            var threadId = session.ThreadId;
            _isLoadingHistory = !string.Equals(_displayedHistoryThreadId, threadId, StringComparison.OrdinalIgnoreCase);
            RefreshActionState();
            try
            {
                while (!IsDisposed && version == _historyLoadVersion && IsActiveHistoryLoadTarget(session, threadId))
                {
                    var revision = _historyRevision;
                    _operations.HistoryLoadWorkflow.Invalidate(threadId);
                    var request = _operations.HistoryLoadWorkflow.BuildRequest(session, includeSystemHeader);
                    var hasDisplayedHistory = string.Equals(_displayedHistoryThreadId, threadId, StringComparison.OrdinalIgnoreCase);
                    if (!hasDisplayedHistory)
                    {
                        _displayedHistoryThreadId = null;
                        ApplyHistoryLoadResult(request.ImmediateResult);
                    }

                    var result = await _operations.HistoryLoadWorkflow.CompleteRemoteLoadAsync(
                        request,
                        (candidate, id) => !IsDisposed && version == _historyLoadVersion &&
                            revision == _historyRevision && IsActiveHistoryLoadTarget(candidate, id),
                        _ => false);
                    if (IsDisposed || version != _historyLoadVersion || !IsActiveHistoryLoadTarget(session, threadId))
                    {
                        return;
                    }

                    if (revision != _historyRevision)
                    {
                        continue;
                    }

                    if (result.Kind == SessionHistoryLoadResultKind.LoadedRemoteHistory)
                    {
                        ApplyObservedSnapshot(result.ThreadDetails);
                        ApplyHistoryLoadResult(result);
                        _displayedHistoryThreadId = threadId;
                    }
                    else if (hasDisplayedHistory)
                    {
                        if (result.HasStatus)
                        {
                            SetStatus(result.Kind == SessionHistoryLoadResultKind.MissingThread
                                ? MyCodex.WinForms.Localization.UiLocalization.Text("SubAgentWorkbenchForm.ThreadUnavailable", "The agent thread is unavailable. Its identity is retained; no replacement will be created.")
                                : result.StatusText);
                        }
                    }
                    else
                    {
                        ApplyHistoryLoadResult(result);
                    }

                    return;
                }
            }
            finally
            {
                if (!IsDisposed && version == _historyLoadVersion)
                {
                    _isLoadingHistory = false;
                    RefreshActionState();
                }
            }
        }
    }
}
