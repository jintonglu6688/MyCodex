using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.BrowserUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BrowserExtensionBridgeInstallerTests
    {
        private string _root;
        private FakeRegistration _registration;
        private BrowserExtensionBridgeInstaller _installer;
        private readonly byte[] _host = { 1, 2, 3, 4 };

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "bridge-install-" + Guid.NewGuid().ToString("N"));
            _registration = new FakeRegistration();
            _installer = new BrowserExtensionBridgeInstaller(_root, () => _host, _registration);
        }

        [TestCase(BrowserExtensionBrowser.Edge)]
        [TestCase(BrowserExtensionBrowser.Chrome)]
        public void FreshInstallPublishesOnlySelectedBrowserAfterFilesAndBackupExist(BrowserExtensionBrowser browser)
        {
            var other = browser == BrowserExtensionBrowser.Edge ? BrowserExtensionBrowser.Chrome : BrowserExtensionBrowser.Edge;
            var untouched = new BrowserNativeMessagingValue { Exists = true, Data = "other-client.json", Kind = RegistryValueKind.String };
            _registration.Values[other] = untouched;
            _registration.BeforeWrite = () => Assert.That(Directory.GetFiles(Path.Combine(_root, "MyCodex/browser-host/backups")), Has.Length.EqualTo(1));
            var backup = _installer.Install(browser, CancellationToken.None);
            var registration = _registration.Read(browser);
            var manifest = ReadJson((string)registration.Data);
            Assert.That(manifest["name"], Is.EqualTo("com.openai.codexextension"));
            Assert.That(manifest["type"], Is.EqualTo("stdio"));
            Assert.That(File.ReadAllBytes((string)manifest["path"]), Is.EqualTo(_host));
            Assert.That(((IList)manifest["allowed_origins"]).Cast<string>(), Is.EquivalentTo(
                WindowsBrowserExtensionSetupService.ExtensionIds.Select(id => "chrome-extension://" + id + "/")));
            Assert.That(ReadJson(backup)["valueExists"], Is.EqualTo(false));
            Assert.That(_registration.Read(other).Matches(untouched), Is.True);
            Assert.That(ReadJson(backup)["registryKey"], Is.EqualTo("HKEY_CURRENT_USER\\" + BrowserNativeMessagingRegistration.KeyPath(browser)));
        }

        [Test]
        public void RepairPreservesRawRegistrationKindAndNeverReplacesAnInUseOldBinary()
        {
            var original = new BrowserNativeMessagingValue { Exists = true, Data = @"%LOCALAPPDATA%\Other\host.json", Kind = RegistryValueKind.ExpandString };
            _registration.Values[BrowserExtensionBrowser.Edge] = original;
            var backup = _installer.Install(BrowserExtensionBrowser.Edge, CancellationToken.None);
            Assert.That(ReadJson(backup)["value"], Is.EqualTo(original.Data));
            Assert.That(ReadJson(backup)["valueKind"], Is.EqualTo("ExpandString"));
            var first = (string)_registration.Read(BrowserExtensionBrowser.Edge).Data;
            var exe = (string)ReadJson(first)["path"];
            using (File.Open(exe, FileMode.Open, FileAccess.Read, FileShare.Read))
                _installer.Install(BrowserExtensionBrowser.Edge, CancellationToken.None);
            Assert.That(_registration.Read(BrowserExtensionBrowser.Edge).Data, Is.Not.EqualTo(first));
            Assert.That(File.ReadAllBytes(exe), Is.EqualTo(_host));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FailureAfterPublishRestoresTheExactOriginalDefaultValue(bool existed)
        {
            var original = new BrowserNativeMessagingValue { Exists = existed, Data = existed ? "before.json" : null, Kind = existed ? RegistryValueKind.String : RegistryValueKind.Unknown };
            _registration.Values[BrowserExtensionBrowser.Edge] = original;
            _registration.FailAfterNextWrite = true;
            Assert.Throws<IOException>(() => _installer.Install(BrowserExtensionBrowser.Edge, CancellationToken.None));
            Assert.That(_registration.Read(BrowserExtensionBrowser.Edge).Matches(original), Is.True);
            Assert.That(Directory.GetFiles(Path.Combine(_root, "MyCodex/browser-host/backups")), Has.Length.EqualTo(1));
        }

        [Test]
        public void ConcurrentForeignRegistrationIsNotOverwritten()
        {
            _registration.ChangeOnSecondRead = true;
            Assert.Throws<IOException>(() => _installer.Install(BrowserExtensionBrowser.Edge, CancellationToken.None));
            Assert.That(_registration.Writes, Is.Zero);
            Assert.That(_registration.Read(BrowserExtensionBrowser.Edge).Data, Is.EqualTo("concurrent-client.json"));
        }

        [Test]
        public void InvalidPayloadAndCancellationLeaveRegistrationAndInstallationUntouched()
        {
            var invalid = new BrowserExtensionBridgeInstaller(_root, () => throw new InvalidDataException(), _registration);
            Assert.Throws<InvalidDataException>(() => invalid.Install(BrowserExtensionBrowser.Edge, CancellationToken.None));
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                Assert.Throws<OperationCanceledException>(() => _installer.Install(BrowserExtensionBrowser.Edge, cancellation.Token));
            }
            Assert.That(_registration.Writes, Is.Zero);
            Assert.That(Directory.Exists(_root), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => _installer.Install((BrowserExtensionBrowser)99, CancellationToken.None));
        }

        [TestCase("valid")]
        [TestCase("tampered")]
        [TestCase("extra-file")]
        [TestCase("escaped-id")]
        public void HostBundleEnforcesSizeHashContentsAndIdentity(string variant)
        {
            var directory = Path.Combine(_root, "runtime/browser-host/h1");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "extension-host.exe");
            File.WriteAllBytes(path, _host);
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(_host)).Replace("-", "");
            var pin = new BrowserExtensionHostBundle.Pin { bundleId = "h1", fileName = "extension-host.exe", size = _host.Length, sha256 = hash };
            if (variant == "tampered") File.WriteAllBytes(path, new byte[] { 4, 3, 2, 1 });
            if (variant == "extra-file") File.WriteAllText(Path.Combine(directory, "extra"), "fixture");
            if (variant == "escaped-id") pin.bundleId = "../h1";
            if (variant == "valid") Assert.That(BrowserExtensionHostBundle.Verify(_root, pin), Is.EqualTo(Path.GetFullPath(path)));
            else Assert.Throws<InvalidDataException>(() => BrowserExtensionHostBundle.Verify(_root, pin));
        }

        private static Dictionary<string, object> ReadJson(string path)
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
        }

        private sealed class FakeRegistration : IBrowserNativeMessagingRegistration
        {
            internal readonly Dictionary<BrowserExtensionBrowser, BrowserNativeMessagingValue> Values = new Dictionary<BrowserExtensionBrowser, BrowserNativeMessagingValue>();
            internal Action BeforeWrite;
            internal bool FailAfterNextWrite, ChangeOnSecondRead;
            internal int Writes;
            private int _reads;

            public BrowserNativeMessagingValue Read(BrowserExtensionBrowser browser)
            {
                if (++_reads == 2 && ChangeOnSecondRead)
                    Values[browser] = new BrowserNativeMessagingValue { Exists = true, Data = "concurrent-client.json", Kind = RegistryValueKind.String };
                return Values.TryGetValue(browser, out var value) ? value : new BrowserNativeMessagingValue();
            }

            public void Write(BrowserExtensionBrowser browser, BrowserNativeMessagingValue value)
            {
                BeforeWrite?.Invoke();
                Writes++;
                Values[browser] = value;
                if (FailAfterNextWrite) { FailAfterNextWrite = false; throw new IOException("fixture publish failed after writing"); }
            }
        }
    }
}
