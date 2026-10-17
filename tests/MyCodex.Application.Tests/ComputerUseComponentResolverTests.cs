using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseComponentResolverTests
    {
        private string root;
        private string codexPath;
        private IDictionary<string, string> hashes;

        [SetUp]
        public void SetUp()
        {
            root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "cua-components-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            codexPath = Path.Combine(root, "selected-codex.exe");
            File.WriteAllText(codexPath, "selected CLI fixture: never executed");
            hashes = new Dictionary<string, string>
            {
                { "bin/node.exe", Hash("node fixture") },
                { "bin/node_repl.exe", Hash("node_repl fixture") },
                { ComputerUseComponentResolver.TransportRelativePath, Hash("transport fixture") },
                { ComputerUseComponentResolver.HelperRelativePath, Hash("helper fixture") }
            };
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase("0.158.0")]
        [TestCase("0.158.0-alpha.2.1")]
        [TestCase("0.159.1")]
        [TestCase("1.0.0-future")]
        public void VerifiedComponentsPreserveTheSelectedCli(string version)
        {
            var candidate = CreateCandidate("verified");
            var result = Resolve(version);

            Assert.That(result.Status, Is.EqualTo(ComputerUseComponentStatus.Ready));
            Assert.That(result.Components.RuntimeRoot, Is.EqualTo(candidate));
            Assert.That(result.Components.SelectedCodexPath, Is.EqualTo(codexPath));
            Assert.That(result.Components.SelectedCodexVersion, Is.EqualTo(version));
            Assert.That(File.Exists(result.Components.HelperPath), Is.True);
            Assert.That(File.ReadAllText(codexPath), Is.EqualTo("selected CLI fixture: never executed"));
        }

        [Test]
        public void MissingSelectedCliDoesNotUseAnyBundledAlternative()
        {
            CreateCandidate("verified");
            File.Delete(codexPath);
            Assert.That(Resolve().Status, Is.EqualTo(ComputerUseComponentStatus.SelectedRuntimeMissing));
        }

        [Test]
        public void EmptyCandidateDirectoryReportsMissing()
        {
            Assert.That(Resolve().Status, Is.EqualTo(ComputerUseComponentStatus.MissingComponents));
        }

        [Test]
        public void MissingEntryFailsClosed()
        {
            var candidate = CreateCandidate("broken");
            File.Delete(Path.Combine(candidate, "bin", "node_repl.exe"));
            var result = Resolve();
            Assert.That(result.Status, Is.EqualTo(ComputerUseComponentStatus.InvalidComponents));
            Assert.That(result.Components, Is.Null);
        }

        [Test]
        public void CorruptEntryFailsClosed()
        {
            var candidate = CreateCandidate("broken");
            File.AppendAllText(Path.Combine(candidate, "bin", "node.exe"), "corruption");
            Assert.That(Resolve().DetailCode, Is.EqualTo("entry-integrity-mismatch"));
        }

        [Test]
        public void ProductionResolverRejectsUnverifiedBytesEvenWithCorrectVersionMetadata()
        {
            CreateCandidate("fake");
            var result = new ComputerUseComponentResolver().Resolve(codexPath, "0.158.0", root);
            Assert.That(result.Status, Is.EqualTo(ComputerUseComponentStatus.InvalidComponents));
            Assert.That(result.Components, Is.Null);
        }

        [Test]
        public void ConflictingCopiesAreNotResolvedByTimestamps()
        {
            CreateCandidate("valid");
            var other = CreateCandidate("other");
            File.AppendAllText(Path.Combine(other, "bin", "node.exe"), "different content");
            Assert.That(Resolve().Status, Is.EqualTo(ComputerUseComponentStatus.ConflictingComponents));
        }

        [Test]
        public void IdenticalVerifiedCopiesHaveDeterministicSelection()
        {
            CreateCandidate("z-copy");
            var expected = CreateCandidate("a-copy");
            Assert.That(Resolve().Components.RuntimeRoot, Is.EqualTo(expected));
        }

        [TestCase("99.0.0/future")]
        [TestCase("0.0.24/20261001000000-new-build")]
        public void NewestVerifiedCandidateWinsAndOlderReferencesCanMigrate(string newerVersion)
        {
            var older = CreateCandidate("verified");
            var newer = CreateCandidate("latest");
            UpdateManifest(newer, "runtime_archive_version", newerVersion);
            Directory.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddDays(1));
            var result = Resolve();
            Assert.That(result.Components.RuntimeRoot, Is.EqualTo(newer));
            Assert.That(result.Components.StaleOfficialRuntimeRoots, Does.Contain(older));
        }

        [Test]
        public void VerifiedStaleReferencesAreReturnedForMigrationOnly()
        {
            var stale = Path.Combine(root, "stale");
            Write(stale, "manifest.json", "{\"runtime_archive_version\":\"old\"}");
            Write(stale, "bin/node_repl.exe", "old node_repl fixture");
            var staleHashes = new Dictionary<string, string>
            {
                { "manifest.json", Hash(File.ReadAllText(Path.Combine(stale, "manifest.json"))) },
                { "bin/node_repl.exe", Hash("old node_repl fixture") }
            };
            var resolver = new ComputerUseComponentResolver(hashes, staleHashes);
            Assert.That(resolver.Resolve(codexPath, "0.158.0", root).Components, Is.Null);

            var valid = CreateCandidate("verified");
            var result = resolver.Resolve(codexPath, "0.158.0", root);
            Assert.That(result.Components.RuntimeRoot, Is.EqualTo(valid));
            Assert.That(result.Components.StaleOfficialRuntimeRoots, Is.EqualTo(new[] { stale }));

            File.AppendAllText(Path.Combine(stale, "bin/node_repl.exe"), "changed");
            Assert.That(resolver.Resolve(codexPath, "0.158.0", root).Components.StaleOfficialRuntimeRoots, Is.Empty);
        }

        [TestCase("node_path", "../../outside.exe", ComputerUseComponentStatus.InvalidComponents)]
        [TestCase("arch", "arm64", ComputerUseComponentStatus.UnsupportedComponentVersion)]
        [TestCase("runtime_archive_version", "invalid", ComputerUseComponentStatus.UnsupportedComponentVersion)]
        public void UnverifiedManifestIsNotUsed(string key, string value, ComputerUseComponentStatus expected)
        {
            var candidate = CreateCandidate("invalid");
            UpdateManifest(candidate, key, value);
            Assert.That(Resolve().Status, Is.EqualTo(expected));
        }

        [TestCase("not JSON")]
        [TestCase("[]")]
        [TestCase("null")]
        public void InvalidManifestDoesNotEscapeAsAnException(string content)
        {
            var candidate = CreateCandidate("invalid");
            File.WriteAllText(Path.Combine(candidate, "manifest.json"), content);
            Assert.That(Resolve().Status, Is.EqualTo(ComputerUseComponentStatus.InvalidComponents));
        }

        private ComputerUseComponentResolution Resolve(string version = "0.158.0")
        {
            return new ComputerUseComponentResolver(hashes).Resolve(codexPath, version, root);
        }

        private string CreateCandidate(string name)
        {
            var candidate = Path.Combine(root, name);
            Write(candidate, "manifest.json", new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "platform", "windows" }, { "arch", "x64" }, { "target", "windows-x64" },
                { "node_version", "24.21.0-cua.1" }, { "node_binary_version", "24.21.0" },
                { "node_archive_path", "v24.21.0-cua.1/node-v24.21.0-win-x64.zip" },
                { "runtime_archive_name", "cua-node-0.0.24-20260924074400-f52ea85e2a98-windows-x64.zip" },
                { "runtime_archive_version", "0.0.24/20260924074400-f52ea85e2a98" },
                { "node_path", "bin/node.exe" }, { "node_repl_path", "bin/node_repl.exe" }, { "node_modules", "bin/node_modules" }
            }));
            Write(candidate, "bin/node.exe", "node fixture");
            Write(candidate, "bin/node_repl.exe", "node_repl fixture");
            Write(candidate, ComputerUseComponentResolver.TransportRelativePath, "transport fixture");
            Write(candidate, ComputerUseComponentResolver.SkyRoot + "dist/project/cua/sky_js/src/index.js", "index fixture");
            Write(candidate, ComputerUseComponentResolver.SkyRoot + "dist/project/cua/sky_js/src/service.js", "service fixture");
            Write(candidate, ComputerUseComponentResolver.HelperRelativePath, "helper fixture");
            Write(candidate, ComputerUseComponentResolver.SkyRoot + "package.json", "{\"name\":\"@oai/sky\",\"version\":\"0.7.4\"}");
            return candidate;
        }

        private static void UpdateManifest(string candidate, string key, string value)
        {
            var path = Path.Combine(candidate, "manifest.json");
            var serializer = new JavaScriptSerializer();
            var manifest = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            manifest[key] = value;
            File.WriteAllText(path, serializer.Serialize(manifest));
        }

        private static void Write(string candidate, string relativePath, string content)
        {
            var path = Path.Combine(candidate, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static string Hash(string content)
        {
            using (var hash = SHA256.Create())
            {
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(content))).Replace("-", string.Empty);
            }
        }
    }
}
