using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public sealed class AuthenticationProvider
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public string Version { get; set; }
        public bool HasApiKey { get; set; }
        // Outcome returned by save only; this is not a persisted provider setting.
        public bool GlobalApplied { get; set; }
        public bool RecoveryRequired { get; set; }
        public string PresetId { get; set; }
        public string PresetName { get; set; }
        public string AccountId { get; set; }
        public string AccountLabel { get; set; }
        public bool NeedsReauthentication { get; set; }
        public List<AuthenticationCatalogModel> Models { get; set; } = new List<AuthenticationCatalogModel>();
        public Dictionary<string, object> Advanced { get; set; } = new Dictionary<string, object>();
    }

    public sealed class AuthenticationProviderEdit
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public string ExpectedVersion { get; set; }
        public string ConfigToml { get; set; }
        public string ApiKey { get; set; }
        public bool ClearApiKey { get; set; }
        public string PresetId { get; set; }
        public string AccountId { get; set; }
        public List<AuthenticationCatalogModel> Models { get; set; }
        public Dictionary<string, object> Advanced { get; set; }
        // Full config editor draft: the text shown when it opened and the edited text.
        public string EditorBase { get; set; }
        public string EditorText { get; set; }
        // null asks the backend to refuse conflicts; "keepMine" or "keepTheirs" resolves them.
        public string EditorConflictPolicy { get; set; }
    }

    public sealed class AuthenticationCatalogModel
    {
        public string Model { get; set; }
        public string DisplayName { get; set; }
        public long? ContextWindow { get; set; }
        public List<string> ReasoningLevels { get; set; }
        public string DefaultReasoningLevel { get; set; }
        public List<string> InputModalities { get; set; }
        public bool? SupportsParallelToolCalls { get; set; }
        public string BaseInstructions { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();
    }

    public sealed class AuthenticationProviderPreset
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Kind { get; set; }
        public string BaseUrl { get; set; }
        public string Model { get; set; }
        public bool Available { get; set; }
        public string UnavailableReason { get; set; }
        public bool AllowsProtocolSelection { get; set; }
        public List<AuthenticationCatalogModel> Models { get; set; } = new List<AuthenticationCatalogModel>();
        public Dictionary<string, object> Advanced { get; set; } = new Dictionary<string, object>();

        public override string ToString()
        {
            return Name;
        }
    }

    public sealed class AuthenticationModelFetchRequest
    {
        public string ProviderId { get; set; }
        public string Kind { get; set; }
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
        public string AccountId { get; set; }
        public Dictionary<string, object> Advanced { get; set; }
    }

    public sealed class AuthenticationCenterSnapshot
    {
        public string TargetName { get; set; }
        public string CodexHome { get; set; }
        public string HostVersion { get; set; }
        public bool SupportsContextSettings { get; set; }
        public bool SupportsConfigEditing { get; set; }
        public bool SupportsFullConfigEditor { get; set; }
        public bool SupportsSessionHistory { get; set; }
        public bool SupportsAggregation { get; set; }
        // Codex is in aggregation mode: routed through the host with several providers listed.
        public bool Aggregating { get; set; }
        public bool RouteRequired { get; set; }
        public bool RouterRunning { get; set; }
        public int RouteInFlight { get; set; }
        public bool StartupRecoveryReady { get; set; } = true;
        public bool ManualRestartRequired { get; set; }
        public string CurrentProviderId { get; set; }
        public string CurrentProviderVersion { get; set; }
        public string LiveState { get; set; }
        public string LiveAccountLabel { get; set; }
        public string SyncError { get; set; }
        public IReadOnlyList<AuthenticationProvider> Providers { get; set; } = new List<AuthenticationProvider>();
    }

    public sealed class AuthenticationApplyPlan
    {
        public string ProviderId { get; set; }
        public string Version { get; set; }
        public string Fingerprint { get; set; }
        public bool RecoveryRequired { get; set; }
    }

    public sealed class AuthenticationOperation
    {
        public string OperationId { get; set; }
        public string ProviderId { get; set; }
        public string Status { get; set; }
    }

    public sealed class AuthenticationApplyResult
    {
        public bool GlobalApplied { get; set; }
        public bool Reconnected { get; set; }
        public string OperationId { get; set; }
    }

    public sealed class AuthenticationSaveResult
    {
        public AuthenticationProvider Provider { get; set; }
        public bool Reconnected { get; set; }
    }

    public sealed class AuthenticationDeleteResult
    {
        public bool AccountRemoved { get; set; }
        public bool Reconnected { get; set; }
    }

    public sealed class AuthenticationAccount
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string Label { get; set; }
        public string WorkspaceId { get; set; }
        public bool IsDefault { get; set; }
        public bool NeedsReauthentication { get; set; }
    }

    public sealed class AuthenticationDeviceLogin
    {
        public string Method { get; set; } = "device";
        public string LoginId { get; set; }
        public string TargetAccountId { get; set; }
        public string Status { get; set; }
        public string UserCode { get; set; }
        public string VerificationUri { get; set; }
        public string ExpiresAt { get; set; }
        public string Error { get; set; }
        public AuthenticationAccount Account { get; set; }
    }

    public sealed class AuthenticationAccountsSnapshot
    {
        public bool SupportsBrowserLogin { get; set; }
        public IReadOnlyList<AuthenticationAccount> Accounts { get; set; } = new List<AuthenticationAccount>();
        public AuthenticationDeviceLogin ActiveLogin { get; set; }
        public AuthenticationDeviceLogin LastLogin { get; set; }
    }

    public sealed class AuthenticationSessionHistoryState
    {
        public bool Unified { get; set; }
        // Moving the sessions for the current setting did not finish; setting it again retries.
        public bool Pending { get; set; }
        // Sessions other providers wrote are repaired before they load.
        public bool RepairOnResume { get; set; }
    }

    public sealed class AuthenticationSessionHistoryPreview
    {
        // Earlier official sessions that turning it on moves to the shared identity.
        public int ToMigrate { get; set; }
        // Moved sessions that turning it off gives their official identity back.
        public int ToRestore { get; set; }
    }

    public sealed class AuthenticationSessionHistoryMove
    {
        public int Sessions { get; set; }
        public int IndexRows { get; set; }
        public string Error { get; set; }
        public bool Moved => Sessions > 0 || IndexRows > 0;
    }

    public sealed class AuthenticationSessionHistoryResult
    {
        public AuthenticationSessionHistoryState State { get; set; }
        public bool GlobalApplied { get; set; }
        public AuthenticationSessionHistoryMove Moved { get; set; }
        public bool Reconnected { get; set; }
    }

    public enum AuthenticationSessionRepairMode
    {
        // Before a thread loads with the official provider: only what it refuses for certain.
        Resume,
        // After a provider refused the history: every reasoning item and encrypted fragment.
        Rejected,
        // Also compaction items another provider issued; loses the conversation they carry.
        Compaction
    }

    public sealed class AuthenticationSessionRepairResult
    {
        public bool Repaired { get; set; }
        public string Skipped { get; set; }
        public int Reasoning { get; set; }
        public int Ids { get; set; }
        public int ConvertedCompactions { get; set; }
        public int EncryptedParts { get; set; }
        public int ReplacedCompactions { get; set; }
        public int RemainingCompactions { get; set; }
        public int Changes => Reasoning + Ids + ConvertedCompactions + EncryptedParts + ReplacedCompactions;
    }
}
