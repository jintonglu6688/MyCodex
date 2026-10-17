using System;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private IHostSettingsOperator BuildHostSettingsOperator()
        {
            var settingsOperator = new AppServerHostSettingsOperator(
                () => _appConfigSnapshot ?? new AppConfigSnapshot(),
                SaveAppConfig,
                GetRemoteAvailableModelsAsync,
                BuildHostSettingsScope,
                BuildHostSettingsDefaults,
                (model, effort) => { },
                SetConfiguredDefaultApprovalMode,
                () => System.Threading.Tasks.Task.CompletedTask,
                CustomApprovalPermissionDisplayText.Get,
                SaveRemoteOfficialModelDefaultsAsync);
            return new AppServerOfficialHostSettingsOperator(settingsOperator, _appServerClient);
        }

        private Task SaveRemoteOfficialModelDefaultsAsync(string model, string effort)
        {
            return OnModelConfigurationUiAsync(async () =>
            {
                if (!await SaveOfficialModelDefaultsAsync(_globalServerConfigSnapshot, BuildModelConfigurationScope(), model, effort))
                {
                    throw new InvalidOperationException("Official model settings were saved, but refreshing them failed.");
                }
                return true;
            });
        }

        private HostSettingsScope BuildHostSettingsScope()
        {
            return new HostSettingsScope
            {
                HostName = Environment.MachineName,
                RuntimeSource = _appConfigSnapshot != null
                    ? _appConfigSnapshot.CodexRuntimeSource
                    : null,
                RuntimeDisplayName = GetCurrentRuntimeDisplayName(),
                RuntimeProfileId = GetConfiguredRuntimeProfileId(),
                AuthenticationDisplayName = null
            };
        }

        private HostSettingsDefaults BuildHostSettingsDefaults()
        {
            return new HostSettingsDefaults
            {
                ModelId = GetConfiguredDefaultModelId(),
                ReasoningEffort = GetConfiguredDefaultReasoningEffort(),
                ApprovalMode = HostSettingsPolicy.FormatApprovalMode(GetConfiguredDefaultApprovalMode())
            };
        }

        private async Task RefreshAfterRemoteHostSettingsUpdateAsync()
        {
            LoadAppConfig();
            _globalServerConfigSnapshot = null;
            _globalConfigRequirementsSnapshot = null;
            _availableModels.Clear();
            _availableModelsScopeKey = null;
            await RefreshGlobalServerConfigAsync(false);
            await LoadAvailableModelsAsync(false);
            RefreshSessionSettingsState();
        }
    }
}
