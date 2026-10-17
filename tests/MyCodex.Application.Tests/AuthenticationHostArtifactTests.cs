using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationHostArtifactTests
    {
        private string _root;
        private string _executable;
        private Dictionary<string, object> _manifest;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "host-artifact-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _executable = Path.Combine(_root, "mycodex-auth-host.exe");
            File.WriteAllText(_executable, "not an executable; invalid packages must fail before process launch");
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(_executable))).Replace("-", "");
            }
            _manifest = new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "hostVersion", "0.3.7" }, { "protocolVersion", 3 },
                { "upstreamRevision", "790ed8009df809bedaf6c31b5ced05da46bb1f1f" },
                { "sourceRevision", new string('a', 40) }, { "sourceDirty", false },
                { "target", "x86_64-pc-windows-msvc" }, { "executable", "mycodex-auth-host.exe" }, { "sha256", hash }
            };
            WriteManifest();
        }

        [TearDown]
        public void TearDown()
        {
            Directory.Delete(_root, true);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ValidPackageAcceptsItsIdentityIncludingHonestDevelopmentMarker(bool dirty)
        {
            _manifest["sourceDirty"] = dirty;
            WriteManifest();
            var artifact = AuthenticationHostArtifact.Load(_executable);
            artifact.ValidateIdentity(_manifest);
            _manifest["sourceDirty"] = !dirty;
            AssertCode(() => artifact.ValidateIdentity(_manifest), "backend_version_mismatch");
        }

        [TestCase("hostVersion", "3.20.3", "backend_incompatible")]
        [TestCase("hostVersion", "0.3.4", "backend_incompatible")]
        [TestCase("protocolVersion", 1, "backend_incompatible")]
        [TestCase("target", "aarch64-apple-darwin", "backend_incompatible")]
        [TestCase("upstreamRevision", "unknown", "backend_incompatible")]
        [TestCase("sourceRevision", "unknown", "backend_manifest_invalid")]
        [TestCase("sourceDirty", "false", "backend_manifest_invalid")]
        [TestCase("schemaVersion", 2, "backend_manifest_invalid")]
        [TestCase("executable", "../mycodex-auth-host.exe", "backend_manifest_invalid")]
        public void BadManifestStopsBeforeLaunchingOrCreatingPrivateState(string field, object value, string code)
        {
            _manifest[field] = value;
            WriteManifest();
            var home = Path.Combine(_root, "home");
            var store = Path.Combine(_root, "private");
            Directory.CreateDirectory(home);
            var host = new LocalAuthenticationHost("Bundled", _executable, home, store);
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(async () =>
                await host.RequestAsync("provider/list", new Dictionary<string, object>(), CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo(code));
            Assert.That(Directory.Exists(store), Is.False);
        }

        [Test]
        public void MissingMalformedAndTamperedArtifactsHaveDistinctFailures()
        {
            File.AppendAllText(_executable, "changed");
            AssertCode(() => AuthenticationHostArtifact.Load(_executable), "backend_integrity_failed");
            File.WriteAllText(ManifestPath, "not json");
            AssertCode(() => AuthenticationHostArtifact.Load(_executable), "backend_manifest_invalid");
            File.Delete(ManifestPath);
            AssertCode(() => AuthenticationHostArtifact.Load(_executable), "backend_manifest_missing");
            File.Delete(_executable);
            AssertCode(() => AuthenticationHostArtifact.Load(_executable), "backend_missing");
        }

        [Test]
        public void LegacyOrDifferentResidentCannotPassIdentityHandshake()
        {
            _manifest["sourceDirty"] = true;
            WriteManifest();
            var artifact = AuthenticationHostArtifact.Load(_executable);
            AssertCode(() => artifact.ValidateIdentity(new Dictionary<string, object> { { "protocolVersion", 1 } }), "backend_version_mismatch");
            _manifest["sourceRevision"] = new string('b', 40);
            AssertCode(() => artifact.ValidateIdentity(_manifest), "backend_version_mismatch");
            _manifest["sourceRevision"] = new string('a', 40);
            // Two modified builds can share the same commit and dirty marker.
            _manifest["sha256"] = new string('0', 64);
            AssertCode(() => artifact.ValidateIdentity(_manifest), "backend_version_mismatch");
        }

        [TestCase("backend_already_running\r\n", "backend_already_running")]
        [TestCase("{\"error\":\"backend_already_running\"}", "backend_already_running")]
        [TestCase("target_mismatch\n", "target_mismatch")]
        [TestCase("failed with secret=value", "backend_unavailable")]
        [TestCase("failed\nsecret", "backend_unavailable")]
        [TestCase("{\"error\":\"credential=private\"}", "backend_unavailable")]
        public void TransportErrorsAcceptBothFormatsWithoutExposingDiagnosticPayloads(string text, string expected)
        {
            Assert.That(LocalAuthenticationHost.ReadErrorCode(new JavaScriptSerializer(), text), Is.EqualTo(expected));
        }

        [Test]
        public void UnixPackageRequiresMatchingTargetHashAndResidentIdentity()
        {
            _manifest["target"] = "x86_64-unknown-linux-gnu";
            _manifest["executable"] = "mycodex-auth-host";
            var text = new JavaScriptSerializer().Serialize(_manifest);
            var artifact = AuthenticationHostArtifact.LoadTarget(text, (string)_manifest["sha256"],
                "x86_64-unknown-linux-gnu");
            artifact.ValidateIdentity(_manifest);
            AssertCode(() => AuthenticationHostArtifact.LoadTarget(text, new string('0', 64),
                "x86_64-unknown-linux-gnu"), "backend_integrity_failed");
            AssertCode(() => AuthenticationHostArtifact.LoadTarget(text, (string)_manifest["sha256"],
                "aarch64-apple-darwin"), "backend_incompatible");
        }

        [TestCase("Linux", "x86_64", "x86_64-unknown-linux-gnu")]
        [TestCase("Linux", "aarch64", "aarch64-unknown-linux-gnu")]
        [TestCase("Darwin", "x86_64", "x86_64-apple-darwin")]
        [TestCase("Darwin", "arm64", "aarch64-apple-darwin")]
        public void RemoteHostSelectsOnlyKnownPlatformArtifacts(string system, string architecture, string target)
        {
            Assert.That(RemoteAuthenticationHost.TargetTriple(system, architecture), Is.EqualTo(target));
            AssertCode(() => RemoteAuthenticationHost.TargetTriple("FreeBSD", architecture), "unsupported_platform");
        }

        [TestCase("x86_64-unknown-linux-gnu")]
        [TestCase("aarch64-unknown-linux-gnu")]
        [TestCase("x86_64-apple-darwin")]
        [TestCase("aarch64-apple-darwin")]
        public void NativeUnixPackageCannotAcceptLegacyOrTamperedArtifacts(string target)
        {
            _manifest["target"] = target;
            _manifest["executable"] = "mycodex-auth-host";
            var serializer = new JavaScriptSerializer();
            var hash = (string)_manifest["sha256"];
            _manifest["hostVersion"] = "0.3.7";
            _manifest["protocolVersion"] = 3;
            _manifest["upstreamRevision"] = "790ed8009df809bedaf6c31b5ced05da46bb1f1f";
            var text = serializer.Serialize(_manifest);
            AuthenticationHostArtifact.LoadTarget(text, hash, target).ValidateIdentity(_manifest);
            _manifest["protocolVersion"] = 1;
            AssertCode(() => AuthenticationHostArtifact.LoadTarget(serializer.Serialize(_manifest), hash, target), "backend_incompatible");
            AssertCode(() => AuthenticationHostArtifact.LoadTarget(text, new string('0', 64), target), "backend_integrity_failed");
            AssertCode(() => AuthenticationHostArtifact.LoadTarget(text, hash, "wrong-target"), "backend_incompatible");
        }

        private string ManifestPath => Path.Combine(_root, AuthenticationHostArtifact.ManifestFileName);

        private void WriteManifest()
        {
            File.WriteAllText(ManifestPath, new JavaScriptSerializer().Serialize(_manifest));
        }

        private static void AssertCode(TestDelegate action, string code)
        {
            Assert.That(Assert.Throws<AuthenticationCenterException>(action).Code, Is.EqualTo(code));
        }
    }
}
