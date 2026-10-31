using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.BrowserUse
{
    internal sealed class BrowserUseComponents
    {
        public string RuntimeRoot { get; set; }
        public string NodePath { get; set; }
        public string NodeReplPath { get; set; }
        public string NodeModulesPath { get; set; }
        public string CuaReplPath { get; set; }
        public string BrowserServicePath { get; set; }
        public string BrowserClientPath { get; set; }
        public string SelectedCodexPath { get; set; }
        public string SkyPipePath { get; set; }
    }

    internal sealed class BrowserUseAccessBinding
    {
        public string ExecutorId { get; set; }
        public string PipePath { get; set; }
        public string VerifyingIdentity { get; set; }
    }

    internal sealed class BrowserUseConfigurationPlan
    {
        public bool IsAvailable { get; set; }
        public string DetailCode { get; set; }
        public IDictionary<string, object> ConfigOverrides { get; set; }
    }

    internal static class BrowserUseConfigurationPlanner
    {
        internal static IDictionary<string, object> SuppressBrowser(IDictionary<string, object> config,
            IDictionary<string, object> overrides, string codexPath)
        {
            var result = Clone(overrides);
            var servers = Clone(Child(result, "mcp_servers"));
            var browser = Clone(Child(Child(config, "mcp_servers"), "cua_repl"));
            Merge(browser, Child(servers, "cua_repl"));
            browser = browser.Where(item => item.Value != null).ToDictionary(item => item.Key, item => item.Value);
            // Disabled MCP entries still require a transport; this command is never launched.
            if (!browser.ContainsKey("command") && !browser.ContainsKey("url"))
                browser["command"] = codexPath;
            browser["enabled"] = false;
            servers["cua_repl"] = browser;
            result["mcp_servers"] = servers;
            return result;
        }

        internal static bool IsRequested(IDictionary<string, object> config, IDictionary<string, object> overrides)
        {
            try
            {
                var effective = Clone(config);
                Merge(effective, overrides);
                if (Disabled(config) || Disabled(effective)) return false;
                return Equals(Value(Child(Child(effective, "mcp_servers"), "cua_repl"), "enabled"), true) ||
                    BrowserPlugins.Any(id => Equals(Value(Child(Child(effective, "plugins"), id), "enabled"), true));
            }
            catch (ArgumentException) { return false; }
        }

        private static readonly string[] BrowserPlugins =
        {
            "chrome@openai-bundled", "unified-computer-use@openai-bundled",
            "browser-use@mycodex-browser-use"
        };

        private static readonly HashSet<string> OwnedEnvironment = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CODEX_HOME", "CODEX_CLI_PATH", "BROWSER_USE_ACCESS_EXECUTOR_ID", "BROWSER_USE_ACCESS_PIPE_PATH",
            "BROWSER_USE_ACCESS_VERIFYING_IDENTITY", "BROWSER_USE_AVAILABLE_BACKENDS", "BROWSER_USE_TINYSKY_ENABLED",
            "BROWSER_USE_SECURITY_MODE"
        };

        public static BrowserUseConfigurationPlan Plan(IDictionary<string, object> effectiveConfig,
            IDictionary<string, object> requirements, IDictionary<string, object> threadOverrides,
            BrowserUseComponents components, BrowserUseAccessBinding access, string codexHome)
        {
            try
            {
                if (effectiveConfig == null || !ValidComponents(components) || !Absolute(codexHome) || !ValidBinding(access))
                    return Unavailable("ConfigurationUnavailable");
                var config = Clone(effectiveConfig);
                Merge(config, threadOverrides);
                var servers = Child(config, "mcp_servers");
                var server = Child(servers, "cua_repl");
                if (Disabled(effectiveConfig) || Disabled(config))
                    return Unavailable("BrowserDisabled");
                if (ServerConflict(Child(Child(effectiveConfig, "mcp_servers"), "cua_repl"), components) || ServerConflict(server, components))
                    return Unavailable("BrowserServerConflict");
                var policyError = CheckRequirements(requirements);
                if (policyError != null) return Unavailable(policyError);

                var node = Clone(server.Where(item => item.Value != null).ToDictionary(item => item.Key, item => item.Value));
                foreach (var key in new[] { "url", "bearer_token_env_var", "http_headers", "env_http_headers" }) node.Remove(key);
                var previousEnvironment = Child(server, "env");
                if (previousEnvironment.Values.Any(value => !(value is string)) ||
                    previousEnvironment.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != previousEnvironment.Count)
                    return Unavailable("ConfigurationUnavailable");
                var env = previousEnvironment.Where(item => !IsOwnedEnvironment(item.Key))
                    .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
                env["CODEX_HOME"] = codexHome;
                env["CODEX_CLI_PATH"] = components.SelectedCodexPath;
                env["NODE_REPL_NODE_PATH"] = components.NodePath;
                env["NODE_REPL_NODE_MODULE_DIRS"] = components.NodeModulesPath;
                env["NODE_REPL_TRUSTED_CODE_PATHS"] = codexHome + ";" + components.NodeModulesPath;
                env["NODE_REPL_TRUSTED_SERVICES"] = new JavaScriptSerializer().Serialize(new Dictionary<string, string>
                {
                    { "browser", components.BrowserServicePath }
                });
                env["BROWSER_USE_ACCESS_EXECUTOR_ID"] = access.ExecutorId;
                env["BROWSER_USE_ACCESS_PIPE_PATH"] = access.PipePath;
                env["BROWSER_USE_ACCESS_VERIFYING_IDENTITY"] = access.VerifyingIdentity;
                env["BROWSER_USE_AVAILABLE_BACKENDS"] = "chrome";
                env["BROWSER_USE_TINYSKY_ENABLED"] = "0";
                env["BROWSER_USE_SECURITY_MODE"] = string.Empty;
                env["CUA_REPL_NODE_REPL_PATH"] = components.NodeReplPath;
                env["CUA_REPL_ENABLED_SURFACES"] = "browser";
                node["command"] = components.NodePath;
                node["args"] = new[] { components.CuaReplPath };
                node["env"] = env;
                node["env_vars"] = InheritedEnvironment(server);
                node["enabled"] = true;
                var result = Clone(threadOverrides);
                var overrides = Clone(Child(result, "mcp_servers"));
                overrides["cua_repl"] = node;
                result["mcp_servers"] = overrides;
                return new BrowserUseConfigurationPlan { IsAvailable = true, DetailCode = "Ready", ConfigOverrides = result };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is FormatException)
            {
                return Unavailable("ConfigurationUnavailable");
            }
        }

        private static string CheckRequirements(IDictionary<string, object> requirements)
        {
            try
            {
                if (MyCodex.Infrastructure.ComputerUse.ComputerUseConfigurationPlanner.HasUnknownManagedRequirements(requirements))
                    return "PolicyUnsupported";
                if (IsFalse(requirements, "allowBrowserAndComputerUse") ||
                    IsFalse(Child(requirements, "featureRequirements"), "browser_use") ||
                    IsFalse(Child(requirements, "featureRequirements"), "browser_use_external")) return "PolicyDenied";
                var global = Value(requirements, "allowBrowserAndComputerUse");
                if (global != null && !(global is bool)) return "PolicyUnsupported";
                var features = Child(requirements, "featureRequirements");
                foreach (var key in new[] { "browser_use", "browser_use_external" })
                    if (Value(features, key) != null && !(Value(features, key) is bool)) return "PolicyUnsupported";
                var policy = Child(requirements, "browserUse");
                if (!Known(policy, "allowGlobalPersistentApproval", "allowHistoryAccess", "allowWebmcp", "disableAutoReview",
                    "defaultOriginPolicy", "origins", "extension")) return "PolicyUnsupported";
                foreach (var key in new[] { "allowGlobalPersistentApproval", "allowHistoryAccess", "allowWebmcp", "disableAutoReview" })
                    if (Value(policy, key) != null && !(Value(policy, key) is bool)) return "PolicyUnsupported";
                if (!ValidOriginPolicy(Child(policy, "defaultOriginPolicy"))) return "PolicyUnsupported";
                foreach (var origin in Child(policy, "origins").Values)
                    if (!(origin is IDictionary<string, object> map) || !ValidOriginPolicy(map)) return "PolicyUnsupported";
                var extension = Child(policy, "extension");
                if (!Known(extension, "requestHeaders")) return "PolicyUnsupported";
                var headers = Value(extension, "requestHeaders");
                if (headers != null && (!(headers is IList list) || list.Cast<object>().Any(item =>
                    !(item is IDictionary<string, object> header) || !Known(header, "name", "value") ||
                    !(Value(header, "name") is string) || !(Value(header, "value") is string)))) return "PolicyUnsupported";
                return null;
            }
            catch (ArgumentException) { return "PolicyUnsupported"; }
        }

        private static bool ValidOriginPolicy(IDictionary<string, object> policy)
        {
            if (!Known(policy, "access", "accessApprovalLifetime", "autoReview", "downloads", "fullCdpAccess", "persistentApproval", "uploads")) return false;
            foreach (var key in new[] { "access", "autoReview", "downloads", "fullCdpAccess", "uploads" })
            {
                var value = Value(policy, key);
                if (value != null && !Equals(value, "allow") && !Equals(value, "deny")) return false;
            }
            var lifetime = Value(policy, "accessApprovalLifetime");
            return (lifetime == null || Equals(lifetime, "turn") || Equals(lifetime, "thread")) &&
                (Value(policy, "persistentApproval") == null || Value(policy, "persistentApproval") is bool);
        }

        private static bool Known(IDictionary<string, object> map, params string[] keys)
        {
            return map.Keys.All(key => keys.Contains(key, StringComparer.Ordinal));
        }

        private static bool Disabled(IDictionary<string, object> config)
        {
            var features = Child(config, "features");
            var plugins = Child(config, "plugins");
            return IsFalse(Child(Child(config, "mcp_servers"), "cua_repl"), "enabled") ||
                IsFalse(features, "browser_use") || IsFalse(features, "browser_use_external") ||
                BrowserPlugins.Any(id => IsFalse(Child(plugins, id), "enabled"));
        }

        private static bool ValidComponents(BrowserUseComponents components)
        {
            return components != null && Absolute(components.RuntimeRoot) && Absolute(components.SelectedCodexPath) &&
                new[] { components.NodePath, components.NodeReplPath, components.NodeModulesPath }.All(path => Inside(path, components.RuntimeRoot)) &&
                new[] { components.CuaReplPath, components.BrowserServicePath, components.BrowserClientPath }.All(path => Inside(path, components.NodeModulesPath));
        }

        private static bool ServerConflict(IDictionary<string, object> server, BrowserUseComponents components)
        {
            if (Value(server, "url") != null) return true;
            var command = Value(server, "command");
            if (command == null) return false;
            if (!(command is string executable) || !Absolute(executable)) return true;
            var normalized = Path.GetFullPath(executable);
            if (string.Equals(normalized, Path.GetFullPath(components.NodePath), StringComparison.OrdinalIgnoreCase)) return false;
            // Cached official paths identify a replaceable registration; that copy is never executed.
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI", "Codex", "runtimes", "cua_node") + Path.DirectorySeparatorChar;
            if (!normalized.StartsWith(cache, StringComparison.OrdinalIgnoreCase)) return true;
            var parts = normalized.Substring(cache.Length).Split(Path.DirectorySeparatorChar);
            return parts.Length != 3 || parts[0].Length != 16 || !parts[0].All(Uri.IsHexDigit) ||
                !string.Equals(parts[1], "bin", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(parts[2], "node.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ValidBinding(BrowserUseAccessBinding access)
        {
            if (access == null || !Guid.TryParseExact(access.ExecutorId, "D", out _) ||
                access.PipePath == null || !Regex.IsMatch(access.PipePath,
                    @"\A\\\\\.\\pipe\\mycodex-browser-access-[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z") ||
                string.IsNullOrWhiteSpace(access.VerifyingIdentity) || access.VerifyingIdentity.Length > 4096) return false;
            var identity = Convert.FromBase64String(access.VerifyingIdentity);
            return identity.Length >= 32;
        }

        private static bool Absolute(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && (Regex.IsMatch(path, @"\A[A-Za-z]:[\\/]") ||
                Regex.IsMatch(path, @"\A\\\\[^\\]+\\[^\\]+[\\]"));
        }

        private static bool Inside(string path, string root)
        {
            return Absolute(path) && Absolute(root) && Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsOwnedEnvironment(string key)
        {
            return OwnedEnvironment.Contains(key) || key.StartsWith("NODE_REPL_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("CUA_REPL_", StringComparison.OrdinalIgnoreCase) || key.StartsWith("SKY_CUA_", StringComparison.OrdinalIgnoreCase);
        }

        private static string[] InheritedEnvironment(IDictionary<string, object> server)
        {
            var names = Value(server, "env_vars");
            if (names == null) return new[] { "CODEX_WINDOWS_REGISTERED_CORE" };
            if (!(names is IList list) || list.Cast<object>().Any(item => !(item is string))) throw new ArgumentException("Expected environment names.");
            return list.Cast<string>().Where(name => !IsOwnedEnvironment(name)).Concat(new[] { "CODEX_WINDOWS_REGISTERED_CORE" })
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static object Value(IDictionary<string, object> map, string key)
        {
            return map != null && map.TryGetValue(key, out var value) ? value : null;
        }

        private static bool IsFalse(IDictionary<string, object> map, string key) { return Equals(Value(map, key), false); }
        private static IDictionary<string, object> Child(IDictionary<string, object> map, string key)
        {
            var value = Value(map, key);
            return value == null ? new Dictionary<string, object>() : value as IDictionary<string, object> ?? throw new ArgumentException("Expected a configuration table.");
        }

        private static IDictionary<string, object> Clone(IDictionary<string, object> map)
        {
            return map == null ? new Dictionary<string, object>() : map.ToDictionary(item => item.Key, item => Copy(item.Value), StringComparer.Ordinal);
        }

        private static object Copy(object value)
        {
            if (value is IDictionary<string, object> map) return Clone(map);
            return value is IEnumerable sequence && !(value is string) ? sequence.Cast<object>().Select(Copy).ToArray() : value;
        }

        private static void Merge(IDictionary<string, object> target, IDictionary<string, object> source)
        {
            if (source == null) return;
            foreach (var item in source)
            {
                if (item.Value is IDictionary<string, object> map && Value(target, item.Key) is IDictionary<string, object> existing) Merge(existing, map);
                else target[item.Key] = Copy(item.Value);
            }
        }

        private static BrowserUseConfigurationPlan Unavailable(string code)
        {
            return new BrowserUseConfigurationPlan { DetailCode = code, ConfigOverrides = new Dictionary<string, object>() };
        }
    }
}
