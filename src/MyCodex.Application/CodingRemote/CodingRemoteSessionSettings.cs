using System.Collections.Generic;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteModelOption
    {
        public CodingRemoteModelOption()
        {
            ReasoningEfforts = new List<string>();
        }

        public string ModelId { get; set; }

        public string DisplayName { get; set; }

        public string DefaultReasoningEffort { get; set; }

        public List<string> ReasoningEfforts { get; set; }
    }

    public sealed class CodingRemoteSessionSettings
    {
        public CodingRemoteSessionSettings()
        {
            AvailableModels = new List<CodingRemoteModelOption>();
        }

        public string SessionId { get; set; }

        public string WorkspaceId { get; set; }

        public string ModelId { get; set; }

        public string ReasoningEffort { get; set; }

        public string ApprovalMode { get; set; }

        public bool IsPlanModeEnabled { get; set; }

        public string EffectiveModelId { get; set; }

        public string EffectiveReasoningEffort { get; set; }

        public string EffectiveApprovalMode { get; set; }

        public string DefaultModelId { get; set; }

        public string DefaultReasoningEffort { get; set; }

        public string DefaultApprovalMode { get; set; }

        public List<CodingRemoteModelOption> AvailableModels { get; set; }
    }

    public sealed class CodingRemoteSessionSettingsUpdateRequest
    {
        public string SessionId { get; set; }

        public bool HasModelId { get; set; }

        public string ModelId { get; set; }

        public bool HasReasoningEffort { get; set; }

        public string ReasoningEffort { get; set; }

        public bool HasApprovalMode { get; set; }

        public string ApprovalMode { get; set; }

        public bool? IsPlanModeEnabled { get; set; }
    }

    public sealed class CodingRemoteSessionSettingsUpdateResult
    {
        public string SessionId { get; set; }

        public string WorkspaceId { get; set; }

        public bool Updated { get; set; }

        public bool? IsPlanModeEnabled { get; set; }

        public string StatusText { get; set; }
    }
}
