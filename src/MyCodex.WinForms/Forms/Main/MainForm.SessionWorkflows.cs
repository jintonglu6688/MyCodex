using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async Task ShowForkSessionDialogAsync(ChatSession sourceSession)
        {
            await ShowForkSessionDialogAsync(sourceSession, null, 0, null);
        }

        private async Task ShowForkSessionDialogAsync(
            ChatSession sourceSession,
            string forkTipText,
            int trimTurnsAfterAnchor,
            string lastTurnId)
        {
            if (sourceSession == null || string.IsNullOrWhiteSpace(sourceSession.ThreadId))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.NoSessionAvailableToFork", "There is no session available to fork."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var workspace = FindWorkspaceById(sourceSession.WorkspaceId);
            if (workspace == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.ForkWorkspaceNotFound", "Workspace for this fork was not found."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var forkIndex = GetNextForkIndex(sourceSession);
            var defaultForkTitle = BuildUniqueSessionTitle(
                SessionTitleStrategy.BuildForkTitle(sourceSession.Title, forkIndex),
                true,
                false);
            using (var form = new ForkSessionForm(defaultForkTitle, forkIndex, forkTipText))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    var forkTitle = BuildUniqueSessionTitle(form.ForkTitle, true, false);
                    await CreateForkSessionAsync(sourceSession, workspace, forkTitle, forkIndex, form.SwitchToForkAfterCreate, trimTurnsAfterAnchor, lastTurnId);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CreateForkFailed", "Failed to create fork"));
                }
            }
        }

        private async Task ShowReviewStartDialogAsync(ChatSession sourceSession)
        {
            if (sourceSession == null || string.IsNullOrWhiteSpace(sourceSession.ThreadId))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.NoSessionAvailableToReview", "There is no session available for review."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var workspace = FindWorkspaceById(sourceSession.WorkspaceId);
            if (!IsWorkspaceAvailable(workspace))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.WorkspacePathUnavailableCannotStartReview", "Current workspace path is unavailable. Unable to start review."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!CanReviewSession(sourceSession))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.SessionBusyCannotStartReview", "Current session is processing another task. Please try review again later."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var form = new ReviewStartForm())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    if (form.RunInNewSession)
                    {
                        await StartDetachedReviewAsync(sourceSession, workspace, form.SelectedTarget);
                    }
                    else
                    {
                        await StartInlineReviewAsync(sourceSession, workspace, form.SelectedTarget);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.StartReviewFailed", "Failed to start review"));
                }
            }
        }

        private async Task ShowRenameSessionDialogAsync(ChatSession session)
        {
            if (session == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.NoPaintingTitleToEdit", "There is no session available to edit title."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using (var form = new SessionTitleForm(
                UiLocalization.Text("MainForm.SessionTitle.EditPainting.Title", "Edit session title"),
                UiLocalization.Text("MainForm.SessionTitle.EditPainting.Header", "Edit current session title"),
                UiLocalization.Text("MainForm.SessionTitle.EditPainting.Description", "After saving, the session list updates immediately and syncs to official thread name."),
                session.Title,
                UiLocalization.Text("MainForm.SessionTitle.EditPainting.SaveButton", "Save(&S)"),
                UiLocalization.Text("MainForm.SessionTitle.EditPainting.SaveAccessible", "Save"),
                UiLocalization.Text("MainForm.SessionTitle.Validation.EnterPaintingTitle", "Please enter a session title.")))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    await RenameSessionAsync(session, form.SessionTitle);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.UpdatePaintingTitleFailed", "Failed to update session title"));
                }
            }
        }

        private async Task StartCurrentSessionCompactionAsync()
        {
            var session = _currentSession;
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.CompactContext", "Compact context")))
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.CompactContext", "Compact context")))
            {
                return;
            }

            var workspace = FindWorkspaceById(session.WorkspaceId);
            if (!IsWorkspaceAvailable(workspace))
            {
                AddChatHistoryItem(
                    ChatHistoryItemFactory.GetErrorSenderText(),
                    UiLocalization.Text("MainForm.Message.WorkspacePathUnavailableCannotCompactContext", "Current workspace path is unavailable. Unable to compact context."),
                    ChatHistoryCategory.Error);
                return;
            }

            if (!TryBeginThreadCompaction(session.ThreadId))
            {
                AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), UiLocalization.Text("MainForm.Message.PaintingCompactingWait", "Current session is compacting context. Please wait."));
                return;
            }

            RefreshInputState();

            try
            {
                await _sessionThreadOperationWorkflow.SubmitCompactionAsync(
                    session,
                    workspace,
                    GetEffectiveApprovalMode(session),
                    GetEffectiveModelId(session));
            }
            catch (Exception ex)
            {
                ClearThreadCompactionState(session.ThreadId);
                RefreshInputState();

                if (IsCurrentSessionThread(session.ThreadId))
                {
                    AddChatHistoryItem(
                        ChatHistoryItemFactory.GetErrorSenderText(),
                        string.Format(UiLocalization.Text("MainForm.Message.PaintingCompactionRequestFailed.Template", "Failed to submit context compaction request for current session: {0}"), ex.Message),
                        ChatHistoryCategory.Error);
                }
            }
        }

        private async Task StartForkFromSelectedHistoryItemAsync()
        {
            var selectedItem = GetPrimaryChatHistoryItem();
            int trimTurnsAfterAnchor;
            if (!TryGetForkTrimTurnsFromHistoryItem(selectedItem, out trimTurnsAfterAnchor))
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text(
                        "MainForm.Message.SelectedMessageCannotBeForkAnchor",
                        "Selected message cannot be used as fork anchor. Please use this action on a user question in history."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var forkTipText = trimTurnsAfterAnchor <= 0
                ? UiLocalization.Text(
                    "MainForm.ForkTip.FromAnchorWithoutTrim",
                    "A new fork will be created from the turn containing this question. After creation, mainline and fork can continue independently.")
                : string.Format(
                    UiLocalization.Text(
                        "MainForm.ForkTip.FromAnchorWithTrim.Template",
                        "A new fork will be created, keeping this turn and previous content, and deleting the following {0} turn(s) in the fork. Mainline is unaffected."),
                    trimTurnsAfterAnchor);

            await ShowForkSessionDialogAsync(_currentSession, forkTipText, trimTurnsAfterAnchor, selectedItem.TurnId);
        }

        private async Task StartFullForkFromCurrentSessionAsync()
        {
            await ShowForkSessionDialogAsync(_currentSession);
        }

        private bool TryGetForkTrimTurnsFromHistoryItem(ChatHistoryItem item, out int trimTurnsAfterAnchor)
        {
            return SessionHistoryAnchorPlanner.TryGetForkTrimTurns(
                _currentSession,
                item,
                ChatHistoryItemFactory.IsUserSender,
                out trimTurnsAfterAnchor);
        }

        private bool TryMoveChatHistorySelectionByTurn(int direction)
        {
            if (_chatHistoryListBox.Items.Count <= 0)
            {
                return false;
            }

            var items = _chatHistoryListBox.Items
                .Cast<object>()
                .Select(item => item as ChatHistoryItem)
                .ToArray();
            var index = ChatHistoryTurnNavigationPlanner.ResolveNextUserMessageIndex(
                items,
                _chatHistoryListBox.FocusedIndex,
                direction,
                ChatHistoryItemFactory.IsUserSender);
            if (index < 0)
            {
                return false;
            }

            _chatHistoryListBox.SelectSingleIndex(index);
            EnsureChatHistoryItemVisible(index);
            return true;
        }

        private async Task CreateForkSessionAsync(
            ChatSession sourceSession,
            WorkspaceInfo workspace,
            string title,
            int forkIndex,
            bool switchToForkAfterCreate,
            int trimTurnsAfterAnchor,
            string lastTurnId)
        {
            if (sourceSession == null || workspace == null)
            {
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.CreateFork", "Create fork")))
            {
                return;
            }

            SetStatus(UiLocalization.Text("MainForm.Status.CreatingFork", "Creating fork..."));

            var settings = await ReadSessionModelSettingsAsync(sourceSession, workspace);
            string warning = null;
            var forkSession = await _sessionThreadOperationWorkflow.CreateForkSessionAsync(
                sourceSession,
                workspace,
                title,
                forkIndex,
                lastTurnId,
                GetEffectiveApprovalMode(sourceSession),
                settings.ModelId,
                settings.ReasoningEffort,
                message => warning = message);

            _sessions.Insert(0, forkSession);
            SortSessions();
            ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderInputPersist);

            if (warning != null)
            {
                AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), warning);
                MessageBox.Show(this, warning, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (switchToForkAfterCreate)
            {
                _currentWorkspace = workspace;
                RefreshWorkspaceView();
                try
                {
                    await ActivateSessionAsync(forkSession, true);
                }
                catch (Exception)
                {
                    var message = string.Format(UiLocalization.Text("MainForm.Fork.OpenFailed", "Fork {0} was created and saved, but its history could not be opened. Select the new session to retry."), forkSession.ThreadId);
                    AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), message);
                    SetHighPriorityStatus(message);
                    MessageBox.Show(this, message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                return;
            }

            if (_currentSession != null &&
                string.Equals(_currentSession.Id, sourceSession.Id, StringComparison.OrdinalIgnoreCase))
            {
                var createdText = trimTurnsAfterAnchor > 0
                    ? string.Format(
                        UiLocalization.Text("MainForm.Status.ForkCreatedWithTrim.Template", "Fork created: {0} (trimmed subsequent {1} turn(s))"),
                        SessionDisplayFormatter.BuildDisplayTitle(forkSession),
                        trimTurnsAfterAnchor)
                    : string.Format(UiLocalization.Text("MainForm.Status.ForkCreated.Template", "Fork created: {0}"), SessionDisplayFormatter.BuildDisplayTitle(forkSession));
                AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), createdText);
            }

            SetLowPriorityStatus(trimTurnsAfterAnchor > 0
                ? string.Format(
                    UiLocalization.Text("MainForm.Status.ForkCreatedWithTrim.Template", "Fork created: {0} (trimmed subsequent {1} turn(s))"),
                    SessionDisplayFormatter.BuildDisplayTitle(forkSession),
                    trimTurnsAfterAnchor)
                : string.Format(UiLocalization.Text("MainForm.Status.ForkCreated.Template", "Fork created: {0}"), SessionDisplayFormatter.BuildDisplayTitle(forkSession)));
        }

        private int GetNextForkIndex(ChatSession sourceSession)
        {
            if (sourceSession == null || string.IsNullOrWhiteSpace(sourceSession.ThreadId))
            {
                return 1;
            }

            var maxForkIndex = 0;
            foreach (var session in _sessions)
            {
                if (session == null ||
                    !session.IsFork ||
                    !string.Equals(session.ForkSourceThreadId, sourceSession.ThreadId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (session.ForkIndex.HasValue)
                {
                    maxForkIndex = Math.Max(maxForkIndex, session.ForkIndex.Value);
                }
            }

            return maxForkIndex + 1;
        }

        private string BuildUniqueSessionTitle(string title, bool isFork, bool isReview)
        {
            return SessionTitleStrategy.BuildUniqueTitle(
                title,
                _sessions,
                _currentWorkspace == null ? null : _currentWorkspace.Id,
                isFork,
                isReview);
        }
    }
}
