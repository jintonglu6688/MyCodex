using System;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public sealed class CustomApprovalPolicyEvaluator
    {
        public CustomApprovalPolicyEvaluation Evaluate(
            ApprovalMode approvalMode,
            CustomApprovalSettingsSnapshot settings,
            ApprovalRequestClassification classification,
            AppServerApprovalRequestKind? approvalKind = null,
            string commandText = null)
        {
            if (approvalMode != ApprovalMode.Custom)
            {
                return new CustomApprovalPolicyEvaluation();
            }

            var categoryEvaluation = EvaluateCategory(settings, classification);
            if (categoryEvaluation.ShouldAutoApprove)
            {
                return categoryEvaluation;
            }

            var matchedPrefix = FindMatchingCommandPrefix(settings, approvalKind, commandText, classification);
            if (!string.IsNullOrEmpty(matchedPrefix))
            {
                return new CustomApprovalPolicyEvaluation
                {
                    ShouldAutoApprove = true,
                    Source = CustomApprovalPolicySource.CommandPrefix,
                    MatchedCommandPrefix = matchedPrefix
                };
            }

            return categoryEvaluation;
        }

        private static CustomApprovalPolicyEvaluation EvaluateCategory(
            CustomApprovalSettingsSnapshot settings,
            ApprovalRequestClassification classification)
        {
            if (classification == null)
            {
                return new CustomApprovalPolicyEvaluation();
            }

            CustomApprovalPermissionKind permission;
            if (!CustomApprovalPermissionCatalog.TryMapCategory(classification.Category, out permission))
            {
                permission = CustomApprovalPermissionKind.RequestUnknown;
            }

            var containsPermission = ContainsPermission(settings, permission);
            return new CustomApprovalPolicyEvaluation
            {
                Permission = permission,
                ShouldAutoApprove = containsPermission,
                Source = containsPermission
                    ? CustomApprovalPolicySource.PermissionCategory
                    : CustomApprovalPolicySource.None
            };
        }

        private static string FindMatchingCommandPrefix(
            CustomApprovalSettingsSnapshot settings,
            AppServerApprovalRequestKind? approvalKind,
            string commandText,
            ApprovalRequestClassification classification)
        {
            if (settings == null ||
                settings.CommandPrefixes == null ||
                approvalKind != AppServerApprovalRequestKind.Command ||
                IsCompoundCommand(commandText, classification))
            {
                return null;
            }

            foreach (var prefix in settings.CommandPrefixes)
            {
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                var normalizedPrefix = prefix.Trim();
                if (CommandStartsWithPrefix(commandText, normalizedPrefix) ||
                    CommandStartsWithPrefix(classification == null ? null : classification.DisplayCommand, normalizedPrefix) ||
                    CommandStartsWithPrefix(classification == null ? null : classification.RawCommand, normalizedPrefix))
                {
                    return normalizedPrefix;
                }
            }

            return null;
        }

        private static bool CommandStartsWithPrefix(string commandText, string prefix)
        {
            if (string.IsNullOrWhiteSpace(commandText) || string.IsNullOrWhiteSpace(prefix))
            {
                return false;
            }

            var command = commandText.TrimStart();
            if (!command.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (command.Length == prefix.Length)
            {
                return true;
            }

            // A prefix ending in a word character must end at a word boundary: "git status"
            // matches "git status --short" but not "git statusx".
            var last = prefix[prefix.Length - 1];
            return !(char.IsLetterOrDigit(last) || last == '_') ||
                   char.IsWhiteSpace(command[prefix.Length]);
        }

        private static bool IsCompoundCommand(string commandText, ApprovalRequestClassification classification)
        {
            // The raw command (or the classifier's copy of it) includes any shell wrapper,
            // so the script inside the wrapper is checked too.
            var source = !string.IsNullOrWhiteSpace(commandText)
                ? commandText
                : classification == null
                    ? null
                    : !string.IsNullOrWhiteSpace(classification.RawCommand)
                        ? classification.RawCommand
                        : classification.DisplayCommand;
            return !string.IsNullOrWhiteSpace(source) && ApprovalRequestClassifier.IsCompoundCommand(source);
        }

        private static bool ContainsPermission(
            CustomApprovalSettingsSnapshot settings,
            CustomApprovalPermissionKind permission)
        {
            if (settings == null || settings.EnabledPermissions == null)
            {
                return false;
            }

            foreach (var enabled in settings.EnabledPermissions)
            {
                if (enabled == permission)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
