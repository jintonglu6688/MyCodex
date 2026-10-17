using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerMcpServerMapper
    {
        internal static List<AppServerMcpServerStatus> BuildMcpServerStatuses(
            IDictionary<string, object> statusResult,
            IDictionary<string, object> configResult)
        {
            var servers = new Dictionary<string, AppServerMcpServerStatus>(StringComparer.OrdinalIgnoreCase);
            var hasEntries = false;

            if (statusResult != null)
            {
                hasEntries =
                    MergeMcpServerDataFromKey(servers, statusResult, "statuses", true, false) |
                    MergeMcpServerDataFromKey(servers, statusResult, "configs", false, true) |
                    MergeMcpServerDataFromKey(servers, statusResult, "servers", true, true) |
                    MergeMcpServerDataFromKey(servers, statusResult, "serverStatuses", true, false) |
                    MergeMcpServerDataFromKey(servers, statusResult, "serverConfigs", false, true) |
                    MergeMcpServerDataFromKey(servers, statusResult, "data", true, true) |
                    MergeMcpServerDataFromKey(servers, statusResult, "items", true, true);
            }

            hasEntries = MergeMcpServerConfigEntries(servers, configResult) | hasEntries;

            if (!hasEntries && LooksLikeMcpServerPayload(statusResult))
            {
                var server = GetOrCreateMcpServerStatus(servers, GetFirstNonEmptyString(statusResult, "serverName", "name", "id"));
                ApplyMcpServerStatusPayload(server, statusResult, server.ServerName);
                ApplyMcpServerConfigPayload(server, statusResult);
            }

            foreach (var item in servers.Values)
            {
                item.DetailText = BuildMcpServerDetailText(item);
            }

            return servers.Values
                .OrderBy(item => item.ServerName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool MergeMcpServerConfigEntries(
            Dictionary<string, AppServerMcpServerStatus> target,
            IDictionary<string, object> configResult)
        {
            if (configResult == null)
            {
                return false;
            }

            var hasEntries = false;
            var config = AppServerJsonValue.GetDictionary(configResult, "config");
            hasEntries = MergeMcpServerConfigObject(target, AppServerJsonValue.GetDictionary(config, "mcp_servers")) | hasEntries;
            hasEntries = MergeMcpServerConfigObject(target, AppServerJsonValue.GetDictionary(config, "mcpServers")) | hasEntries;
            hasEntries = MergeMcpServerConfigObject(target, AppServerJsonValue.GetDictionary(configResult, "mcp_servers")) | hasEntries;
            hasEntries = MergeMcpServerConfigObject(target, AppServerJsonValue.GetDictionary(configResult, "mcpServers")) | hasEntries;
            return hasEntries;
        }

        private static bool MergeMcpServerConfigObject(
            Dictionary<string, AppServerMcpServerStatus> target,
            IDictionary<string, object> configEntries)
        {
            if (target == null || configEntries == null || configEntries.Count == 0)
            {
                return false;
            }

            var hasEntries = false;
            foreach (var pair in configEntries)
            {
                var payload = pair.Value as IDictionary<string, object>;
                if (payload == null)
                {
                    continue;
                }

                var server = GetOrCreateMcpServerStatus(target, pair.Key);
                ApplyMcpServerConfigPayload(server, payload);
                hasEntries = true;
            }

            return hasEntries;
        }

        private static bool MergeMcpServerDataFromKey(
            Dictionary<string, AppServerMcpServerStatus> target,
            IDictionary<string, object> source,
            string key,
            bool includeStatusPayload,
            bool includeConfigPayload)
        {
            var rawValue = GetValue(source, key);
            if (rawValue == null)
            {
                return false;
            }

            var hasEntries = false;
            foreach (var entry in EnumerateNamedMcpServerPayloads(rawValue))
            {
                var server = GetOrCreateMcpServerStatus(target, entry.Key);
                if (includeStatusPayload)
                {
                    ApplyMcpServerStatusPayload(server, entry.Value, entry.Key);
                }

                if (includeConfigPayload)
                {
                    ApplyMcpServerConfigPayload(server, entry.Value);
                }

                hasEntries = true;
            }

            return hasEntries;
        }

        private static IEnumerable<KeyValuePair<string, IDictionary<string, object>>> EnumerateNamedMcpServerPayloads(object rawValue)
        {
            var dictionary = rawValue as IDictionary<string, object>;
            if (dictionary != null)
            {
                if (LooksLikeMcpServerPayload(dictionary))
                {
                    yield return new KeyValuePair<string, IDictionary<string, object>>(
                        GetFirstNonEmptyString(dictionary, "serverName", "name", "id"),
                        dictionary);
                    yield break;
                }

                foreach (var pair in dictionary)
                {
                    var child = pair.Value as IDictionary<string, object>;
                    if (child == null)
                    {
                        continue;
                    }

                    yield return new KeyValuePair<string, IDictionary<string, object>>(pair.Key, child);
                }

                yield break;
            }

            var enumerable = rawValue as System.Collections.IEnumerable;
            if (enumerable == null || rawValue is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                var child = item as IDictionary<string, object>;
                if (child == null)
                {
                    continue;
                }

                yield return new KeyValuePair<string, IDictionary<string, object>>(
                    GetFirstNonEmptyString(child, "serverName", "name", "id"),
                    child);
            }
        }

        private static bool LooksLikeMcpServerPayload(IDictionary<string, object> payload)
        {
            return payload != null &&
                   (payload.ContainsKey("status") ||
                    payload.ContainsKey("authStatus") ||
                    payload.ContainsKey("enabled") ||
                    payload.ContainsKey("resources") ||
                    payload.ContainsKey("resourceTemplates") ||
                    payload.ContainsKey("tools") ||
                    payload.ContainsKey("command") ||
                    payload.ContainsKey("args") ||
                    payload.ContainsKey("enabled_tools") ||
                    payload.ContainsKey("disabled_tools") ||
                    payload.ContainsKey("oauth_resource") ||
                    payload.ContainsKey("required") ||
                    payload.ContainsKey("url") ||
                    payload.ContainsKey("authorizationUrl") ||
                    !string.IsNullOrWhiteSpace(GetFirstNonEmptyString(payload, "serverName", "name", "id")));
        }

        private static AppServerMcpServerStatus GetOrCreateMcpServerStatus(
            Dictionary<string, AppServerMcpServerStatus> target,
            string serverName)
        {
            var normalizedServerName = string.IsNullOrWhiteSpace(serverName) ? null : serverName.Trim();
            var key = normalizedServerName ?? "__unnamed_" + target.Count.ToString();

            AppServerMcpServerStatus result;
            if (!target.TryGetValue(key, out result))
            {
                result = new AppServerMcpServerStatus
                {
                    ServerName = normalizedServerName ?? L("AppServerClient.Value.UnnamedServer", "Unnamed server")
                };
                target[key] = result;
            }
            else if (string.IsNullOrWhiteSpace(result.ServerName) && !string.IsNullOrWhiteSpace(normalizedServerName))
            {
                result.ServerName = normalizedServerName;
            }

            return result;
        }

        private static void ApplyMcpServerStatusPayload(
            AppServerMcpServerStatus target,
            IDictionary<string, object> payload,
            string fallbackServerName)
        {
            if (target == null || payload == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(target.ServerName))
            {
                target.ServerName = GetFirstNonEmptyString(payload, "serverName", "name", "id") ??
                                    fallbackServerName ??
                                    L("AppServerClient.Value.UnnamedServer", "Unnamed server");
            }

            var status = ExtractMcpServerStatusText(payload);
            if (!string.IsNullOrWhiteSpace(status))
            {
                target.Status = status;
            }

            var authStatus = ExtractMcpServerAuthStatusText(payload);
            if (!string.IsNullOrWhiteSpace(authStatus))
            {
                target.AuthStatus = authStatus;
                target.SupportsOAuth = !string.Equals(authStatus, "unsupported", StringComparison.OrdinalIgnoreCase);
            }

            var toolCount = GetCollectionCount(payload, "tools");
            if (toolCount.HasValue)
            {
                target.ToolCount = toolCount.Value;
            }

            var resourceCount = GetCollectionCount(payload, "resources");
            if (resourceCount.HasValue)
            {
                target.ResourceCount = resourceCount.Value;
            }
            ApplyMcpResourceSummaries(target.Resources, GetValue(payload, "resources"), false);

            var resourceTemplateCount = GetCollectionCount(payload, "resourceTemplates");
            if (resourceTemplateCount.HasValue)
            {
                target.ResourceTemplateCount = resourceTemplateCount.Value;
            }
            ApplyMcpResourceSummaries(target.ResourceTemplates, GetValue(payload, "resourceTemplates"), true);

            var errorText = GetFirstNonEmptyString(payload, "error", "statusMessage", "message");
            if (string.IsNullOrWhiteSpace(errorText))
            {
                errorText = GetFirstNonEmptyString(AppServerJsonValue.GetDictionary(payload, "status"), "message", "error");
            }

            if (!string.IsNullOrWhiteSpace(errorText))
            {
                target.ErrorText = errorText;
            }

        }

        private static void ApplyMcpServerConfigPayload(AppServerMcpServerStatus target, IDictionary<string, object> payload)
        {
            if (target == null || payload == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(target.ServerName))
            {
                target.ServerName = GetFirstNonEmptyString(payload, "serverName", "name", "id") ?? L("AppServerClient.Value.UnnamedServer", "Unnamed server");
            }

            target.ConfigValues = CloneDictionary(payload);

            var enabled = GetNullableBool(payload, "enabled");
            if (enabled.HasValue)
            {
                target.Enabled = enabled.Value;
            }
            else if (!target.Enabled.HasValue &&
                     (payload.ContainsKey("command") ||
                      payload.ContainsKey("args") ||
                      payload.ContainsKey("url") ||
                      payload.ContainsKey("baseUrl") ||
                      payload.ContainsKey("serverUrl") ||
                      payload.ContainsKey("endpoint")))
            {
                target.Enabled = true;
            }

            var commandText = BuildMcpServerCommandText(payload);
            var sourceDetail = commandText;
            var sourceSummary = string.IsNullOrWhiteSpace(commandText) ? null : L("AppServerClient.Mcp.SourceSummary.Command", "Command");

            if (string.IsNullOrWhiteSpace(sourceDetail))
            {
                sourceDetail = GetFirstNonEmptyString(payload, "url", "baseUrl", "serverUrl", "endpoint");
                if (!string.IsNullOrWhiteSpace(sourceDetail))
                {
                    sourceSummary = L("AppServerClient.Mcp.SourceSummary.Http", "HTTP");
                }
            }

            if (!string.IsNullOrWhiteSpace(sourceSummary))
            {
                target.SourceSummary = sourceSummary;
            }

            if (!string.IsNullOrWhiteSpace(sourceDetail))
            {
                target.SourceDetail = sourceDetail;
            }

            var required = GetNullableBool(payload, "required");
            if (required.HasValue)
            {
                target.IsRequired = required.Value;
            }

            var oauthResource = GetFirstNonEmptyString(payload, "oauth_resource", "oauthResource");
            if (!string.IsNullOrWhiteSpace(oauthResource))
            {
                target.OAuthResource = oauthResource;
                target.SupportsOAuth = true;
            }

            var enabledToolsText = BuildDelimitedString(GetValue(payload, "enabled_tools"));
            if (!string.IsNullOrWhiteSpace(enabledToolsText))
            {
                target.EnabledToolsText = enabledToolsText;
            }

            var disabledToolsText = BuildDelimitedString(GetValue(payload, "disabled_tools"));
            if (!string.IsNullOrWhiteSpace(disabledToolsText))
            {
                target.DisabledToolsText = disabledToolsText;
            }

            var environmentText = BuildEnvironmentSummary(GetValue(payload, "env_vars"));
            if (!string.IsNullOrWhiteSpace(environmentText))
            {
                target.EnvironmentText = environmentText;
            }
        }

        private static string ExtractMcpServerStatusText(IDictionary<string, object> payload)
        {
            var rawStatus = GetValue(payload, "status");
            var status = rawStatus as IDictionary<string, object>;
            if (status != null)
            {
                return GetFirstNonEmptyString(status, "type", "status", "state");
            }

            var statusText = rawStatus == null ? null : Convert.ToString(rawStatus);
            return !string.IsNullOrWhiteSpace(statusText) ? statusText : null;
        }

        private static string ExtractMcpServerAuthStatusText(IDictionary<string, object> payload)
        {
            var rawAuthStatus = GetValue(payload, "authStatus");
            var authStatus = rawAuthStatus as IDictionary<string, object>;
            if (authStatus != null)
            {
                return GetFirstNonEmptyString(authStatus, "type", "status", "state");
            }

            var authStatusText = rawAuthStatus == null ? null : Convert.ToString(rawAuthStatus);
            return !string.IsNullOrWhiteSpace(authStatusText) ? authStatusText : null;
        }

        private static int? GetCollectionCount(IDictionary<string, object> source, string key)
        {
            object rawValue;
            if (source == null || !source.TryGetValue(key, out rawValue) || rawValue == null)
            {
                return null;
            }

            var dictionary = rawValue as IDictionary<string, object>;
            if (dictionary != null)
            {
                return dictionary.Count;
            }

            var list = rawValue as System.Collections.ICollection;
            if (list != null)
            {
                return list.Count;
            }

            int numericValue;
            return int.TryParse(Convert.ToString(rawValue), out numericValue) ? numericValue : (int?)null;
        }

        private static void ApplyMcpResourceSummaries(
            IList<AppServerMcpResourceSummary> target,
            object rawValue,
            bool isTemplate)
        {
            if (target == null || rawValue == null)
            {
                return;
            }

            var summaries = BuildMcpResourceSummaries(rawValue, isTemplate).ToList();
            if (summaries.Count == 0)
            {
                return;
            }

            target.Clear();
            foreach (var summary in summaries)
            {
                target.Add(summary);
            }
        }

        private static IEnumerable<AppServerMcpResourceSummary> BuildMcpResourceSummaries(object rawValue, bool isTemplate)
        {
            if (rawValue == null)
            {
                yield break;
            }

            var text = rawValue as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                yield return CreateMcpResourceSummary(text.Trim(), null, null, isTemplate);
                yield break;
            }

            var dictionary = rawValue as IDictionary<string, object>;
            if (dictionary != null)
            {
                if (LooksLikeMcpResourcePayload(dictionary, isTemplate))
                {
                    yield return CreateMcpResourceSummary(null, dictionary, null, isTemplate);
                    yield break;
                }

                foreach (var pair in dictionary)
                {
                    var child = pair.Value as IDictionary<string, object>;
                    if (child != null)
                    {
                        yield return CreateMcpResourceSummary(pair.Key, child, pair.Key, isTemplate);
                        continue;
                    }

                    var valueText = pair.Value == null ? null : Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                    if (string.IsNullOrWhiteSpace(pair.Key) && string.IsNullOrWhiteSpace(valueText))
                    {
                        continue;
                    }

                    yield return new AppServerMcpResourceSummary
                    {
                        Uri = !string.IsNullOrWhiteSpace(pair.Key) ? pair.Key.Trim() : valueText,
                        Name = !string.IsNullOrWhiteSpace(pair.Key) ? pair.Key.Trim() : null,
                        Description = !string.IsNullOrWhiteSpace(valueText) && !string.Equals(pair.Key, valueText, StringComparison.Ordinal) ? valueText.Trim() : null,
                        IsTemplate = isTemplate
                    };
                }

                yield break;
            }

            var enumerable = rawValue as System.Collections.IEnumerable;
            if (enumerable == null)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                if (item == null)
                {
                    continue;
                }

                var child = item as IDictionary<string, object>;
                if (child != null)
                {
                    yield return CreateMcpResourceSummary(null, child, null, isTemplate);
                    continue;
                }

                var itemText = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(itemText))
                {
                    yield return CreateMcpResourceSummary(itemText.Trim(), null, null, isTemplate);
                }
            }
        }

        private static bool LooksLikeMcpResourcePayload(IDictionary<string, object> payload, bool isTemplate)
        {
            return payload != null &&
                   (payload.ContainsKey("uri") ||
                    payload.ContainsKey("uriTemplate") ||
                    payload.ContainsKey("template") ||
                    payload.ContainsKey("name") ||
                    payload.ContainsKey("title") ||
                    payload.ContainsKey("description") ||
                    payload.ContainsKey("mimeType") ||
                    payload.ContainsKey("mime_type") ||
                    isTemplate && payload.ContainsKey("path"));
        }

        private static AppServerMcpResourceSummary CreateMcpResourceSummary(
            string rawText,
            IDictionary<string, object> payload,
            string fallbackName,
            bool isTemplate)
        {
            var uri = GetFirstNonEmptyString(payload, "uri", "uriTemplate", "template", "path");
            var name = GetFirstNonEmptyString(payload, "name", "id");
            var title = GetFirstNonEmptyString(payload, "title");
            var description = GetFirstNonEmptyString(payload, "description");
            var mimeType = GetFirstNonEmptyString(payload, "mimeType", "mime_type");

            if (string.IsNullOrWhiteSpace(uri))
            {
                uri = rawText;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = fallbackName;
            }

            return new AppServerMcpResourceSummary
            {
                Uri = string.IsNullOrWhiteSpace(uri) ? null : uri.Trim(),
                Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                Title = string.IsNullOrWhiteSpace(title) ? null : title.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? null : mimeType.Trim(),
                IsTemplate = isTemplate
            };
        }

        private static string BuildMcpServerCommandText(IDictionary<string, object> payload)
        {
            var command = AppServerJsonValue.GetString(payload, "command");
            if (string.IsNullOrWhiteSpace(command))
            {
                return null;
            }

            var arguments = new List<string>();
            foreach (var argument in GetArray(payload, "args"))
            {
                var text = argument == null ? null : Convert.ToString(argument);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    arguments.Add(text.Trim());
                }
            }

            return arguments.Count == 0
                ? command.Trim()
                : command.Trim() + " " + string.Join(" ", arguments);
        }

        private static string BuildDelimitedString(object rawValue)
        {
            if (rawValue == null)
            {
                return null;
            }

            var text = rawValue as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }

            var values = new List<string>();
            var enumerable = rawValue as System.Collections.IEnumerable;
            if (enumerable != null && !(rawValue is string))
            {
                foreach (var item in enumerable)
                {
                    var itemText = item == null ? null : Convert.ToString(item);
                    if (!string.IsNullOrWhiteSpace(itemText))
                    {
                        values.Add(itemText.Trim());
                    }
                }
            }

            return values.Count == 0 ? null : string.Join(", ", values);
        }

        private static string BuildEnvironmentSummary(object rawValue)
        {
            var dictionary = rawValue as IDictionary<string, object>;
            if (dictionary == null)
            {
                return BuildDelimitedString(rawValue);
            }

            var keys = dictionary.Keys
                .Where(key => !string.IsNullOrWhiteSpace(key))
                .Take(6)
                .ToArray();
            if (keys.Length == 0)
            {
                return L("AppServerClient.Mcp.Environment.Configured", "Configured");
            }

            var summary = string.Join(", ", keys);
            return dictionary.Count > keys.Length
                ? LF("AppServerClient.Mcp.Environment.ItemsWithEllipsis.Template", "{0} items: {1}...", dictionary.Count, summary)
                : LF("AppServerClient.Mcp.Environment.Items.Template", "{0} items: {1}", dictionary.Count, summary);
        }

        private static string BuildMcpServerDetailText(AppServerMcpServerStatus item)
        {
            var builder = new StringBuilder();
            builder.AppendLine(LF("AppServerClient.Mcp.Detail.Server.Template", "Server: {0}", item.ServerName ?? L("AppServerClient.Value.UnnamedServer", "Unnamed server")));
            builder.AppendLine(LF("AppServerClient.Mcp.Detail.Enabled.Template", "Enabled: {0}", DescribeMcpServerEnabled(item.Enabled)));
            builder.AppendLine(LF("AppServerClient.Mcp.Detail.Auth.Template", "Auth: {0}", DescribeMcpServerAuthStatus(item.AuthStatus)));

            if (!string.IsNullOrWhiteSpace(item.Status))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.Runtime.Template", "Runtime: {0}", DescribeMcpServerStatus(item.Status)));
            }

            if (item.ToolCount.HasValue || item.ResourceCount.HasValue || item.ResourceTemplateCount.HasValue)
            {
                builder.AppendLine(string.Format(
                    L("AppServerClient.Mcp.Detail.Capabilities.Template", "Capabilities: tools {0}, resources {1}, templates {2}"),
                    item.ToolCount.HasValue ? item.ToolCount.Value.ToString() : L("AppServerClient.Value.Unknown", "Unknown"),
                    item.ResourceCount.HasValue ? item.ResourceCount.Value.ToString() : L("AppServerClient.Value.Unknown", "Unknown"),
                    item.ResourceTemplateCount.HasValue ? item.ResourceTemplateCount.Value.ToString() : L("AppServerClient.Value.Unknown", "Unknown")));
            }

            if (!string.IsNullOrWhiteSpace(item.SourceSummary))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.Source.Template", "Source: {0}", item.SourceSummary));
            }

            if (!string.IsNullOrWhiteSpace(item.SourceDetail))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.Entry.Template", "Entry: {0}", item.SourceDetail));
            }

            if (item.IsRequired.HasValue)
            {
                builder.AppendLine(LF(
                    "AppServerClient.Mcp.Detail.Required.Template",
                    "Required: {0}",
                    item.IsRequired.Value
                        ? L("AppServerClient.Value.Yes", "Yes")
                        : L("AppServerClient.Value.No", "No")));
            }

            if (!string.IsNullOrWhiteSpace(item.OAuthResource))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.OAuthResource.Template", "OAuth resource: {0}", item.OAuthResource));
            }

            if (!string.IsNullOrWhiteSpace(item.EnabledToolsText))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.EnabledTools.Template", "Enabled tools: {0}", item.EnabledToolsText));
            }

            if (!string.IsNullOrWhiteSpace(item.DisabledToolsText))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.DisabledTools.Template", "Disabled tools: {0}", item.DisabledToolsText));
            }

            if (!string.IsNullOrWhiteSpace(item.EnvironmentText))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.Environment.Template", "Environment variables: {0}", item.EnvironmentText));
            }

            if (!string.IsNullOrWhiteSpace(item.ErrorText))
            {
                builder.AppendLine(LF("AppServerClient.Mcp.Detail.Error.Template", "Error: {0}", item.ErrorText));
            }

            return builder.ToString().TrimEnd();
        }

        private static string DescribeMcpServerEnabled(bool? enabled)
        {
            if (!enabled.HasValue)
            {
                return L("AppServerClient.Value.Unknown", "Unknown");
            }

            return enabled.Value ? L("AppServerClient.Value.Enabled", "Enabled") : L("AppServerClient.Value.Disabled", "Disabled");
        }

        private static string DescribeMcpServerStatus(string status)
        {
            switch ((status ?? string.Empty).Trim())
            {
                case "active":
                    return L("AppServerClient.Mcp.Status.Active", "Active");
                case "idle":
                    return L("AppServerClient.Mcp.Status.Idle", "Idle");
                case "notLoaded":
                    return L("AppServerClient.Mcp.Status.Unloaded", "Unloaded");
                case "systemError":
                    return L("AppServerClient.Mcp.Status.SystemError", "System error");
                default:
                    return string.IsNullOrWhiteSpace(status) ? L("AppServerClient.Value.Unknown", "Unknown") : status;
            }
        }

        private static string DescribeMcpServerAuthStatus(string authStatus)
        {
            switch ((authStatus ?? string.Empty).Trim())
            {
                case "unsupported":
                    return L("AppServerClient.Mcp.AuthStatus.Unsupported", "Unsupported");
                case "notLoggedIn":
                    return L("AppServerClient.Mcp.AuthStatus.NotLoggedIn", "Not logged in");
                case "bearerToken":
                    return L("AppServerClient.Mcp.AuthStatus.BearerToken", "Bearer Token");
                case "oAuth":
                    return L("AppServerClient.Mcp.AuthStatus.OAuth", "OAuth");
                case "pendingInit":
                    return L("AppServerClient.Mcp.AuthStatus.Initializing", "Initializing");
                case "notFound":
                    return L("AppServerClient.Mcp.AuthStatus.NotFound", "Not configured or not logged in");
                default:
                    return string.IsNullOrWhiteSpace(authStatus) ? L("AppServerClient.Mcp.AuthStatus.NotProvided", "Not provided") : authStatus;
            }
        }

        private static Dictionary<string, object> CloneDictionary(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                result[pair.Key] = CloneValue(pair.Value);
            }

            return result;
        }

        private static object CloneValue(object value)
        {
            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                return CloneDictionary(dictionary);
            }

            if (value == null || value is string)
            {
                return value;
            }

            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable != null)
            {
                var items = new List<object>();
                foreach (var item in enumerable)
                {
                    items.Add(CloneValue(item));
                }

                return items.ToArray();
            }

            return value;
        }

        private static object GetValue(IDictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                return value;
            }

            return null;
        }

        private static string GetFirstNonEmptyString(IDictionary<string, object> source, params string[] keys)
        {
            if (keys == null)
            {
                return null;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var value = AppServerJsonValue.GetString(source, keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return null;
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                yield break;
            }

            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array)
                {
                    yield return item;
                }

                yield break;
            }

            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null || value is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                yield return item;
            }
        }

        private static bool? GetNullableBool(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            bool result;
            if (bool.TryParse(Convert.ToString(value), out result))
            {
                return result;
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
