using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.BrowserUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class BrowserUseRuntimeBundleTests
    {
        private string installation;
        private string root;
        private string codex;
        private IDictionary<string, object> pin;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();

        [SetUp]
        public void SetUp()
        {
            installation = Path.Combine(TestContext.CurrentContext.WorkDirectory, "browser bundle 中文 " + Guid.NewGuid().ToString("N"));
            root = Path.Combine(installation, "runtime", "browser", "test-1");
            codex = Path.Combine(installation, "codex.exe");
            Write("bin/node.exe", "node fixture");
            Write("bin/node_repl.exe", "repl fixture");
            Write("bin/node_modules/@oai/cua-repl/bin/cua-repl.mjs", "repl entry");
            Write("bin/node_modules/@oai/browser-desktop/scripts/browser-service.mjs", "service entry");
            Write("bin/node_modules/@oai/browser-desktop/scripts/browser-client.mjs", "client entry");
            Write("bin/node_modules/@oai/cua-repl/package.json", "{\"name\":\"@oai/cua-repl\",\"version\":\"0.1.2\"}");
            Write("bin/node_modules/@oai/browser-desktop/package.json", "{\"name\":\"@oai/browser-desktop\",\"version\":\"0.1.1\"}");
            Write("manifest.json", json.Serialize(new
            {
                platform = "windows", arch = "x64", target = "windows-x64", runtime_archive_version = "runtime-test",
                node_version = "node-test", node_path = "bin/node.exe", node_repl_path = "bin/node_repl.exe", node_modules = "bin/node_modules"
            }));
            File.WriteAllText(codex, "selected codex fixture");
            pin = new Dictionary<string, object>
            {
                { "bundleId", "test-1" }, { "runtimeVersion", "runtime-test" }, { "nodeVersion", "node-test" },
                { "browserVersion", "0.1.1" }, { "cuaReplVersion", "0.1.2" }
            };
            Repin();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(installation)) Directory.Delete(installation, true);
        }

        [Test]
        public void Load_UsesOnlyReviewedInstallationFilesAndSelectedCodex()
        {
            var components = Load();
            Assert.That(components.RuntimeRoot, Is.EqualTo(root));
            Assert.That(components.NodePath, Is.EqualTo(Path.Combine(components.RuntimeRoot, "bin", "node.exe")));
            Assert.That(components.CuaReplPath, Does.StartWith(components.NodeModulesPath));
            Assert.That(components.BrowserServicePath, Does.StartWith(components.NodeModulesPath));
            Assert.That(components.SelectedCodexPath, Is.EqualTo(codex));
            Assert.That(components.SkyPipePath, Is.Null);
        }

        [TestCase("changed")]
        [TestCase("missing")]
        [TestCase("extra")]
        public void Load_RejectsUnreviewedPayload(string state)
        {
            if (state == "changed") Write("bin/node.exe", "changed");
            else if (state == "missing") File.Delete(Path.Combine(root, "bin", "node.exe"));
            else Write("unreviewed.mjs", "extra");
            var error = Assert.Catch(() => Load());
            Assert.That(error is InvalidDataException || error is IOException, Is.True);
        }

        [TestCase("../escape")]
        [TestCase(".")]
        [TestCase("C:/escape")]
        public void Load_RejectsUnsafeBundleIdentity(string value)
        {
            pin["bundleId"] = value;
            Assert.Throws<InvalidDataException>(() => Load());
        }

        [Test]
        public void Load_RejectsWrongManifestHashBeforeParsing()
        {
            pin["manifestSha256"] = new string('0', 64);
            Assert.Throws<InvalidDataException>(() => Load());
        }

        [TestCase("../outside")]
        [TestCase("C:/outside")]
        [TestCase("/absolute")]
        [TestCase("cua_node\\entry")]
        [TestCase("/entry")]
        public void Load_RejectsUnsafePathsEvenWithMatchingManifestPin(string value)
        {
            var manifest = Manifest(new[] { new { path = value, size = 1, sha256 = new string('0', 64) } });
            Write("bundle.json", json.Serialize(manifest));
            pin["manifestSha256"] = Hash(Path.Combine(root, "bundle.json"));
            Assert.Throws<InvalidDataException>(() => Load());
        }

        [Test]
        public void Load_RejectsChangedPackageMetadataEvenAfterInventoryRepin()
        {
            Write("bin/node_modules/@oai/browser-desktop/package.json", "{\"name\":\"@oai/browser-desktop\",\"version\":\"other\"}");
            Repin();
            Assert.Throws<InvalidDataException>(() => Load());
        }

        [Test]
        public void Load_RejectsLargeManifestAndMissingRequiredEntry()
        {
            Write("bundle.json", new string(' ', 4 * 1024 * 1024 + 1));
            pin["manifestSha256"] = Hash(Path.Combine(root, "bundle.json"));
            Assert.Throws<InvalidDataException>(() => Load());
            File.Delete(Path.Combine(root, "bin", "node_modules", "@oai", "cua-repl", "bin", "cua-repl.mjs"));
            Repin();
            Assert.Throws<InvalidDataException>(() => Load());
        }

        [Test]
        public void Load_RejectsLinkedEntryEvenAfterInventoryRepin()
        {
            var target = Path.Combine(installation, "link-target.mjs");
            File.WriteAllText(target, "linked code");
            if (!CreateSymbolicLink(Path.Combine(root, "link.mjs"), target, 2))
                Assert.Ignore("Creating an unprivileged test symlink is unavailable.");
            Repin();
            Assert.Throws<InvalidDataException>(() => Load());
        }

        [Test]
        public void Load_RejectsDuplicateInventoryAndWrongPlatform()
        {
            var entry = new { path = "bin/node.exe", size = new FileInfo(Path.Combine(root, "bin", "node.exe")).Length,
                sha256 = Hash(Path.Combine(root, "bin", "node.exe")) };
            Write("bundle.json", json.Serialize(Manifest(new[] { entry, entry })));
            pin["manifestSha256"] = Hash(Path.Combine(root, "bundle.json"));
            Assert.Throws<InvalidDataException>(() => Load());
            Write("manifest.json", "{\"platform\":\"linux\"}");
            Repin();
            Assert.Throws<InvalidDataException>(() => Load());
        }

        private BrowserUseComponents Load() => BrowserUseRuntimeBundle.Load(installation, codex, pin);

        private void Repin()
        {
            var entries = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != "bundle.json").Select(path => new
                {
                    path = path.Substring(root.Length + 1).Replace('\\', '/'), size = new FileInfo(path).Length, sha256 = Hash(path)
                }).ToArray();
            Write("bundle.json", json.Serialize(Manifest(entries)));
            pin["manifestSha256"] = Hash(Path.Combine(root, "bundle.json"));
        }

        private object Manifest(object entries) => new
        {
            schemaVersion = 1, bundleId = "test-1", runtimeVersion = "runtime-test", nodeVersion = "node-test",
            browserVersion = "0.1.1", cuaReplVersion = "0.1.2", files = entries
        };

        private void Write(string relative, string value)
        {
            var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, value);
        }

        private static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool CreateSymbolicLink(string link, string target, uint flags);
    }
}
