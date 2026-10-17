using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteSessionSettingsOperator : ICodingRemoteSessionSettingsOperator
    {
        private readonly IAppStateStore _appStateStore;
        private readonly Func<CancellationToken, Task<IReadOnlyList<AppServerModelOption>>> _modelOptionsProvider;
        private readonly Func<string> _defaultModelIdResolver;
        private readonly Func<string> _defaultReasoningEffortResolver;
        private readonly Func<ApprovalMode> _defaultApprovalModeResolver;
        private readonly Func<ChatSession, string> _effectiveModelIdResolver;
        private readonly Func<ChatSession, string> _effectiveReasoningEffortResolver;
        private readonly Func<ChatSession, ApprovalMode> _effectiveApprovalModeResolver;
        private readonly Func<string, bool> _reasoningSupportResolver;
        private readonly AppInteractionMode _interactionMode;

        public AppServerCodingRemoteSessionSettingsOperator(
            IAppStateStore appStateStore,
            Func<CancellationToken, Task<IReadOnlyList<AppServerModelOption>>> modelOptionsProvider,
            Func<string> defaultModelIdResolver,
            Func<string> defaultReasoningEffortResolver,
            Func<ApprovalMode> defaultApprovalModeResolver,
            Func<ChatSession, string> effectiveModelIdResolver,
            Func<ChatSession, string> effectiveReasoningEffortResolver,
            Func<ChatSession, ApprovalMode> effectiveApprovalModeResolver,
            Func<string, bool> reasoningSupportResolver)
            : this(
                appStateStore,
                modelOptionsProvider,
                defaultModelIdResolver,
                defaultReasoningEffortResolver,
                defaultApprovalModeResolver,
                effectiveModelIdResolver,
                effectiveReasoningEffortResolver,
                effectiveApprovalModeResolver,
                reasoningSupportResolver,
                AppInteractionMode.Coding)
        {
        }

        public AppServerCodingRemoteSessionSettingsOperator(
            IAppStateStore appStateStore,
            Func<CancellationToken, Task<IReadOnlyList<AppServerModelOption>>> modelOptionsProvider,
            Func<string> defaultModelIdResolver,
            Func<string> defaultReasoningEffortResolver,
            Func<ApprovalMode> defaultApprovalModeResolver,
            Func<ChatSession, string> effectiveModelIdResolver,
            Func<ChatSession, string> effectiveReasoningEffortResolver,
            Func<ChatSession, ApprovalMode> effectiveApprovalModeResolver,
            Func<string, bool> reasoningSupportResolver,
            AppInteractionMode interactionMode)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _modelOptionsProvider = modelOptionsProvider ?? throw new ArgumentNullException(nameof(modelOptionsProvider));
            _defaultModelIdResolver = defaultModelIdResolver ?? throw new ArgumentNullException(nameof(defaultModelIdResolver));
            _defaultReasoningEffortResolver = defaultReasoningEffortResolver ?? throw new ArgumentNullException(nameof(defaultReasoningEffortResolver));
            _defaultApprovalModeResolver = defaultApprovalModeResolver ?? throw new ArgumentNullException(nameof(defaultApprovalModeResolver));
            _effectiveModelIdResolver = effectiveModelIdResolver ?? throw new ArgumentNullException(nameof(effectiveModelIdResolver));
            _effectiveReasoningEffortResolver = effectiveReasoningEffortResolver ?? throw new ArgumentNullException(nameof(effectiveReasoningEffortResolver));
            _effectiveApprovalModeResolver = effectiveApprovalModeResolver ?? throw new ArgumentNullException(nameof(effectiveApprovalModeResolver));
            _reasoningSupportResolver = reasoningSupportResolver ?? throw new ArgumentNullException(nameof(reasoningSupportResolver));
            _interactionMode = interactionMode;
        }

        public async Task<CodingRemoteSessionSettings> GetSessionSettingsAsync(string sessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            var session = FindCodingSession(snapshot, sessionId);
            if (session == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Session was not found.");
            }

            return await BuildSettingsAsync(session, cancellationToken).ConfigureAwait(false);
        }

        public Task<CodingRemoteSessionSettingsUpdateResult> UpdateSessionSettingsAsync(CodingRemoteSessionSettingsUpdateRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new CodingRemoteSessionSettingsUpdateRequest();
            if (string.IsNullOrWhiteSpace(request.SessionId))
            {
                throw new InvalidOperationException("Session ID is required.");
            }

            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            var session = FindCodingSession(snapshot, request.SessionId);
            if (session == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.SessionNotFound, "Session was not found.");
            }

            if (request.HasModelId)
            {
                session.ModelId = NormalizeNullable(request.ModelId);
            }

            if (request.HasReasoningEffort)
            {
                session.ReasoningEffort = ShouldKeepReasoningEffort(session.ModelId, request.ReasoningEffort)
                    ? NormalizeNullable(request.ReasoningEffort)
                    : null;
            }

            if (request.HasApprovalMode)
            {
                session.ApprovalMode = ParseApprovalMode(request.ApprovalMode);
            }

            if (request.IsPlanModeEnabled.HasValue)
            {
                session.IsPlanModeEnabled = request.IsPlanModeEnabled.Value;
            }

            _appStateStore.Save(snapshot);

            return Task.FromResult(new CodingRemoteSessionSettingsUpdateResult
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                Updated = true,
                IsPlanModeEnabled = session.IsPlanModeEnabled,
                StatusText = "Session settings updated."
            });
        }

        private async Task<CodingRemoteSessionSettings> BuildSettingsAsync(ChatSession session, CancellationToken cancellationToken)
        {
            var availableModels = await _modelOptionsProvider(cancellationToken).ConfigureAwait(false);
            return new CodingRemoteSessionSettings
            {
                SessionId = session.Id,
                WorkspaceId = session.WorkspaceId,
                ModelId = NormalizeNullable(session.ModelId),
                ReasoningEffort = NormalizeNullable(session.ReasoningEffort),
                ApprovalMode = FormatApprovalMode(session.ApprovalMode),
                IsPlanModeEnabled = session.IsPlanModeEnabled,
                EffectiveModelId = _effectiveModelIdResolver(session),
                EffectiveReasoningEffort = _effectiveReasoningEffortResolver(session),
                EffectiveApprovalMode = FormatApprovalMode(_effectiveApprovalModeResolver(session)),
                DefaultModelId = _defaultModelIdResolver(),
                DefaultReasoningEffort = _defaultReasoningEffortResolver(),
                DefaultApprovalMode = FormatApprovalMode(_defaultApprovalModeResolver()),
                AvailableModels = MapModels(availableModels)
            };
        }

        private bool ShouldKeepReasoningEffort(string modelId, string reasoningEffort)
        {
            return !string.IsNullOrWhiteSpace(reasoningEffort) &&
                   !string.IsNullOrWhiteSpace(modelId) &&
                   _reasoningSupportResolver(modelId);
        }

        private ChatSession FindCodingSession(AppStateSnapshot snapshot, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            return (snapshot.Sessions ?? new List<ChatSession>())
                .FirstOrDefault(session =>
                    session != null &&
                    session.Mode == _interactionMode &&
                    string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        private static List<CodingRemoteModelOption> MapModels(IEnumerable<AppServerModelOption> models)
        {
            var result = new List<CodingRemoteModelOption>();
            if (models == null)
            {
                return result;
            }

            foreach (var model in models)
            {
                if (model == null || string.IsNullOrWhiteSpace(model.ModelId))
                {
                    continue;
                }

                result.Add(new CodingRemoteModelOption
                {
                    ModelId = model.ModelId,
                    DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId : model.DisplayName,
                    DefaultReasoningEffort = model.DefaultReasoningEffort,
                    ReasoningEfforts = model.ReasoningEfforts == null
                        ? new List<string>()
                        : model.ReasoningEfforts
                            .Where(effort => effort != null && !string.IsNullOrWhiteSpace(effort.EffortId))
                            .Select(effort => effort.EffortId.Trim())
                            .ToList()
                });
            }

            return result;
        }

        private static ApprovalMode? ParseApprovalMode(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "read-only":
                case "readonly":
                    return ApprovalMode.ReadOnly;
                case "full-access":
                case "fullaccess":
                    return ApprovalMode.FullAccess;
                case "full-access-auto-approve":
                    return ApprovalMode.FullAccess;
                case "auto":
                    return ApprovalMode.Auto;
                case "custom":
                    return ApprovalMode.Custom;
                default:
                    throw new InvalidOperationException("Unsupported approval mode.");
            }
        }

        private static string FormatApprovalMode(ApprovalMode? value)
        {
            return value.HasValue ? FormatApprovalMode(value.Value) : null;
        }

        private static string FormatApprovalMode(ApprovalMode value)
        {
            switch (value)
            {
                case ApprovalMode.ReadOnly:
                    return "read-only";
                case ApprovalMode.FullAccess:
                case ApprovalMode.FullAccessAutoApprove:
                    return "full-access";
                case ApprovalMode.Custom:
                    return "custom";
                default:
                    return "auto";
            }
        }

        private static string NormalizeNullable(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }
}
