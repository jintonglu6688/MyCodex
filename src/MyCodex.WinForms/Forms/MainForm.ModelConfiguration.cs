using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private AppServerModelConfigurationService _modelConfigurationService;
        private string _modelConfigurationScope;

        private string BuildModelConfigurationScope()
        {
            var capabilities = _appServerClient.ProtocolCapabilities;
            return GetConfiguredRuntimeProfileId() + "|" +
                (capabilities == null ? 0 : RuntimeHelpers.GetHashCode(capabilities));
        }

        private Task<T> OnModelConfigurationUiAsync<T>(Func<Task<T>> action)
        {
            if (IsDisposed || Disposing)
            {
                return Task.FromException<T>(new OperationCanceledException());
            }
            if (!InvokeRequired)
            {
                return action();
            }
            var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!this.BeginInvokeSafe(async () =>
            {
                try { completion.TrySetResult(await action()); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }))
            {
                completion.TrySetCanceled();
            }
            return completion.Task;
        }

        private Task<AppServerModelConfigurationObservation> RefreshModelConfigurationAsync(
            string workspacePath, bool showStatus, bool ensureConnection = true)
        {
            return OnModelConfigurationUiAsync(() => ReadModelConfigurationAsync(workspacePath, showStatus, ensureConnection));
        }

        private async Task<AppServerModelConfigurationObservation> ReadModelConfigurationAsync(
            string workspacePath, bool showStatus, bool ensureConnection)
        {
            if (!await WaitForTargetExitDecisionAsync()) return null;
            if (ensureConnection && !await EnsureAppServerAvailableForActionAsync(
                UiLocalization.Text("MainForm.SessionSettings.Action.ReadOfficialConfig", "Read official config")))
            {
                return null;
            }
            var scope = BuildModelConfigurationScope();
            if (_modelConfigurationService == null)
            {
                _modelConfigurationService = new AppServerModelConfigurationService(_appServerClient);
            }
            if (!string.Equals(_modelConfigurationScope, scope, StringComparison.Ordinal))
            {
                _modelConfigurationService.Invalidate();
                _modelConfigurationScope = scope;
                _globalServerConfigSnapshot = null;
                _globalConfigRequirementsSnapshot = null;
                _availableModels.Clear();
            }
            try
            {
                var capabilities = _appServerClient.ProtocolCapabilities;
                var observed = await _modelConfigurationService.RefreshAsync(
                    scope, workspacePath, capabilities != null && capabilities.SupportsConfigRequirements);
                if (!await WaitForTargetExitDecisionAsync() || !string.Equals(scope, BuildModelConfigurationScope(), StringComparison.Ordinal))
                {
                    return null;
                }
                _globalServerConfigSnapshot = observed.GlobalConfig;
                _globalConfigRequirementsSnapshot = observed.Requirements;
                _availableModels.Clear();
                _availableModels.AddRange(observed.Models);
                _availableModelsScopeKey = BuildAvailableModelsScopeKey();
                RefreshSessionSettingsState();
                return observed;
            }
            catch (OperationCanceledException)
            {
                Trace.TraceInformation("[ModelConfiguration] Refresh cancelled. RuntimeTransition={0}; Recovering={1}",
                    _isRuntimeWindowTransitionInProgress, _isRecoveringAppServerConnection);
                return null;
            }
            catch (Exception error)
            {
                Trace.TraceWarning("[ModelConfiguration] Refresh failed. ExceptionType={0}", error.GetType().FullName);
                if (await WaitForTargetExitDecisionAsync() && string.Equals(scope, BuildModelConfigurationScope(), StringComparison.Ordinal))
                {
                    _globalServerConfigSnapshot = null;
                    _globalConfigRequirementsSnapshot = null;
                    _availableModels.Clear();
                    _availableModelsScopeKey = null;
                    RefreshSessionSettingsState();
                    if (showStatus)
                    {
                        SetHighPriorityStatus(UiLocalization.Text("MainForm.ModelConfig.ReadFailed",
                            "Could not refresh official model settings. Try again before sending."));
                    }
                }
                return null;
            }
        }

        private async Task RefreshGlobalServerConfigAsync(bool showStatus)
        {
            await RefreshModelConfigurationAsync(null, showStatus);
        }

        private async void OnModelConfigurationActivated(object sender, EventArgs e)
        {
            if (_modelConfigurationService == null || _isRuntimeWindowTransitionInProgress)
            {
                return;
            }
            await RefreshModelConfigurationAsync(_currentWorkspace != null ? _currentWorkspace.RootPath : null, false, false);
        }

        private SessionDefaultsResolution ResolveSessionModelSettings(ChatSession session)
        {
            var workspace = session != null ? FindWorkspaceById(session.WorkspaceId) : _currentWorkspace;
            var observed = _modelConfigurationService != null
                ? _modelConfigurationService.GetObserved(BuildModelConfigurationScope(), workspace != null ? workspace.RootPath : null)
                : null;
            var defaults = _sessionDefaultsService.Resolve(_appConfigSnapshot, _availableModels,
                observed != null ? observed.Requirements : _globalConfigRequirementsSnapshot,
                observed != null ? observed.WorkspaceConfig : _globalServerConfigSnapshot);
            return SessionDefaultsService.ResolveSession(defaults, session, _availableModels);
        }

        private Task<SessionDefaultsResolution> ReadSessionModelSettingsAsync(ChatSession session, WorkspaceInfo workspace)
        {
            return OnModelConfigurationUiAsync(async () =>
            {
                if (!string.IsNullOrWhiteSpace(session.RuntimeProfileId) &&
                    !_runtimeProfileService.IsSameRuntimeProfile(session.RuntimeProfileId, GetConfiguredRuntimeProfileId()))
                {
                    throw new OperationCanceledException();
                }
                var observed = await ReadModelConfigurationAsync(workspace.RootPath, true, true);
                if (observed == null)
                {
                    throw new InvalidOperationException(UiLocalization.Text("MainForm.ModelConfig.ReadFailed",
                        "Could not refresh official model settings. Try again before sending."));
                }
                var defaults = _sessionDefaultsService.Resolve(_appConfigSnapshot, observed.Models, observed.Requirements, observed.WorkspaceConfig);
                var resolved = SessionDefaultsService.ResolveSession(defaults, session, observed.Models);
                if (string.IsNullOrWhiteSpace(resolved.ModelId))
                {
                    throw new InvalidOperationException(UiLocalization.Text("MainForm.Status.SessionModelUnavailable",
                        "The selected model is unavailable. Select a model before sending."));
                }
                if (!SessionDefaultsService.IsReasoningEffortAvailable(resolved.ModelId, resolved.ReasoningEffort, observed.Models))
                {
                    throw new InvalidOperationException(UiLocalization.Text("MainForm.ModelConfig.InvalidEffort",
                        "The selected reasoning effort is not supported by this model. Update the session or official model settings."));
                }
                return resolved;
            });
        }

        private Task<bool> SaveOfficialModelDefaultsAsync(AppServerConfigSnapshot observed, string scope, string modelId, string effort)
        {
            return OnModelConfigurationUiAsync(async () =>
            {
                if (!string.Equals(scope, BuildModelConfigurationScope(), StringComparison.Ordinal))
                {
                    throw new OperationCanceledException();
                }
                await _modelConfigurationService.WriteAsync(observed, modelId, effort);
                return await ReadModelConfigurationAsync(_currentWorkspace != null ? _currentWorkspace.RootPath : null, true, false) != null;
            });
        }

        private Task<SessionDefaultsResolution> ReadAutomationModelSettingsAsync(AutomationDefinitionSnapshot definition)
        {
            return ReadSessionModelSettingsAsync(
                new ChatSession
                {
                    ModelId = definition.ModelId, ReasoningEffort = definition.ReasoningEffort,
                    RuntimeProfileId = definition.Context.RuntimeProfileId,
                    Mode = definition.Context.InteractionMode
                },
                new WorkspaceInfo { RootPath = definition.Context.WorkspacePath });
        }
    }
}
