using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.ComputerUse
{
    public sealed class ComputerUseComponentResolver
    {
        internal const string SkyRoot = "bin/node_modules/@oai/sky/";
        internal const string TransportRelativePath = SkyRoot + "dist/project/cua/sky_js/src/targets/windows/internal/helper_transport.js";
        internal const string HelperRelativePath = SkyRoot + "bin/windows/codex-computer-use.exe";
        private readonly IDictionary<string, string> requiredFileHashes;
        private readonly IDictionary<string, string> staleFileHashes;

        // Version labels describe an installation, not its wire compatibility. Native
        // entry points must be signed by OpenAI; the host checks the transport contract.
        public ComputerUseComponentResolver() : this(null)
        {
        }

        internal ComputerUseComponentResolver(IDictionary<string, string> requiredFileHashes, IDictionary<string, string> staleFileHashes = null)
        {
            this.requiredFileHashes = requiredFileHashes == null ? null : new Dictionary<string, string>(requiredFileHashes, StringComparer.Ordinal);
            this.staleFileHashes = new Dictionary<string, string>(staleFileHashes ?? new Dictionary<string, string>
            {
                { "manifest.json", "355141F9AD030296E26404ACDB489D3C4DF0F9FC860E750E61F170F8C9C1B957" },
                { "bin/node_repl.exe", "173038AF74AF16E9A34BFE0AE788D05740B1176D6B66E9BF23E7F915EB13890A" }
            }, StringComparer.Ordinal);
        }

        public ComputerUseComponentResolution Resolve(string selectedCodexPath, string selectedCodexVersion, string runtimeDirectory = null)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || !Environment.Is64BitProcess)
            {
                return Failure(ComputerUseComponentStatus.UnsupportedPlatform, "windows-x64-required");
            }
            if (string.IsNullOrWhiteSpace(selectedCodexPath) || !File.Exists(selectedCodexPath) || !Path.IsPathRooted(selectedCodexPath))
            {
                return Failure(ComputerUseComponentStatus.SelectedRuntimeMissing, "selected-codex-missing");
            }
            try
            {
                runtimeDirectory = runtimeDirectory ?? ComputerUseBundle.Load().RuntimeDirectory;
                if (!Directory.Exists(runtimeDirectory))
                {
                    return Failure(ComputerUseComponentStatus.MissingComponents, "cua-directory-missing");
                }
                var candidates = Directory.GetDirectories(runtimeDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
                ComputerUseComponentResolution selected = null;
                ComputerUseComponentResolution lastFailure = null;
                var corruptKnownCandidate = false;
                var staleRoots = new List<string>();
                var verifiedRoots = new List<string>();
                foreach (var candidate in candidates)
                {
                    var result = Inspect(candidate, selectedCodexPath, selectedCodexVersion);
                    if (result.Status == ComputerUseComponentStatus.Ready)
                    {
                        verifiedRoots.Add(result.Components.RuntimeRoot);
                        if (selected == null || CompareRuntimeVersions(result.Components.RuntimeVersion, selected.Components.RuntimeVersion) > 0)
                            selected = result;
                    }
                    else
                    {
                        // Do not silently bypass a failed integrity check by using another copy.
                        corruptKnownCandidate |= result.DetailCode == "entry-integrity-mismatch" || result.DetailCode == "official-signature-invalid";
                        lastFailure = lastFailure == null || result.Status == ComputerUseComponentStatus.InvalidComponents ? result : lastFailure;
                        if (IsVerifiedStaleRoot(candidate))
                        {
                            staleRoots.Add(Path.GetFullPath(candidate));
                        }
                    }
                }
                if (selected != null && corruptKnownCandidate)
                {
                    return Failure(ComputerUseComponentStatus.ConflictingComponents, "supported-candidates-conflict");
                }
                if (selected != null)
                {
                    selected.Components.StaleOfficialRuntimeRoots = staleRoots.Concat(verifiedRoots)
                        .Where(root => !string.Equals(root, selected.Components.RuntimeRoot, StringComparison.OrdinalIgnoreCase)).ToArray();
                }
                return selected ?? lastFailure ?? Failure(ComputerUseComponentStatus.MissingComponents, "cua-candidates-missing");
            }
            catch (DirectoryNotFoundException)
            {
                return Failure(ComputerUseComponentStatus.MissingComponents, "mycodex-bundle-missing");
            }
            catch (Exception exception) when (IsInvalidInstallation(exception))
            {
                return Failure(ComputerUseComponentStatus.InvalidComponents, "cua-directory-unreadable");
            }
        }

        private ComputerUseComponentResolution Inspect(string root, string codexPath, string codexVersion)
        {
            try
            {
                if (IsReparsePoint(root))
                {
                    return Failure(ComputerUseComponentStatus.InvalidComponents, "linked-component-path");
                }
                var manifest = ReadObject(CheckedPath(root, "manifest.json"));
                if (!Has(manifest, "platform", "windows") || !Has(manifest, "arch", "x64") || !Has(manifest, "target", "windows-x64"))
                {
                    return Failure(ComputerUseComponentStatus.UnsupportedComponentVersion, "component-platform-mismatch");
                }
                var runtimeVersion = Text(manifest, "runtime_archive_version");
                if (RuntimeVersion(runtimeVersion) == null)
                {
                    return Failure(ComputerUseComponentStatus.UnsupportedComponentVersion, "component-version-invalid");
                }
                // No executable or import path supplied by a manifest is ever followed.
                if (!Has(manifest, "node_path", "bin/node.exe") || !Has(manifest, "node_repl_path", "bin/node_repl.exe")
                    || !Has(manifest, "node_modules", "bin/node_modules"))
                {
                    return Failure(ComputerUseComponentStatus.InvalidComponents, "manifest-entry-mismatch");
                }
                var package = ReadObject(CheckedPath(root, SkyRoot + "package.json"));
                if (!Has(package, "name", "@oai/sky") || string.IsNullOrWhiteSpace(Text(package, "version")))
                {
                    return Failure(ComputerUseComponentStatus.UnsupportedComponentVersion, "sky-version-unverified");
                }
                foreach (var relative in new[] { "bin/node.exe", "bin/node_repl.exe", HelperRelativePath,
                    TransportRelativePath, SkyRoot + "dist/project/cua/sky_js/src/index.js", SkyRoot + "dist/project/cua/sky_js/src/service.js" })
                    CheckedPath(root, relative);
                if (requiredFileHashes == null && !ComputerUseComponentTrust.Verify(root))
                    return Failure(ComputerUseComponentStatus.InvalidComponents, "official-signature-invalid");
                foreach (var entry in requiredFileHashes ?? new Dictionary<string, string>())
                {
                    if (!MatchesHash(CheckedPath(root, entry.Key), entry.Value))
                    {
                        return Failure(ComputerUseComponentStatus.InvalidComponents, "entry-integrity-mismatch");
                    }
                }
                return new ComputerUseComponentResolution
                {
                    Status = ComputerUseComponentStatus.Ready,
                    DetailCode = "verified-local-components",
                    Components = new ComputerUseComponents
                    {
                        RuntimeRoot = Path.GetFullPath(root),
                        NodePath = CheckedPath(root, "bin/node.exe"),
                        NodeReplPath = CheckedPath(root, "bin/node_repl.exe"),
                        NodeModulesPath = CheckedPath(root, "bin/node_modules"),
                        SkyTransportPath = CheckedPath(root, TransportRelativePath),
                        HelperPath = CheckedPath(root, HelperRelativePath),
                        SelectedCodexPath = Path.GetFullPath(codexPath),
                        SelectedCodexVersion = codexVersion,
                        RuntimeVersion = runtimeVersion,
                        NodeVersion = Text(manifest, "node_version"),
                        SkyVersion = Text(package, "version")
                    }
                };
            }
            catch (Exception exception) when (IsInvalidInstallation(exception))
            {
                return Failure(ComputerUseComponentStatus.InvalidComponents, "component-unreadable-or-incomplete");
            }
        }

        private bool IsVerifiedStaleRoot(string root)
        {
            try
            {
                // Older official installations can retain node_repl and their manifest
                // after Node/sky have been removed. Recognize their identity only to repair
                // exact stale configuration references, never to run the old executable.
                return !IsReparsePoint(root) && staleFileHashes.All(entry => MatchesHash(CheckedPath(root, entry.Key), entry.Value));
            }
            catch (Exception exception) when (IsInvalidInstallation(exception))
            {
                return false;
            }
        }

        private static bool MatchesHash(string path, string expected)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
            {
                var actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty);
                return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static IDictionary<string, object> ReadObject(string path)
        {
            if (new FileInfo(path).Length > 65536)
            {
                throw new InvalidDataException("Component metadata exceeds its size limit.");
            }
            var value = new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 16 }.DeserializeObject(File.ReadAllText(path)) as IDictionary<string, object>;
            if (value == null)
            {
                throw new InvalidDataException("Component metadata must be an object.");
            }
            return value;
        }

        private static bool Has(IDictionary<string, object> values, string key, string expected)
        {
            return values.TryGetValue(key, out var actual) && string.Equals(actual as string, expected, StringComparison.Ordinal);
        }

        private static string Text(IDictionary<string, object> values, string key)
        {
            return values.TryGetValue(key, out var value) ? value as string : null;
        }

        private static Version RuntimeVersion(string value)
        {
            return Version.TryParse((value ?? string.Empty).Split('/')[0], out var version) ? version : null;
        }

        private static int CompareRuntimeVersions(string left, string right)
        {
            var comparison = RuntimeVersion(left).CompareTo(RuntimeVersion(right));
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left, right);
        }

        private static string CheckedPath(string root, string relativePath)
        {
            var path = root;
            foreach (var segment in relativePath.Split('/'))
            {
                path = Path.Combine(path, segment);
                if (IsReparsePoint(path))
                {
                    throw new InvalidDataException("Linked component entry is not supported.");
                }
            }
            return Path.GetFullPath(path);
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static bool IsInvalidInstallation(Exception exception)
        {
            return exception is IOException || exception is InvalidDataException || exception is UnauthorizedAccessException || exception is SecurityException
                || exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException;
        }

        private static ComputerUseComponentResolution Failure(ComputerUseComponentStatus status, string code)
        {
            return new ComputerUseComponentResolution { Status = status, DetailCode = code };
        }
    }
}
