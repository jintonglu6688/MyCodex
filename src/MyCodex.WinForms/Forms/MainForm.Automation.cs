using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.Capabilities;
using MyCodex.Application.Interfaces;
using MyCodex.Application.InternalTools;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.Infrastructure.Markets;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.Infrastructure.Updates;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.RemoteHost;
using MyCodex.WinForms.Skills;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private async void ShowAutomationsForm(AutomationCreationContext creationContext)
        {
            if (_automationStore == null)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Automation.Status.Unavailable", "Automation service is unavailable."));
                return;
            }

            if (_automationsForm == null || _automationsForm.IsDisposed)
            {
                _automationsForm = new AutomationsForm(
                    _automationStore,
                    _automationService,
                    _appConfigService.GetAutomationWorkspaceDirectory(),
                    CreateAutomationInputSelectionCallbacks(),
                    _automationRunner,
                    CreateAutomationRunActionCallbacks(),
                    CreateAutomationModelSelectionCallbacks(),
                    CreateAutomationRunActivityCallbacks());
                _automationsForm.FormClosed += (sender, e) => _automationsForm = null;
            }

            _automationsForm.AssignPendingAutomations = _targetCatalog == null ? (Action)null : ShowPendingAutomationAssignments;
            _automationsForm.SetTargetCreationContext(new AutomationCreationContext
            {
                Kind = AutomationKind.Standalone,
                InteractionMode = _currentInteractionMode,
                RuntimeProfileId = GetConfiguredRuntimeProfileId(),
                OfficeRuntimeScopeId = _currentInteractionMode == AppInteractionMode.Office ? GetActiveOfficeRuntimeTarget().RuntimeScopeId : null,
                WorkspacePath = _currentInteractionMode == AppInteractionMode.Office && _currentWorkspace != null
                    ? _currentWorkspace.RootPath
                    : _fixedTarget == null || _fixedTarget.Kind == "Windows"
                    ? _appConfigService.GetAutomationWorkspaceDirectory()
                    : _currentWorkspace == null ? null : _currentWorkspace.RootPath,
                OfficeRootPath = _currentInteractionMode == AppInteractionMode.Office ? _resolvedOfficeRootPath : null
            });

            _automationsForm.Show(this);
            _automationsForm.BringToFront();

            if (creationContext == null)
            {
                return;
            }

            if (_automationService == null)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Automation.Status.Unavailable", "Automation service is unavailable."));
                return;
            }

            var modelOptions = await LoadAutomationModelOptionsAsync().ConfigureAwait(true);
            using (var form = new AutomationEditForm(
                creationContext,
                CreateAutomationInputSelectionCallbacks(),
                modelOptions,
                SupportsReasoningEffortForModel))
            {
                if (form.ShowDialog(_automationsForm) == DialogResult.OK && form.Request != null)
                {
                    _automationService.Create(form.Request);
                }
            }
        }

        private AutomationInputSelectionCallbacks CreateAutomationInputSelectionCallbacks()
        {
            return new AutomationInputSelectionCallbacks
            {
                SelectSkillAsync = SelectAutomationSkillInputAsync,
                SelectPluginAsync = SelectAutomationPluginInputAsync
            };
        }

        private void ShowPendingAutomationAssignments()
        {
            try
            {
                using (var form = new PendingAutomationAssignmentForm(_targetCatalog))
                {
                    form.ShowDialog(_automationsForm);
                }
            }
            catch (Exception ex)
            {
                SetHighPriorityStatus(string.Format(UiLocalization.Text("Automation.Assignment.Failed", "Assignment failed: {0}"), ex.Message));
            }
        }

        private AutomationModelSelectionCallbacks CreateAutomationModelSelectionCallbacks()
        {
            return new AutomationModelSelectionCallbacks
            {
                LoadModelsAsync = LoadAutomationModelOptionsAsync,
                SupportsReasoningEffortForModel = SupportsReasoningEffortForModel
            };
        }

        private async Task<IReadOnlyList<AppServerModelOption>> LoadAutomationModelOptionsAsync()
        {
            await EnsureAvailableModelsLoadedAsync().ConfigureAwait(true);
            return MainFormModelOptionComposer.BuildOrderedModelOptions(_availableModels);
        }

        private void ConfigureAutomationModelResolver()
        {
            var runner = _automationRunner as AutomationRunner;
            if (runner == null)
            {
                return;
            }

            runner.ModelResolver = new AutomationModelResolver(
                LoadAutomationModelOptionsAsync,
                GetConfiguredDefaultModelId,
                GetConfiguredDefaultReasoningEffort,
                SupportsReasoningEffortForModel,
                ReadAutomationModelSettingsAsync);
        }

        private AutomationRunActionCallbacks CreateAutomationRunActionCallbacks()
        {
            return new AutomationRunActionCallbacks
            {
                CanStopRun = CanStopAutomationRun,
                StopRunAsync = StopAutomationRunAsync,
                CanOpenThread = CanOpenAutomationThread,
                OpenThreadAsync = OpenAutomationThreadAsync,
                DeleteLocalHistoryAsync = DeleteAutomationRunLocalHistoryAsync,
                OpenPathAsync = OpenAutomationPathAsync,
                LoadConversationAsync = LoadAutomationRunConversationAsync
            };
        }

        private AutomationRunActivityCallbacks CreateAutomationRunActivityCallbacks()
        {
            return new AutomationRunActivityCallbacks
            {
                GetActivityItems = GetAutomationRunActivityItems,
                SubscribeActivityUpdates = SubscribeAutomationRunActivityUpdates
            };
        }

        private IReadOnlyList<string> GetAutomationRunActivityItems(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return new[] { UiLocalization.Text("AutomationRunDetailsForm.Activity.Empty", "No activity records currently.") };
            }

            ActivityTimelineState state;
            if (!_threadActivityTimelineStates.TryGetValue(threadId, out state))
            {
                return new[] { UiLocalization.Text("AutomationRunDetailsForm.Activity.Empty", "No activity records currently.") };
            }

            return state.BuildDisplayItems(
                UiLocalization.Text("AutomationRunDetailsForm.Activity.Empty", "No activity records currently."),
                _appConfigSnapshot);
        }

        private IDisposable SubscribeAutomationRunActivityUpdates(string threadId, Action<IReadOnlyList<string>> onUpdated)
        {
            if (string.IsNullOrWhiteSpace(threadId) || onUpdated == null)
            {
                return new DisposableAction(delegate { });
            }

            List<Action<IReadOnlyList<string>>> subscribers;
            if (!_automationRunActivitySubscribers.TryGetValue(threadId, out subscribers))
            {
                subscribers = new List<Action<IReadOnlyList<string>>>();
                _automationRunActivitySubscribers[threadId] = subscribers;
            }

            subscribers.Add(onUpdated);
            return new DisposableAction(() =>
            {
                List<Action<IReadOnlyList<string>>> current;
                if (!_automationRunActivitySubscribers.TryGetValue(threadId, out current))
                {
                    return;
                }

                current.Remove(onUpdated);
                if (current.Count == 0)
                {
                    _automationRunActivitySubscribers.Remove(threadId);
                }
            });
        }

        private void NotifyAutomationRunActivitySubscribers(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            List<Action<IReadOnlyList<string>>> subscribers;
            if (!_automationRunActivitySubscribers.TryGetValue(threadId, out subscribers) || subscribers.Count == 0)
            {
                return;
            }

            var items = GetAutomationRunActivityItems(threadId);
            var snapshot = subscribers.ToArray();
            for (var i = 0; i < snapshot.Length; i++)
            {
                snapshot[i](items);
            }
        }

        private bool CanStopAutomationRun(AutomationRunSnapshot run)
        {
            return AutomationRunActionPlanner.CanStop(run, CanInterruptThread);
        }

        private async Task StopAutomationRunAsync(AutomationRunSnapshot run)
        {
            if (!CanStopAutomationRun(run))
            {
                throw new InvalidOperationException(UiLocalization.Text("AutomationsForm.Status.NoStoppableRun", "Select a running automation run first."));
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("AutomationsForm.Action.StopRun", "Stop automation run")))
            {
                throw new InvalidOperationException(UiLocalization.Text("AutomationsForm.Status.AppServerUnavailableForStop", "App Server is not connected. Unable to stop automation."));
            }

            var turnId = string.IsNullOrWhiteSpace(run.TurnId)
                ? GetInterruptibleTurnId(run.ThreadId)
                : run.TurnId;
            await _appServerClient.InterruptAsync(run.ThreadId, turnId);
        }

        private async Task DeleteAutomationRunLocalHistoryAsync(AutomationRunSnapshot run)
        {
            if (run == null || string.IsNullOrWhiteSpace(run.ThreadId))
            {
                throw new InvalidOperationException(UiLocalization.Text("AutomationsForm.Status.ThreadNotFound", "Automation thread was not found."));
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("AutomationsForm.Action.DeleteRunLocalHistory", "Delete automation local history")))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.AppServerDisconnectedCannotDeleteSession", "App Server is not connected. Unable to delete session."));
            }

            var session = FindSessionByThreadId(run.ThreadId);
            var target = session ?? new ChatSession
            {
                ThreadId = run.ThreadId,
                Title = string.IsNullOrWhiteSpace(run.AutomationId) ? run.ThreadId : run.AutomationId
            };

            if (await DeleteSessionFilesAsync(new List<ChatSession> { target }) == 0) throw new OperationCanceledException();

            if (session != null)
            {
                RemoveSessionLocally(session);
                FinalizeDeletionUiState();
                PersistState();
            }
        }

        private async Task<IReadOnlyList<ChatHistoryItem>> LoadAutomationRunConversationAsync(AutomationRunSnapshot run)
        {
            if (run == null || string.IsNullOrWhiteSpace(run.ThreadId))
            {
                return new[]
                {
                    ChatHistoryItemFactory.Create(
                        ChatHistoryItemFactory.GetSystemSenderText(),
                        UiLocalization.Text("AutomationRunDetailsForm.Conversation.NoThread", "This automation run has no associated thread."),
                        ChatHistoryCategory.System)
                };
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("AutomationsForm.Action.LoadConversation", "Load automation messages")).ConfigureAwait(true))
            {
                return new[]
                {
                    ChatHistoryItemFactory.Create(
                        ChatHistoryItemFactory.GetSystemSenderText(),
                        UiLocalization.Text("AutomationRunDetailsForm.Conversation.AppServerUnavailable", "App Server is not connected. Unable to load messages."),
                        ChatHistoryCategory.System)
                };
            }

            try
            {
                var details = await _appServerClient.ReadThreadAsync(run.ThreadId).ConfigureAwait(true);
                return _sessionHistoryDisplayBuilder.BuildHistoryChatItems(details);
            }
            catch (Exception ex)
            {
                return new[]
                {
                    ChatHistoryItemFactory.Create(
                        ChatHistoryItemFactory.GetErrorSenderText(),
                        string.Format(
                            UiLocalization.Text("AutomationRunDetailsForm.Conversation.LoadFailed.Template", "Failed to load messages: {0}"),
                            ex.Message),
                        ChatHistoryCategory.Error)
                };
            }
        }

        private bool CanOpenAutomationThread(AutomationRunSnapshot run)
        {
            return MainFormAutomationNavigationPlanner.CanOpenThread(run);
        }

        private async Task OpenAutomationThreadAsync(AutomationRunSnapshot run)
        {
            if (!CanOpenAutomationThread(run))
            {
                var message = UiLocalization.Text("AutomationsForm.Status.ThreadNotFound", "Automation thread was not found.");
                AnnounceAutomationRunNavigationStatus(message);
                return;
            }

            var session = FindSessionByThreadId(run.ThreadId);
            if (session != null)
            {
                ApplyAutomationRunNavigationScope(run, session, null);
                await ActivateSessionAsync(session, true);
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("AutomationsForm.Action.OpenThread", "Open automation thread")))
            {
                return;
            }

            var threads = await _appServerClient.ListThreadsAsync();
            var thread = (threads ?? new List<AppServerThreadSummary>())
                .FirstOrDefault(candidate =>
                    candidate != null &&
                    string.Equals(candidate.ThreadId, run.ThreadId, StringComparison.OrdinalIgnoreCase));
            if (thread == null)
            {
                var message = UiLocalization.Text("AutomationsForm.Status.ThreadNotFound", "Automation thread was not found.");
                AnnounceAutomationRunNavigationStatus(message);
                return;
            }

            ApplyAutomationRunNavigationScope(run, null, thread.WorkingDirectory);
            ImportThreads(new List<AppServerThreadSummary> { thread }, true);
            session = FindSessionByThreadId(run.ThreadId);
            if (session == null)
            {
                var message = UiLocalization.Text("AutomationsForm.Status.ThreadNotFound", "Automation thread was not found.");
                AnnounceAutomationRunNavigationStatus(message);
                return;
            }

            ApplyAutomationRunNavigationScope(run, session, thread.WorkingDirectory);
            PersistState();
            await ActivateSessionAsync(session, true);
        }

        private Task OpenAutomationPathAsync(string path)
        {
            var decision = MainFormAutomationNavigationPlanner.BuildPathDecision(path, File.Exists, Directory.Exists);
            if (decision.Kind == MainFormAutomationNavigationDecisionKind.MissingPath)
            {
                var message = UiLocalization.Text("AutomationsForm.Status.PathNotFound", "Automation path was not found.");
                AnnounceAutomationRunNavigationStatus(message);
                return Task.CompletedTask;
            }

            if (decision.Kind == MainFormAutomationNavigationDecisionKind.RemotePathUnsupported)
            {
                var message = UiLocalization.Text("AutomationsForm.Status.RemotePathOpenUnsupported", "Opening WSL or remote Unix paths from automation results is not supported yet.");
                AnnounceAutomationRunNavigationStatus(message);
                return Task.CompletedTask;
            }

            if (decision.Kind == MainFormAutomationNavigationDecisionKind.OpenLocalPath)
            {
                try
                {
                    Process.Start(decision.Path);
                    SetStatus(UiLocalization.Text("AutomationsForm.Status.PathOpened", "Automation path opened."));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    SetHighPriorityStatus(UiLocalization.Text("AutomationsForm.Status.OpenPathFailed", "Failed to open automation path."));
                }

                return Task.CompletedTask;
            }

            var notFoundMessage = UiLocalization.Text("AutomationsForm.Status.PathNotFound", "Automation path was not found.");
            AnnounceAutomationRunNavigationStatus(notFoundMessage);
            return Task.CompletedTask;
        }

        private void ApplyAutomationRunNavigationScope(AutomationRunSnapshot run, ChatSession session, string workspacePath)
        {
            var officeTarget = GetActiveOfficeRuntimeTarget();
            var workspace = MainFormSessionWorkspacePlanner.ResolveWorkspace(
                run,
                session,
                workspacePath,
                FindWorkspaceById,
                path => MainFormSessionWorkspacePlanner.FindWorkspaceByPath(_recentWorkspaces, path),
                path => _officeWorkspaceService.ResolveOfficeWorkspaceForImport(
                    officeTarget.RuntimeScopeId,
                    officeTarget.HostPlatform,
                    ResolveOfficeRootPathForHistorySync(),
                    path,
                    _recentWorkspaces),
                IsAutomationOfficePath);
            var interactionMode = MainFormSessionWorkspacePlanner.ResolveInteractionMode(
                session,
                workspace,
                workspacePath,
                IsAutomationOfficePath);
            if (_currentInteractionMode != interactionMode)
            {
                if (!SelectInteractionModeTab(interactionMode))
                {
                    return;
                }
            }

            if (workspace == null)
            {
                return;
            }

            UpsertRecentWorkspace(workspace);
            _currentWorkspace = workspace;
            if (interactionMode == AppInteractionMode.Office)
            {
                _currentOfficeWorkspaceId = workspace.Id;
            }
            else
            {
                _currentCodingWorkspaceId = workspace.Id;
            }
        }

        private bool IsAutomationOfficePath(string path)
        {
            var target = GetActiveOfficeRuntimeTarget();
            if (string.IsNullOrWhiteSpace(path) ||
                string.IsNullOrWhiteSpace(_resolvedOfficeRootPath) ||
                MainFormAutomationNavigationPlanner.IsRemotePath(path) != target.UsesUnixPaths)
            {
                return false;
            }

            try
            {
                _officeWorkspaceService.ResolveOfficeWorkspaceForImport(
                    target.RuntimeScopeId,
                    target.HostPlatform,
                    ResolveOfficeRootPathForHistorySync(),
                    path,
                    _recentWorkspaces);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void AnnounceAutomationRunNavigationStatus(string message)
        {
            SetHighPriorityStatus(message);
            SpeakScreenReaderStatus(message);
        }

        private async Task<AutomationInputItemSnapshot> SelectAutomationSkillInputAsync(IWin32Window owner)
        {
            if (_currentWorkspace == null || !IsCurrentWorkspaceAvailable())
            {
                MessageBox.Show(
                    owner,
                    UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeLoadSkill", "Please open a workspace first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return null;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.LoadSkill", "Load skill")))
            {
                return null;
            }

            SetStatus(UiLocalization.Text("MainForm.Status.LoadingSkills", "Loading skills..."));
            var snapshots = await _appServerClient.ListSkillsAsync(new[] { _currentWorkspace.RootPath }, true);
            var skills = snapshots == null
                ? new List<AppServerSkillInfo>()
                : snapshots
                    .SelectMany(snapshot => snapshot != null && snapshot.Skills != null
                        ? snapshot.Skills
                        : Enumerable.Empty<AppServerSkillInfo>())
                    .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.Path))
                    .OrderByDescending(skill => skill.Enabled)
                    .ThenBy(skill => ComposeAttachmentState.GetSkillInsertionDisplayName(skill), StringComparer.CurrentCultureIgnoreCase)
                    .ToList();

            if (skills.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    UiLocalization.Text("MainForm.Message.NoAvailableSkillsInWorkspace", "No available skills in current workspace."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetStatus(UiLocalization.Text("MainForm.Status.NoAvailableSkillsInWorkspace", "No available skills in current workspace"));
                return null;
            }

            var pluginSources = await SkillSourcePluginLoader.TryLoadAsync(_appServerClient);
            using (var form = new InsertSkillForm(skills, _currentWorkspace.RootPath, pluginSources))
            {
                return form.ShowDialog(owner) == DialogResult.OK
                    ? AutomationInputItemFactory.FromSkill(form.SelectedSkill)
                    : null;
            }
        }

        private async Task<AutomationInputItemSnapshot> SelectAutomationPluginInputAsync(IWin32Window owner)
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.LoadPlugin", "Load plugin")))
            {
                return null;
            }

            SetStatus(UiLocalization.Text("MainForm.Status.LoadingPlugins", "Loading plugins..."));
            LoadAppConfigForAction();
            var installed = await AppServerInstalledPluginObservationService.LoadAsync(
                _appServerClient,
                _appServerClient.ProtocolCapabilities,
                System.Threading.CancellationToken.None);
            var insertablePlugins = PluginAttachmentSelectionBuilder.BuildInsertablePlugins(installed.Plugins);

            if (insertablePlugins.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    UiLocalization.Text("MainForm.Message.NoAvailablePlugins", "No installed plugins are available to insert."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetStatus(UiLocalization.Text("MainForm.Status.NoAvailablePlugins", "No installed plugins are available to insert"));
                return null;
            }

            using (var form = new InsertPluginForm(insertablePlugins))
            {
                return form.ShowDialog(owner) == DialogResult.OK
                    ? AutomationInputItemFactory.FromPlugin(form.SelectedPlugin)
                    : null;
            }
        }

    }
}
