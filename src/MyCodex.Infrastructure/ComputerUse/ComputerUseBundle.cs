using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.ComputerUse
{
    internal sealed class ComputerUseBundle
    {
        internal const string MarketplaceName = "mycodex-computer-use";
        internal const string PluginId = "computer-use@" + MarketplaceName;
        internal const string LegacyPluginId = "computer-use@openai-bundled";
        private const string PluginPrefix = "marketplace/plugins/computer-use/";
        private readonly Dictionary<string, string> _hashes;

        internal string Root { get; }
        internal string RuntimeDirectory => Root;
        internal string MarketplaceRoot => Path.Combine(Root, "marketplace");
        internal string MarketplacePath => Path.Combine(MarketplaceRoot, ".agents", "plugins", "marketplace.json");
        internal string PluginRoot => Path.Combine(MarketplaceRoot, "plugins", "computer-use");
        internal string PluginVersion { get; }

        private ComputerUseBundle(string root, string pluginVersion, Dictionary<string, string> hashes)
        {
            Root = root;
            PluginVersion = pluginVersion;
            _hashes = hashes;
        }

        internal static ComputerUseBundle Load()
        {
            using (var stream = typeof(ComputerUseBundle).Assembly.GetManifestResourceStream("MyCodex.ComputerUse.BundlePin"))
            using (var reader = new StreamReader(stream ?? throw new InvalidDataException("CUA_BUNDLE_PIN_MISSING")))
            {
                var pin = ReadObject(reader.ReadToEnd());
                return Load(AppDomain.CurrentDomain.BaseDirectory, Text(pin, "bundleId"), Text(pin, "manifestSha256"));
            }
        }

        internal static ComputerUseBundle Load(string installation, string bundleId, string manifestHash)
        {
            if (!IsSafeName(bundleId) || !IsHash(manifestHash)) throw new InvalidDataException("CUA_BUNDLE_PIN_INVALID");
            var root = Path.GetFullPath(Path.Combine(installation, "runtime", "cua", bundleId));
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("CUA_BUNDLE_MISSING");
            CheckedPath(Path.GetFullPath(installation), "runtime/cua/" + bundleId);
            var manifestPath = CheckedPath(root, "bundle.json");
            if (new FileInfo(manifestPath).Length > 1024 * 1024 || !MatchesHash(manifestPath, manifestHash))
                throw new InvalidDataException("CUA_BUNDLE_MANIFEST_INVALID");
            var manifest = ReadObject(File.ReadAllText(manifestPath));
            if (!Equals(Value(manifest, "schemaVersion"), 1) || Text(manifest, "bundleId") != bundleId ||
                !IsSafeName(Text(manifest, "pluginVersion")))
                throw new InvalidDataException("CUA_BUNDLE_METADATA_INVALID");
            var files = Value(manifest, "files") as IEnumerable;
            if (files == null || files is string) throw new InvalidDataException("CUA_BUNDLE_FILES_INVALID");
            var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in files)
            {
                var entry = item as IDictionary<string, object>;
                var relative = Text(entry, "path");
                var hash = Text(entry, "sha256");
                if (!IsHash(hash) || hashes.Count >= 10000) throw new InvalidDataException("CUA_BUNDLE_HASH_INVALID");
                var path = CheckedPath(root, relative);
                if (hashes.ContainsKey(relative) || new FileInfo(path).Length != Convert.ToInt64(Value(entry, "size")) ||
                    !MatchesHash(path, hash)) throw new InvalidDataException("CUA_BUNDLE_CONTENT_INVALID");
                hashes.Add(relative, hash);
            }
            var actual = EnumerateFiles(root).Where(path => !string.Equals(path, manifestPath, StringComparison.OrdinalIgnoreCase)).ToList();
            if (hashes.Count == 0 || actual.Count != hashes.Count ||
                !hashes.ContainsKey("marketplace/.agents/plugins/marketplace.json") ||
                !hashes.ContainsKey(PluginPrefix + ".codex-plugin/plugin.json"))
                throw new InvalidDataException("CUA_BUNDLE_FILES_INVALID");
            return new ComputerUseBundle(root, Text(manifest, "pluginVersion"), hashes);
        }

        internal void VerifyInstalledPlugin(string codexHome)
        {
            var root = Path.GetFullPath(Path.Combine(codexHome, "plugins", "cache", MarketplaceName, "computer-use", PluginVersion));
            CheckedPath(Path.GetFullPath(codexHome), "plugins/cache/" + MarketplaceName + "/computer-use/" + PluginVersion);
            var entries = _hashes.Where(entry => entry.Key.StartsWith(PluginPrefix, StringComparison.Ordinal)).ToList();
            if (entries.Count == 0 || EnumerateFiles(root).Count() != entries.Count) throw new InvalidDataException("CUA_PLUGIN_CONTENT_INVALID");
            foreach (var entry in entries)
            {
                if (!MatchesHash(CheckedPath(root, entry.Key.Substring(PluginPrefix.Length)), entry.Value))
                    throw new InvalidDataException("CUA_PLUGIN_CONTENT_INVALID");
            }
        }

        private static IEnumerable<string> EnumerateFiles(string root)
        {
            RejectLink(root);
            foreach (var entry in Directory.GetFileSystemEntries(root))
            {
                RejectLink(entry);
                if (Directory.Exists(entry))
                {
                    foreach (var file in EnumerateFiles(entry)) yield return file;
                }
                else yield return entry;
            }
        }

        private static string CheckedPath(string root, string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Contains("\\") || relative.Contains(":") ||
                relative.Split('/').Any(segment => string.IsNullOrEmpty(segment) || segment == "." || segment == ".."))
                throw new InvalidDataException("CUA_BUNDLE_PATH_INVALID");
            var path = root;
            RejectLink(path);
            foreach (var segment in relative.Split('/'))
            {
                path = Path.Combine(path, segment);
                RejectLink(path);
            }
            return Path.GetFullPath(path);
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CUA_BUNDLE_LINK_INVALID");
        }

        private static bool MatchesHash(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = new SHA256Cng())
                return string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSafeName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
                value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_');
        }

        private static bool IsHash(string value)
        {
            return value != null && value.Length == 64 && value.All(Uri.IsHexDigit);
        }

        private static IDictionary<string, object> ReadObject(string value)
        {
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 16 }.DeserializeObject(value)
                as IDictionary<string, object> ?? throw new InvalidDataException("CUA_BUNDLE_JSON_INVALID");
        }

        private static object Value(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) ? value : null;
        }

        private static string Text(IDictionary<string, object> source, string key) => Value(source, key) as string;
    }
}
