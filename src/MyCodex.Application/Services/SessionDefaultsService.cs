using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public sealed class SessionDefaultsResolution
    {
        public string RuntimeProfileId { get; set; }

        public string ModelId { get; set; }

        public string ReasoningEffort { get; set; }

        public ApprovalMode ApprovalMode { get; set; }

        public bool UsesFirstObservedModelFallback { get; set; }
    }

    public sealed class SessionDefaultsService
    {
        public const ApprovalMode BuiltInDefaultApprovalMode = ApprovalMode.Auto;

        private readonly RuntimeProfileService _runtimeProfileService;

        public SessionDefaultsService()
            : this(new RuntimeProfileService())
        {
        }

        public SessionDefaultsService(RuntimeProfileService runtimeProfileService)
        {
            _runtimeProfileService = runtimeProfileService ?? throw new ArgumentNullException(nameof(runtimeProfileService));
        }

        public SessionDefaultsResolution Resolve(
            AppConfigSnapshot appConfigSnapshot,
            IEnumerable<AppServerModelOption> observedModels,
            AppServerConfigRequirementsSnapshot configRequirements)
        {
            return Resolve(appConfigSnapshot, observedModels, configRequirements, null);
        }

        public SessionDefaultsResolution Resolve(
            AppConfigSnapshot appConfigSnapshot,
            IEnumerable<AppServerModelOption> observedModels,
            AppServerConfigRequirementsSnapshot configRequirements,
            AppServerConfigSnapshot officialConfig)
        {
            appConfigSnapshot = appConfigSnapshot ?? new AppConfigSnapshot();
            var runtimeProfileId = _runtimeProfileService.GetCurrentRuntimeProfileId(appConfigSnapshot);
            var runtimeConfig = FindRuntimeProfileConfig(appConfigSnapshot, runtimeProfileId);
            var models = NormalizeModels(observedModels);
            var required = configRequirements != null ? configRequirements.NewThreadModelDefaults : null;
            var recommendedModel = models.FirstOrDefault(model => model.IsDefault);
            var modelId = Normalize(required != null ? required.ModelId : null) ??
                Normalize(officialConfig != null ? officialConfig.ModelId : null) ??
                (recommendedModel != null ? recommendedModel.ModelId : null);
            var selectedModel = FindModel(models, modelId);
            var officialEffort = officialConfig != null &&
                (string.IsNullOrWhiteSpace(officialConfig.ModelId) ||
                 string.Equals(modelId, officialConfig.ModelId, StringComparison.OrdinalIgnoreCase))
                ? officialConfig.ModelReasoningEffort : null;

            return new SessionDefaultsResolution
            {
                RuntimeProfileId = _runtimeProfileService.NormalizeRuntimeProfileId(runtimeProfileId),
                ModelId = modelId,
                ReasoningEffort = ResolveDefaultReasoningEffort(selectedModel,
                    Normalize(required != null ? required.ModelReasoningEffort : null) ?? Normalize(officialEffort)),
                ApprovalMode = runtimeConfig != null && runtimeConfig.DefaultApprovalMode.HasValue
                    ? runtimeConfig.DefaultApprovalMode.Value
                    : BuiltInDefaultApprovalMode,
                UsesFirstObservedModelFallback = false
            };
        }

        public static SessionDefaultsResolution ResolveSession(
            SessionDefaultsResolution defaults,
            ChatSession session,
            IEnumerable<AppServerModelOption> models)
        {
            defaults = defaults ?? new SessionDefaultsResolution();
            var modelId = Normalize(session != null ? session.ModelId : null) ?? defaults.ModelId;
            var model = FindModel(models, modelId);
            return new SessionDefaultsResolution
            {
                RuntimeProfileId = defaults.RuntimeProfileId,
                ModelId = modelId,
                ReasoningEffort = Normalize(session != null ? session.ReasoningEffort : null) ??
                    (string.Equals(modelId, defaults.ModelId, StringComparison.OrdinalIgnoreCase)
                        ? defaults.ReasoningEffort
                        : Normalize(model != null ? model.DefaultReasoningEffort : null)),
                ApprovalMode = defaults.ApprovalMode
            };
        }

        public static bool IsReasoningEffortAvailable(string modelId, string effort, IEnumerable<AppServerModelOption> models)
        {
            var model = FindModel(models, modelId);
            return string.IsNullOrWhiteSpace(effort) || model == null || model.ReasoningEfforts.Count == 0 ||
                model.ReasoningEfforts.Any(option => option != null &&
                    string.Equals(option.EffortId, effort, StringComparison.OrdinalIgnoreCase));
        }

        private static string ResolveDefaultReasoningEffort(AppServerModelOption model, string configuredEffort)
        {
            var defaultEffort = Normalize(model != null ? model.DefaultReasoningEffort : null);
            var preferred = Normalize(configuredEffort) ?? defaultEffort;
            var efforts = model != null
                ? model.ReasoningEfforts.Where(option => option != null && !string.IsNullOrWhiteSpace(option.EffortId)).ToList()
                : new List<AppServerModelEffortOption>();
            // Missing capability data is not evidence that an explicit configuration is invalid.
            if (preferred == null || efforts.Count == 0)
            {
                return preferred;
            }

            var selected = efforts.FirstOrDefault(option =>
                string.Equals(option.EffortId, preferred, StringComparison.OrdinalIgnoreCase)) ??
                efforts.FirstOrDefault(option =>
                    string.Equals(option.EffortId, defaultEffort, StringComparison.OrdinalIgnoreCase)) ?? efforts[0];
            return selected.EffortId;
        }

        public RuntimeProfileConfigSnapshot GetRuntimeProfileConfig(AppConfigSnapshot appConfigSnapshot, bool create)
        {
            if (appConfigSnapshot == null)
            {
                return null;
            }

            if (appConfigSnapshot.RuntimeProfiles == null)
            {
                appConfigSnapshot.RuntimeProfiles = new List<RuntimeProfileConfigSnapshot>();
            }

            var runtimeProfileId = _runtimeProfileService.GetCurrentRuntimeProfileId(appConfigSnapshot);
            var profile = FindRuntimeProfileConfig(appConfigSnapshot, runtimeProfileId);
            if (profile != null || !create)
            {
                return profile;
            }

            profile = new RuntimeProfileConfigSnapshot
            {
                RuntimeProfileId = _runtimeProfileService.NormalizeRuntimeProfileId(runtimeProfileId)
            };
            appConfigSnapshot.RuntimeProfiles.Add(profile);
            return profile;
        }

        public void SetModelDefaults(
            AppConfigSnapshot appConfigSnapshot,
            string modelId,
            string reasoningEffort)
        {
            var runtimeConfig = GetRuntimeProfileConfig(appConfigSnapshot, true);
            if (runtimeConfig == null)
            {
                return;
            }

            runtimeConfig.DefaultModelId = Normalize(modelId);
            runtimeConfig.DefaultReasoningEffort = Normalize(reasoningEffort);
        }

        public void SetApprovalMode(AppConfigSnapshot appConfigSnapshot, ApprovalMode approvalMode)
        {
            var runtimeConfig = GetRuntimeProfileConfig(appConfigSnapshot, true);
            if (runtimeConfig != null)
            {
                runtimeConfig.DefaultApprovalMode = approvalMode;
            }
        }

        public static bool ContainsModelId(IEnumerable<string> modelIds, string modelId)
        {
            return !string.IsNullOrWhiteSpace(modelId) &&
                (modelIds ?? Enumerable.Empty<string>()).Any(candidate =>
                    string.Equals(candidate, modelId.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsExplicitConversationModelAvailable(
            string modelId,
            IEnumerable<AppServerModelOption> observedModels)
        {
            return string.IsNullOrWhiteSpace(modelId) || FindModel(observedModels, modelId) != null;
        }

        private static List<AppServerModelOption> NormalizeModels(IEnumerable<AppServerModelOption> models)
        {
            var result = new List<AppServerModelOption>();
            foreach (var model in models ?? Enumerable.Empty<AppServerModelOption>())
            {
                if (model == null || string.IsNullOrWhiteSpace(model.ModelId) || FindModel(result, model.ModelId) != null)
                {
                    continue;
                }

                result.Add(model);
            }

            return result;
        }

        private static AppServerModelOption FindModel(IEnumerable<AppServerModelOption> models, string modelId)
        {
            var normalizedModelId = Normalize(modelId);
            return normalizedModelId == null
                ? null
                : (models ?? Enumerable.Empty<AppServerModelOption>()).FirstOrDefault(model =>
                    model != null &&
                    string.Equals(model.ModelId, normalizedModelId, StringComparison.OrdinalIgnoreCase));
        }

        private static RuntimeProfileConfigSnapshot FindRuntimeProfileConfig(
            AppConfigSnapshot appConfigSnapshot,
            string runtimeProfileId)
        {
            if (appConfigSnapshot == null || appConfigSnapshot.RuntimeProfiles == null)
            {
                return null;
            }

            return appConfigSnapshot.RuntimeProfiles.FirstOrDefault(profile =>
                profile != null &&
                string.Equals(profile.RuntimeProfileId, runtimeProfileId, StringComparison.OrdinalIgnoreCase));
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
