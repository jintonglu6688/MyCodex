using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Office;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeCliInstallationServiceTests
    {
        [Test]
        public async Task InstallFromLocalPackageAsync_CopiesExecutableInstallsSkillsAndUpdatesUserPath()
        {
            var root = CreateTempDirectory();
            try
            {
                var localAppData = Path.Combine(root, "LocalAppData");
                var userProfile = Path.Combine(root, "User");
                var sourceExe = Path.Combine(root, "officecli-win-x64.exe");
                Directory.CreateDirectory(Path.GetDirectoryName(sourceExe));
                File.WriteAllText(sourceExe, "fake exe");
                using (var env = new FakeEnvironmentStore(@"C:\Windows"))
                {
                    var service = new OfficeCliInstallationService(env);
                    var paths = OfficeCliIntegrationPlanner.BuildPaths(localAppData, userProfile, null);

                    var result = await service.InstallFromLocalPackageAsync(
                        paths,
                        sourceExe,
                        "1.2.3",
                        "# OfficeCLI\nUse officecli.",
                        CancellationToken.None);

                    Assert.That(result.Success, Is.True);
                    Assert.That(File.Exists(paths.ExecutablePath), Is.True);
                    Assert.That(File.ReadAllText(paths.ExecutablePath), Is.EqualTo("fake exe"));
                    Assert.That(env.UserPath.Split(Path.PathSeparator)[0], Is.EqualTo(paths.InstallDirectory));
                    Assert.That((Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator), Does.Contain(paths.InstallDirectory));
                    foreach (var skillDirectory in paths.SkillDirectories)
                    {
                        Assert.That(File.Exists(Path.Combine(skillDirectory, "SKILL.md")), Is.True, skillDirectory);
                    }

                    Assert.That(File.Exists(Path.Combine(paths.InstallDirectory, "mycodex-install.json")), Is.True);
                    Assert.That(result.Status.IsReady, Is.True);
                }
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public async Task UninstallAsync_RemovesManagedInstallSkillsAndPathEntry()
        {
            var root = CreateTempDirectory();
            try
            {
                var localAppData = Path.Combine(root, "LocalAppData");
                var userProfile = Path.Combine(root, "User");
                var sourceExe = Path.Combine(root, "officecli-win-x64.exe");
                File.WriteAllText(sourceExe, "fake exe");
                using (var env = new FakeEnvironmentStore(@"C:\Windows"))
                {
                    var service = new OfficeCliInstallationService(env);
                    var paths = OfficeCliIntegrationPlanner.BuildPaths(localAppData, userProfile, null);
                    await service.InstallFromLocalPackageAsync(
                        paths,
                        sourceExe,
                        "1.2.3",
                        "# OfficeCLI",
                        CancellationToken.None);

                    var result = await service.UninstallAsync(paths, CancellationToken.None);

                    Assert.That(result.Success, Is.True);
                    Assert.That(Directory.Exists(paths.InstallDirectory), Is.False);
                    Assert.That(env.UserPath.Split(Path.PathSeparator), Does.Not.Contain(paths.InstallDirectory));
                    foreach (var skillDirectory in paths.SkillDirectories)
                    {
                        Assert.That(Directory.Exists(skillDirectory), Is.False, skillDirectory);
                    }
                }
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public async Task Results_UseChineseSharedLocalization()
        {
            var root = CreateTempDirectory();
            using (new TemporaryCulture("zh-CN"))
            {
                try
                {
                    var localAppData = Path.Combine(root, "LocalAppData");
                    var userProfile = Path.Combine(root, "User");
                    var sourceExe = Path.Combine(root, "officecli-win-x64.exe");
                    File.WriteAllText(sourceExe, "fake exe");
                    using (var env = new FakeEnvironmentStore(@"C:\Windows"))
                    {
                        var service = new OfficeCliInstallationService(env);
                        var paths = OfficeCliIntegrationPlanner.BuildPaths(localAppData, userProfile, null);

                        var installResult = await service.InstallFromLocalPackageAsync(
                            paths,
                            sourceExe,
                            "1.2.3",
                            "# OfficeCLI",
                            CancellationToken.None);
                        var uninstallResult = await service.UninstallAsync(paths, CancellationToken.None);

                        Assert.That(installResult.Message, Is.EqualTo("OfficeCLI 已安装。"));
                        Assert.That(uninstallResult.Message, Is.EqualTo("OfficeCLI 已卸载。"));
                    }
                }
                finally
                {
                    DeleteDirectory(root);
                }
            }
        }

        [Test]
        public async Task InstallLatestAsync_WhenGitCodeMirrorSucceeds_InstallsFromMirrorWithoutGitHub()
        {
            var root = CreateTempDirectory();
            try
            {
                var localAppData = Path.Combine(root, "LocalAppData");
                var userProfile = Path.Combine(root, "User");
                var downloadedUrls = new List<string>();
                var exeBytes = Encoding.UTF8.GetBytes("mirror exe");
                var skillBytes = Encoding.UTF8.GetBytes("# Mirror OfficeCLI");
                using (var env = new FakeEnvironmentStore(@"C:\Windows"))
                {
                    var service = new OfficeCliInstallationService(
                        env,
                        (uri, cancellationToken) =>
                        {
                            downloadedUrls.Add(uri.AbsoluteUri);
                            if (uri.AbsoluteUri.Contains("officecli-stable"))
                            {
                                return Task.FromResult(BuildMirrorManifest(
                                    "1.2.3",
                                    "https://gitcode.example/officecli-win-x64.exe",
                                    ComputeSha256Hex(exeBytes),
                                    "https://gitcode.example/SKILL.md",
                                    ComputeSha256Hex(skillBytes)));
                            }

                            throw new InvalidOperationException("GitHub should not be used.");
                        },
                        (uri, path, cancellationToken) =>
                        {
                            downloadedUrls.Add(uri.AbsoluteUri);
                            File.WriteAllBytes(path, uri.AbsoluteUri.EndsWith("SKILL.md", StringComparison.OrdinalIgnoreCase) ? skillBytes : exeBytes);
                            return Task.CompletedTask;
                        });
                    var paths = OfficeCliIntegrationPlanner.BuildPaths(localAppData, userProfile, null);

                    var result = await service.InstallLatestAsync(paths, CancellationToken.None);

                    Assert.That(result.Success, Is.True);
                    Assert.That(result.Version, Is.EqualTo("1.2.3"));
                    Assert.That(File.ReadAllText(paths.ExecutablePath), Is.EqualTo("mirror exe"));
                    foreach (var skillDirectory in paths.SkillDirectories)
                    {
                        Assert.That(File.ReadAllText(Path.Combine(skillDirectory, "SKILL.md")), Is.EqualTo("# Mirror OfficeCLI"));
                    }

                    Assert.That(downloadedUrls, Does.Contain("https://gitcode.example/officecli-win-x64.exe"));
                    Assert.That(downloadedUrls.Any(url => url.Contains("api.github.com")), Is.False);
                }
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public async Task InstallLatestAsync_WhenGitCodeMirrorFails_FallsBackToGitHub()
        {
            var root = CreateTempDirectory();
            try
            {
                var localAppData = Path.Combine(root, "LocalAppData");
                var userProfile = Path.Combine(root, "User");
                var downloadedUrls = new List<string>();
                var exeBytes = Encoding.UTF8.GetBytes("github exe");
                using (var env = new FakeEnvironmentStore(@"C:\Windows"))
                {
                    var service = new OfficeCliInstallationService(
                        env,
                        (uri, cancellationToken) =>
                        {
                            downloadedUrls.Add(uri.AbsoluteUri);
                            if (uri.AbsoluteUri.Contains("officecli-stable"))
                            {
                                throw new InvalidOperationException("mirror unavailable");
                            }

                            if (uri.AbsoluteUri.Contains("api.github.com"))
                            {
                                return Task.FromResult(BuildGitHubReleaseJson());
                            }

                            if (uri.AbsoluteUri.EndsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase))
                            {
                                return Task.FromResult(ComputeSha256Hex(exeBytes) + "  officecli-win-x64.exe\n");
                            }

                            if (uri.AbsoluteUri.Contains("raw.githubusercontent.com"))
                            {
                                return Task.FromResult("# GitHub OfficeCLI");
                            }

                            throw new InvalidOperationException("unexpected string download: " + uri.AbsoluteUri);
                        },
                        (uri, path, cancellationToken) =>
                        {
                            downloadedUrls.Add(uri.AbsoluteUri);
                            File.WriteAllBytes(path, exeBytes);
                            return Task.CompletedTask;
                        });
                    var paths = OfficeCliIntegrationPlanner.BuildPaths(localAppData, userProfile, null);

                    var result = await service.InstallLatestAsync(paths, CancellationToken.None);

                    Assert.That(result.Success, Is.True);
                    Assert.That(result.Version, Is.EqualTo("1.2.3"));
                    Assert.That(File.ReadAllText(paths.ExecutablePath), Is.EqualTo("github exe"));
                    Assert.That(downloadedUrls.Any(url => url.Contains("officecli-stable")), Is.True);
                    Assert.That(downloadedUrls.Any(url => url.Contains("api.github.com")), Is.True);
                    Assert.That(downloadedUrls, Does.Contain("https://github.example/officecli-win-x64.exe"));
                }
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public async Task InstallLatestAsync_WhenGitCodeMirrorShaMismatches_FallsBackToGitHub()
        {
            var root = CreateTempDirectory();
            try
            {
                var localAppData = Path.Combine(root, "LocalAppData");
                var userProfile = Path.Combine(root, "User");
                var exeBytes = Encoding.UTF8.GetBytes("github exe after mirror mismatch");
                using (var env = new FakeEnvironmentStore(@"C:\Windows"))
                {
                    var service = new OfficeCliInstallationService(
                        env,
                        (uri, cancellationToken) =>
                        {
                            if (uri.AbsoluteUri.Contains("officecli-stable"))
                            {
                                return Task.FromResult(BuildMirrorManifest(
                                    "1.2.3",
                                    "https://gitcode.example/officecli-win-x64.exe",
                                    new string('0', 64),
                                    "https://gitcode.example/SKILL.md",
                                    new string('1', 64)));
                            }

                            if (uri.AbsoluteUri.Contains("api.github.com"))
                            {
                                return Task.FromResult(BuildGitHubReleaseJson());
                            }

                            if (uri.AbsoluteUri.EndsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase))
                            {
                                return Task.FromResult(ComputeSha256Hex(exeBytes) + "  officecli-win-x64.exe\n");
                            }

                            if (uri.AbsoluteUri.Contains("raw.githubusercontent.com"))
                            {
                                return Task.FromResult("# GitHub OfficeCLI");
                            }

                            throw new InvalidOperationException("unexpected string download: " + uri.AbsoluteUri);
                        },
                        (uri, path, cancellationToken) =>
                        {
                            File.WriteAllBytes(path, uri.AbsoluteUri.Contains("gitcode.example")
                                ? Encoding.UTF8.GetBytes("bad mirror exe")
                                : exeBytes);
                            return Task.CompletedTask;
                        });
                    var paths = OfficeCliIntegrationPlanner.BuildPaths(localAppData, userProfile, null);

                    var result = await service.InstallLatestAsync(paths, CancellationToken.None);

                    Assert.That(result.Success, Is.True);
                    Assert.That(File.ReadAllText(paths.ExecutablePath), Is.EqualTo("github exe after mirror mismatch"));
                }
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void InstallLatestAsync_WhenBothSourcesFail_ReportsBothSources()
        {
            using (var env = new FakeEnvironmentStore(@"C:\Windows"))
            {
                var service = new OfficeCliInstallationService(
                    env,
                    (uri, cancellationToken) =>
                    {
                        if (uri.AbsoluteUri.Contains("officecli-stable"))
                        {
                            throw new InvalidOperationException("mirror unavailable");
                        }

                        throw new InvalidOperationException("github unavailable");
                    },
                    (uri, path, cancellationToken) => throw new InvalidOperationException("download unavailable"));
                var paths = OfficeCliIntegrationPlanner.BuildPaths(
                    Path.Combine(Path.GetTempPath(), "MyCodex-OfficeCliBothFail"),
                    Path.Combine(Path.GetTempPath(), "MyCodex-OfficeCliBothFailUser"),
                    null);

                var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await service.InstallLatestAsync(paths, CancellationToken.None));

                Assert.That(ex.Message, Does.Contain("GitCode"));
                Assert.That(ex.Message, Does.Contain("GitHub"));
                Assert.That(ex.Message, Does.Contain("mirror unavailable"));
                Assert.That(ex.Message, Does.Contain("github unavailable"));
            }
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-OfficeCliInstallTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }

        private sealed class FakeEnvironmentStore : IOfficeCliEnvironmentStore, IDisposable
        {
            private readonly string _originalProcessPath;

            public FakeEnvironmentStore(string userPath)
            {
                _originalProcessPath = Environment.GetEnvironmentVariable("PATH");
                UserPath = userPath;
                Environment.SetEnvironmentVariable("PATH", userPath);
            }

            public string UserPath { get; private set; }

            public string GetUserPath()
            {
                return UserPath;
            }

            public void SetUserPath(string path)
            {
                UserPath = path;
            }

            public string GetProcessPath()
            {
                return Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            }

            public void SetProcessPath(string path)
            {
                Environment.SetEnvironmentVariable("PATH", path);
            }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("PATH", _originalProcessPath);
            }
        }

        private sealed class TemporaryCulture : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;

            public TemporaryCulture(string name)
            {
                _originalCulture = Thread.CurrentThread.CurrentCulture;
                _originalUiCulture = Thread.CurrentThread.CurrentUICulture;
                var culture = CultureInfo.GetCultureInfo(name);
                Thread.CurrentThread.CurrentCulture = culture;
                Thread.CurrentThread.CurrentUICulture = culture;
            }

            public void Dispose()
            {
                Thread.CurrentThread.CurrentCulture = _originalCulture;
                Thread.CurrentThread.CurrentUICulture = _originalUiCulture;
            }
        }

        private static string BuildMirrorManifest(
            string version,
            string exeUrl,
            string exeSha256,
            string skillUrl,
            string skillSha256)
        {
            return "{\n" +
                "  \"version\": \"" + version + "\",\n" +
                "  \"tag\": \"v" + version + "\",\n" +
                "  \"assets\": {\n" +
                "    \"win-x64\": {\n" +
                "      \"fileName\": \"officecli-win-x64.exe\",\n" +
                "      \"url\": \"" + exeUrl + "\",\n" +
                "      \"sha256\": \"" + exeSha256 + "\"\n" +
                "    }\n" +
                "  },\n" +
                "  \"skill\": {\n" +
                "    \"fileName\": \"SKILL.md\",\n" +
                "    \"url\": \"" + skillUrl + "\",\n" +
                "    \"sha256\": \"" + skillSha256 + "\"\n" +
                "  }\n" +
                "}\n";
        }

        private static string BuildGitHubReleaseJson()
        {
            return "{\n" +
                "  \"tag_name\": \"v1.2.3\",\n" +
                "  \"assets\": [\n" +
                "    { \"name\": \"officecli-win-x64.exe\", \"browser_download_url\": \"https://github.example/officecli-win-x64.exe\" },\n" +
                "    { \"name\": \"SHA256SUMS\", \"browser_download_url\": \"https://github.example/SHA256SUMS\" }\n" +
                "  ]\n" +
                "}\n";
        }

        private static string ComputeSha256Hex(byte[] bytes)
        {
            using (var sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
