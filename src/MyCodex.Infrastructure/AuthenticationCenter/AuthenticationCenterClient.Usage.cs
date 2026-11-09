using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient
    {
        public async Task<AuthenticationUsageSettings> ReadUsageAsync(string providerId, CancellationToken cancellationToken)
        {
            return ReadUsageSettings(await _request("usage/get", UsageSelection(providerId), cancellationToken).ConfigureAwait(false));
        }

        public async Task<AuthenticationUsageSettings> SaveUsageAsync(AuthenticationUsageEdit edit, CancellationToken cancellationToken)
        {
            if (edit == null) throw new ArgumentNullException(nameof(edit));
            var parameters = UsageSelection(edit.ProviderId);
            parameters["enabled"] = edit.Enabled;
            parameters["templateType"] = edit.TemplateType;
            parameters["baseUrl"] = edit.BaseUrl;
            parameters["userId"] = edit.UserId;
            parameters["timeout"] = edit.Timeout;
            parameters["autoQueryInterval"] = edit.AutoQueryInterval;
            parameters["code"] = edit.Code;
            // An omitted secret keeps the saved one.
            if (!string.IsNullOrEmpty(edit.ApiKey)) parameters["apiKey"] = edit.ApiKey;
            if (edit.ClearApiKey) parameters["clearApiKey"] = true;
            if (!string.IsNullOrEmpty(edit.AccessToken)) parameters["accessToken"] = edit.AccessToken;
            if (edit.ClearAccessToken) parameters["clearAccessToken"] = true;
            if (edit.ZhipuTeam)
            {
                parameters["zhipuTeam"] = true;
                parameters["teamOrganizationId"] = edit.TeamOrganizationId;
                parameters["teamProjectId"] = edit.TeamProjectId;
            }
            if (!string.IsNullOrEmpty(edit.AccessKeyId)) parameters["accessKeyId"] = edit.AccessKeyId;
            if (!string.IsNullOrEmpty(edit.SecretAccessKey)) parameters["secretAccessKey"] = edit.SecretAccessKey;
            if (edit.ClearAccessKey) parameters["clearAccessKey"] = true;
            return ReadUsageSettings(await _request("usage/save", parameters, cancellationToken).ConfigureAwait(false));
        }

        public async Task<AuthenticationUsageResult> QueryUsageAsync(string providerId, CancellationToken cancellationToken)
        {
            var result = await _request("usage/query", UsageSelection(providerId), cancellationToken).ConfigureAwait(false);
            if (!result.TryGetValue("success", out var success) || !(success is bool))
                throw new AuthenticationCenterException("invalid_response");
            return new AuthenticationUsageResult
            {
                Success = (bool)success,
                Error = Text(result, "error"),
                Items = Objects(result, "data").Select(entry => new AuthenticationUsageItem
                {
                    PlanName = Text(entry, "planName"),
                    Remaining = Number(entry, "remaining"),
                    Used = Number(entry, "used"),
                    Total = Number(entry, "total"),
                    Unit = Text(entry, "unit"),
                    IsValid = entry.TryGetValue("isValid", out var valid) && valid is bool flag ? (bool?)flag : null,
                    InvalidMessage = Text(entry, "invalidMessage"),
                    Extra = Text(entry, "extra")
                }).ToList()
            };
        }

        private static Dictionary<string, object> UsageSelection(string providerId)
        {
            if (string.IsNullOrEmpty(providerId)) throw new ArgumentNullException(nameof(providerId));
            return new Dictionary<string, object> { { "providerId", providerId } };
        }

        private static AuthenticationUsageSettings ReadUsageSettings(IDictionary<string, object> value)
        {
            var settings = new AuthenticationUsageSettings
            {
                ProviderId = Text(value, "providerId") ?? throw new AuthenticationCenterException("invalid_response"),
                ProviderName = Text(value, "providerName"),
                Detected = Text(value, "detected"),
                PlanProvider = Text(value, "planProvider")
            };
            if (value.TryGetValue("config", out var item) && item is IDictionary<string, object> config)
            {
                settings.Configured = true;
                settings.Enabled = Flag(config, "enabled");
                settings.TemplateType = Text(config, "templateType");
                settings.BaseUrl = Text(config, "baseUrl");
                settings.HasApiKey = Flag(config, "hasApiKey");
                settings.HasAccessToken = Flag(config, "hasAccessToken");
                settings.UserId = Text(config, "userId");
                settings.Timeout = (int)(Number(config, "timeout") ?? 10);
                settings.AutoQueryInterval = (int)(Number(config, "autoQueryInterval") ?? 5);
                settings.Code = Text(config, "code");
                settings.ZhipuTeam = Flag(config, "zhipuTeam");
                settings.HasAccessKey = Flag(config, "hasAccessKey");
                settings.TeamOrganizationId = Text(config, "teamOrganizationId");
                settings.TeamProjectId = Text(config, "teamProjectId");
            }
            return settings;
        }

        // Invariant conversion: the serializer yields int, long, decimal or double for JSON numbers.
        private static decimal? Number(IDictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var item) || item == null || item is string || item is bool) return null;
            try
            {
                return Convert.ToDecimal(item, CultureInfo.InvariantCulture);
            }
            catch (Exception error) when (error is OverflowException || error is InvalidCastException)
            {
                return null;
            }
        }
    }
}
