using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.BrowserUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BrowserExtensionSetupServiceTests
    {
        private string _root;
        private Dictionary<string, string> _registry;
        private WindowsBrowserExtensionSetupService _service;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser-setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _registry = new Dictionary<string, string>();
            _service = new WindowsBrowserExtensionSetupService(_root, key => _registry.TryGetValue(key, out var value) ? value : null);
        }

        [TestCase(BrowserExtensionBrowser.Edge)]
        [TestCase(BrowserExtensionBrowser.Chrome)]
        public async Task CleanMachineReportsMissingBridgeAndNoProfilesWithoutCreatingAnything(BrowserExtensionBrowser browser)
        {
            var report = await _service.CheckAsync(browser, CancellationToken.None);
            Assert.That(report.Bridge, Is.EqualTo(BrowserExtensionBridgeState.Missing));
            Assert.That(report.Profiles, Is.Empty);
            Assert.That(Directory.GetFileSystemEntries(_root), Is.Empty);
        }

        [TestCase("{\"state\":1}", BrowserExtensionInstallState.Enabled)]
        [TestCase("{}", BrowserExtensionInstallState.Enabled)]
        [TestCase("{\"state\":0}", BrowserExtensionInstallState.Disabled)]
        [TestCase("{\"disable_reasons\":[1]}", BrowserExtensionInstallState.Disabled)]
        [TestCase("{\"disable_reasons\":2}", BrowserExtensionInstallState.Disabled)]
        [TestCase("{\"disable_reasons\":0}", BrowserExtensionInstallState.Enabled)]
        [TestCase("{\"state\":\"enabled\"}", BrowserExtensionInstallState.Unknown)]
        [TestCase("{\"disable_reasons\":\"unknown\"}", BrowserExtensionInstallState.Unknown)]
        public async Task ProfileStateRespectsRegistrationAndDisableReasons(string entry, BrowserExtensionInstallState expected)
        {
            var profile = Profile(BrowserExtensionBrowser.Edge, "Default");
            WriteExtension(profile, 0);
            File.WriteAllText(Path.Combine(profile, "Secure Preferences"), Preferences(0, entry));
            var report = await _service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None);
            Assert.That(report.Profiles.Single().State, Is.EqualTo(expected));
        }

        [Test]
        public async Task ProfilesAreReportedSeparatelyAndOtherBrowserDataIsIgnored()
        {
            var first = Profile(BrowserExtensionBrowser.Chrome, "Default");
            var second = Profile(BrowserExtensionBrowser.Chrome, "Profile 2");
            File.WriteAllText(Path.Combine(first, "Preferences"), "{}");
            File.WriteAllText(Path.Combine(second, "Secure Preferences"), Preferences(1, "{\"state\":1}"));
            WriteExtension(second, 1);
            Profile(BrowserExtensionBrowser.Edge, "Default");
            Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(first), "Unrelated"));
            var files = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
            var report = await _service.CheckAsync(BrowserExtensionBrowser.Chrome, CancellationToken.None);
            Assert.That(report.Profiles.Select(item => item.ProfileDirectory), Is.EqualTo(new[] { "Default", "Profile 2" }));
            Assert.That(report.Profiles.Select(item => item.State), Is.EqualTo(new[] { BrowserExtensionInstallState.NotInstalled, BrowserExtensionInstallState.Enabled }));
            foreach (var file in files) Assert.That(File.ReadAllBytes(file.Key), Is.EqualTo(file.Value), "Detection must be read-only.");
            Assert.That(Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length, Is.EqualTo(files.Count));
        }

        [TestCase("broken")]
        [TestCase("missing-preferences")]
        [TestCase("unregistered-files")]
        [TestCase("missing-files")]
        [TestCase("invalid-schema")]
        public async Task UnreadableOrIncompleteInstallationIsUnknownInsteadOfInstalledOrMissing(string variant)
        {
            var profile = Profile(BrowserExtensionBrowser.Edge, "Default");
            if (variant == "broken") File.WriteAllText(Path.Combine(profile, "Secure Preferences"), "{invalid");
            if (variant == "invalid-schema") File.WriteAllText(Path.Combine(profile, "Preferences"), "{\"extensions\":\"invalid\"}");
            if (variant == "unregistered-files") { WriteExtension(profile, 0); File.WriteAllText(Path.Combine(profile, "Preferences"), "{}"); }
            if (variant == "missing-files") File.WriteAllText(Path.Combine(profile, "Preferences"), Preferences(0, "{\"state\":1}"));
            var report = await _service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None);
            Assert.That(report.Profiles.Single().State, Is.EqualTo(BrowserExtensionInstallState.Unknown));
        }

        [Test]
        public async Task SecurePreferencesOverrideOlderOrdinaryPreferences()
        {
            var profile = Profile(BrowserExtensionBrowser.Edge, "Default");
            WriteExtension(profile, 0);
            File.WriteAllText(Path.Combine(profile, "Secure Preferences"), Preferences(0, "{\"state\":0}"));
            File.WriteAllText(Path.Combine(profile, "Preferences"), Preferences(0, "{\"state\":1}"));
            var report = await _service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None);
            Assert.That(report.Profiles.Single().State, Is.EqualTo(BrowserExtensionInstallState.Disabled));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task UninstallInSecurePreferencesOverridesStaleFilesAndOrdinaryPreferences(bool staleOrdinaryRegistration)
        {
            var profile = Profile(BrowserExtensionBrowser.Edge, "Default");
            WriteExtension(profile, 0);
            File.WriteAllText(Path.Combine(profile, "Secure Preferences"), Preferences(0, "{\"state\":1}"));
            File.WriteAllText(Path.Combine(profile, "Preferences"), Preferences(0, "{\"state\":1}"));
            Assert.That((await _service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None)).Profiles.Single().State,
                Is.EqualTo(BrowserExtensionInstallState.Enabled));
            File.WriteAllText(Path.Combine(profile, "Secure Preferences"), "{\"extensions\":{\"settings\":{}}}");
            if (!staleOrdinaryRegistration)
                File.WriteAllText(Path.Combine(profile, "Preferences"), "{\"extensions\":{\"settings\":{}}}");
            Assert.That((await _service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None)).Profiles.Single().State,
                Is.EqualTo(BrowserExtensionInstallState.NotInstalled));
        }

        [TestCase("valid", BrowserExtensionBridgeState.Registered)]
        [TestCase("wrong-name", BrowserExtensionBridgeState.Invalid)]
        [TestCase("wrong-type", BrowserExtensionBridgeState.Invalid)]
        [TestCase("missing-origin", BrowserExtensionBridgeState.Invalid)]
        [TestCase("missing-host", BrowserExtensionBridgeState.Invalid)]
        [TestCase("missing-manifest", BrowserExtensionBridgeState.Invalid)]
        [TestCase("malformed", BrowserExtensionBridgeState.Unknown)]
        public async Task BridgeCheckValidatesRegistrationOriginsAndReferencedFile(string variant, BrowserExtensionBridgeState expected)
        {
            var path = Path.Combine(_root, "host.json");
            var exe = Path.Combine(_root, "host.exe");
            if (variant != "missing-host") File.WriteAllText(exe, "fixture; never executed");
            _registry["HKEY_CURRENT_USER\\Software\\Google\\Chrome\\NativeMessagingHosts\\com.openai.codexextension"] = path;
            var origins = WindowsBrowserExtensionSetupService.ExtensionIds.Select(id => "chrome-extension://" + id + "/").ToList();
            if (variant == "missing-origin") origins.RemoveAt(0);
            var manifest = new Dictionary<string, object>
            {
                { "name", variant == "wrong-name" ? "other" : "com.openai.codexextension" },
                { "type", variant == "wrong-type" ? "http" : "stdio" }, { "path", exe }, { "allowed_origins", origins }
            };
            if (variant != "missing-manifest") File.WriteAllText(path, variant == "malformed" ? "invalid" : new JavaScriptSerializer().Serialize(manifest));
            var report = await _service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None);
            Assert.That(report.Bridge, Is.EqualTo(expected));
        }

        [Test]
        public async Task EdgeSpecificRegistrationTakesPrecedenceAndPermissionErrorsAreUnknown()
        {
            _registry["HKEY_CURRENT_USER\\Software\\Microsoft\\Edge\\NativeMessagingHosts\\com.openai.codexextension"] = Path.Combine(_root, "absent.json");
            var report = await _service.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None);
            Assert.That(report.Bridge, Is.EqualTo(BrowserExtensionBridgeState.Invalid));
            var denied = new WindowsBrowserExtensionSetupService(_root, key => throw new SecurityException());
            Assert.That((await denied.CheckAsync(BrowserExtensionBrowser.Edge, CancellationToken.None)).Bridge, Is.EqualTo(BrowserExtensionBridgeState.Unknown));
        }

        [Test]
        public void CancellationAndInvalidBrowserDoNotInspectAnyInstallation()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.ThrowsAsync<TaskCanceledException>(() => _service.CheckAsync(BrowserExtensionBrowser.Edge, cancellation.Token));
            }
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _service.CheckAsync((BrowserExtensionBrowser)99, CancellationToken.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => WindowsBrowserExtensionSetupService.PageUrl(BrowserExtensionBrowser.Edge, (BrowserExtensionSetupPage)99));
        }

        [TestCase(BrowserExtensionBrowser.Edge, "microsoftedge.microsoft.com", "edge://extensions/")]
        [TestCase(BrowserExtensionBrowser.Chrome, "chromewebstore.google.com", "chrome://extensions/")]
        public void SetupPagesAreFixedOfficialStoreAndBrowserManagementDestinations(BrowserExtensionBrowser browser, string host, string management)
        {
            var store = new Uri(WindowsBrowserExtensionSetupService.PageUrl(browser, BrowserExtensionSetupPage.Store));
            Assert.That(store.Scheme, Is.EqualTo("https"));
            Assert.That(store.Host, Is.EqualTo(host));
            Assert.That(WindowsBrowserExtensionSetupService.PageUrl(browser, BrowserExtensionSetupPage.Extensions), Is.EqualTo(management));
        }

        private string Profile(BrowserExtensionBrowser browser, string name)
        {
            var path = Path.Combine(_root, browser == BrowserExtensionBrowser.Edge ? "Microsoft/Edge/User Data" : "Google/Chrome/User Data", name);
            Directory.CreateDirectory(path);
            return path;
        }

        private static void WriteExtension(string profile, int id)
        {
            var path = Path.Combine(profile, "Extensions", WindowsBrowserExtensionSetupService.ExtensionIds[id], "1.0");
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "manifest.json"), "{}");
        }

        private static string Preferences(int id, string entry)
        {
            return "{\"extensions\":{\"settings\":{\"" + WindowsBrowserExtensionSetupService.ExtensionIds[id] + "\":" + entry + "}}}";
        }
    }
}
