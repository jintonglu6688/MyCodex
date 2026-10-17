using System.Collections.Generic;
using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class AppStateSnapshot
    {
        [DataMember]
        public List<WorkspaceInfo> RecentWorkspaces { get; set; } = new List<WorkspaceInfo>();

        [DataMember]
        public List<ChatSession> Sessions { get; set; } = new List<ChatSession>();

        [DataMember]
        public List<RuntimeProfileStateSnapshot> RuntimeProfiles { get; set; } = new List<RuntimeProfileStateSnapshot>();

        [DataMember]
        public List<OfficeRuntimeStateSnapshot> OfficeRuntimeStates { get; set; } =
            new List<OfficeRuntimeStateSnapshot>();

        [DataMember]
        public bool HasCompletedThreadImportPrompt { get; set; }

        [DataMember]
        public bool HasCompletedStartupModeOnboarding { get; set; }

        [DataMember]
        public bool HasCompletedOfficeCliOnboarding { get; set; }

        [DataMember]
        public AppInteractionMode CurrentInteractionMode { get; set; } = AppInteractionMode.Coding;

        [DataMember]
        public string CurrentCodingWorkspaceId { get; set; }

        [DataMember]
        public string CurrentCodingSessionId { get; set; }

        [DataMember]
        public string CurrentOfficeWorkspaceId { get; set; }

        [DataMember]
        public string CurrentOfficeSessionId { get; set; }

        [DataMember]
        public string ThreadSyncSummary { get; set; }

        [DataMember]
        public string CurrentWorkspaceId { get; set; }

        [DataMember]
        public string CurrentSessionId { get; set; }

        [DataMember]
        public int? MainSplitterDistance { get; set; }

        [DataMember]
        public int? ContentSplitterDistance { get; set; }

        [DataMember]
        public int? RightSplitterDistance { get; set; }
    }
}
