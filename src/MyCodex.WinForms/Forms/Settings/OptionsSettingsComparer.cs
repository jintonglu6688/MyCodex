using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class OptionsSettingsComparer
    {
        internal static bool AreCustomApprovalSettingsEqual(
            CustomApprovalSettingsSnapshot left,
            CustomApprovalSettingsSnapshot right)
        {
            var leftValues = new HashSet<CustomApprovalPermissionKind>(
                left == null ? new CustomApprovalPermissionKind[0] : left.EnabledPermissions);
            var rightValues = new HashSet<CustomApprovalPermissionKind>(
                right == null ? new CustomApprovalPermissionKind[0] : right.EnabledPermissions);
            return leftValues.SetEquals(rightValues) &&
                AreCustomApprovalCommandPrefixesEqual(
                    left == null ? new string[0] : left.CommandPrefixes,
                    right == null ? new string[0] : right.CommandPrefixes);
        }

        internal static bool AreCustomApprovalCommandPrefixesEqual(
            IEnumerable<string> left,
            IEnumerable<string> right)
        {
            var leftPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(left);
            var rightPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(right);
            if (leftPrefixes.Count != rightPrefixes.Count)
            {
                return false;
            }

            for (var i = 0; i < leftPrefixes.Count; i++)
            {
                if (!string.Equals(leftPrefixes[i], rightPrefixes[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        internal static CustomApprovalSettingsSnapshot CloneCustomApprovalSettings(CustomApprovalSettingsSnapshot source)
        {
            return new CustomApprovalSettingsSnapshot
            {
                EnabledPermissions = new List<CustomApprovalPermissionKind>(
                    source == null ? new CustomApprovalPermissionKind[0] : source.EnabledPermissions),
                CommandPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(
                    source == null ? new string[0] : source.CommandPrefixes).ToList()
            };
        }

    }
}
