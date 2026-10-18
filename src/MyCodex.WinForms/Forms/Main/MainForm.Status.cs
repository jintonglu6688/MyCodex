using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void RefreshHeader()
        {
            var notSelectedText = UiLocalization.Text("MainForm.Header.Value.NotSelected", "(Not selected)");
            var notSyncedText = UiLocalization.Text("MainForm.Header.Value.NotSynced", "Not synced yet");
            var summary = _currentInteractionMode == AppInteractionMode.Office
                ? BuildOfficeHeaderSummary(notSelectedText, notSyncedText)
                : SessionHeaderSummaryBuilder.Build(
                    _currentSession,
                    _currentWorkspace,
                    BuildEffectiveModelSummaryText(_currentSession),
                    _threadSyncSummary,
                    notSelectedText,
                    notSyncedText);
            _workspaceNameValueLabel.Text = summary.TitleText;
            _workspacePathValueLabel.Text = SessionHeaderSummaryBuilder.CombineDetails(
                summary.ContextText,
                summary.StatusText,
                UiLocalization.Text("MainForm.Header.Summary.Separator", "  |  "));
            _branchValueLabel.Text = summary.StatusText;
            RefreshTurnSummaryDisplay();
            RefreshAuthenticationUi();
        }

        private SessionHeaderSummary BuildOfficeHeaderSummary(string notSelectedText, string notSyncedText)
        {
            var titleText = _currentSession != null
                ? SessionDisplayFormatter.BuildDisplayTitle(_currentSession)
                : UiLocalization.Text("MainForm.Header.Office.NoSession", "No office session selected");
            var officeDirectory = _currentWorkspace != null && !string.IsNullOrWhiteSpace(_currentWorkspace.RootPath)
                ? _currentWorkspace.RootPath
                : (notSelectedText ?? string.Empty);
            var contextText = string.Format(
                UiLocalization.Text("MainForm.Header.Office.Context.Template", "Office directory: {0}"),
                officeDirectory);
            var syncText = string.IsNullOrWhiteSpace(_threadSyncSummary)
                ? notSyncedText
                : _threadSyncSummary.Trim();
            var statusText = string.Format(
                UiLocalization.Text("MainForm.Header.Office.Status.Template", "Model: {0} | Sync: {1}"),
                BuildEffectiveModelSummaryText(_currentSession),
                syncText);
            return new SessionHeaderSummary(titleText, contextText, statusText);
        }

        private void RefreshDisplayedSessionTitle(ChatSession session)
        {
            if (session == null ||
                _currentSession == null ||
                !string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase) ||
                _allChatHistoryItems.Count <= 0)
            {
                return;
            }

            var firstItem = _allChatHistoryItems[0];
            if (firstItem == null || !string.Equals(firstItem.Sender, ChatHistoryItemFactory.GetSystemSenderText(), StringComparison.Ordinal))
            {
                return;
            }

            var headerText = BuildDisplayedSessionHeaderText(firstItem, session);
            if (string.IsNullOrWhiteSpace(headerText))
            {
                return;
            }

            var updatedItems = new ChatHistoryItem[_allChatHistoryItems.Count];
            updatedItems[0] = ChatHistoryItemFactory.Create(ChatHistoryItemFactory.GetSystemSenderText(), headerText);
            for (var i = 1; i < _allChatHistoryItems.Count; i++)
            {
                updatedItems[i] = _allChatHistoryItems[i];
            }

            ReplaceChatHistoryItems(updatedItems);
        }

        private void UpdateThreadSyncSummary(string summary)
        {
            _threadSyncSummary = summary;
            RefreshHeader();
            PersistState();
        }

        private void RefreshWindowTitle()
        {
            Text = ComposeWindowTitle();
            _targetWindowGroup?.RefreshTitles();
        }

        private string ComposeWindowTitle()
        {
            return MainFormWindowTitleComposer.BuildWindowTitle(
                _appDisplayName,
                GetWindowStatusText(),
                GetCurrentWindowRuntimeDisplayName(),
                GetCurrentModeWindowTitleText());
        }

        private string GetCurrentWindowRuntimeDisplayName()
        {
            if (_fixedTarget != null)
                return _fixedTarget.Kind == "Windows" ? null : _fixedTarget.Name;
            if (!IsUnixRemoteRuntimeEnabled())
            {
                return null;
            }

            return GetCurrentRuntimeDisplayName();
        }

        private string BuildRuntimeFileWindowTitle(string purpose)
        {
            return MainFormWindowTitleComposer.BuildRuntimeFileWindowTitle(
                GetCurrentWindowRuntimeDisplayName(),
                purpose,
                UiLocalization.Text("AppServerFileBrowserForm.Title", "File Browser"));
        }

        private string GetCurrentRuntimeDisplayName()
        {
            if (_fixedTarget != null) return TargetManagerForm.DisplayName(_fixedTarget);
            if (IsRemoteMacRuntimeEnabled())
            {
                var remoteName = GetCurrentRemoteUnixProfileDisplayName();
                if (!string.IsNullOrWhiteSpace(remoteName))
                {
                    return remoteName;
                }

                var remoteConfig = GetCurrentRemoteMacConfig();
                if (!string.IsNullOrWhiteSpace(remoteConfig.HostName))
                {
                    return remoteConfig.HostName.Trim();
                }

                return UiLocalization.Text("MainForm.Runtime.RemoteUnix", "Remote Unix");
            }

            if (IsWslRuntimeEnabled())
            {
                var wslConfig = GetCurrentWslConfig();
                return string.IsNullOrWhiteSpace(wslConfig.DistributionName)
                    ? UiLocalization.Text("MainForm.Runtime.Wsl", "WSL")
                    : wslConfig.DistributionName.Trim();
            }

            return UiLocalization.Text("MainForm.Runtime.Windows", "Windows");
        }

        private string GetWindowStatusText()
        {
            if (_currentSession != null && IsThreadBusy(_currentSession.ThreadId))
            {
                return SessionDisplayFormatter.BuildWorkingStatusText(_currentSession);
            }

            if (_currentSession != null && IsSessionWorking(_currentSession))
            {
                return UiLocalization.Text("MainForm.WindowStatus.Working", "Working");
            }

            if (_busyThreadIds.Count > 0)
            {
                return UiLocalization.Text("MainForm.WindowStatus.BackgroundWorking", "Sessions working in background");
            }

            if (MainFormAuthenticationStatePlanner.RequiresOfficialLogin(_authenticationState))
            {
                return UiLocalization.Text("MainForm.WindowStatus.NotLoggedIn", "Not logged in");
            }

            return string.Empty;
        }

        private void SetStatus(string text)
        {
            SetStatus(text, StatusPriority.Normal);
        }

        private void SetLowPriorityStatus(string text)
        {
            SetStatus(text, StatusPriority.Low);
        }

        private void SetHighPriorityStatus(string text)
        {
            SetStatus(text, StatusPriority.High);
        }

        private void SetStatus(string text, StatusPriority priority)
        {
            var normalizedText = string.IsNullOrWhiteSpace(text)
                ? UiLocalization.Text("Status.Ready", "Ready")
                : text.Trim();
            var now = DateTime.UtcNow;

            if (string.Equals(_statusLabel.Text, normalizedText, StringComparison.Ordinal))
            {
                if (priority > _currentStatusPriority)
                {
                    _currentStatusPriority = priority;
                }

                _lastStatusUpdatedAtUtc = now;
                return;
            }

            // Skip low-priority status confirmations for a short period after a higher-priority status,
            // so important notifications are not quickly overwritten.
            if (priority < _currentStatusPriority &&
                now - _lastStatusUpdatedAtUtc < TimeSpan.FromMilliseconds(HigherPriorityStatusHoldMilliseconds))
            {
                return;
            }

            _computerUseStatusThread = null;
            _computerUseStatusText = null;
            _statusLabel.Text = normalizedText;
            _currentStatusPriority = priority;
            _lastStatusUpdatedAtUtc = now;

            // Update status indicator icon based on status text
            var statusType = DetermineStatusType(normalizedText);
            _statusIndicator.Image = UiTheme.CreateStatusIndicator(statusType);
        }

        private static UiTheme.StatusType DetermineStatusType(string statusText)
        {
            return MainFormStatusPresenter.DetermineStatusType(statusText);
        }
    }
}
