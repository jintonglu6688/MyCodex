using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseBundleTests
    {
        private const string Id = "windows-x64-test";
        private const string Version = "1.0";
        private string installation;
        private string root;
        private string home;
        private string manifestHash;
        private ComputerUseBundle bundle;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        [SetUp]
        public void SetUp()
        {
            installation = Path.Combine(TestContext.CurrentContext.WorkDirectory, "cua bundle 中文-" + Guid.NewGuid().ToString("N"));
            root = Path.Combine(installation, "runtime", "cua", Id);
            home = Path.Combine(installation, "home");
            Write("marketplace/.agents/plugins/marketplace.json", "{}");
            Write("marketplace/plugins/computer-use/.codex-plugin/plugin.json", "{}");
            Write("marketplace/plugins/computer-use/skills/computer-use/SKILL.md", "official fixture");
            Write("cua_node/bin/node.exe", "runtime fixture");
            var entries = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(path => new
            {
                path = path.Substring(root.Length + 1).Replace('\\', '/'),
                size = new FileInfo(path).Length,
                sha256 = Hash(path)
            }).ToArray();
            Write("bundle.json", json.Serialize(new { schemaVersion = 1, bundleId = Id, pluginVersion = Version, files = entries }));
            manifestHash = Hash(Path.Combine(root, "bundle.json"));
            bundle = ComputerUseBundle.Load(installation, Id, manifestHash);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(installation)) Directory.Delete(installation, true);
        }

        [Test]
        public void Load_UsesInstallationOwnedPathsAndPinnedInventory()
        {
            Assert.That(bundle.RuntimeDirectory, Is.EqualTo(root));
            Assert.That(bundle.PluginRoot, Does.StartWith(root));
            Assert.Throws<InvalidDataException>(() => ComputerUseBundle.Load(installation, "../escape", manifestHash));
            Assert.Throws<InvalidDataException>(() => ComputerUseBundle.Load(installation, Id, new string('0', 64)));
            Assert.Throws<DirectoryNotFoundException>(() => ComputerUseBundle.Load(installation, "missing", manifestHash));
        }

        [TestCase("change")]
        [TestCase("extra")]
        [TestCase("missing")]
        public void Load_RejectsChangedMissingAndExtraFiles(string change)
        {
            if (change == "change") Write("cua_node/bin/node.exe", "runtime changed");
            else if (change == "extra") Write("unreviewed.js", "extra");
            else File.Delete(Path.Combine(root, "cua_node", "bin", "node.exe"));
            var error = Assert.Catch(() => ComputerUseBundle.Load(installation, Id, manifestHash));
            Assert.That(error is InvalidDataException || error is IOException, Is.True);
        }

        [TestCase("../outside")]
        [TestCase("/absolute")]
        [TestCase("C:/absolute")]
        [TestCase("marketplace\\linked")]
        public void Load_RejectsUnsafeInventoryPathsEvenWithMatchingManifestPin(string path)
        {
            Write("bundle.json", json.Serialize(new
            {
                schemaVersion = 1, bundleId = Id, pluginVersion = Version,
                files = new[] { new { path, size = 1, sha256 = new string('0', 64) } }
            }));
            Assert.Throws<InvalidDataException>(() => ComputerUseBundle.Load(installation, Id, Hash(Path.Combine(root, "bundle.json"))));
        }

        [Test]
        public async Task PluginPreparation_InstallsOwnSourceOnceThenVerifiesCacheOnReuse()
        {
            var requests = new List<string>();
            var installed = false;
            var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
            {
                requests.Add(method);
                if (method == "marketplace/add")
                {
                    Assert.That(parameters["source"], Is.EqualTo(bundle.MarketplaceRoot));
                    return Reply(new Dictionary<string, object> { { "marketplaceName", ComputerUseBundle.MarketplaceName } });
                }
                if (method == "plugin/install")
                {
                    Assert.That(parameters["marketplacePath"], Is.EqualTo(bundle.MarketplacePath));
                    Assert.That(parameters["pluginName"], Is.EqualTo("computer-use"));
                    CopyPlugin();
                    installed = true;
                    return Reply(new Dictionary<string, object>());
                }
                return Reply(Snapshot(installed, true, Version, bundle.PluginRoot));
            });
            Assert.That(await preparation.EnsureAsync(bundle, home, CancellationToken.None, true), Is.True);
            Assert.That(requests, Is.EqualTo(new[] { "plugin/installed", "marketplace/add", "plugin/install", "plugin/installed" }));
            requests.Clear();
            Assert.That(await preparation.EnsureAsync(bundle, home, CancellationToken.None, true), Is.True);
            Assert.That(requests, Is.EqualTo(new[] { "plugin/installed" }));
            File.AppendAllText(Path.Combine(CacheRoot(), "skills", "computer-use", "SKILL.md"), "tampered");
            Assert.ThrowsAsync<InvalidDataException>(() => preparation.EnsureAsync(bundle, home, CancellationToken.None, false));
        }

        [TestCase("disabled")]
        [TestCase("wrong-source")]
        public void PluginPreparation_RefusesDisabledOrUnverifiedInstalledPluginWithoutMutation(string state)
        {
            CopyPlugin();
            var requests = new List<string>();
            var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
            {
                requests.Add(method);
                return Reply(Snapshot(true, state != "disabled", state == "wrong-version" ? "99" : Version,
                    state == "wrong-source" ? installation : bundle.PluginRoot));
            });
            if (state == "disabled")
                Assert.That(preparation.EnsureAsync(bundle, home, CancellationToken.None, true).GetAwaiter().GetResult(), Is.False);
            else Assert.ThrowsAsync<InvalidDataException>(() => preparation.EnsureAsync(bundle, home, CancellationToken.None, true));
            Assert.That(requests, Is.EqualTo(new[] { "plugin/installed" }));
        }

        [TestCase("relocated")]
        [TestCase("extended")]
        [TestCase("upgrade")]
        [TestCase("same-directory-upgrade")]
        [TestCase("missing-source")]
        public async Task PluginPreparation_RebindsBundledSourceToCurrentApplicationDirectory(string state)
        {
            CopyPlugin();
            var upgrade = state.EndsWith("upgrade", StringComparison.Ordinal);
            if (upgrade) Directory.Move(CacheRoot(), Path.Combine(Path.GetDirectoryName(CacheRoot()), "0.9"));
            var oldMarketplace = Path.Combine(installation, "old-install", "runtime", "cua", "previous-bundle", "marketplace");
            if (state == "same-directory-upgrade") oldMarketplace = bundle.MarketplaceRoot;
            var oldPlugin = Path.Combine(oldMarketplace, "plugins", "computer-use");
            if (state == "extended")
            {
                oldMarketplace = @"\\?\" + oldMarketplace;
                oldPlugin = @"\\?\" + oldPlugin;
            }
            var config = new Dictionary<string, object>
            {
                { "marketplaces", new Dictionary<string, object>
                    { { ComputerUseBundle.MarketplaceName, new Dictionary<string, object>
                        { { "source_type", "local" }, { "source", oldMarketplace } } } } }
            };
            var requests = new List<string>();
            var current = false;
            var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
            {
                requests.Add(method);
                if (method == "marketplace/remove")
                {
                    Assert.That(parameters["marketplaceName"], Is.EqualTo(ComputerUseBundle.MarketplaceName));
                    return Reply(new Dictionary<string, object>());
                }
                if (method == "marketplace/add")
                {
                    Assert.That(parameters["source"], Is.EqualTo(bundle.MarketplaceRoot));
                    return Reply(new Dictionary<string, object> { { "marketplaceName", ComputerUseBundle.MarketplaceName } });
                }
                if (method == "plugin/install")
                {
                    Assert.That(parameters["marketplacePath"], Is.EqualTo(bundle.MarketplacePath));
                    if (upgrade) CopyPlugin();
                    current = true;
                    return Reply(new Dictionary<string, object>());
                }
                if (!current && state == "missing-source")
                    return Reply(new Dictionary<string, object>
                    {
                        { "marketplaces", new object[0] },
                        { "marketplaceLoadErrors", new object[] { new Dictionary<string, object>
                            { { "marketplacePath", Path.Combine(oldMarketplace, ".agents", "plugins", "marketplace.json") },
                              { "message", "Old installation no longer exists" } } } }
                    });
                return Reply(Snapshot(true, true, !current && upgrade ? "0.9" : Version,
                    current ? bundle.PluginRoot : oldPlugin));
            });
            Assert.That(await preparation.EnsureAsync(bundle, home, CancellationToken.None, false, config),
                Is.EqualTo(state == "relocated" || state == "extended"));
            Assert.That(requests, Is.EqualTo(new[] { "plugin/installed" }));
            requests.Clear();
            Assert.That(await preparation.EnsureAsync(bundle, home, CancellationToken.None, true, config), Is.True);
            var expected = new[] { "plugin/installed", "marketplace/remove", "marketplace/add", "plugin/install", "plugin/installed" };
            Assert.That(requests, Is.EqualTo(state == "same-directory-upgrade"
                ? expected.Where(method => method != "marketplace/remove") : expected));
        }

        [Test]
        public async Task PluginPreparation_ReusesNormalizedCurrentSourceWithoutRegistrationChanges()
        {
            CopyPlugin();
            var requests = new List<string>();
            var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
            {
                requests.Add(method);
                return Reply(Snapshot(true, true, Version, @"\\?\" + bundle.PluginRoot + "\\"));
            });
            Assert.That(await preparation.EnsureAsync(bundle, home, CancellationToken.None, true), Is.True);
            Assert.That(requests, Is.EqualTo(new[] { "plugin/installed" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PluginPreparation_DoesNotMigrateTamperedCacheOrUnknownRegistration(bool unknownRegistration)
        {
            CopyPlugin();
            var source = Path.Combine(installation, "old-install", "runtime", "cua", Id, "marketplace");
            var pluginSource = Path.Combine(source, "plugins", "computer-use");
            var config = new Dictionary<string, object>
            {
                { "marketplaces", new Dictionary<string, object>
                    { { ComputerUseBundle.MarketplaceName, new Dictionary<string, object>
                        { { "source_type", unknownRegistration ? "git" : "local" }, { "source", source } } } } }
            };
            if (!unknownRegistration)
                File.AppendAllText(Path.Combine(CacheRoot(), "skills", "computer-use", "SKILL.md"), "tampered");
            var requests = new List<string>();
            var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
            {
                requests.Add(method);
                return Reply(Snapshot(true, true, Version, pluginSource));
            });
            Assert.ThrowsAsync<InvalidDataException>(() => preparation.EnsureAsync(bundle, home, CancellationToken.None, true, config));
            Assert.That(requests, Is.EqualTo(new[] { "plugin/installed" }));
        }

        [Test]
        public async Task PluginPreparation_PreservesDisableWhenOldMarketplaceCannotBeLoaded()
        {
            var config = new Dictionary<string, object>
            {
                { "plugins", new Dictionary<string, object>
                    { { ComputerUseBundle.PluginId, new Dictionary<string, object> { { "enabled", false } } } } }
            };
            var requests = new List<string>();
            var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
            {
                requests.Add(method);
                return Reply(Snapshot(false, true, Version, bundle.PluginRoot));
            });
            Assert.That(await preparation.EnsureAsync(bundle, home, CancellationToken.None, true, config), Is.False);
            Assert.That(requests, Is.EqualTo(new[] { "plugin/installed" }));
        }

        [Test]
        public void PluginPreparation_CancellationAfterRemovingOldRegistrationDoesNotInstall()
        {
            CopyPlugin();
            using (var cancellation = new CancellationTokenSource())
            {
                var requests = new List<string>();
                var oldPlugin = Path.Combine(installation, "old-install", "runtime", "cua", Id, "marketplace", "plugins", "computer-use");
                var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
                {
                    requests.Add(method);
                    if (method == "marketplace/remove")
                    {
                        cancellation.Cancel();
                        return Reply(new Dictionary<string, object>());
                    }
                    return Reply(Snapshot(true, true, Version, oldPlugin));
                });
                Assert.ThrowsAsync<OperationCanceledException>(() => preparation.EnsureAsync(bundle, home, cancellation.Token, true));
                Assert.That(requests, Is.EqualTo(new[] { "plugin/installed", "marketplace/remove" }));
            }
        }

        [Test]
        public void PluginPreparation_DoesNotContinueInstallationAfterCancellation()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var requests = new List<string>();
                var preparation = new ComputerUsePluginPreparation((method, parameters, token) =>
                {
                    requests.Add(method);
                    if (method == "marketplace/add")
                    {
                        cancellation.Cancel();
                        return Reply(new Dictionary<string, object> { { "marketplaceName", ComputerUseBundle.MarketplaceName } });
                    }
                    return Reply(Snapshot(false, true, Version, bundle.PluginRoot));
                });
                Assert.ThrowsAsync<OperationCanceledException>(() => preparation.EnsureAsync(bundle, home, cancellation.Token, true));
                Assert.That(requests, Does.Not.Contain("plugin/install"));
            }
        }

        private IDictionary<string, object> Snapshot(bool installed, bool enabled, string version, string source)
        {
            var plugin = new Dictionary<string, object>
            {
                { "id", ComputerUseBundle.PluginId }, { "name", "computer-use" }, { "installed", installed },
                { "enabled", enabled }, { "localVersion", version },
                { "source", new Dictionary<string, object> { { "type", "local" }, { "path", source } } }
            };
            return new Dictionary<string, object>
            {
                { "marketplaces", new object[] { new Dictionary<string, object>
                    { { "name", ComputerUseBundle.MarketplaceName }, { "plugins", installed ? new object[] { plugin } : new object[0] } } } }
            };
        }

        private void CopyPlugin()
        {
            foreach (var file in Directory.GetFiles(bundle.PluginRoot, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(CacheRoot(), file.Substring(bundle.PluginRoot.Length + 1));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target);
            }
        }

        private string CacheRoot() => Path.Combine(home, "plugins", "cache", ComputerUseBundle.MarketplaceName, "computer-use", Version);

        private void Write(string relative, string value)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, value);
        }

        private static Task<IDictionary<string, object>> Reply(IDictionary<string, object> value) => Task.FromResult(value);

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }
    }
}
