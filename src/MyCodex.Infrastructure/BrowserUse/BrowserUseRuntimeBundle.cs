using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.BrowserUse
{
    internal static class BrowserUseRuntimeBundle
    {
        private const int ManifestLimit = 4 * 1024 * 1024;
        private const string Modules = "bin/node_modules/";
        private const string CuaRepl = Modules + "@oai/cua-repl/bin/cua-repl.mjs";
        private const string BrowserService = Modules + "@oai/browser-desktop/scripts/browser-service.mjs";
        private const string BrowserClient = Modules + "@oai/browser-desktop/scripts/browser-client.mjs";
        private static readonly string[] Metadata = { "bundleId", "runtimeVersion", "nodeVersion", "browserVersion", "cuaReplVersion" };

        internal static BrowserUseComponents Load(string selectedCodexPath)
        {
            using (var stream = typeof(BrowserUseRuntimeBundle).Assembly.GetManifestResourceStream("MyCodex.BrowserUse.BundlePin"))
            using (var reader = new StreamReader(stream ?? throw new InvalidDataException("BROWSER_BUNDLE_PIN_MISSING")))
                return Load(AppDomain.CurrentDomain.BaseDirectory, selectedCodexPath, ReadObject(reader.ReadToEnd()));
        }

        internal static BrowserUseComponents Load(string installation, string selectedCodexPath, IDictionary<string, object> pin)
        {
            var id = AppServerJsonValue.GetString(pin, "bundleId");
            var expectedHash = AppServerJsonValue.GetString(pin, "manifestSha256");
            if (!SafeName(id) || !IsHash(expectedHash) || Metadata.Any(key => string.IsNullOrWhiteSpace(AppServerJsonValue.GetString(pin, key))))
                throw new InvalidDataException("BROWSER_BUNDLE_PIN_INVALID");
            var installationPath = Path.GetFullPath(installation);
            for (var ancestor = new DirectoryInfo(installationPath); ancestor != null; ancestor = ancestor.Parent)
                RejectLink(ancestor.FullName);
            var root = CheckedPath(installationPath, "runtime/browser/" + id);
            var manifestPath = CheckedPath(root, "bundle.json");
            if (new FileInfo(manifestPath).Length > ManifestLimit || !MatchesHash(manifestPath, expectedHash))
                throw new InvalidDataException("BROWSER_BUNDLE_MANIFEST_INVALID");
            var manifest = ReadObject(File.ReadAllText(manifestPath));
            if (!Equals(Value(manifest, "schemaVersion"), 1) ||
                Metadata.Any(key => AppServerJsonValue.GetString(manifest, key) != AppServerJsonValue.GetString(pin, key)))
                throw new InvalidDataException("BROWSER_BUNDLE_METADATA_INVALID");
            var files = Value(manifest, "files") as IEnumerable;
            if (files == null || files is string) throw new InvalidDataException("BROWSER_BUNDLE_FILES_INVALID");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in files)
            {
                var entry = value as IDictionary<string, object>;
                var relative = AppServerJsonValue.GetString(entry, "path");
                var hash = AppServerJsonValue.GetString(entry, "sha256");
                var size = Value(entry, "size");
                if (!IsHash(hash) || paths.Count >= 10000 || !(size is int || size is long) || Convert.ToInt64(size) < 0)
                    throw new InvalidDataException("BROWSER_BUNDLE_ENTRY_INVALID");
                var path = CheckedPath(root, relative);
                if (!paths.Add(relative) || string.Equals(relative, "bundle.json", StringComparison.OrdinalIgnoreCase) ||
                    new FileInfo(path).Length != Convert.ToInt64(size) || !MatchesHash(path, hash))
                    throw new InvalidDataException("BROWSER_BUNDLE_CONTENT_INVALID");
            }
            if (paths.Count == 0 || EnumerateFiles(root).Count(path => !string.Equals(path, manifestPath, StringComparison.OrdinalIgnoreCase)) != paths.Count)
                throw new InvalidDataException("BROWSER_BUNDLE_FILES_INVALID");
            foreach (var required in new[] { "manifest.json", "bin/node.exe", "bin/node_repl.exe",
                CuaRepl, BrowserService, BrowserClient, Modules + "@oai/browser-desktop/package.json", Modules + "@oai/cua-repl/package.json" })
                if (!paths.Contains(required)) throw new InvalidDataException("BROWSER_BUNDLE_ENTRY_MISSING");
            VerifyMetadata(root, pin);
            if (string.IsNullOrWhiteSpace(selectedCodexPath) || !Path.IsPathRooted(selectedCodexPath) || !File.Exists(selectedCodexPath))
                throw new InvalidDataException("BROWSER_CODEX_RUNTIME_MISSING");
            return new BrowserUseComponents
            {
                RuntimeRoot = root, NodePath = CheckedPath(root, "bin/node.exe"),
                NodeReplPath = CheckedPath(root, "bin/node_repl.exe"), NodeModulesPath = CheckedPath(root, "bin/node_modules"),
                CuaReplPath = CheckedPath(root, CuaRepl), BrowserServicePath = CheckedPath(root, BrowserService),
                BrowserClientPath = CheckedPath(root, BrowserClient), SelectedCodexPath = Path.GetFullPath(selectedCodexPath)
            };
        }

        private static void VerifyMetadata(string root, IDictionary<string, object> pin)
        {
            var runtime = ReadMetadata(root, "manifest.json");
            if (AppServerJsonValue.GetString(runtime, "platform") != "windows" || AppServerJsonValue.GetString(runtime, "arch") != "x64" ||
                AppServerJsonValue.GetString(runtime, "target") != "windows-x64" ||
                AppServerJsonValue.GetString(runtime, "runtime_archive_version") != AppServerJsonValue.GetString(pin, "runtimeVersion") ||
                AppServerJsonValue.GetString(runtime, "node_version") != AppServerJsonValue.GetString(pin, "nodeVersion") ||
                AppServerJsonValue.GetString(runtime, "node_path") != "bin/node.exe" ||
                AppServerJsonValue.GetString(runtime, "node_repl_path") != "bin/node_repl.exe" ||
                AppServerJsonValue.GetString(runtime, "node_modules") != "bin/node_modules")
                throw new InvalidDataException("BROWSER_RUNTIME_METADATA_INVALID");
            foreach (var package in new[] { new[] { "@oai/browser-desktop", "browserVersion" }, new[] { "@oai/cua-repl", "cuaReplVersion" } })
            {
                var metadata = ReadMetadata(root, Modules + package[0] + "/package.json");
                if (AppServerJsonValue.GetString(metadata, "name") != package[0] ||
                    AppServerJsonValue.GetString(metadata, "version") != AppServerJsonValue.GetString(pin, package[1]))
                    throw new InvalidDataException("BROWSER_PACKAGE_VERSION_INVALID");
            }
        }

        private static IDictionary<string, object> ReadMetadata(string root, string relative)
        {
            var path = CheckedPath(root, relative);
            if (new FileInfo(path).Length > 65536) throw new InvalidDataException("BROWSER_PACKAGE_METADATA_TOO_LARGE");
            return ReadObject(File.ReadAllText(path));
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
                relative.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == ".."))
                throw new InvalidDataException("BROWSER_BUNDLE_PATH_INVALID");
            var path = root;
            RejectLink(path);
            foreach (var part in relative.Split('/'))
            {
                path = Path.Combine(path, part);
                RejectLink(path);
            }
            return Path.GetFullPath(path);
        }

        private static void RejectLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("BROWSER_BUNDLE_LINK_INVALID");
        }

        private static bool MatchesHash(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = new SHA256Cng())
                return string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SafeName(string value) => !string.IsNullOrWhiteSpace(value) && value != "." && value != ".." &&
            value.All(character => char.IsLetterOrDigit(character) || character == '.' || character == '-' || character == '_');

        private static bool IsHash(string value) => value != null && value.Length == 64 && value.All(Uri.IsHexDigit);

        private static IDictionary<string, object> ReadObject(string value) =>
            new JavaScriptSerializer { MaxJsonLength = ManifestLimit, RecursionLimit = 16 }.DeserializeObject(value)
                as IDictionary<string, object> ?? throw new InvalidDataException("BROWSER_BUNDLE_JSON_INVALID");

        private static object Value(IDictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) ? value : null;
        }
    }
}
