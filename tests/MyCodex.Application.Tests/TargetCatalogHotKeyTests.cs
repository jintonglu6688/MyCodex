using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetCatalogHotKeyTests
    {
        [Test]
        public void DormantTargetConflict_ReportsItsConfiguredNameWithoutWritingAnyForeignFiles()
        {
            var store = CreateStore();
            var linux = store.AddWsl("Research Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            SaveHotKey(store, linux.Id, true, 3, 0x78);
            var paths = store.GetPaths(linux.Id);
            var before = File.ReadAllBytes(paths.GetConfigFilePath());
            var called = false;
            var conflict = Assert.Throws<TargetHotKeyConflictException>(() => store.WithHotKeyReservation("windows", true, 3, 0x78,
                () => { called = true; return true; }));
            Assert.That(conflict.Targets.Select(t => t.Name), Is.EqualTo(new[] { "Research Linux" }));
            Assert.That(called, Is.False);
            Assert.That(File.ReadAllBytes(paths.GetConfigFilePath()), Is.EqualTo(before));
            Assert.That(store.WithHotKeyReservation("windows", false, 3, 0x78, () => true), Is.True);
        }

        [Test]
        public void MigratedNullableEnabledFlag_RetainsOriginalTargetsLegacyBinding()
        {
            var store = CreateStore();
            var linux = store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var old = new JsonAppConfigStore(store.GetPaths("windows")).Load();
            Assert.That(old.MainWindowHotKeyEnabled, Is.Null);
            var conflict = Assert.Throws<TargetHotKeyConflictException>(() => store.WithHotKeyReservation(linux.Id, true,
                MainWindowHotKeyConfig.DefaultModifiers, MainWindowHotKeyConfig.DefaultVirtualKey, () => true));
            Assert.That(conflict.Targets.Single().Id, Is.EqualTo("windows"));
            Assert.That(new JsonAppConfigStore(store.GetPaths(linux.Id)).Load().MainWindowHotKeyEnabled, Is.False);
        }

        [Test]
        public void ConcurrentReservationAndSave_OnlyOneTargetReceivesTheKey()
        {
            var store = CreateStore();
            var linux = store.AddWsl("Linux", new WslAppServerConfigSnapshot { DistributionName = "Debian" });
            var tasks = new[] { "windows", linux.Id }.Select(id => Task.Run(() =>
            {
                try
                {
                    return store.WithHotKeyReservation(id, true, 3, 0x78, () =>
                    { SaveHotKey(store, id, true, 3, 0x78); return true; });
                }
                catch (TargetHotKeyConflictException) { return false; }
            })).ToArray();
            Task.WaitAll(tasks);
            Assert.That(tasks.Count(t => t.Result), Is.EqualTo(1));
            var assigned = new[] { "windows", linux.Id }.Select(id => new JsonAppConfigStore(store.GetPaths(id)).Load())
                .Count(config => config.MainWindowHotKeyEnabled == true && config.MainWindowHotKeyVirtualKey == 0x78);
            Assert.That(assigned, Is.EqualTo(1));
        }

        private static TargetCatalogStore CreateStore()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "target-hotkeys", Guid.NewGuid().ToString("N"));
            var legacy = new AppConfigService(root);
            new TargetDataMigration(legacy).Run();
            return new TargetCatalogStore(legacy);
        }

        private static void SaveHotKey(TargetCatalogStore store, string id, bool enabled, int modifiers, int key)
        {
            var configStore = new JsonAppConfigStore(store.GetPaths(id));
            var config = configStore.Load();
            config.MainWindowHotKeyEnabled = enabled;
            config.MainWindowHotKeyModifiers = modifiers;
            config.MainWindowHotKeyVirtualKey = key;
            configStore.Save(config);
        }
    }
}
