using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Office
{
    public sealed class OfficeCliInstallationService
    {
        private const string GitCodeMirrorManifestUrl = "https://api.gitcode.com/api/v5/repos/gcw_SpGZ48lW/mycodex-updates/releases/officecli-stable/attach_files/officecli.json/download";
        private const string LatestReleaseApiUrl = "https://api.github.com/repos/iOfficeAI/OfficeCLI/releases/latest";
        private const string SkillRawUrlTemplate = "https://raw.githubusercontent.com/iOfficeAI/OfficeCLI/{0}/SKILL.md";
        private const string InstallManifestFileName = "mycodex-install.json";

        private readonly IOfficeCliEnvironmentStore _environmentStore;
        private readonly Func<Uri, CancellationToken, Task<string>> _downloadStringAsync;
        private readonly Func<Uri, string, CancellationToken, Task> _downloadFileAsync;

        public OfficeCliInstallationService()
            : this(new OfficeCliEnvironmentStore())
        {
        }

        public OfficeCliInstallationService(IOfficeCliEnvironmentStore environmentStore)
            : this(environmentStore, DefaultDownloadStringAsync, DefaultDownloadFileAsync)
        {
        }

        internal OfficeCliInstallationService(
            IOfficeCliEnvironmentStore environmentStore,
            Func<Uri, CancellationToken, Task<string>> downloadStringAsync,
            Func<Uri, string, CancellationToken, Task> downloadFileAsync)
        {
            _environmentStore = environmentStore ?? throw new ArgumentNullException(nameof(environmentStore));
            _downloadStringAsync = downloadStringAsync ?? throw new ArgumentNullException(nameof(downloadStringAsync));
            _downloadFileAsync = downloadFileAsync ?? throw new ArgumentNullException(nameof(downloadFileAsync));
        }

        public async Task<OfficeCliInstallationResult> InstallLatestAsync(
            OfficeCliIntegrationPaths paths,
            CancellationToken cancellationToken)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            Exception mirrorFailure = null;
            try
            {
                return await InstallLatestFromGitCodeMirrorAsync(paths, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                mirrorFailure = ex;
                Trace.TraceWarning("OfficeCLI GitCode mirror installation failed; falling back to GitHub official source: {0}", ex.Message);
            }

            try
            {
                return await InstallLatestFromGitHubAsync(paths, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception githubFailure) when (mirrorFailure != null && !(githubFailure is OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        SharedLocalization.Text(
                            "OfficeCliInstallation.Error.AllSourcesFailed",
                            "OfficeCLI installation failed from GitCode mirror and GitHub official source. GitCode: {0}; GitHub: {1}"),
                        mirrorFailure.Message,
                        githubFailure.Message),
                    githubFailure);
            }
        }

        private async Task<OfficeCliInstallationResult> InstallLatestFromGitCodeMirrorAsync(
            OfficeCliIntegrationPaths paths,
            CancellationToken cancellationToken)
        {
            var manifestJson = await _downloadStringAsync(new Uri(GitCodeMirrorManifestUrl), cancellationToken).ConfigureAwait(false);
            var manifest = MirrorManifest.Parse(manifestJson);
            var assetKey = ResolveWindowsMirrorAssetKey();
            var asset = manifest.FindAsset(assetKey);
            if (asset == null || !asset.IsComplete)
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "OfficeCliInstallation.Error.ReleaseAssetMissing",
                    "OfficeCLI release assets are incomplete."));
            }

            if (manifest.Skill == null || !manifest.Skill.IsComplete)
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "OfficeCliInstallation.Error.ReleaseAssetMissing",
                    "OfficeCLI release assets are incomplete."));
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "MyCodex-OfficeCLI-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                var packagePath = Path.Combine(tempDirectory, SafeFileName(asset.FileName, ResolveWindowsAssetName()));
                await _downloadFileAsync(new Uri(asset.Url), packagePath, cancellationToken).ConfigureAwait(false);
                VerifySha256(packagePath, asset.Sha256);

                var skillPath = Path.Combine(tempDirectory, SafeFileName(manifest.Skill.FileName, "SKILL.md"));
                await _downloadFileAsync(new Uri(manifest.Skill.Url), skillPath, cancellationToken).ConfigureAwait(false);
                VerifySha256(skillPath, manifest.Skill.Sha256);
                var skillText = File.ReadAllText(skillPath, Encoding.UTF8);

                return await InstallFromLocalPackageAsync(
                    paths,
                    packagePath,
                    manifest.Version,
                    skillText,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        private async Task<OfficeCliInstallationResult> InstallLatestFromGitHubAsync(
            OfficeCliIntegrationPaths paths,
            CancellationToken cancellationToken)
        {
            var release = await LoadLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            var assetName = ResolveWindowsAssetName();
            var assetUrl = release.FindAssetUrl(assetName);
            var shaUrl = release.FindAssetUrl("SHA256SUMS");
            if (string.IsNullOrWhiteSpace(assetUrl) || string.IsNullOrWhiteSpace(shaUrl))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "OfficeCliInstallation.Error.ReleaseAssetMissing",
                    "OfficeCLI release assets are incomplete."));
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "MyCodex-OfficeCLI-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                var shaText = await _downloadStringAsync(new Uri(shaUrl), cancellationToken).ConfigureAwait(false);
                var expectedSha256 = ParseSha256(shaText, assetName);
                var packagePath = Path.Combine(tempDirectory, assetName);
                await _downloadFileAsync(new Uri(assetUrl), packagePath, cancellationToken).ConfigureAwait(false);
                VerifySha256(packagePath, expectedSha256);
                var skillText = await _downloadStringAsync(
                    new Uri(string.Format(CultureInfo.InvariantCulture, SkillRawUrlTemplate, release.TagName)),
                    cancellationToken).ConfigureAwait(false);

                return await InstallFromLocalPackageAsync(
                    paths,
                    packagePath,
                    TrimVersionPrefix(release.TagName),
                    skillText,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                TryDeleteDirectory(tempDirectory);
            }
        }

        public Task<OfficeCliInstallationResult> InstallFromLocalPackageAsync(
            OfficeCliIntegrationPaths paths,
            string sourceExecutablePath,
            string version,
            string skillMarkdown,
            CancellationToken cancellationToken)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            if (string.IsNullOrWhiteSpace(sourceExecutablePath) || !File.Exists(sourceExecutablePath))
            {
                throw new FileNotFoundException(
                    SharedLocalization.Text("OfficeCliInstallation.Error.PackageMissing", "OfficeCLI package was not found."),
                    sourceExecutablePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(paths.InstallDirectory);
            File.Copy(sourceExecutablePath, paths.ExecutablePath, true);
            InstallSkills(paths.SkillDirectories, skillMarkdown ?? string.Empty);
            WriteInstallManifest(paths, version);
            ApplyUserAndProcessPath(paths.InstallDirectory);

            var status = OfficeCliIntegrationPlanner.EvaluateStatus(paths, _environmentStore.GetUserPath(), File.Exists);
            return Task.FromResult(new OfficeCliInstallationResult
            {
                Success = status.IsReady,
                Version = version,
                Message = status.IsReady
                    ? SharedLocalization.Text("OfficeCliInstallation.Status.Ready", "OfficeCLI is installed.")
                    : SharedLocalization.Text("OfficeCliInstallation.Status.Partial", "OfficeCLI installation finished with missing components."),
                Status = status
            });
        }

        public Task<OfficeCliInstallationResult> UninstallAsync(
            OfficeCliIntegrationPaths paths,
            CancellationToken cancellationToken)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            cancellationToken.ThrowIfCancellationRequested();
            TryDeleteDirectory(paths.InstallDirectory);
            DeleteSkillDirectories(paths.SkillDirectories);
            RemoveDirectoryFromUserAndProcessPath(paths.InstallDirectory);

            var status = OfficeCliIntegrationPlanner.EvaluateStatus(paths, _environmentStore.GetUserPath(), File.Exists);
            return Task.FromResult(new OfficeCliInstallationResult
            {
                Success = !status.IsExecutableInstalled && !status.IsUserPathConfigured,
                Message = SharedLocalization.Text("OfficeCliInstallation.Status.Uninstalled", "OfficeCLI was uninstalled."),
                Status = status
            });
        }

        public OfficeCliIntegrationStatus GetStatus(OfficeCliIntegrationPaths paths)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            return OfficeCliIntegrationPlanner.EvaluateStatus(paths, _environmentStore.GetUserPath(), File.Exists);
        }

        private async Task<ReleaseInfo> LoadLatestReleaseAsync(CancellationToken cancellationToken)
        {
            var json = await _downloadStringAsync(new Uri(LatestReleaseApiUrl), cancellationToken).ConfigureAwait(false);
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
            if (root == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "OfficeCliInstallation.Error.ReleaseInvalid",
                    "OfficeCLI release response is invalid."));
            }

            object tagValue;
            var tagName = root.TryGetValue("tag_name", out tagValue) ? tagValue as string : null;
            object assetsValue;
            var assets = root.TryGetValue("assets", out assetsValue) ? assetsValue as object[] : null;
            if (string.IsNullOrWhiteSpace(tagName) || assets == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "OfficeCliInstallation.Error.ReleaseInvalid",
                    "OfficeCLI release response is invalid."));
            }

            var release = new ReleaseInfo { TagName = tagName.Trim() };
            for (var i = 0; i < assets.Length; i++)
            {
                var asset = assets[i] as Dictionary<string, object>;
                if (asset == null)
                {
                    continue;
                }

                object nameValue;
                object urlValue;
                var name = asset.TryGetValue("name", out nameValue) ? nameValue as string : null;
                var url = asset.TryGetValue("browser_download_url", out urlValue) ? urlValue as string : null;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url))
                {
                    release.Assets[name.Trim()] = url.Trim();
                }
            }

            return release;
        }

        private static async Task<string> DefaultDownloadStringAsync(Uri uri, CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = CreateWebClient())
            using (cancellationToken.Register(client.CancelAsync))
            {
                try
                {
                    return await client.DownloadStringTaskAsync(uri).ConfigureAwait(false);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        private static async Task DefaultDownloadFileAsync(Uri uri, string destinationPath, CancellationToken cancellationToken)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = CreateWebClient())
            using (cancellationToken.Register(client.CancelAsync))
            {
                try
                {
                    await client.DownloadFileTaskAsync(uri, destinationPath).ConfigureAwait(false);
                }
                catch (WebException) when (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
            }
        }

        private static WebClient CreateWebClient()
        {
            var client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "MyCodex";
            return client;
        }

        private static string ResolveWindowsAssetName()
        {
            var architecture = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty) + ";" +
                (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? string.Empty);
            return architecture.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0
                ? "officecli-win-arm64.exe"
                : "officecli-win-x64.exe";
        }

        private static string ResolveWindowsMirrorAssetKey()
        {
            var architecture = (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? string.Empty) + ";" +
                (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ?? string.Empty);
            return architecture.IndexOf("ARM64", StringComparison.OrdinalIgnoreCase) >= 0
                ? "win-arm64"
                : "win-x64";
        }

        private static string ParseSha256(string shaText, string assetName)
        {
            var lines = (shaText ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.EndsWith(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && parts[0].Length >= 64)
                    {
                        return parts[0];
                    }
                }
            }

            throw new InvalidOperationException(SharedLocalization.Text(
                "OfficeCliInstallation.Error.ShaMissing",
                "OfficeCLI SHA256 entry was not found."));
        }

        private static void VerifySha256(string filePath, string expectedSha256)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(filePath))
            {
                var actual = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
                if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "OfficeCliInstallation.Error.ShaMismatch",
                        "OfficeCLI SHA256 verification failed."));
                }
            }
        }

        private static void InstallSkills(IEnumerable<string> skillDirectories, string skillMarkdown)
        {
            foreach (var directory in skillDirectories ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                Directory.CreateDirectory(directory);
                var skillPath = Path.Combine(directory, "SKILL.md");
                BackupChangedSkill(skillPath, skillMarkdown);
                File.WriteAllText(skillPath, skillMarkdown ?? string.Empty, new UTF8Encoding(false));
            }
        }

        private static void BackupChangedSkill(string skillPath, string skillMarkdown)
        {
            if (!File.Exists(skillPath))
            {
                return;
            }

            var existing = File.ReadAllText(skillPath, Encoding.UTF8);
            if (string.Equals(existing, skillMarkdown ?? string.Empty, StringComparison.Ordinal))
            {
                return;
            }

            var backupPath = skillPath + ".bak." + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.Copy(skillPath, backupPath, false);
        }

        private static void DeleteSkillDirectories(IEnumerable<string> skillDirectories)
        {
            foreach (var directory in skillDirectories ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }

                TryDeleteDirectory(directory);
            }
        }

        private void ApplyUserAndProcessPath(string installDirectory)
        {
            var userPath = OfficeCliIntegrationPlanner.PrependDirectoryToPath(_environmentStore.GetUserPath(), installDirectory);
            _environmentStore.SetUserPath(userPath);
            var processPath = OfficeCliIntegrationPlanner.PrependDirectoryToPath(_environmentStore.GetProcessPath(), installDirectory);
            _environmentStore.SetProcessPath(processPath);
        }

        private void RemoveDirectoryFromUserAndProcessPath(string installDirectory)
        {
            _environmentStore.SetUserPath(RemoveDirectoryFromPath(_environmentStore.GetUserPath(), installDirectory));
            _environmentStore.SetProcessPath(RemoveDirectoryFromPath(_environmentStore.GetProcessPath(), installDirectory));
        }

        private static string RemoveDirectoryFromPath(string pathValue, string directory)
        {
            if (string.IsNullOrWhiteSpace(pathValue) || string.IsNullOrWhiteSpace(directory))
            {
                return pathValue ?? string.Empty;
            }

            var normalizedDirectory = NormalizePath(directory);
            var entries = pathValue.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries);
            var kept = new List<string>();
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i].Trim();
                if (entry.Length == 0 ||
                    string.Equals(NormalizePath(entry), normalizedDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                kept.Add(entry);
            }

            return string.Join(Path.PathSeparator.ToString(), kept.ToArray());
        }

        private static string NormalizePath(string path)
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static void WriteInstallManifest(OfficeCliIntegrationPaths paths, string version)
        {
            var manifestPath = Path.Combine(paths.InstallDirectory, InstallManifestFileName);
            var json = "{\n" +
                "  \"installedBy\": \"MyCodex\",\n" +
                "  \"version\": \"" + EscapeJson(version ?? string.Empty) + "\",\n" +
                "  \"installedAt\": \"" + DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + "\",\n" +
                "  \"executablePath\": \"" + EscapeJson(paths.ExecutablePath ?? string.Empty) + "\"\n" +
                "}\n";
            File.WriteAllText(manifestPath, json, new UTF8Encoding(false));
        }

        private static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string TrimVersionPrefix(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName))
            {
                return string.Empty;
            }

            var value = tagName.Trim();
            return value.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? value.Substring(1) : value;
        }

        private static string SafeFileName(string value, string fallback)
        {
            var fileName = string.IsNullOrWhiteSpace(value) ? null : Path.GetFileName(value.Trim());
            return string.IsNullOrWhiteSpace(fileName) ? fallback : fileName;
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to delete OfficeCLI directory '{0}': {1}", path, ex.Message);
            }
        }

        private sealed class ReleaseInfo
        {
            public ReleaseInfo()
            {
                Assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            public string TagName { get; set; }

            public Dictionary<string, string> Assets { get; private set; }

            public string FindAssetUrl(string name)
            {
                string value;
                return Assets.TryGetValue(name, out value) ? value : null;
            }
        }

        private sealed class MirrorManifest
        {
            public MirrorManifest()
            {
                Assets = new Dictionary<string, MirrorAsset>(StringComparer.OrdinalIgnoreCase);
            }

            public string Version { get; private set; }

            public Dictionary<string, MirrorAsset> Assets { get; private set; }

            public MirrorAsset Skill { get; private set; }

            public MirrorAsset FindAsset(string key)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    return null;
                }

                MirrorAsset asset;
                return Assets.TryGetValue(key.Trim(), out asset) ? asset : null;
            }

            public static MirrorManifest Parse(string json)
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(json) as Dictionary<string, object>;
                if (root == null)
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "OfficeCliInstallation.Error.ReleaseInvalid",
                        "OfficeCLI release response is invalid."));
                }

                var manifest = new MirrorManifest();
                manifest.Version = GetString(root, "version");
                if (string.IsNullOrWhiteSpace(manifest.Version))
                {
                    manifest.Version = TrimVersionPrefix(GetString(root, "tag"));
                }

                var assets = GetDictionary(root, "assets");
                if (assets != null)
                {
                    foreach (var pair in assets)
                    {
                        var asset = MirrorAsset.FromObject(pair.Value);
                        if (asset != null && !string.IsNullOrWhiteSpace(pair.Key))
                        {
                            manifest.Assets[pair.Key.Trim()] = asset;
                        }
                    }
                }

                manifest.Skill = MirrorAsset.FromObject(GetValue(root, "skill"));
                if (string.IsNullOrWhiteSpace(manifest.Version) || manifest.Assets.Count == 0)
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "OfficeCliInstallation.Error.ReleaseInvalid",
                        "OfficeCLI release response is invalid."));
                }

                return manifest;
            }
        }

        private sealed class MirrorAsset
        {
            public string FileName { get; private set; }

            public string Url { get; private set; }

            public string Sha256 { get; private set; }

            public bool IsComplete
            {
                get
                {
                    return !string.IsNullOrWhiteSpace(FileName) &&
                        !string.IsNullOrWhiteSpace(Url) &&
                        !string.IsNullOrWhiteSpace(Sha256);
                }
            }

            public static MirrorAsset FromObject(object value)
            {
                var dictionary = value as Dictionary<string, object>;
                if (dictionary == null)
                {
                    return null;
                }

                return new MirrorAsset
                {
                    FileName = GetString(dictionary, "fileName"),
                    Url = GetString(dictionary, "url"),
                    Sha256 = GetString(dictionary, "sha256")
                };
            }
        }

        private static object GetValue(IDictionary<string, object> dictionary, string key)
        {
            if (dictionary == null || string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            object value;
            return dictionary.TryGetValue(key, out value) ? value : null;
        }

        private static Dictionary<string, object> GetDictionary(IDictionary<string, object> dictionary, string key)
        {
            return GetValue(dictionary, key) as Dictionary<string, object>;
        }

        private static string GetString(IDictionary<string, object> dictionary, string key)
        {
            var value = GetValue(dictionary, key);
            return value == null ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }
}
