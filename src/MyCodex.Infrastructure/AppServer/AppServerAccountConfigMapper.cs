using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerAccountConfigMapper
    {
        internal static AppServerAuthenticationState BuildAuthenticationState(
            IDictionary<string, object> accountState)
        {
            var account = AppServerJsonValue.GetDictionary(accountState, "account");
            var accountType = AppServerJsonValue.GetString(account, "type");
            var normalizedAuthMode = NormalizeAuthMode(accountType);
            var email = GetAuthenticationEmail(account);

            return new AppServerAuthenticationState
            {
                HasAccount = HasAccount(accountState),
                IsAuthenticated = !string.IsNullOrWhiteSpace(normalizedAuthMode) || !string.IsNullOrWhiteSpace(accountType),
                RequiresOpenAiAuth = AppServerJsonValueReader.GetNullableBool(accountState, "requiresOpenaiAuth").GetValueOrDefault(),
                AuthMode = normalizedAuthMode,
                AccountType = accountType,
                Email = email,
                PlanType = AppServerJsonValue.GetString(account, "planType")
            };
        }

        private static bool HasAccount(IDictionary<string, object> accountState)
        {
            return AppServerJsonValueReader.GetValue(accountState, "account") != null;
        }

        private static string NormalizeAuthMode(string accountType)
        {
            if (string.Equals(accountType, "apiKey", StringComparison.OrdinalIgnoreCase))
            {
                return "apikey";
            }

            if (string.Equals(accountType, "chatgpt", StringComparison.OrdinalIgnoreCase))
            {
                return "chatgpt";
            }

            return null;
        }

        private static string GetAuthenticationEmail(IDictionary<string, object> account)
        {
            return GetFirstNonEmpty(
                AppServerJsonValue.GetString(account, "email"),
                GetNestedString(account, "user", "email"),
                AppServerJsonValue.GetString(account, "userEmail"),
                AppServerJsonValue.GetString(account, "emailAddress"),
                GetNestedString(account, "address", "email"));
        }

        private static string GetNestedString(IDictionary<string, object> source, string nestedKey, string valueKey)
        {
            return AppServerJsonValue.GetString(AppServerJsonValue.GetDictionary(source, nestedKey), valueKey);
        }

        internal static AppServerRateLimitsSnapshot BuildRateLimitsSnapshot(IDictionary<string, object> result)
        {
            var snapshot = new AppServerRateLimitsSnapshot();
            var resetCredits = AppServerJsonValue.GetDictionary(result, "rateLimitResetCredits");
            snapshot.RateLimitResetCreditsAvailableCount = AppServerJsonValueReader.GetNullableInt(resetCredits, "availableCount");
            if (AppServerJsonValueReader.GetValue(resetCredits, "credits") != null)
            {
                snapshot.ResetCredits = new List<AppServerRateLimitResetCredit>();
                foreach (var creditObject in AppServerJsonValueReader.GetArray(resetCredits, "credits"))
                {
                    var credit = creditObject as IDictionary<string, object>;
                    if (credit != null)
                    {
                        snapshot.ResetCredits.Add(BuildRateLimitResetCredit(credit));
                    }
                }
            }

            var buckets = AppServerJsonValue.GetDictionary(result, "rateLimitsByLimitId");
            foreach (var pair in buckets)
            {
                var bucket = pair.Value as IDictionary<string, object>;
                if (bucket == null)
                {
                    continue;
                }

                snapshot.Buckets.Add(BuildRateLimitBucket(bucket, pair.Key));
            }

            if (snapshot.Buckets.Count == 0)
            {
                var singleBucket = AppServerJsonValue.GetDictionary(result, "rateLimits");
                if (singleBucket.Count > 0)
                {
                    snapshot.Buckets.Add(BuildRateLimitBucket(singleBucket, AppServerJsonValue.GetString(singleBucket, "limitId")));
                }
            }

            return snapshot;
        }

        internal static AppServerConfigSnapshot BuildConfigSnapshot(IDictionary<string, object> result)
        {
            var snapshot = new AppServerConfigSnapshot();
            AppServerCredentialReferenceMapper.Apply(snapshot, result);
            var config = AppServerJsonValue.GetDictionary(result, "config");
            snapshot.ModelId = AppServerJsonValue.GetString(config, "model");
            snapshot.ReviewModelId = AppServerJsonValue.GetString(config, "review_model");
            snapshot.ModelProviderId = AppServerJsonValue.GetString(config, "model_provider");
            snapshot.AuthCredentialsStore = AppServerJsonValue.GetString(config, "cli_auth_credentials_store");
            snapshot.ModelReasoningEffort = AppServerJsonValue.GetString(config, "model_reasoning_effort");
            snapshot.ApprovalPolicy = DescribeApprovalPolicyValue(AppServerJsonValueReader.GetValue(config, "approval_policy"));
            snapshot.SandboxMode = AppServerJsonValue.GetString(config, "sandbox_mode");
            snapshot.ModelContextWindow = AppServerJsonValueReader.GetNullableLong(config, "model_context_window");
            snapshot.ModelAutoCompactTokenLimit = AppServerJsonValueReader.GetNullableLong(config, "model_auto_compact_token_limit");
            snapshot.ModelReasoningSummary = AppServerJsonValue.GetString(config, "model_reasoning_summary");
            snapshot.ModelVerbosity = AppServerJsonValue.GetString(config, "model_verbosity");
            snapshot.ServiceTier = AppServerJsonValue.GetString(config, "service_tier");
            snapshot.WebSearchMode = AppServerJsonValue.GetString(config, "web_search");
            snapshot.ToolOutputTokenLimit = AppServerJsonValueReader.GetNullableLong(config, "tool_output_token_limit");
            snapshot.ProjectDocMaxBytes = AppServerJsonValueReader.GetNullableLong(config, "project_doc_max_bytes");

            var tools = AppServerJsonValue.GetDictionary(config, "tools");
            snapshot.ViewImageToolEnabled = AppServerJsonValueReader.GetNullableBool(tools, "view_image");
            var webSearchTool = AppServerJsonValue.GetDictionary(tools, "web_search");
            snapshot.WebSearchContextSize = AppServerJsonValue.GetString(webSearchTool, "context_size");

            var origins = AppServerJsonValue.GetDictionary(result, "origins");
            foreach (var pair in origins)
            {
                var metadata = pair.Value as IDictionary<string, object>;
                if (metadata == null)
                {
                    continue;
                }

                var source = AppServerJsonValue.GetDictionary(metadata, "name");
                snapshot.Origins[pair.Key] = new AppServerConfigOrigin
                {
                    KeyPath = pair.Key,
                    SourceText = BuildConfigLayerSourceText(source),
                    SourceType = GetConfigLayerSourceType(source),
                    Version = AppServerJsonValue.GetString(metadata, "version")
                };
            }

            foreach (var layerObject in AppServerJsonValueReader.GetArray(result, "layers"))
            {
                var layer = layerObject as IDictionary<string, object>;
                if (layer == null)
                {
                    continue;
                }

                var source = AppServerJsonValue.GetDictionary(layer, "name");
                snapshot.Layers.Add(new AppServerConfigLayer
                {
                    SourceText = BuildConfigLayerSourceText(source),
                    SourceType = GetConfigLayerSourceType(source),
                    Version = AppServerJsonValue.GetString(layer, "version"),
                    DisabledReason = AppServerJsonValue.GetString(layer, "disabledReason")
                });
            }

            return snapshot;
        }

        internal static AppServerModelOption BuildModelOption(IDictionary<string, object> source)
        {
            var modelId = AppServerJsonValue.GetString(source, "model");
            if (string.IsNullOrWhiteSpace(modelId))
            {
                modelId = AppServerJsonValue.GetString(source, "id");
            }

            var displayName = AppServerJsonValue.GetString(source, "displayName");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = modelId;
            }

            var option = new AppServerModelOption
            {
                ModelId = modelId,
                DisplayName = displayName,
                Description = AppServerJsonValue.GetString(source, "description"),
                IsDefault = AppServerJsonValueReader.GetNullableBool(source, "isDefault").GetValueOrDefault(),
                DefaultReasoningEffort = AppServerJsonValue.GetString(source, "defaultReasoningEffort"),
                DefaultServiceTier = AppServerJsonValue.GetString(source, "defaultServiceTier")
            };

            foreach (var effortObject in AppServerJsonValueReader.GetArray(source, "supportedReasoningEfforts"))
            {
                var effort = effortObject as IDictionary<string, object>;
                if (effort == null)
                {
                    continue;
                }

                var effortId = AppServerJsonValue.GetString(effort, "reasoningEffort");
                if (string.IsNullOrWhiteSpace(effortId))
                {
                    continue;
                }

                option.ReasoningEfforts.Add(new AppServerModelEffortOption
                {
                    EffortId = effortId,
                    Description = AppServerJsonValue.GetString(effort, "description")
                });
            }

            AddServiceTiers(option.ServiceTiers, AppServerJsonValueReader.GetArray(source, "serviceTiers"));
            if (option.ServiceTiers.Count == 0)
            {
                AddServiceTiers(option.ServiceTiers, AppServerJsonValueReader.GetArray(source, "additionalSpeedTiers"));
            }

            return option;
        }

        private static AppServerRateLimitResetCredit BuildRateLimitResetCredit(IDictionary<string, object> source)
        {
            var grantedAt = AppServerJsonValueReader.GetNullableLong(source, "grantedAt");
            var expiresAt = AppServerJsonValueReader.GetNullableLong(source, "expiresAt");
            return new AppServerRateLimitResetCredit
            {
                Id = AppServerJsonValue.GetString(source, "id"),
                ResetType = AppServerJsonValue.GetString(source, "resetType"),
                Status = AppServerJsonValue.GetString(source, "status"),
                GrantedAtLocalTime = grantedAt.HasValue
                    ? DateTimeOffset.FromUnixTimeSeconds(grantedAt.Value).LocalDateTime
                    : default(DateTime),
                ExpiresAtLocalTime = expiresAt.HasValue
                    ? (DateTime?)DateTimeOffset.FromUnixTimeSeconds(expiresAt.Value).LocalDateTime
                    : null,
                Title = AppServerJsonValue.GetString(source, "title"),
                Description = AppServerJsonValue.GetString(source, "description")
            };
        }

        private static void AddServiceTiers(List<AppServerModelServiceTier> target, IEnumerable<object> source)
        {
            foreach (var tierObject in source)
            {
                var legacyId = tierObject as string;
                if (!string.IsNullOrWhiteSpace(legacyId))
                {
                    target.Add(new AppServerModelServiceTier
                    {
                        Id = legacyId,
                        Name = legacyId
                    });
                    continue;
                }

                var tier = tierObject as IDictionary<string, object>;
                if (tier == null)
                {
                    continue;
                }

                var id = AppServerJsonValue.GetString(tier, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                target.Add(new AppServerModelServiceTier
                {
                    Id = id,
                    Name = AppServerJsonValue.GetString(tier, "name"),
                    Description = AppServerJsonValue.GetString(tier, "description")
                });
            }
        }

        private static AppServerRateLimitBucket BuildRateLimitBucket(IDictionary<string, object> source, string fallbackLimitId)
        {
            return new AppServerRateLimitBucket
            {
                LimitId = AppServerJsonValue.GetString(source, "limitId") ?? fallbackLimitId,
                LimitName = AppServerJsonValue.GetString(source, "limitName"),
                PlanType = AppServerJsonValue.GetString(source, "planType"),
                Primary = BuildRateLimitWindow(AppServerJsonValue.GetDictionary(source, "primary")),
                Secondary = BuildRateLimitWindow(AppServerJsonValue.GetDictionary(source, "secondary")),
                Credits = BuildCreditsInfo(AppServerJsonValue.GetDictionary(source, "credits"))
            };
        }

        private static AppServerRateLimitWindowInfo BuildRateLimitWindow(IDictionary<string, object> source)
        {
            if (source == null || source.Count == 0)
            {
                return null;
            }

            var resetsAt = AppServerJsonValueReader.GetNullableLong(source, "resetsAt");
            return new AppServerRateLimitWindowInfo
            {
                UsedPercent = AppServerJsonValueReader.GetNullableDecimal(source, "usedPercent").GetValueOrDefault(),
                WindowDurationMinutes = AppServerJsonValueReader.GetNullableInt(source, "windowDurationMins"),
                ResetsAtLocalTime = resetsAt.HasValue
                    ? (DateTime?)DateTimeOffset.FromUnixTimeSeconds(resetsAt.Value).LocalDateTime
                    : null
            };
        }

        private static AppServerCreditsInfo BuildCreditsInfo(IDictionary<string, object> source)
        {
            if (source == null || source.Count == 0)
            {
                return null;
            }

            return new AppServerCreditsInfo
            {
                HasCredits = AppServerJsonValueReader.GetNullableBool(source, "hasCredits").GetValueOrDefault(),
                Unlimited = AppServerJsonValueReader.GetNullableBool(source, "unlimited").GetValueOrDefault(),
                Balance = AppServerJsonValue.GetString(source, "balance")
            };
        }

        private static string DescribeApprovalPolicyValue(object value)
        {
            if (value == null)
            {
                return null;
            }

            var text = value as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                return dictionary.ContainsKey("granular") ? "granular" : "object";
            }

            return Convert.ToString(value);
        }

        private static string BuildConfigLayerSourceText(IDictionary<string, object> source)
        {
            var sourceType = GetConfigLayerSourceType(source);
            if (string.Equals(sourceType, "mdm", StringComparison.Ordinal))
            {
                return LF(
                    "AppServerClient.ConfigSource.Mdm.Template",
                    "MDM: {0}/{1}",
                    AppServerJsonValue.GetString(source, "domain") ?? L("AppServerClient.Value.UnknownDomain", "(Unknown domain)"),
                    AppServerJsonValue.GetString(source, "key") ?? L("AppServerClient.Value.UnknownKey", "(Unknown key)"));
            }

            switch (sourceType)
            {
                case "user":
                    return L("AppServerClient.ConfigSource.User.Prefix", "User config: ") + AppServerPathUtilities.NormalizeDisplayPath(AppServerJsonValue.GetString(source, "file"));
                case "project":
                    return L("AppServerClient.ConfigSource.Project.Prefix", "Project config: ") + AppServerPathUtilities.NormalizeDisplayPath(AppServerJsonValue.GetString(source, "dotCodexFolder"));
                case "system":
                    return L("AppServerClient.ConfigSource.System.Prefix", "System config: ") + AppServerPathUtilities.NormalizeDisplayPath(AppServerJsonValue.GetString(source, "file"));
                case "sessionFlags":
                    return L("AppServerClient.ConfigSource.SessionArguments", "Session arguments");
                case "legacyManagedConfigTomlFromFile":
                    return L("AppServerClient.ConfigSource.LegacyManaged.Prefix", "Legacy managed config: ") + AppServerPathUtilities.NormalizeDisplayPath(AppServerJsonValue.GetString(source, "file"));
                case "legacyManagedConfigTomlFromMdm":
                    return L("AppServerClient.ConfigSource.LegacyMdmManaged", "Legacy MDM managed config");
                default:
                    return L("AppServerClient.ConfigSource.UnknownLayer", "Unknown config layer");
            }
        }

        private static string GetConfigLayerSourceType(IDictionary<string, object> source)
        {
            return AppServerJsonValue.GetString(source, "type") ?? string.Empty;
        }

        private static string GetFirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? Array.Empty<object>());
        }
    }
}
