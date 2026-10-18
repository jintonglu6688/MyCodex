using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Markets;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Dialogs;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Skills;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly Dictionary<string, SessionWorkbenchForm> _sessionWorkbenches =
            new Dictionary<string, SessionWorkbenchForm>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, SessionCommandTerminalHost> _sessionCommandTerminals =
            new Dictionary<string, SessionCommandTerminalHost>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ChatHistorySelectionSnapshot> _sessionWorkbenchMessageSelections =
            new Dictionary<string, ChatHistorySelectionSnapshot>(StringComparer.OrdinalIgnoreCase);
        private MenuItem _sessionOpenInWindowMenuItem;

        private void ConfigureSessionWorkbenchContextMenu()
        {
            _sessionOpenInWindowMenuItem = new MenuItem(
                UiLocalization.Text("MainForm.Session.OpenInWindow", "Open in New Window(&W)"));
            _sessionOpenInWindowMenuItem.Click += delegate { OpenSelectedSessionWorkbench(); };
            _sessionContextMenu.MenuItems.Add(1, _sessionOpenInWindowMenuItem);
        }

        private void RefreshSessionWorkbenchContextMenuState(SessionListItem selectedItem)
        {
            if (_sessionOpenInWindowMenuItem != null)
            {
                _sessionOpenInWindowMenuItem.Enabled = selectedItem != null &&
                    selectedItem.Session != null &&
                    IsWorkspaceAvailable(FindWorkspaceById(selectedItem.Session.WorkspaceId));
            }
        }

        private void OpenSelectedSessionWorkbench()
        {
            var selectedItem = GetSelectedSessionListItem();
            if (selectedItem == null || selectedItem.Session == null)
            {
                return;
            }

            var workspace = FindWorkspaceById(selectedItem.Session.WorkspaceId);
            if (!IsWorkspaceAvailable(workspace))
            {
                return;
            }

            OpenSessionWorkbench(selectedItem.Session, workspace);
        }

        private void OpenSessionWorkbench(ChatSession session, WorkspaceInfo workspace)
        {
            if (_isRuntimeWindowTransitionInProgress ||
                session == null || string.IsNullOrWhiteSpace(session.Id) || !IsWorkspaceAvailable(workspace))
            {
                return;
            }

            SessionWorkbenchForm existing;
            if (_sessionWorkbenches.TryGetValue(session.Id, out existing) &&
                existing != null && !existing.IsDisposed)
            {
                existing.Show();
                if (existing.WindowState == FormWindowState.Minimized)
                {
                    existing.WindowState = FormWindowState.Normal;
                }

                existing.Activate();
                return;
            }

            var runtimeProfileId = SessionRuntimeIdentityResolver.ResolveRuntimeProfileId(
                session,
                GetConfiguredRuntimeProfileId());
            var runtimeScope = AppServerFileWindowRuntimeScopeComposer.FromSnapshot(
                _appConfigSnapshot,
                runtimeProfileId,
                session.Mode);
            var draftScopeKey = GetComposeDraftScopeKey(session);
            var context = new SessionWorkbenchContext(session, workspace, runtimeScope, draftScopeKey);
            SessionWorkbenchForm form = null;
            form = new SessionWorkbenchForm(
                context,
                CreateSessionWorkbenchOperations(context, () => form));
            _sessionWorkbenches[session.Id] = form;
            form.FormClosed += (sender, e) => RemoveSessionWorkbenchRegistration(session.Id, sender as SessionWorkbenchForm);
            form.Show();
        }

        private SessionWorkbenchOperations CreateSessionWorkbenchOperations(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider)
        {
            return new SessionWorkbenchOperations
            {
                HistoryLoadWorkflow = new SessionHistoryLoadWorkflow(
                    new SessionHistoryStateStore(),
                    _sessionHistoryDisplayBuilder,
                    _appServerClient.ReadThreadAsync,
                    SessionThreadService.IsMissingThreadRolloutError,
                    IsAppServerConnectivityFailure),
                WorkbenchCallbacks = CreateSessionWorkbenchCallbacks(context, formProvider),
                ManualBrowseContext = GetOrCreateSessionManualBrowseContext(context.Session.Id),
                SendAsync = (session, request) => SendSessionWorkbenchInputAsync(session, context.Workspace, request),
                PlayMessageSentSound = PlayMessageSentSoundIfEnabled,
                CanCloseWithEscape = () => _appConfigSnapshot == null || _appConfigSnapshot.CloseSessionWorkbenchOnEscape,
                SaveMessageSelection = snapshot => SaveSessionWorkbenchMessageSelection(context.Session.Id, snapshot),
                LoadMessageSelection = () => GetSessionWorkbenchMessageSelection(context.Session.Id),
                StopAsync = StopSessionWorkbenchAsync,
                IsWorking = session => session != null && IsThreadBusy(session.ThreadId),
                OpenFileBrowser = () => _appServerFileWindowManager.OpenBrowser(context.Workspace.RootPath, context.RuntimeScope),
                OpenGitWorkbench = () => ShowGitWorkbenchForWorkspace(context.Workspace),
                OpenSubAgents = () => OpenSubAgentWorkbench(context.Session, GetSubAgentsForParent(context.Session)),
                CanOpenSubAgents = () => !string.IsNullOrWhiteSpace(context.Session.ThreadId),
                OpenTerminal = () => ShowSessionCommandTerminal(context.Session, context.Workspace),
                LoadDraft = () => _composeDraftStore.Get(context.DraftScopeKey, context.Session.Id),
                SaveDraft = draft => _composeDraftStore.Save(context.DraftScopeKey, context.Session.Id, draft != null ? draft.DraftText : string.Empty, draft != null ? draft.Attachments : null),
                ClearDraft = () => _composeDraftStore.Remove(context.DraftScopeKey, context.Session.Id, false, DeleteTemporaryAttachmentFile),
                DiscardDraft = () => _composeDraftStore.Remove(context.DraftScopeKey, context.Session.Id, true, DeleteTemporaryAttachmentFile),
                PersistState = PersistState
            };
        }

        private ChatWorkbenchHostCallbacks CreateSessionWorkbenchCallbacks(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider)
        {
            return new ChatWorkbenchHostCallbacks
            {
                SelectImageAttachmentsAsync = () => SelectSessionWorkbenchImageAttachmentsAsync(formProvider),
                SelectFileAttachmentsAsync = () => SelectSessionWorkbenchFileAttachmentsAsync(context, formProvider),
                SelectFolderAttachmentsAsync = () => SelectSessionWorkbenchFolderAttachmentsAsync(context, formProvider),
                SelectSkillAttachmentsAsync = () => SelectSessionWorkbenchSkillAttachmentsAsync(context, formProvider),
                SelectPluginAttachmentsAsync = () => SelectSessionWorkbenchPluginAttachmentsAsync(context, formProvider),
                CanPasteAttachments = () => CanPasteSessionWorkbenchAttachments(context),
                PasteAttachmentsAsync = () => PasteSessionWorkbenchAttachmentsAsync(context, formProvider),
                ValidateAttachmentsAsync = ValidateSubAgentWorkbenchAttachmentsAsync,
                ReadImageBytes = GetImageAttachmentBytes,
                OpenMessageDetail = item => OpenSessionWorkbenchMessageDetail(formProvider, item),
                GetSendMessageShortcutMode = GetConfiguredSendMessageShortcutMode,
                DeleteTemporaryAttachment = DeleteTemporaryAttachmentFile
            };
        }

        private Task<IReadOnlyList<PendingAttachmentItem>> SelectSessionWorkbenchImageAttachmentsAsync(Func<SessionWorkbenchForm> formProvider)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = UiLocalization.Text("MainForm.FileDialog.Image.Filter", "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All Files|*.*");
                dialog.Multiselect = true;
                dialog.Title = UiLocalization.Text("MainForm.FileDialog.Image.Title", "Select image");
                if (dialog.ShowDialog(GetSessionWorkbenchOwner(formProvider)) != DialogResult.OK)
                {
                    return Task.FromResult((IReadOnlyList<PendingAttachmentItem>)new PendingAttachmentItem[0]);
                }

                return Task.FromResult((IReadOnlyList<PendingAttachmentItem>)CreateSubAgentWorkbenchAttachments(
                    dialog.FileNames.Select(path => new AttachmentCandidate(path, false, AttachmentKind.Image, null))));
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSessionWorkbenchFileAttachmentsAsync(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider)
        {
            if (IsUnixRemoteRuntimeEnabled())
            {
                return await SelectSessionWorkbenchUnixAttachmentsAsync(context, formProvider, false);
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = UiLocalization.Text("MainForm.FileDialog.All.Filter", "All Files|*.*");
                dialog.Multiselect = true;
                dialog.Title = UiLocalization.Text("MainForm.FileDialog.File.Title", "Select file");
                if (dialog.ShowDialog(GetSessionWorkbenchOwner(formProvider)) != DialogResult.OK)
                {
                    return new PendingAttachmentItem[0];
                }

                return CreateSubAgentWorkbenchAttachments(
                    dialog.FileNames.Select(path => new AttachmentCandidate(path, false, AttachmentKind.File, null)));
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSessionWorkbenchFolderAttachmentsAsync(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider)
        {
            if (IsUnixRemoteRuntimeEnabled())
            {
                return await SelectSessionWorkbenchUnixAttachmentsAsync(context, formProvider, true);
            }

            string selectedPath;
            if (!WindowsFolderPicker.TryPickFolder(
                GetSessionWorkbenchOwner(formProvider),
                UiLocalization.Text("MainForm.DialogDescription.SelectFolderToAttach", "Select folder to attach"),
                GetSessionWorkbenchDefaultLocalPath(context),
                out selectedPath))
            {
                return new PendingAttachmentItem[0];
            }

            return CreateSubAgentWorkbenchAttachments(new[]
            {
                new AttachmentCandidate(selectedPath, false, AttachmentKind.File, null, true, false)
            });
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSessionWorkbenchSkillAttachmentsAsync(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider)
        {
            if (!IsWorkspaceAvailable(context.Workspace) ||
                !await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.LoadSkill", "Load skill")))
            {
                return new PendingAttachmentItem[0];
            }

            try
            {
                var snapshots = await _appServerClient.ListSkillsAsync(new[] { context.Workspace.RootPath }, true);
                var skills = (snapshots ?? new List<AppServerSkillsSnapshot>())
                    .Where(snapshot => snapshot != null && snapshot.Skills != null)
                    .SelectMany(snapshot => snapshot.Skills)
                    .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.Path))
                    .OrderByDescending(skill => skill.Enabled)
                    .ThenBy(GetSkillInsertionDisplayName, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                if (skills.Count == 0)
                {
                    return new PendingAttachmentItem[0];
                }

                var pluginSources = await SkillSourcePluginLoader.TryLoadAsync(_appServerClient);
                using (var form = new InsertSkillForm(skills, context.Workspace.RootPath, pluginSources))
                {
                    return form.ShowDialog(GetSessionWorkbenchOwner(formProvider)) == DialogResult.OK && form.SelectedSkill != null
                        ? CreateSubAgentWorkbenchSkillAttachment(form.SelectedSkill)
                        : new PendingAttachmentItem[0];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(GetSessionWorkbenchOwner(formProvider), ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new PendingAttachmentItem[0];
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSessionWorkbenchPluginAttachmentsAsync(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.LoadPlugin", "Load plugin")))
            {
                return new PendingAttachmentItem[0];
            }

            try
            {
                var installed = await AppServerInstalledPluginObservationService.LoadAsync(
                    _appServerClient,
                    _appServerClient.ProtocolCapabilities,
                    System.Threading.CancellationToken.None);
                var insertablePlugins = PluginAttachmentSelectionBuilder.BuildInsertablePlugins(installed.Plugins);
                if (insertablePlugins.Count == 0)
                {
                    MessageBox.Show(
                        GetSessionWorkbenchOwner(formProvider),
                        UiLocalization.Text("MainForm.Message.NoAvailablePlugins", "No installed plugins are available to insert."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return new PendingAttachmentItem[0];
                }

                using (var form = new InsertPluginForm(insertablePlugins))
                {
                    if (form.ShowDialog(GetSessionWorkbenchOwner(formProvider)) != DialogResult.OK || form.SelectedPlugin == null)
                    {
                        return new PendingAttachmentItem[0];
                    }

                    var plugin = form.SelectedPlugin;
                    var displayName = GetPluginInsertionDisplayName(plugin);
                    return new[]
                    {
                        new PendingAttachmentItem(
                            plugin.SourcePath,
                            displayName,
                            false,
                            AttachmentKind.Plugin,
                            ComposeAttachmentState.BuildAttachmentMarkerText(null, AttachmentKind.Plugin, displayName, plugin.SourcePath),
                            null,
                            string.IsNullOrWhiteSpace(plugin.Name) ? displayName : plugin.Name,
                            true,
                            context.RuntimeScope != null &&
                            (string.Equals(context.RuntimeScope.RuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) ||
                             CodexRuntimeSources.IsRemoteUnixSsh(context.RuntimeScope.RuntimeSource)))
                    };
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(GetSessionWorkbenchOwner(formProvider), ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new PendingAttachmentItem[0];
            }
        }

        private bool CanPasteSessionWorkbenchAttachments(SessionWorkbenchContext context)
        {
            try
            {
                IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths;
                return TryGetAppServerFileBrowserClipboardPaths(context.RuntimeScope, out copiedPaths) ||
                    Clipboard.ContainsImage() ||
                    Clipboard.ContainsFileDropList();
            }
            catch
            {
                return false;
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> PasteSessionWorkbenchAttachmentsAsync(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider)
        {
            try
            {
                IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths;
                if (TryGetAppServerFileBrowserClipboardPaths(context.RuntimeScope, out copiedPaths))
                {
                    return await CreateSessionWorkbenchRuntimeClipboardAttachmentsAsync(copiedPaths, formProvider);
                }

                return CreateSubAgentWorkbenchClipboardAttachments();
            }
            catch (Exception ex)
            {
                MessageBox.Show(GetSessionWorkbenchOwner(formProvider), ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new PendingAttachmentItem[0];
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> CreateSessionWorkbenchRuntimeClipboardAttachmentsAsync(
            IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths,
            Func<SessionWorkbenchForm> formProvider)
        {
            var paths = (copiedPaths ?? Array.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RuntimeFileBrowserSelectedPath(item.Path.Trim(), item.IsDirectory))
                .GroupBy(item => item.Path, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            foreach (var path in paths)
            {
                if (await UnixRuntimePathExistsAsync(path.Path, path.IsDirectory))
                {
                    continue;
                }

                MessageBox.Show(
                    GetSessionWorkbenchOwner(formProvider),
                    string.Format(
                        UiLocalization.Text(
                            path.IsDirectory
                                ? "MainForm.Message.RemoteAttachmentFolderNotFound.Template"
                                : "MainForm.Message.RemoteAttachmentFileNotFound.Template",
                            path.IsDirectory
                                ? "The runtime folder was not found: {0}"
                                : "The runtime file was not found: {0}"),
                        path.Path),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return new PendingAttachmentItem[0];
            }

            return CreateSubAgentWorkbenchAttachments(paths.Select(path => new AttachmentCandidate(
                path.Path,
                false,
                AttachmentKind.File,
                null,
                path.IsDirectory,
                true)));
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSessionWorkbenchUnixAttachmentsAsync(
            SessionWorkbenchContext context,
            Func<SessionWorkbenchForm> formProvider,
            bool isFolder)
        {
            var selectedPaths = new List<RuntimeFileBrowserSelectedPath>();
            using (var form = new RuntimeFileBrowserForm(
                BuildRuntimeFileWindowTitle(UiLocalization.Text(
                    isFolder ? "RuntimeFileBrowserForm.Title.AttachFolder" : "RuntimeFileBrowserForm.Title.AttachFile",
                    isFolder ? "Attach Runtime Folder" : "Attach Runtime File")),
                GetSessionWorkbenchDefaultRemotePath(context),
                isFolder ? RuntimeFileBrowserSelectionMode.Directory : RuntimeFileBrowserSelectionMode.File,
                ListCurrentUnixRuntimeDirectoryAsync,
                true))
            {
                form.PathSelected += (sender, e) => selectedPaths = (e.SelectedPaths ?? Array.Empty<RuntimeFileBrowserSelectedPath>()).ToList();
                form.ShowDialog(GetSessionWorkbenchOwner(formProvider));
            }

            var paths = selectedPaths
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => item.Path.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            foreach (var path in paths)
            {
                if (!await UnixRuntimePathExistsAsync(path, isFolder))
                {
                    MessageBox.Show(GetSessionWorkbenchOwner(formProvider), path, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return new PendingAttachmentItem[0];
                }
            }

            return CreateSubAgentWorkbenchAttachments(paths.Select(path =>
                new AttachmentCandidate(path, false, AttachmentKind.File, null, isFolder, true)));
        }

        private static IWin32Window GetSessionWorkbenchOwner(Func<SessionWorkbenchForm> formProvider)
        {
            var form = formProvider != null ? formProvider() : null;
            return form != null && !form.IsDisposed ? (IWin32Window)form : null;
        }

        private static string GetSessionWorkbenchDefaultLocalPath(SessionWorkbenchContext context)
        {
            return context != null && context.Workspace != null && Directory.Exists(context.Workspace.RootPath)
                ? context.Workspace.RootPath
                : Environment.CurrentDirectory;
        }

        private string GetSessionWorkbenchDefaultRemotePath(SessionWorkbenchContext context)
        {
            return context != null && context.Workspace != null && IsUnixRuntimePath(context.Workspace.RootPath)
                ? context.Workspace.RootPath
                : GetDefaultUnixWorkspacePath();
        }

        private void OpenSessionWorkbenchMessageDetail(Func<SessionWorkbenchForm> formProvider, ChatHistoryItem item)
        {
            ChatMessageViewerLauncher.Show(
                GetSessionWorkbenchOwner(formProvider),
                item,
                GetConfiguredMessageDetailPreviewMode(),
                text => { });
        }

        private async Task<ChatWorkbenchSendResult> SendSessionWorkbenchInputAsync(
            ChatSession session,
            WorkspaceInfo workspace,
            ChatWorkbenchSendRequest request)
        {
            if (session == null || request == null || !IsWorkspaceAvailable(workspace))
            {
                return new ChatWorkbenchSendResult(false, false, UiLocalization.Text("SubAgentWorkbenchForm.Status.WorkspaceUnavailable", "Workspace is unavailable"));
            }

            var planCommand = ParsePlanSlashCommand(request.RawText);
            if (planCommand.IsPlanCommand && string.IsNullOrWhiteSpace(planCommand.PromptText))
            {
                SetSessionPlanMode(session, true, true);
                PersistState();
                return new ChatWorkbenchSendResult(true, true, false, string.Empty);
            }

            var dispatchText = planCommand.IsPlanCommand ? planCommand.PromptText : request.RawText;
            var inputItems = BuildOutgoingInputItems(dispatchText, request.Attachments);
            var validation = SendInputPreflight.EvaluateReadyToDispatch(
                IsWorkspaceAvailable(workspace),
                inputItems.Count);
            if (!validation.CanContinue)
            {
                return new ChatWorkbenchSendResult(false, false, validation.StatusText);
            }

            var preparedSettings = await PrepareSendSettingsAsync(session, workspace);
            if (preparedSettings == null)
                return new ChatWorkbenchSendResult(false, false, UiLocalization.Text("SubAgentWorkbenchForm.Status.AppServerUnavailable", "App Server is unavailable"));

            var approvalMode = GetEffectiveApprovalMode(session);
            var modelId = preparedSettings.ModelId;
            var reasoningEffort = preparedSettings.ReasoningEffort;
            if (planCommand.IsPlanCommand)
            {
                SetSessionPlanMode(session, true, true);
            }

            var previousThreadId = session.ThreadId;
            if (string.IsNullOrWhiteSpace(session.ThreadId))
            {
                await _sessionThreadService.RecreateThreadAsync(
                    session,
                    workspace,
                    approvalMode,
                    modelId,
                    reasoningEffort,
                    BuildThreadToolContext(session, workspace), true, preparedSettings);
                ResetThreadVolatileState(previousThreadId);
            }

            var collaborationMode = BuildCollaborationModeForSend(session, workspace, modelId, reasoningEffort);
            var toolContext = BuildThreadToolContext(session, workspace);
            var decision = MainFormSendDispatchPlanner.Choose(new MainFormSendDispatchContext
            {
                ThreadId = session.ThreadId,
                IsThreadRunning = IsThreadCurrentlyRunning(session.ThreadId),
                ActiveTurnFollowUpMode = GetConfiguredActiveTurnFollowUpMode(),
                InputItemCount = inputItems.Count,
                IsPlanCommand = planCommand.IsPlanCommand,
                HasSpecialCollaborationPayload = collaborationMode != null,
                ThreadWasRecreatedDuringSend = !string.Equals(previousThreadId, session.ThreadId, StringComparison.OrdinalIgnoreCase)
            });
            if (decision.Mode == MainFormSendDispatchMode.Queue)
            {
                EnqueueFollowUpMessage(
                    session,
                    workspace,
                    inputItems,
                    approvalMode,
                    modelId,
                    reasoningEffort,
                    collaborationMode,
                    toolContext,
                    dispatchText,
                    request.PreviewText,
                    request.Attachments);
                await CompleteSessionWorkbenchSendAsync(
                    SessionSendCompletionPlanner.Build(new SessionSendResult(), session, workspace, dispatchText, request.PreviewText),
                    session, false);
                return new ChatWorkbenchSendResult(
                    true,
                    true,
                    true,
                    UiLocalization.Text("MainForm.Status.MessageQueued", "Message queued"));
            }

            if (decision.Mode == MainFormSendDispatchMode.TurnSteer)
            {
                MarkLocalDesktopRealtimeTurn(session.ThreadId);
                var steer = await _appServerClient.TrySteerTurnAsync(session.ThreadId, inputItems);
                if (steer != null && steer.Accepted)
                {
                    session.LastActiveAt = DateTime.Now;
                    await CompleteSessionWorkbenchSendAsync(
                        SessionSendCompletionPlanner.Build(new SessionSendResult(), session, workspace, dispatchText, request.PreviewText),
                        session);
                    return new ChatWorkbenchSendResult(true, true, true, UiLocalization.Text("SubAgentWorkbenchForm.Status.MessageSent", "Message sent"));
                }

                ClearLocalDesktopRealtimeTurn(session.ThreadId);
            }

            MarkLocalDesktopRealtimeTurn(session.ThreadId);
            RegisterPendingInvalidEncryptedContentRetry(
                session,
                workspace,
                inputItems,
                approvalMode,
                modelId,
                reasoningEffort,
                collaborationMode,
                toolContext,
                dispatchText,
                request.PreviewText);
            var result = await _sessionThreadService.SendUserMessageAsync(
                session,
                workspace,
                inputItems,
                approvalMode,
                modelId,
                reasoningEffort,
                collaborationMode,
                toolContext, preparedSettings, deferMetadata: true);
            MarkCollaborationModeSent(session, collaborationMode);
            await CompleteSessionWorkbenchSendAsync(
                SessionSendCompletionPlanner.Build(result, session, workspace, dispatchText, request.PreviewText),
                session);
            return new ChatWorkbenchSendResult(true, true, true, UiLocalization.Text("SubAgentWorkbenchForm.Status.MessageSent", "Message sent"));
        }

        private Task CompleteSessionWorkbenchSendAsync(SessionSendCompletionPlan completionPlan, ChatSession session, bool acknowledged = true)
        {
            completionPlan?.ApplyLocalSessionTitleUpdate(session);
            if (acknowledged) ScheduleSendMetadataUpdate(session);

            SortSessions();
            PersistState();
            RefreshSessionList();
            return Task.CompletedTask;
        }

        private async Task StopSessionWorkbenchAsync(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId) ||
                !await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.StopResponse", "Stop response")))
            {
                return;
            }

            await _appServerClient.InterruptAsync(session.ThreadId, GetInterruptibleTurnId(session.ThreadId));
        }

        private void ShowSessionCommandTerminal(ChatSession session, WorkspaceInfo workspace)
        {
            if (_isRuntimeWindowTransitionInProgress ||
                session == null || string.IsNullOrWhiteSpace(session.Id) || !IsWorkspaceAvailable(workspace))
            {
                return;
            }

            var sessionId = session.Id;
            GetOrCreateSessionManualBrowseContext(sessionId);
            SessionCommandTerminalHost host;
            if (!_sessionCommandTerminals.TryGetValue(session.Id, out host))
            {
                host = new SessionCommandTerminalHost(
                    _appServerClient,
                    session,
                    workspace,
                    GetEffectiveApprovalMode,
                    () => EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.RunCommand", "Run command")));
                host.ScreenReaderHotkeyPressed += (sender, e) => HandleSessionTerminalScreenReaderHotkey(sessionId, e);
                host.TerminalBufferReset += (sender, e) => HandleSessionTerminalBufferReset(sessionId);
                _sessionCommandTerminals[sessionId] = host;
            }

            host.Show();
        }

        private IReadOnlyList<ChatSession> GetSubAgentsForParent(ChatSession parentSession)
        {
            return parentSession == null || string.IsNullOrWhiteSpace(parentSession.ThreadId)
                ? new ChatSession[0]
                : SessionDescendantCollector.Collect(parentSession, _sessions)
                    .Where(session => !ReferenceEquals(session, parentSession) && SessionListProjection.IsSubAgent(session))
                    .ToArray();
        }

        private void RefreshSessionWorkbenches()
        {
            foreach (var pair in _sessionWorkbenches.ToArray())
            {
                var session = _sessions.FirstOrDefault(item => item != null && string.Equals(item.Id, pair.Key, StringComparison.OrdinalIgnoreCase));
                if (session == null)
                {
                    CloseSessionWorkbench(pair.Key);
                    continue;
                }

                if (pair.Value == null || pair.Value.IsDisposed)
                {
                    _sessionWorkbenches.Remove(pair.Key);
                    continue;
                }

                pair.Value.RefreshSession(session);
            }
        }

        private void ForwardAppServerEventToSessionWorkbenches(AppServerEventArgs e)
        {
            foreach (var pair in _sessionWorkbenches.Values.ToArray())
            {
                if (pair != null && !pair.IsDisposed)
                {
                    pair.HandleAppServerEvent(e);
                }
            }

            foreach (var host in _sessionCommandTerminals.Values.ToArray())
            {
                host?.HandleAppServerEvent(e);
            }
        }

        private void CloseSessionWorkbench(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            SessionCommandTerminalHost terminal;
            if (_sessionCommandTerminals.TryGetValue(sessionId, out terminal))
            {
                _sessionCommandTerminals.Remove(sessionId);
                terminal.Close();
            }

            SessionWorkbenchForm form;
            if (_sessionWorkbenches.TryGetValue(sessionId, out form))
            {
                _sessionWorkbenches.Remove(sessionId);
                if (form != null && !form.IsDisposed)
                {
                    form.CloseForSessionRemoval();
                }
            }

            _sessionManualBrowseContexts.Remove(sessionId);
            _sessionWorkbenchMessageSelections.Remove(sessionId);
        }

        private void CloseSessionWorkbenchForms()
        {
            var sessionIds = _sessionWorkbenches.Keys
                .Concat(_sessionCommandTerminals.Keys)
                .Concat(_sessionManualBrowseContexts.Keys)
                .Concat(_sessionWorkbenchMessageSelections.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            foreach (var sessionId in sessionIds)
            {
                CloseSessionWorkbench(sessionId);
            }
        }

        private void RemoveSessionWorkbenchRegistration(string sessionId, SessionWorkbenchForm form)
        {
            SessionWorkbenchForm registered;
            if (_sessionWorkbenches.TryGetValue(sessionId, out registered) && ReferenceEquals(registered, form))
            {
                _sessionWorkbenches.Remove(sessionId);
            }

        }

        private void SaveSessionWorkbenchMessageSelection(string sessionId, ChatHistorySelectionSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return;
            }

            if (snapshot == null)
            {
                _sessionWorkbenchMessageSelections.Remove(sessionId);
                return;
            }

            _sessionWorkbenchMessageSelections[sessionId] = snapshot;
        }

        private ChatHistorySelectionSnapshot GetSessionWorkbenchMessageSelection(string sessionId)
        {
            ChatHistorySelectionSnapshot snapshot;
            return !string.IsNullOrWhiteSpace(sessionId) &&
                   _sessionWorkbenchMessageSelections.TryGetValue(sessionId, out snapshot)
                ? snapshot
                : null;
        }

    }
}
