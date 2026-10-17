using System;
using System.IO;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetPreferenceIsolationTests
    {
        [Test]
        public async Task SavingWindowsPreferencesAndOfficeRootKeepsWslSnapshotAndRuntimeRoot()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "preferences-" + Guid.NewGuid().ToString("N"));
            var legacy = new AppConfigService(root);
            new TargetDataMigration(legacy).Run();
            var catalog = new TargetCatalogStore(legacy);
            var linux = catalog.AddWsl("Ubuntu", new WslAppServerConfigSnapshot { DistributionName = "Ubuntu" });
            var windowsPaths = catalog.GetPaths("windows");
            var linuxPaths = catalog.GetPaths(linux.Id);
            var windowsStore = new JsonAppConfigStore(windowsPaths);
            var linuxStore = new JsonAppConfigStore(linuxPaths);
            var linuxConfig = linuxStore.Load();
            linuxConfig.UiLanguagePreference = "zh-CN";
            linuxConfig.UiThemePreference = "dark";
            linuxConfig.TurnCompletedNotificationMode = NotificationPromptMode.Off;
            linuxConfig.EnableAutomaticChatReading = true;
            linuxConfig.OfficeShowActivityPane = false;
            linuxConfig.ScreenReaderIndependentChannelName = "Linux replies";
            linuxStore.Save(linuxConfig);
            var linuxBytes = File.ReadAllBytes(linuxPaths.GetConfigFilePath());

            var runtime = new ModeRuntimeStore();
            var linuxModeStore = new JsonModeSettingsStore(linuxPaths, runtime);
            await linuxModeStore.SaveForCurrentRuntimeAsync(linuxConfig, new ModeSettingsSnapshot
            {
                DefaultStartupMode = AppInteractionMode.Office,
                OfficeRootPath = "/home/linux/office"
            });
            var linuxModeContent = runtime.Content;
            var windowsConfig = windowsStore.Load();
            windowsConfig.UiLanguagePreference = "en-US";
            windowsConfig.UiThemePreference = "light";
            windowsConfig.EnableAutomaticChatReading = false;
            windowsConfig.ApprovalRequestNotificationMode = NotificationPromptMode.Off;
            windowsStore.Save(windowsConfig);
            var windowsModeStore = new JsonModeSettingsStore(windowsPaths);
            var officeRoot = Path.Combine(root, "windows-office");
            await windowsModeStore.SaveForCurrentRuntimeAsync(windowsConfig,
                new ModeSettingsSnapshot { OfficeRootPath = officeRoot });

            Assert.That(File.ReadAllBytes(linuxPaths.GetConfigFilePath()), Is.EqualTo(linuxBytes));
            Assert.That(linuxStore.Load().EnableAutomaticChatReading, Is.True);
            Assert.That(windowsStore.Load().UiLanguagePreference, Is.EqualTo("en-US"));
            Assert.That(windowsStore.Load().UiThemePreference, Is.EqualTo("light"));
            Assert.That((await windowsModeStore.LoadForCurrentRuntimeAsync(windowsConfig)).OfficeRootPath,
                Is.EqualTo(officeRoot));
            Assert.That((await linuxModeStore.LoadForCurrentRuntimeAsync(linuxConfig)).OfficeRootPath,
                Is.EqualTo("/home/linux/office"));
            Assert.That(runtime.Content, Is.EqualTo(linuxModeContent));
            Assert.That(runtime.LastDistribution, Is.EqualTo("Ubuntu"));
            Assert.That(File.Exists(linuxPaths.GetModeSettingsFilePath()), Is.False);
        }

        private sealed class ModeRuntimeStore : IRuntimeScopedConfigFileStore
        {
            public string Content { get; private set; }
            public string LastDistribution { get; private set; }

            public bool TryGetRuntimeModeSettingsFilePathForAsyncAccess(AppConfigSnapshot config, out string path)
            {
                path = "~/.codex/mycodex/mode-settings.json";
                return true;
            }

            public Task<Tuple<bool, string>> ReadTextFileAsync(AppConfigSnapshot config, string path)
            {
                LastDistribution = config.WslAppServer.DistributionName;
                return Task.FromResult(Tuple.Create(Content != null, Content));
            }

            public Task WriteTextFileAsync(AppConfigSnapshot config, string path, string content)
            {
                LastDistribution = config.WslAppServer.DistributionName;
                Content = content;
                return Task.CompletedTask;
            }

            public bool IsRuntimeEndpointStorageAvailableSynchronously(AppConfigSnapshot config) => true;
            public bool TryGetRuntimeAppStateFilePathForAsyncAccess(AppConfigSnapshot config, out string path) { path = null; return false; }
            public bool ReadTextFile(AppConfigSnapshot config, string path, out string content) { content = null; return false; }
            public void WriteTextFile(AppConfigSnapshot config, string path, string content) => throw new NotSupportedException();
            public void DeleteFile(AppConfigSnapshot config, string path) => throw new NotSupportedException();
            public Task DeleteFileAsync(AppConfigSnapshot config, string path) => throw new NotSupportedException();
        }
    }
}
