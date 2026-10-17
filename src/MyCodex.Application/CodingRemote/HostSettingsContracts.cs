using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.CodingRemote
{
    public sealed class HostSettingsScope
    {
        public string HostName { get; set; }

        public string RuntimeSource { get; set; }

        public string RuntimeDisplayName { get; set; }

        public string RuntimeProfileId { get; set; }

        public string AuthenticationDisplayName { get; set; }
    }

    public sealed class HostSettingsDefaults
    {
        public string ModelId { get; set; }

        public string ReasoningEffort { get; set; }

        public string ApprovalMode { get; set; }
    }

    public sealed class HostSettingsPermissionOption
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }
    }

    public sealed class HostSettingsCustomApproval
    {
        public HostSettingsCustomApproval()
        {
            AvailablePermissions = new List<HostSettingsPermissionOption>();
            EnabledPermissions = new List<string>();
            CommandPrefixes = new List<string>();
        }

        public List<HostSettingsPermissionOption> AvailablePermissions { get; set; }

        public List<string> EnabledPermissions { get; set; }

        public List<string> CommandPrefixes { get; set; }
    }

    public sealed class HostSettingsCustomApprovalUpdate
    {
        public HostSettingsCustomApprovalUpdate()
        {
            EnabledPermissions = new List<string>();
            CommandPrefixes = new List<string>();
        }

        public List<string> EnabledPermissions { get; set; }

        public List<string> CommandPrefixes { get; set; }
    }

    public sealed class HostSettingsSnapshot
    {
        public HostSettingsSnapshot()
        {
            Models = new List<CodingRemoteModelOption>();
        }

        public HostSettingsScope Scope { get; set; }

        public HostSettingsDefaults Defaults { get; set; }

        public List<CodingRemoteModelOption> Models { get; set; }

        public HostSettingsCustomApproval CustomApproval { get; set; }

        public OfficialCodexSettingsSelection Official { get; set; }

    }

    public sealed class HostSettingsUpdateRequest
    {
        public HostSettingsDefaults Defaults { get; set; }

        public HostSettingsCustomApprovalUpdate CustomApproval { get; set; }

        public OfficialCodexSettingsSelection Official { get; set; }

    }

    public sealed class HostSettingsUpdateResult
    {
        public bool Updated { get; set; }

        public HostSettingsSnapshot Settings { get; set; }
    }
}
