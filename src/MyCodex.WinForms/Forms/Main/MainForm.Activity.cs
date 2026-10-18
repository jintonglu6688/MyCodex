using System;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void AppendLog(string text)
        {
            _recordedLogState.AppendLogLine(text);
            _visibleLogState.AppendLogLine(text);
            EnsurePendingLogFlushScheduled();
        }

        private void LogFlushTimer_Tick(object sender, EventArgs e)
        {
            var shouldRender = FlushPendingLogText();
            shouldRender = _activityDisplayRefreshState.ConsumeRefreshRequest() || shouldRender;
            if (shouldRender)
            {
                RenderActivityDisplay();
            }

            if (!_visibleLogState.HasPendingText && !_activityDisplayRefreshState.HasPendingRefresh)
            {
                _logFlushTimer.Stop();
            }
        }

        private bool FlushPendingLogText()
        {
            if (_visibleLogState.FlushPendingText() == null)
            {
                return false;
            }

            return true;
        }

        private void ClearRenderedLog()
        {
            _visibleLogState.Clear();
            if (_logFlushTimer.Enabled)
            {
                _logFlushTimer.Stop();
            }

            RefreshActivityDisplay();
        }

        private void ClearAllLogBuffers()
        {
            ClearRenderedLog();
            _recordedLogState.Clear();
        }

        private bool HasRecordedLogContent()
        {
            return _recordedLogState.HasContent;
        }

        private bool HasActivityTimelineContent()
        {
            foreach (var state in _threadActivityTimelineStates.Values)
            {
                if (state != null && state.HasItems)
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsurePendingLogFlushScheduled()
        {
            if (!_logFlushTimer.Enabled &&
                (_visibleLogState.HasPendingText || _activityDisplayRefreshState.HasPendingRefresh))
            {
                _logFlushTimer.Start();
            }
        }

        private void RefreshActivityDisplay()
        {
            _activityDisplayRefreshState.RequestRefresh();
            EnsurePendingLogFlushScheduled();
        }

        private void RenderActivityDisplay()
        {
            if (_activityBrowseControl == null)
            {
                return;
            }

            var currentStatus = ActivityDisplayState.BuildCurrentStatus(
                _activitySummaryText,
                UiLocalization.Text("MainForm.Activity.Current.Empty", "No active activity currently."));
            var items = ActivityDisplayState.BuildStableItems(
                _activitySummaryText,
                UiLocalization.Text("MainForm.Activity.List.Empty", "No activity records currently."));
            items = BuildCurrentActivityTimelineItems(items);
            var text = currentStatus + Environment.NewLine + string.Join(Environment.NewLine, items);

            if (!_activityDisplayRefreshState.ShouldRender(text))
            {
                return;
            }

            if (!string.Equals(_activityCurrentStatusLabel.Text, currentStatus, StringComparison.Ordinal))
            {
                _activityCurrentStatusLabel.Text = currentStatus;
            }
            _activityBrowseControl.SetItems(items);
            _activityDisplayRefreshState.MarkRendered(text);
        }

        private string[] BuildCurrentActivityTimelineItems(string[] fallbackItems)
        {
            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.ThreadId))
            {
                return fallbackItems ?? new string[0];
            }

            ActivityTimelineState state;
            if (!_threadActivityTimelineStates.TryGetValue(_currentSession.ThreadId, out state))
            {
                return fallbackItems ?? new string[0];
            }

            return state.BuildDisplayItems(
                UiLocalization.Text("MainForm.Activity.List.Empty", "No activity records currently."),
                _appConfigSnapshot);
        }

        private bool ShouldShowActivityLog()
        {
            return _appConfigSnapshot == null || _appConfigSnapshot.ShowCommandOutput;
        }
    }
}
