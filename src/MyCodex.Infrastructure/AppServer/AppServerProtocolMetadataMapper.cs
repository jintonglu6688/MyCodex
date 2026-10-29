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
            var userAgent = AppServerJsonValueReader.GetString(initializeResult, "userAgent");
            var platformOs = AppServerJsonValueReader.GetString(initializeResult, "platformOs");
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
                // Voice uses WebRTC realtime v3 and thread/timeline/list from 0.163; only the local Windows target is supported.
                SupportsRealtimeVoice = IsKnownRuntimeAtLeast(runtimeVersion, 0, 163, 0) &&
                    (launchInfo == null || launchInfo.HostPlatform == AppServerHostPlatform.Windows),
                SupportsSubagentThreadFilters = IsKnownRuntimeAtLeast(runtimeVersion, 0, 154, 0),
                SupportsCollabAgentToolCall = IsKnownRuntimeAtLeast(runtimeVersion, 0, 154, 0),
                SupportsBrowserUseIntegration = IsKnownRuntimeAtLeast(runtimeVersion, 0, 164, 0) &&
                    (launchInfo == null || launchInfo.HostPlatform == AppServerHostPlatform.Windows)
            };
        }

        internal static AppServerThreadGoal BuildThreadGoalFromResult(IDictionary<string, object> result, string fallbackThreadId)
        {
            if (result == null)
            {
                return null;
            }

            var goal = AppServerJsonValueReader.GetDictionary(result, "goal");
            if (goal.Count == 0 && (!string.IsNullOrWhiteSpace(AppServerJsonValueReader.GetString(result, "objective")) ||
                                    !string.IsNullOrWhiteSpace(AppServerJsonValueReader.GetString(result, "status"))))
            {
                goal = result;
            }

            var objective = AppServerJsonValueReader.GetString(goal, "objective");
            var status = AppServerJsonValueReader.GetString(goal, "status");
            if (string.IsNullOrWhiteSpace(objective) && string.IsNullOrWhiteSpace(status))
            {
                return null;
            }

            return new AppServerThreadGoal
            {
                ThreadId = AppServerJsonValueReader.GetString(goal, "threadId") ?? fallbackThreadId,
                Objective = objective,
                Status = status,
                TokenBudget = AppServerJsonValueReader.GetNullableLong(goal, "tokenBudget"),
                TokensUsed = AppServerJsonValueReader.GetNullableLong(goal, "tokensUsed") ?? 0,
                TimeUsedSeconds = AppServerJsonValueReader.GetNullableLong(goal, "timeUsedSeconds") ?? 0,
                CreatedAt = AppServerJsonValueReader.GetNullableLong(goal, "createdAt") ?? 0,
                UpdatedAt = AppServerJsonValueReader.GetNullableLong(goal, "updatedAt") ?? 0
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

            return ExtractRuntimeVersion(AppServerJsonValueReader.GetString(initializeResult, "userAgent"));
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
    }
}
