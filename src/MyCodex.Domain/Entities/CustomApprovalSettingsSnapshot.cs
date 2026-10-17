using System.Collections.Generic;
using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class CustomApprovalSettingsSnapshot
    {
        private List<CustomApprovalPermissionKind> _enabledPermissions =
            new List<CustomApprovalPermissionKind>();
        private List<string> _commandPrefixes = new List<string>();

        [DataMember]
        public List<CustomApprovalPermissionKind> EnabledPermissions
        {
            get
            {
                if (_enabledPermissions == null)
                {
                    _enabledPermissions = new List<CustomApprovalPermissionKind>();
                }

                return _enabledPermissions;
            }
            set { _enabledPermissions = value ?? new List<CustomApprovalPermissionKind>(); }
        }

        [DataMember]
        public List<string> CommandPrefixes
        {
            get
            {
                if (_commandPrefixes == null)
                {
                    _commandPrefixes = new List<string>();
                }

                return _commandPrefixes;
            }
            set { _commandPrefixes = value ?? new List<string>(); }
        }
    }
}
