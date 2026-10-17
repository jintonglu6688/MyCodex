using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerProtocolMetadataMapper
    {
        internal static AppServerProtocolCapabilities BuildProtocolCapabilities(
            AppConfigSnapshot appConfig,
            AppServerLaunchInfo launchInfo,
            IDictionary<string, object> initializeResult)
        {
            var runtimeSource = CodexRuntimeSources.CanonicalizeForExecution(
                GetLaunchInfoEnvironmentValue(launchInfo, "MYCODEX_CODEX_SOURCE") ??
                (appConfig == null ? null : appConfig.CodexRuntimeSource));
            var runtimeVersion = ResolveRuntimeVersion(runtimeSource, launchInfo, initializeResult);
            var userAgent = GetString(initializeResult, "userAgent");
            var platformOs = GetString(initializeResult, "platformOs");
            var isLegacyRuntime = IsKnownRuntimeOlderThan(runtimeVersion, 0, 128, 0);
            var supportsModernProtocol = !isLegacyRuntime;

            return new AppServerProtocolCapabilities
            {
                RuntimeSource = runtimeSource,
                RuntimeVersion = runtimeVersion,
                HostPlatform = launchInfo == null ? AppServerHostPlatform.Windows : launchInfo.HostPlatform,
                PlatformOs = platformOs,
                UserAgent = userAgent,
                ProtocolProfile = supportsModernProtocol ? "modern" : "legacy-compatible",
                IsLegacyRuntime = isLegacyRuntime,
                SupportsModernAppServerProtocol = supportsModernProtocol,
                SupportsExperimentalApi = supportsModernProtocol,
                SupportsPluginManagement = true,
                SupportsPluginCatalogObservation = true,
                SupportsInstalledPluginObservation = true,
                SupportsMarketplaceRegistration = true,
                SupportsPluginMutation = true,
                SupportsSkills = supportsModernProtocol,
                SupportsMcpOAuth = supportsModernProtocol,
                SupportsMcpManagement = supportsModernProtocol,
                SupportsMcpElicitation = supportsModernProtocol,
                SupportsFileSystemApi = supportsModernProtocol,
                SupportsFileSystem = supportsModernProtocol,
                SupportsFuzzyFileSearch = supportsModernProtocol,
                SupportsWindowsSandbox = supportsModernProtocol,
                SupportsConfigRequirements = supportsModernProtocol,
                SupportsExternalAgentConfigImport = supportsModernProtocol,
                SupportsModelProviderCapabilities = supportsModernProtocol,
                SupportsCommandExecIo = supportsModernProtocol,
                SupportsThreadForkLastTurnId = IsKnownRuntimeAtLeast(runtimeVersion, 0, 143, 0),
                // Verified full-item pagination and excludeTurns contract in 0.158.
                SupportsThreadHistoryPagination = IsKnownRuntimeAtLeast(runtimeVersion, 0, 158, 0),
                SupportsSubagentThreadFilters = IsKnownRuntimeAtLeast(runtimeVersion, 0, 154, 0),
                SupportsCollabAgentToolCall = IsKnownRuntimeAtLeast(runtimeVersion, 0, 154, 0),
                SupportsBrowserUseIntegration = false
            };
        }

        internal static AppServerThreadGoal BuildThreadGoalFromResult(IDictionary<string, object> result, string fallbackThreadId)
        {
            if (result == null)
            {
                return null;
            }

            var goal = GetDictionary(result, "goal");
            if (goal.Count == 0 && (!string.IsNullOrWhiteSpace(GetString(result, "objective")) ||
                                    !string.IsNullOrWhiteSpace(GetString(result, "status"))))
            {
                goal = result;
            }

            var objective = GetString(goal, "objective");
            var status = GetString(goal, "status");
            if (string.IsNullOrWhiteSpace(objective) && string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            return new AppServerThreadGoal
            {
                ThreadId = GetString(goal, "threadId") ?? fallbackThreadId,
                Objective = objective,
                Status = status,
                TokenBudget = GetNullableLong(goal, "tokenBudget"),
                TokensUsed = GetNullableLong(goal, "tokensUsed") ?? 0,
                TimeUsedSeconds = GetNullableLong(goal, "timeUsedSeconds") ?? 0,
                CreatedAt = GetNullableLong(goal, "createdAt") ?? 0,
                UpdatedAt = GetNullableLong(goal, "updatedAt") ?? 0
            };
        }

        internal static Version ParseRuntimeVersion(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var start = -1;
            for (var i = 0; i < text.Length; i++)
            {
                if (char.IsDigit(text[i]))
                {
                    start = i;
                    break;
                }
            }

            if (start < 0)
            {
                return null;
            }

            var end = start;
            while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.'))
            {
                end++;
            }

            var token = text.Substring(start, end - start).Trim('.');
            if (string.IsNullOrWhiteSpace(token))
            {
                return null;
            }

            var rawParts = token.Split('.');
            var parts = new List<int>();
            for (var i = 0; i < rawParts.Length && parts.Count < 4; i++)
            {
                int value;
                if (!int.TryParse(rawParts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                {
                    break;
                }

                parts.Add(value);
            }

            if (parts.Count == 0)
            {
                return null;
            }

            while (parts.Count < 3)
            {
                parts.Add(0);
            }

            return parts.Count >= 4
                ? new Version(parts[0], parts[1], parts[2], parts[3])
                : new Version(parts[0], parts[1], parts[2]);
        }

        private static string ResolveRuntimeVersion(
            string runtimeSource,
            AppServerLaunchInfo launchInfo,
            IDictionary<string, object> initializeResult)
        {
            var explicitVersion = GetLaunchInfoEnvironmentValue(launchInfo, "MYCODEX_CODEX_VERSION");
            if (!string.IsNullOrWhiteSpace(explicitVersion))
            {
                return explicitVersion.Trim();
            }

            return ExtractRuntimeVersion(GetString(initializeResult, "userAgent"));
        }

        private static string GetLaunchInfoEnvironmentValue(AppServerLaunchInfo launchInfo, string name)
        {
            if (launchInfo == null ||
                launchInfo.EnvironmentVariables == null ||
                string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            string value;
            return launchInfo.EnvironmentVariables.TryGetValue(name, out value) &&
                !string.IsNullOrWhiteSpace(value)
                    ? value.Trim()
                    : null;
        }

        private static string ExtractRuntimeVersion(string text)
        {
            var version = ParseRuntimeVersion(text);
            return version == null ? null : version.ToString();
        }

        private static bool IsKnownRuntimeOlderThan(string versionText, int major, int minor, int build)
        {
            var version = ParseRuntimeVersion(versionText);
            return version != null && version.CompareTo(new Version(major, minor, build)) < 0;
        }

        private static bool IsKnownRuntimeAtLeast(string versionText, int major, int minor, int build)
        {
            var version = ParseRuntimeVersion(versionText);
            return version != null && version.CompareTo(new Version(major, minor, build)) >= 0;
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetDictionary(source, key);
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetString(source, key);
        }

        private static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            return AppServerJsonValueReader.GetNullableLong(source, key);
        }
    }
}
