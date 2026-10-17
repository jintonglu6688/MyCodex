using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public static class HostSettingsPolicy
    {
        public static IReadOnlyList<string> NormalizeCommandPrefixes(IEnumerable<string> prefixes)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var prefix in prefixes ?? Enumerable.Empty<string>())
            {
                var value = string.IsNullOrWhiteSpace(prefix) ? null : prefix.Trim();
                if (value != null && seen.Add(value))
                {
                    result.Add(value);
                }
            }

            return result;
        }

        public static void ValidateDefaults(
            HostSettingsDefaults defaults,
            IReadOnlyList<CodingRemoteModelOption> models)
        {
            if (defaults == null || string.IsNullOrWhiteSpace(defaults.ModelId))
            {
                throw Invalid("A model is required.");
            }

            ParseApprovalMode(defaults.ApprovalMode);
            var model = (models ?? new List<CodingRemoteModelOption>())
                .FirstOrDefault(item => item != null &&
                    string.Equals(item.ModelId, defaults.ModelId.Trim(), StringComparison.Ordinal));
            if (model == null)
            {
                throw Invalid("The selected model is unavailable.");
            }

            var efforts = model.ReasoningEfforts ?? new List<string>();
            if (efforts.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(defaults.ReasoningEffort))
                {
                    throw Invalid("The selected model does not support reasoning effort.");
                }

                return;
            }

            if (string.IsNullOrWhiteSpace(defaults.ReasoningEffort) ||
                !efforts.Any(value => string.Equals(value, defaults.ReasoningEffort.Trim(), StringComparison.Ordinal)))
            {
                throw Invalid("The selected reasoning effort is unavailable.");
            }
        }

        public static List<CustomApprovalPermissionKind> ParsePermissionIds(IEnumerable<string> permissionIds)
        {
            if (permissionIds == null)
            {
                throw Invalid("Permission IDs are required.");
            }

            var allowed = CustomApprovalPermissionCatalog.GetOrderedPermissions()
                .ToDictionary(value => value.ToString(), value => value, StringComparer.Ordinal);
            var seen = new HashSet<CustomApprovalPermissionKind>();
            var result = new List<CustomApprovalPermissionKind>();
            foreach (var id in permissionIds)
            {
                CustomApprovalPermissionKind permission;
                if (string.IsNullOrWhiteSpace(id) || !allowed.TryGetValue(id.Trim(), out permission))
                {
                    throw Invalid("An unknown custom permission was supplied.");
                }

                if (seen.Add(permission))
                {
                    result.Add(permission);
                }
            }

            return result;
        }

        public static string FormatApprovalMode(ApprovalMode mode)
        {
            switch (mode)
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

        public static ApprovalMode ParseApprovalMode(string value)
        {
            switch (NormalizeOptional(value))
            {
                case "read-only":
                    return ApprovalMode.ReadOnly;
                case "auto":
                    return ApprovalMode.Auto;
                case "full-access":
                    return ApprovalMode.FullAccess;
                case "full-access-auto-approve":
                    return ApprovalMode.FullAccess;
                case "custom":
                    return ApprovalMode.Custom;
                default:
                    throw Invalid("Unsupported approval mode.");
            }
        }

        private static string NormalizeOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        private static CodingRemoteOperationException Invalid(string message)
        {
            return new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, message);
        }
    }
}
