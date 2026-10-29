using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal sealed class ComputerUseConfigurationPlan
    {
        public bool IsAvailable { get; set; }
        public string DetailCode { get; set; }
        public IDictionary<string, object> ConfigOverrides { get; set; }
        public IDictionary<string, object> HelperPolicy { get; set; }
    }

    internal static class ComputerUseConfigurationPlanner
    {
        // Unknown managed policy cannot safely be dropped when the helper uses another HOME.
        private static readonly HashSet<string> KnownRequirements = new HashSet<string>(StringComparer.Ordinal)
        {
            "modelProvider", "modelProviders", "allowedLoginMethods", "cliAuthCredentialsStore",
            "chatgptBaseUrl", "additionalDeveloperInstructions", "allowedApprovalPolicies",
            "allowedApprovalsReviewers", "allowedSandboxModes", "allowedWindowsSandboxImplementations",
            "allowedPermissionProfiles", "defaultPermissions", "allowedWebSearchModes",
            "allowManagedHooksOnly", "allowBrowserAndComputerUse", "allowAppshots", "allowRemoteControl",
            "computerUse", "browserUse", "inAppBrowser", "featureRequirements", "hooks",
            "enforceResidency", "network", "application", "autoReview", "models"
        };

        public static ComputerUseConfigurationPlan Plan(
            IDictionary<string, object> effectiveConfig,
            IDictionary<string, object> requirements,
            IDictionary<string, object> threadOverrides,
            ComputerUseComponents components,
            string pipePath,
            string codexHome)
        {
            if (effectiveConfig == null || components == null || string.IsNullOrWhiteSpace(codexHome) ||
                string.IsNullOrWhiteSpace(pipePath) || !pipePath.StartsWith(@"\\.\pipe\mycodex-", StringComparison.Ordinal))
                return Unavailable("ConfigurationUnavailable");

            try
            {
                var config = Clone(effectiveConfig);
                Merge(config, threadOverrides);
                if (IsPluginDisabled(config)) return Unavailable("PluginDisabled");
                var policyError = CheckPolicy(config, requirements);
                if (policyError != null) return Unavailable(policyError);
                var overrides = Clone(threadOverrides);
                var servers = Child(config, "mcp_servers");
                var node = StripNulls(Child(servers, "node_repl"));
                if (IsFalse(node, "enabled")) return Unavailable("NodeReplDisabled");
                if (node.ContainsKey("url") || HasItems(Value(node, "args")) ||
                    !CompatibleComponentPath(Value(node, "command"), components.NodeReplPath, components, "node_repl.exe"))
                    return Unavailable("NodeReplConflict");

                var env = Clone(Child(node, "env"));
                if (env.Values.Any(value => !(value is string)) ||
                    env.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != env.Count ||
                    env.Keys.Any(key => IsReservedEnvironmentKey(key) && !IsCanonicalEnvironmentKey(key)))
                    return Unavailable("NodeReplConflict");
                if (!CompatibleComponentPath(Value(env, "NODE_REPL_NODE_PATH"), components.NodePath, components, "node.exe") ||
                    !CompatiblePath(Value(env, "CODEX_HOME"), codexHome))
                    return Unavailable("NodeReplConflict");
                var existingPipe = Value(env, "SKY_CUA_NATIVE_PIPE_DIRECTORY") as string;
                if (!string.IsNullOrEmpty(existingPipe) &&
                    !existingPipe.StartsWith(@"\\.\pipe\codex-computer-use-", StringComparison.Ordinal) &&
                    !existingPipe.StartsWith(@"\\.\pipe\mycodex-", StringComparison.Ordinal))
                    return Unavailable("NodeReplConflict");
                var nativePipe = Value(env, "SKY_CUA_NATIVE_PIPE");
                if (nativePipe != null && !string.Equals(nativePipe as string, "1", StringComparison.Ordinal))
                    return Unavailable("NodeReplConflict");

                var modulePaths = PathList(Value(env, "NODE_REPL_NODE_MODULE_DIRS"));
                if (modulePaths.Any(path => !CompatibleComponentPath(path, components.NodeModulesPath, components, "node_modules")))
                    return Unavailable("NodeReplConflict");
                var servicesText = Value(env, "NODE_REPL_TRUSTED_SERVICES") as string;
                var services = string.IsNullOrWhiteSpace(servicesText)
                    ? new Dictionary<string, object>()
                    : new JavaScriptSerializer().DeserializeObject(servicesText) as IDictionary<string, object>;
                if (services == null || services.Values.Any(value => !(value is string)) ||
                    (services.ContainsKey("sky") && !Equals(services["sky"], "@oai/sky/service")))
                    return Unavailable("NodeReplConflict");
                services["sky"] = "@oai/sky/service";

                env["CODEX_CLI_PATH"] = components.SelectedCodexPath;
                env["CODEX_HOME"] = codexHome;
                env["NODE_REPL_NODE_PATH"] = components.NodePath;
                env["NODE_REPL_NODE_MODULE_DIRS"] = components.NodeModulesPath;
                env["NODE_REPL_TRUSTED_CODE_PATHS"] = AppendComponentPath(Value(env, "NODE_REPL_TRUSTED_CODE_PATHS"), components);
                env["NODE_REPL_TRUSTED_SERVICES"] = new JavaScriptSerializer().Serialize(services);
                env["SKY_CUA_NATIVE_PIPE"] = "1";
                env["SKY_CUA_NATIVE_PIPE_DIRECTORY"] = pipePath;
                env["NODE_REPL_UNTRUSTED_ENV_ALLOWLIST"] = AppendName(Value(env, "NODE_REPL_UNTRUSTED_ENV_ALLOWLIST"), "CODEX_WINDOWS_REGISTERED_CORE");
                node["command"] = components.NodeReplPath;
                node["env"] = env;
                var overrideServers = Clone(Child(overrides, "mcp_servers"));
                overrideServers["node_repl"] = node;
                overrides["mcp_servers"] = overrideServers;
                var plugins = Clone(Child(overrides, "plugins"));
                var own = Clone(Child(plugins, ComputerUseBundle.PluginId));
                own["enabled"] = true;
                plugins[ComputerUseBundle.PluginId] = own;
                var legacy = Clone(Child(plugins, ComputerUseBundle.LegacyPluginId));
                legacy["enabled"] = false;
                plugins[ComputerUseBundle.LegacyPluginId] = legacy;
                overrides["plugins"] = plugins;
                return new ComputerUseConfigurationPlan
                {
                    IsAvailable = true,
                    DetailCode = "Ready",
                    ConfigOverrides = overrides,
                    HelperPolicy = StripNulls(Child(config, "computer_use"))
                };
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is NotSupportedException)
            {
                return Unavailable("NodeReplConflict");
            }
        }

        private static string CheckPolicy(IDictionary<string, object> config, IDictionary<string, object> requirements)
        {
            if (IsFalse(requirements, "allowBrowserAndComputerUse") ||
                IsFalse(Child(config, "features"), "computer_use") ||
                IsFalse(Child(requirements, "featureRequirements"), "computer_use")) return "PolicyDenied";
            if (requirements != null && (HasUnknownManagedRequirements(requirements) ||
                HasMeaningfulValue(Value(requirements, "computerUse")) ||
                HasMeaningfulValue(Value(requirements, "network")) ||
                HasMeaningfulValue(Value(requirements, "application")) ||
                (Value(requirements, "allowBrowserAndComputerUse") != null &&
                    !(Value(requirements, "allowBrowserAndComputerUse") is bool)))) return "PolicyUnsupported";
            // Config and requirements have different schemas. Persistent-grant and locked-desktop
            // requirements cannot be represented by copying their camelCase keys into config.toml.
            return null;
        }

        internal static bool HasUnknownManagedRequirements(IDictionary<string, object> requirements)
        {
            return requirements != null && requirements.Any(pair => pair.Value != null && !KnownRequirements.Contains(pair.Key));
        }

        public static bool HasUnsafeDisabledLayers(IDictionary<string, object> response, IDictionary<string, object> threadOverrides)
        {
            // config/read is evaluated before thread-level project trust overrides. It cannot
            // prove the eventual policy if that override enables a currently disabled layer.
            var projects = Value(threadOverrides, "projects") as IDictionary<string, object>;
            if (projects == null || !projects.Values.Any(project =>
                Equals(Value(project as IDictionary<string, object>, "trust_level"), "trusted"))) return false;
            var layers = Value(response, "layers") as IEnumerable;
            if (layers == null || layers is string) return true;
            foreach (var item in layers)
            {
                var layer = item as IDictionary<string, object>;
                if (layer == null) return true;
                var disabled = Value(layer, "disabledReason");
                if (disabled == null || Equals(disabled, string.Empty)) continue;
                var config = Value(layer, "config") as IDictionary<string, object>;
                if (config == null) return true;
                if (HasMeaningfulValue(Value(config, "computer_use")) ||
                    HasMeaningfulValue(Value(config, "mcp_servers")) ||
                    HasMeaningfulValue(Value(config, "plugins")) ||
                    HasMeaningfulValue(Value(config, "features"))) return true;
            }
            return false;
        }

        private static bool HasMeaningfulValue(object value)
        {
            var map = value as IDictionary<string, object>;
            return map != null ? map.Values.Any(HasMeaningfulValue) : value != null;
        }

        private static bool IsReservedEnvironmentKey(string key)
        {
            return key.StartsWith("NODE_REPL_", StringComparison.OrdinalIgnoreCase) ||
                key.StartsWith("SKY_CUA_", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "CODEX_HOME", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "CODEX_CLI_PATH", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCanonicalEnvironmentKey(string key) { return key == key.ToUpperInvariant(); }
        private static bool IsFalse(IDictionary<string, object> map, string key) { return Equals(Value(map, key), false); }
        internal static bool IsPluginDisabled(IDictionary<string, object> config)
        {
            var plugins = Child(config, "plugins");
            return IsFalse(Child(plugins, ComputerUseBundle.PluginId), "enabled") ||
                IsFalse(Child(plugins, ComputerUseBundle.LegacyPluginId), "enabled");
        }
        private static object Value(IDictionary<string, object> map, string key)
        {
            object value;
            return map != null && map.TryGetValue(key, out value) ? value : null;
        }

        private static IDictionary<string, object> Child(IDictionary<string, object> map, string key)
        {
            var value = Value(map, key);
            if (value == null) return new Dictionary<string, object>();
            var child = value as IDictionary<string, object>;
            if (child == null) throw new ArgumentException("Expected a configuration table.");
            return child;
        }

        private static bool HasItems(object value)
        {
            var sequence = value as IEnumerable;
            return value != null && (sequence == null || sequence.Cast<object>().Any());
        }

        private static bool CompatiblePath(object value, string expected) { return value == null || (value is string && SamePath((string)value, expected)); }
        private static bool CompatibleComponentPath(object value, string expected, ComputerUseComponents components, string leaf)
        {
            return CompatiblePath(value, expected) || (value is string &&
                ((components.StaleOfficialRuntimeRoots ?? new string[0]).Any(root => SamePath((string)value, Path.Combine(root, "bin", leaf))) ||
                IsPreviousOfficialComponentPath((string)value, leaf)));
        }

        private static bool IsPreviousOfficialComponentPath(string value, string leaf)
        {
            // Only rewrite the known official cache layout. Never load or execute that copy.
            var cache = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenAI", "Codex", "runtimes", "cua_node")).TrimEnd('\\') + "\\";
            var path = Path.GetFullPath(value);
            if (!path.StartsWith(cache, StringComparison.OrdinalIgnoreCase)) return false;
            var segments = path.Substring(cache.Length).Split('\\');
            return segments.Length == 3 && segments[0].Length == 16 && segments[0].All(Uri.IsHexDigit) &&
                string.Equals(segments[1], "bin", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(segments[2], leaf, StringComparison.OrdinalIgnoreCase);
        }
        private static bool SamePath(string left, string right)
        {
            return string.Equals(Path.GetFullPath(left).TrimEnd('\\', '/'), Path.GetFullPath(right).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> PathList(object value)
        {
            if (value == null) return new List<string>();
            if (!(value is string)) throw new ArgumentException("Expected an environment string.");
            return ((string)value).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static string AppendComponentPath(object value, ComputerUseComponents components)
        {
            var paths = PathList(value).Select(path => CompatibleComponentPath(path, components.NodeModulesPath, components, "node_modules")
                ? components.NodeModulesPath : path).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (!paths.Any(existing => SamePath(existing, components.NodeModulesPath))) paths.Add(components.NodeModulesPath);
            return string.Join(";", paths);
        }

        private static string AppendName(object value, string name)
        {
            if (value != null && !(value is string)) throw new ArgumentException("Expected an environment string.");
            var names = ((string)value ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim()).ToList();
            if (!names.Contains(name, StringComparer.Ordinal)) names.Add(name);
            return string.Join(",", names);
        }

        private static IDictionary<string, object> Clone(IDictionary<string, object> value)
        {
            return value == null ? new Dictionary<string, object>() : (IDictionary<string, object>)CloneValue(value);
        }

        private static object CloneValue(object value)
        {
            var map = value as IDictionary<string, object>;
            if (map != null) return map.ToDictionary(pair => pair.Key, pair => CloneValue(pair.Value), StringComparer.Ordinal);
            var sequence = value as IEnumerable;
            if (sequence != null && !(value is string)) return sequence.Cast<object>().Select(CloneValue).ToArray();
            return value;
        }

        private static void Merge(IDictionary<string, object> target, IDictionary<string, object> source)
        {
            if (source == null) return;
            foreach (var pair in source)
            {
                var table = pair.Value as IDictionary<string, object>;
                var old = Value(target, pair.Key) as IDictionary<string, object>;
                if (table != null && old != null) Merge(old, table);
                else target[pair.Key] = CloneValue(pair.Value);
            }
        }

        private static IDictionary<string, object> StripNulls(IDictionary<string, object> map)
        {
            // config/read serializes absent optional values as null; TOML has no null value.
            return map.Where(pair => pair.Value != null).ToDictionary(pair => pair.Key,
                pair => pair.Value is IDictionary<string, object> ? StripNulls((IDictionary<string, object>)pair.Value) : StripArrayNulls(pair.Value),
                StringComparer.Ordinal);
        }

        private static object StripArrayNulls(object value)
        {
            var sequence = value as IEnumerable;
            return sequence == null || value is string ? value : sequence.Cast<object>().Select(item =>
                item is IDictionary<string, object> ? StripNulls((IDictionary<string, object>)item) : CloneValue(item)).ToArray();
        }

        private static ComputerUseConfigurationPlan Unavailable(string code)
        {
            return new ComputerUseConfigurationPlan { DetailCode = code, ConfigOverrides = new Dictionary<string, object>(), HelperPolicy = new Dictionary<string, object>() };
        }
    }
}
