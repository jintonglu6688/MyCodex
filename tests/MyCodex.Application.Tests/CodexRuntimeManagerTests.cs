using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class CodexRuntimeManagerTests
    {
        [Test]
        public void GetStatus_WhenBundledRuntimeMissing_ReportsUnavailableRuntime()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var manager = new CodexRuntimeManager(new AppConfigService(rootPath));
                var status = manager.GetStatus(new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.Bundled
                });

                Assert.That(status.CanLaunch, Is.False);
                Assert.That(status.PreferredSource, Is.EqualTo(CodexRuntimeSources.Bundled));
                Assert.That(status.EffectiveSource, Is.Null);
                Assert.That(status.Message, Does.Contain("Codex"));
                Assert.That(
                    status.Message.IndexOf("download", StringComparison.OrdinalIgnoreCase),
                    Is.EqualTo(-1));
                Assert.That(status.Message, Does.Not.Contain("下载"));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolveLaunchInfo_WhenCustomRuntimeSelected_IncludesUtf8Environment()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var executablePath = Path.Combine(rootPath, "codex.cmd");
                File.WriteAllText(executablePath, string.Empty);

                var manager = new CodexRuntimeManager(new AppConfigService(rootPath));
                var launchInfo = manager.ResolveLaunchInfo(new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.Custom,
                    CodexCustomExecutablePath = executablePath
                });

                Assert.That(launchInfo.EnvironmentVariables["MYCODEX_TEXT_ENCODING"], Is.EqualTo("utf-8"));
                Assert.That(launchInfo.EnvironmentVariables["PYTHONUTF8"], Is.EqualTo("1"));
                Assert.That(launchInfo.EnvironmentVariables["PYTHONIOENCODING"], Is.EqualTo("utf-8"));
                Assert.That(launchInfo.EnvironmentVariables["LANG"], Is.EqualTo("C.UTF-8"));
                Assert.That(launchInfo.EnvironmentVariables["LC_ALL"], Is.EqualTo("C.UTF-8"));
                Assert.That(launchInfo.EnvironmentVariables["LESSCHARSET"], Is.EqualTo("utf-8"));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolveLaunchInfo_WhenLocalWindowsRuntimeStarts_DoesNotOverrideCodexHome()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var executablePath = Path.Combine(rootPath, "codex.cmd");
                File.WriteAllText(executablePath, string.Empty);

                var manager = new CodexRuntimeManager(new AppConfigService(rootPath));
                var launchInfo = manager.ResolveLaunchInfo(new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.Custom,
                    CodexCustomExecutablePath = executablePath
                });

                Assert.That(launchInfo.EnvironmentVariables.ContainsKey("CODEX_HOME"), Is.False);
                Assert.That(launchInfo.EnvironmentVariables.ContainsKey("MYCODEX_CODEX_HOME"), Is.False);
                Assert.That(launchInfo.EnvironmentVariables.ContainsKey("HOME"), Is.False);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ApplyOfficeCliEnvironmentIfInstalled_PrependsDefaultOfficeCliDirectory()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var officeCliDirectory = Path.Combine(rootPath, "OfficeCLI");
                Directory.CreateDirectory(officeCliDirectory);
                File.WriteAllText(Path.Combine(officeCliDirectory, "officecli.exe"), string.Empty);
                var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    { "PATH", @"C:\Windows" }
                };

                var applied = CodexRuntimeManager.ApplyOfficeCliEnvironmentIfInstalled(environment, rootPath);

                Assert.That(applied, Is.True);
                Assert.That(environment["MYCODEX_OFFICECLI_PATH"], Is.EqualTo(Path.Combine(officeCliDirectory, "officecli.exe")));
                Assert.That(environment["PATH"].Split(Path.PathSeparator)[0], Is.EqualTo(officeCliDirectory));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void GetStatus_WhenNewerBundledRuntimeInstalled_UsesNewestVersion()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);
                CreateBundledExecutable(service, "1.0.0", "x86_64-pc-windows-msvc");
                CreateBundledExecutable(service, "9.0.0", "x86_64-pc-windows-msvc");
                CreateBundledExecutable(service, "1.0.0", "aarch64-pc-windows-msvc");
                CreateBundledExecutable(service, "9.0.0", "aarch64-pc-windows-msvc");

                var manager = new CodexRuntimeManager(service);
                var status = manager.GetStatus(new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.Bundled
                });

                Assert.That(status.CanLaunch, Is.True);
                Assert.That(status.Version, Is.EqualTo("9.0.0"));
                Assert.That(status.BundledExecutablePath, Does.Contain("9.0.0"));
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResolveLaunchInfo_SeparateTargetsReuseTheSameBundledInstallation(bool legacyLayout)
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var installationRoot = Path.Combine(rootPath, "installation");
                var sharedRoot = Path.Combine(rootPath, "channel");
                var first = new AppConfigService(Path.Combine(sharedRoot, "targets", "first"), installationRoot, "MyCodex", sharedRoot);
                var second = new AppConfigService(Path.Combine(sharedRoot, "targets", "second"), installationRoot, "MyCodex", sharedRoot);
                foreach (var targetTriple in new[] { "x86_64-pc-windows-msvc", "aarch64-pc-windows-msvc" })
                {
                    var executablePath = Path.Combine(first.GetBundledCodexInstallDirectory("9.0.0", targetTriple),
                        "vendor", targetTriple, legacyLayout ? "codex" : "bin", "codex.exe");
                    Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
                    File.WriteAllText(executablePath, string.Empty);
                }
                var config = new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Bundled };

                var firstLaunch = new CodexRuntimeManager(first).ResolveLaunchInfo(config);
                var secondLaunch = new CodexRuntimeManager(second).ResolveLaunchInfo(config);

                Assert.That(firstLaunch.FileName, Is.EqualTo(secondLaunch.FileName));
                Assert.That(firstLaunch.FileName, Does.StartWith(installationRoot + Path.DirectorySeparatorChar));
                Assert.That(firstLaunch.EnvironmentVariables["MYCODEX_CODEX_VERSION"], Is.EqualTo("9.0.0"));
                Assert.That(secondLaunch.EnvironmentVariables.ContainsKey("CODEX_HOME"), Is.False);
                Assert.That(Directory.Exists(first.GetAppDataDirectory()), Is.False);
                Assert.That(Directory.Exists(second.GetAppDataDirectory()), Is.False);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

    [Test]
    public void GetStatus_WhenRemotePasswordRuntimeSelected_DoesNotCreatePasswordTempFile()
    {
        var rootPath = CreateTempDirectory();
        var before = GetPasswordTempFiles();
            var created = new List<string>();
            try
            {
                var plinkPath = Path.Combine(rootPath, "plink.exe");
                File.WriteAllText(plinkPath, string.Empty);

                var manager = new CodexRuntimeManager(new AppConfigService(rootPath));
                var status = manager.GetStatus(new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                    SelectedRemoteUnixSshProfileId = "mac-mini",
                    RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                    {
                        new RemoteUnixSshProfileConfigSnapshot
                        {
                            Id = "mac-mini",
                            Ssh = new RemoteMacSshConfigSnapshot
                            {
                                HostName = "192.0.2.10",
                                UserName = "tester",
                                AuthenticationMode = RemoteSshAuthenticationMode.Password,
                                ProtectedPassword = RemoteSshPasswordProtector.Protect("secret"),
                                PlinkExecutablePath = plinkPath
                            }
                        }
                    }
                });

                created.AddRange(GetNewPasswordTempFiles(before));
                Assert.That(status.CanLaunch, Is.True);
                Assert.That(created, Is.Empty);
            }
            finally
            {
                foreach (var path in created)
                {
                    TryDeleteFile(path);
                }

                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolveLaunchInfo_WhenBundledSelected_DoesNotMaterializeRemoteSensitiveFiles()
        {
            var rootPath = CreateTempDirectory();
            var before = GetSshTempFiles();
            var created = new List<string>();
            try
            {
                var service = new AppConfigService(rootPath);
                CreateBundledExecutable(service, "1.0.0", "x86_64-pc-windows-msvc");
                CreateBundledExecutable(service, "1.0.0", "aarch64-pc-windows-msvc");
                var plinkPath = Path.Combine(rootPath, "plink.exe");
                File.WriteAllText(plinkPath, string.Empty);
                var manager = new CodexRuntimeManager(service);

                var launchInfo = manager.ResolveLaunchInfo(new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.Bundled,
                    SelectedRemoteUnixSshProfileId = "remote",
                    RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                    {
                        new RemoteUnixSshProfileConfigSnapshot
                        {
                            Id = "remote",
                            Ssh = new RemoteMacSshConfigSnapshot
                            {
                                HostName = "192.0.2.10",
                                UserName = "tester",
                                AuthenticationMode = RemoteSshAuthenticationMode.Password,
                                ProtectedPassword = RemoteSshPasswordProtector.Protect("secret"),
                                PlinkExecutablePath = plinkPath
                            }
                        }
                    }
                });

                created.AddRange(GetNewSshTempFiles(before));
                Assert.That(
                    launchInfo.EnvironmentVariables["MYCODEX_CODEX_SOURCE"],
                    Is.EqualTo(CodexRuntimeSources.Bundled));
                Assert.That(created, Is.Empty);
                Assert.That(GetSshTempFiles(service.GetTempDirectory()), Is.Empty);
            }
            finally
            {
                foreach (var path in created)
                {
                    TryDeleteFile(path);
                }

                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolveLaunchInfo_WhenRemotePasswordSelected_UsesConfiguredTempDirectory()
        {
            var rootPath = CreateTempDirectory();
            AppServerLaunchInfo launchInfo = null;
            try
            {
                var service = new AppConfigService(rootPath);
                var plinkPath = Path.Combine(rootPath, "plink.exe");
                File.WriteAllText(plinkPath, string.Empty);
                var manager = new CodexRuntimeManager(service);

                launchInfo = manager.ResolveLaunchInfo(new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                    SelectedRemoteUnixSshProfileId = "remote",
                    RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                    {
                        new RemoteUnixSshProfileConfigSnapshot
                        {
                            Id = "remote",
                            Ssh = new RemoteMacSshConfigSnapshot
                            {
                                HostName = "192.0.2.10",
                                UserName = "tester",
                                AuthenticationMode = RemoteSshAuthenticationMode.Password,
                                ProtectedPassword = RemoteSshPasswordProtector.Protect("secret"),
                                PlinkExecutablePath = plinkPath
                            }
                        }
                    }
                });

                Assert.That(launchInfo.SensitiveTempFilePaths, Has.Count.EqualTo(2));
                Assert.That(launchInfo.SensitiveTempFilePaths, Is.All.StartsWith(
                    service.GetTempDirectory() + Path.DirectorySeparatorChar));
                Assert.That(launchInfo.SensitiveTempFilePaths.All(File.Exists), Is.True);
            }
            finally
            {
                if (launchInfo != null)
                {
                    foreach (var path in launchInfo.SensitiveTempFilePaths)
                    {
                        TryDeleteFile(path);
                    }
                }

                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolveLaunchInfo_WhenRemoteMaterializationFails_CleansCreatedCommandFile()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var service = new AppConfigService(rootPath);
                var plinkPath = Path.Combine(rootPath, "plink.exe");
                File.WriteAllText(plinkPath, string.Empty);
                var manager = new CodexRuntimeManager(service);
                var config = new AppConfigSnapshot
                {
                    CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                    SelectedRemoteUnixSshProfileId = "remote",
                    RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                    {
                        new RemoteUnixSshProfileConfigSnapshot
                        {
                            Id = "remote",
                            Ssh = new RemoteMacSshConfigSnapshot
                            {
                                HostName = "192.0.2.10",
                                UserName = "tester",
                                AuthenticationMode = RemoteSshAuthenticationMode.Password,
                                ProtectedPassword = "invalid-protected-password",
                                PlinkExecutablePath = plinkPath
                            }
                        }
                    }
                };

                Assert.Throws<CodexRuntimeUnavailableException>(() => manager.ResolveLaunchInfo(config));
                Assert.That(GetSshTempFiles(service.GetTempDirectory()), Is.Empty);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CleanupStaleFiles_DoesNotDeleteOtherDataRootFiles(bool targetScoped)
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var stableRoot = Path.Combine(rootPath, "stable");
                var devRoot = Path.Combine(rootPath, "dev");
                var stableTemp = (targetScoped
                    ? new AppConfigService(stableRoot, AppDomain.CurrentDomain.BaseDirectory, "MyCodex", rootPath)
                    : new AppConfigService(stableRoot)).GetTempDirectory();
                var devTemp = (targetScoped
                    ? new AppConfigService(devRoot, AppDomain.CurrentDomain.BaseDirectory, "MyCodex", rootPath)
                    : new AppConfigService(devRoot)).GetTempDirectory();
                var stablePasswordPath = RemoteSshPasswordFile.Create(stableTemp, "stable-secret");
                var stableCommandPath = RemoteSshSensitiveCommandFile.CreateCommandFile(stableTemp, "stable-command");
                var devPasswordPath = RemoteSshPasswordFile.Create(devTemp, "dev-secret");
                var devCommandPath = RemoteSshSensitiveCommandFile.CreateCommandFile(devTemp, "dev-command");

                RemoteSshPasswordFile.CleanupStaleFiles(devTemp, TimeSpan.Zero);
                RemoteSshSensitiveCommandFile.CleanupStaleFiles(devTemp, TimeSpan.Zero);

                Assert.That(File.Exists(stablePasswordPath), Is.True);
                Assert.That(File.Exists(stableCommandPath), Is.True);
                Assert.That(File.Exists(devPasswordPath), Is.False);
                Assert.That(File.Exists(devCommandPath), Is.False);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        [NonParallelizable]
        public void ConfigureSensitiveFileDirectory_RoutesLegacyOverloadsToCurrentChannel()
        {
            var rootPath = CreateTempDirectory();
            var originalDirectory = RemoteSshSensitiveFileDirectory.GetDirectory();
            string passwordPath = null;
            string commandPath = null;
            try
            {
                var channelTemp = Path.Combine(rootPath, "MyCodex-Dev", "temp");
                RemoteSshSensitiveFileDirectory.Configure(channelTemp);

                passwordPath = RemoteSshPasswordFile.Create("dev-secret");
                commandPath = RemoteSshSensitiveCommandFile.CreateCommandFile("dev-command");

                Assert.That(passwordPath, Does.StartWith(channelTemp + Path.DirectorySeparatorChar));
                Assert.That(commandPath, Does.StartWith(channelTemp + Path.DirectorySeparatorChar));
            }
            finally
            {
                TryDeleteFile(passwordPath);
                TryDeleteFile(commandPath);
                RemoteSshSensitiveFileDirectory.Configure(originalDirectory);
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void StartProcess_WhenStartInfoConstructionFails_DeletesSensitiveFiles()
        {
            var rootPath = CreateTempDirectory();
            try
            {
                var sensitivePath = RemoteSshPasswordFile.Create(
                    Path.Combine(rootPath, "temp"),
                    "secret");
                var launchInfo = new AppServerLaunchInfo(
                    "not-used.exe",
                    string.Empty,
                    new Dictionary<string, string>(),
                    AppServerHostPlatform.Windows,
                    new[] { sensitivePath });

                using (var coordinator = new AppServerRuntimeStartupCoordinator(
                    new WslNetworkReadinessProbe(),
                    null))
                {
                    Assert.Throws<IOException>(() => coordinator.StartProcess(
                        new AppConfigSnapshot(),
                        launchInfo,
                        null,
                        (info, environmentVariableNames) =>
                        {
                            throw new IOException("Injected start-info construction failure.");
                        }));
                }

                Assert.That(File.Exists(sensitivePath), Is.False);
            }
            finally
            {
                DeleteDirectory(rootPath);
            }
        }

        [Test]
        public void ResolveLaunchInfo_WhenRemoteRuntimeSelected_DoesNotPutAuthenticationSecretInArguments()
        {
            var rootPath = CreateTempDirectory();
            AppServerLaunchInfo launchInfo = null;
            try
            {
                var service = new AppConfigService(rootPath);
                var manager = new CodexRuntimeManager(service);
                launchInfo = manager.ResolveLaunchInfo(
                    new AppConfigSnapshot
                    {
                        CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh,
                        SelectedRemoteUnixSshProfileId = "mac-mini",
                        RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
                        {
                            new RemoteUnixSshProfileConfigSnapshot
                            {
                                Id = "mac-mini",
                                Ssh = new RemoteMacSshConfigSnapshot
                                {
                                    HostName = "192.0.2.10",
                                    UserName = "tester",
                                    AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey
                                }
                            }
                        }
                    },
                    new Dictionary<string, string>
                    {
                        { "MYCODEX_COMPAT_API_KEY", "sk-test" }
                    });

                Assert.That(launchInfo.EnvironmentVariables["MYCODEX_COMPAT_API_KEY"], Is.EqualTo("sk-test"));
                Assert.That(launchInfo.Arguments, Does.Not.Contain("MYCODEX_COMPAT_API_KEY"));
                Assert.That(launchInfo.Arguments, Does.Not.Contain("sk-test"));
                Assert.That(launchInfo.Arguments, Does.Not.Contain("codex app-server --enable goals --listen stdio://"));
                Assert.That(launchInfo.SensitiveTempFilePaths, Is.Not.Empty);
                Assert.That(launchInfo.SensitiveTempFilePaths, Is.All.StartsWith(
                    service.GetTempDirectory() + Path.DirectorySeparatorChar));
                Assert.That(launchInfo.SensitiveTempFilePaths.Any(File.Exists), Is.True);
            }
            finally
            {
                if (launchInfo != null)
                {
                    foreach (var path in launchInfo.SensitiveTempFilePaths)
                    {
                        TryDeleteFile(path);
                    }
                }

                DeleteDirectory(rootPath);
            }
        }


        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "MyCodex-CodexRuntimeTests-" + Guid.NewGuid().ToString("N"));
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

        private static void CreateBundledExecutable(AppConfigService service, string version, string targetTriple)
        {
            var executablePath = service.GetBundledCodexExecutablePath(version, targetTriple);
            Directory.CreateDirectory(Path.GetDirectoryName(executablePath));
            File.WriteAllText(executablePath, string.Empty);
        }

        private static string[] GetPasswordTempFiles()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyCodex",
                "temp");
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "mycodex-ssh-password-*.tmp")
                : new string[0];
        }

        private static IEnumerable<string> GetNewPasswordTempFiles(IEnumerable<string> before)
        {
            var beforeSet = new HashSet<string>(before ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (var path in GetPasswordTempFiles())
            {
                if (!beforeSet.Contains(path))
                {
                    yield return path;
                }
            }
        }

        private static string[] GetSshTempFiles()
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyCodex",
                "temp");
            return GetSshTempFiles(directory);
        }

        private static string[] GetSshTempFiles(string directory)
        {
            return Directory.Exists(directory)
                ? Directory.GetFiles(directory, "mycodex-ssh-*.tmp")
                : new string[0];
        }

        private static IEnumerable<string> GetNewSshTempFiles(IEnumerable<string> before)
        {
            var beforeSet = new HashSet<string>(before ?? new string[0], StringComparer.OrdinalIgnoreCase);
            foreach (var path in GetSshTempFiles())
            {
                if (!beforeSet.Contains(path))
                {
                    yield return path;
                }
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }
}
