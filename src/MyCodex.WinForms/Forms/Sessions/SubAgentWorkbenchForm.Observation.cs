using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed partial class SubAgentWorkbenchForm
    {
        private Timer _observationTimer;
        private bool _refreshingObservation;

        private void ConfigureObservation()
        {
            var refresh = new MenuItem(UiLocalization.Text("SubAgentWorkbenchForm.Refresh", "Refresh (&R)"));
            refresh.Click += async (sender, args) => await RefreshObservedSubAgentsAsync();
            _subAgentContextMenu.MenuItems.Add(refresh);
            var work = new MenuItem(UiLocalization.Text("SubAgentWorkbenchForm.RecentWork", "View recent work (&W)"));
            work.Click += (sender, args) =>
            {
                var session = GetSelectedListSession();
                if (session == null) return;
                var content = _operations.GetRecentWork?.Invoke(session.ThreadId) ?? session.Preview;
                _operations.WorkbenchCallbacks?.OpenMessageDetail?.Invoke(CreateHistoryItem(
                    session.SubAgentNickname ?? session.Title,
                    (session.ThreadId ?? string.Empty) + Environment.NewLine + (content ?? string.Empty),
                    MyCodex.WinForms.Controls.ChatHistoryCategory.Conversation));
            };
            _subAgentContextMenu.Popup += (sender, args) => work.Enabled = GetSelectedListSession() != null && _operations.WorkbenchCallbacks?.OpenMessageDetail != null;
            _subAgentContextMenu.MenuItems.Add(work);
            if (_operations.RefreshAsync == null) return;
            // Read-only polling deliberately avoids resume configuration and shared subscription side effects.
            _observationTimer = new Timer { Interval = 5000 };
            _observationTimer.Tick += async (sender, args) => await RefreshObservedSubAgentsAsync();
            Shown += async (sender, args) =>
            {
                _observationTimer.Start();
                await RefreshObservedSubAgentsAsync();
            };
            FormClosed += (sender, args) => _observationTimer.Stop();
        }

        internal async Task RefreshObservedSubAgentsAsync()
        {
            if (_refreshingObservation || IsDisposed || _isDeleting) return;
            _refreshingObservation = true;
            try
            {
                if (_operations.RefreshAsync != null) await _operations.RefreshAsync();
                if (IsDisposed) return;
                if (ActiveSession != null) await LoadActiveSessionHistoryAsync(true);
                if (!IsDisposed && ActiveSession == null)
                {
                    SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.Observation", "Read-only refresh every 5 seconds; Enter opens history, F5 refreshes."));
                }
            }
            catch (Exception)
            {
                if (!IsDisposed)
                {
                    _runningTurns.Clear();
                    SetStatus(UiLocalization.Text("SubAgentWorkbenchForm.RefreshFailed", "Refresh failed; status may be outdated. Press F5 to retry."));
                    RefreshActionState();
                }
            }
            finally
            {
                _refreshingObservation = false;
            }
        }

        private void ApplyObservedSnapshot(AppServerThreadDetails details)
        {
            if (details == null || string.IsNullOrWhiteSpace(details.ThreadId)) return;
            var active = details.TurnStatuses.LastOrDefault(pair => string.Equals(pair.Value, "inProgress", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(active.Key)) _runningTurns[details.ThreadId] = active.Key;
            else if (details.ThreadStatus != null)
            {
                if (string.Equals(details.ThreadStatus.Type, "active", StringComparison.Ordinal))
                    _runningTurns[details.ThreadId] = null;
                else _runningTurns.Remove(details.ThreadId);
            }
            foreach (var activity in details.Activities) ApplySubAgentActivityEvent(activity);
            SetStatus(SubAgentDisplayPlanner.BuildStatus(details.ThreadStatus, _operations.GetAgentStatus?.Invoke(details.ThreadId)) +
                "; " + UiLocalization.Text("SubAgentWorkbenchForm.Observation", "Read-only refresh every 5 seconds; Enter opens history, F5 refreshes."));
            RefreshActivityDisplay();
            RefreshActionState();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _observationTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
