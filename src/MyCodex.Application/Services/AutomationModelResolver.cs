using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AutomationModelResolver : IAutomationModelResolver
    {
        private readonly Func<Task<IReadOnlyList<AppServerModelOption>>> _loadModelsAsync;
        private readonly Func<string> _defaultModelIdProvider;
        private readonly Func<string> _defaultReasoningEffortProvider;
        private readonly Func<string, bool> _supportsReasoningEffortForModel;
        private readonly Func<AutomationDefinitionSnapshot, Task<SessionDefaultsResolution>> _officialSettingsResolver;

        public AutomationModelResolver(
            Func<Task<IReadOnlyList<AppServerModelOption>>> loadModelsAsync,
            Func<string> defaultModelIdProvider,
            Func<string> defaultReasoningEffortProvider,
            Func<string, bool> supportsReasoningEffortForModel,
            Func<AutomationDefinitionSnapshot, Task<SessionDefaultsResolution>> officialSettingsResolver = null)
        {
            _loadModelsAsync = loadModelsAsync;
            _defaultModelIdProvider = defaultModelIdProvider;
            _defaultReasoningEffortProvider = defaultReasoningEffortProvider;
            _supportsReasoningEffortForModel = supportsReasoningEffortForModel;
            _officialSettingsResolver = officialSettingsResolver;
        }

        public async Task<AutomationModelResolution> ResolveAsync(AutomationDefinitionSnapshot definition)
        {
            var configuredModelId = TextValueHelper.TrimToNull(definition != null ? definition.ModelId : null);
            var configuredReasoningEffort = TextValueHelper.TrimToNull(definition != null ? definition.ReasoningEffort : null);
            if (_officialSettingsResolver != null)
            {
                var settings = await _officialSettingsResolver(definition).ConfigureAwait(false);
                return BuildResolution(configuredModelId, configuredReasoningEffort,
                    settings.ModelId, settings.ReasoningEffort, false, null);
            }
            var models = await LoadModelsSafeAsync().ConfigureAwait(false);
            var defaultModelId = TextValueHelper.TrimToNull(_defaultModelIdProvider != null ? _defaultModelIdProvider() : null);
            var defaultReasoningEffort = TextValueHelper.TrimToNull(_defaultReasoningEffortProvider != null ? _defaultReasoningEffortProvider() : null);
            var configuredModel = FindModel(models, configuredModelId);

            if (!string.IsNullOrWhiteSpace(configuredModelId) &&
                (models.Count == 0 || configuredModel != null))
            {
                return BuildResolution(
                    configuredModelId,
                    configuredReasoningEffort,
                    configuredModelId,
                    ResolveReasoningEffort(configuredModelId, configuredModel, configuredReasoningEffort, defaultReasoningEffort),
                    false,
                    null);
            }

            var fallbackModel = SelectFallbackModel(models, defaultModelId);
            var fallbackModelId = TextValueHelper.TrimToNull(fallbackModel != null ? fallbackModel.ModelId : defaultModelId);
            var fallbackReasoningEffort = ResolveReasoningEffort(fallbackModelId, fallbackModel, null, defaultReasoningEffort);
            var usedFallback = !string.IsNullOrWhiteSpace(configuredModelId) &&
                               !string.Equals(configuredModelId, fallbackModelId, StringComparison.OrdinalIgnoreCase);
            var message = usedFallback
                ? BuildFallbackMessage(configuredModelId, fallbackModelId)
                : null;

            return BuildResolution(
                configuredModelId,
                configuredReasoningEffort,
                fallbackModelId,
                fallbackReasoningEffort,
                usedFallback,
                message);
        }

        private async Task<List<AppServerModelOption>> LoadModelsSafeAsync()
        {
            if (_loadModelsAsync == null)
            {
                return new List<AppServerModelOption>();
            }

            try
            {
                var models = await _loadModelsAsync().ConfigureAwait(false);
                return (models ?? new AppServerModelOption[0])
                    .Where(model => model != null && !string.IsNullOrWhiteSpace(model.ModelId))
                    .ToList();
            }
            catch
            {
                return new List<AppServerModelOption>();
            }
        }

        private string ResolveReasoningEffort(string modelId, AppServerModelOption model, string preferredReasoningEffort, string defaultReasoningEffort)
        {
            modelId = TextValueHelper.TrimToNull(modelId);
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return NormalizeReasoningEffort(preferredReasoningEffort, defaultReasoningEffort);
            }

            if (!SupportsReasoningEffortForModel(modelId))
            {
                return null;
            }

            return NormalizeReasoningEffort(
                preferredReasoningEffort,
                TextValueHelper.TrimToNull(model != null ? model.DefaultReasoningEffort : null),
                defaultReasoningEffort);
        }

        private bool SupportsReasoningEffortForModel(string modelId)
        {
            return _supportsReasoningEffortForModel == null || _supportsReasoningEffortForModel(modelId);
        }

        private static AppServerModelOption SelectFallbackModel(IEnumerable<AppServerModelOption> models, string defaultModelId)
        {
            var modelList = (models ?? new AppServerModelOption[0]).ToList();
            var defaultModel = FindModel(modelList, defaultModelId);
            return defaultModel ?? modelList.FirstOrDefault();
        }

        private static AppServerModelOption FindModel(IEnumerable<AppServerModelOption> models, string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId))
            {
                return null;
            }

            return (models ?? new AppServerModelOption[0]).FirstOrDefault(model =>
                model != null &&
                string.Equals(model.ModelId, modelId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        private static AutomationModelResolution BuildResolution(
            string configuredModelId,
            string configuredReasoningEffort,
            string modelId,
            string reasoningEffort,
            bool usedFallback,
            string message)
        {
            return new AutomationModelResolution
            {
                ConfiguredModelId = TextValueHelper.TrimToNull(configuredModelId),
                ConfiguredReasoningEffort = TextValueHelper.TrimToNull(configuredReasoningEffort),
                ModelId = TextValueHelper.TrimToNull(modelId),
                ReasoningEffort = TextValueHelper.TrimToNull(reasoningEffort),
                UsedFallback = usedFallback,
                Message = TextValueHelper.TrimToNull(message)
            };
        }

        private static string BuildFallbackMessage(string configuredModelId, string fallbackModelId)
        {
            var fallback = string.IsNullOrWhiteSpace(fallbackModelId)
                ? SharedLocalization.Text("AutomationModelResolver.Message.AppServerDefault", "the App Server default model")
                : fallbackModelId.Trim();
            return string.Format(
                SharedLocalization.Text("AutomationModelResolver.Message.Fallback.Template", "Configured model '{0}' is unavailable for the current authentication source. This run used '{1}' instead."),
                configuredModelId,
                fallback);
        }

        private static string NormalizeReasoningEffort(params string[] values)
        {
            foreach (var value in values ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }
    }
}
