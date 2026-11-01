using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Shared.Helpers;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class LocalAuthenticationHostRecoveryTests
    {
        private string _root;
        private string _home;
        private string _store;
        private IDictionary<string, object> _status;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-recovery-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _home = Path.Combine(_root, "home");
            _store = Path.Combine(_root, "store");
            var executable = Path.Combine(_root, "mycodex-auth-host.exe");
            File.WriteAllText(executable, "not executed");
            string hash;
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(executable))).Replace("-", "");
            _status = new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "hostVersion", "0.3.11" }, { "protocolVersion", 3 },
                { "upstreamRevision", "85ad4969b3399500baaea9d46b1e08f87aba80e1" },
                { "sourceRevision", new string('a', 40) }, { "sourceDirty", false },
                { "target", "x86_64-pc-windows-msvc" }, { "executable", "mycodex-auth-host.exe" },
                { "sha256", hash }, { "executablePath", executable }, { "codexHome", _home }, { "dataDir", _store }
            };
            WriteManifest();
        }

        [TearDown]
        public void TearDown() { Directory.Delete(_root, true); }

        [Test]
        [Explicit("Requires MYCODEX_AUTH_CORE_TEST_PATH; starts only copied hosts with an isolated empty home/store.")]
        public async Task NativeIdleResidentIsReplacedButActiveOwnerIsPreserved()
        {
            var source = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(source), Is.True);
            Directory.CreateDirectory(_home);
            Directory.CreateDirectory(_store);
            var first = Path.Combine(_root, "first", "mycodex-auth-host.exe");
            var second = Path.Combine(_root, "second", "mycodex-auth-host.exe");
            foreach (var path in new[] { first, second })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.Copy(source, path);
                File.Copy(Path.Combine(Path.GetDirectoryName(source), AuthenticationHostArtifact.ManifestFileName),
                    Path.Combine(Path.GetDirectoryName(path), AuthenticationHostArtifact.ManifestFileName));
            }
            using (var stream = File.Open(second, FileMode.Append)) stream.WriteByte(0);
            var serializer = new JavaScriptSerializer();
            var manifestPath = Path.Combine(Path.GetDirectoryName(second), AuthenticationHostArtifact.ManifestFileName);
            var manifest = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath));
            using (var sha = SHA256.Create()) manifest["sha256"] = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(second))).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(manifestPath, serializer.Serialize(manifest));
            var start = new ProcessStartInfo(first, "serve --data-dir " + WindowsCommandLineArgumentEscaper.Quote(_store) +
                " --codex-home " + WindowsCommandLineArgumentEscaper.Quote(_home))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            var owner = new AuthenticationHostUsage(_store);
            var replacement = new LocalAuthenticationHost("Bundled", second, _home, _store);
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
            using (var resident = Process.Start(start))
            {
                try
                {
                    var ready = resident.StandardOutput.ReadLineAsync();
                    Assert.That(await Task.WhenAny(ready, Task.Delay(10000)), Is.SameAs(ready));
                    var readyText = await ready;
                    Assert.That(readyText, Is.Not.Null, resident.HasExited ? await resident.StandardError.ReadToEndAsync() : "No readiness event");
                    Assert.That(readyText, Does.Contain("ready"));
                    using (await owner.EnterAsync(timeout.Token)) owner.Register();
                    var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => replacement.RequestAsync("status", new Dictionary<string, object>(), timeout.Token));
                    Assert.That(error.Code, Is.EqualTo("backend_version_mismatch"));
                    Assert.That(resident.HasExited, Is.False);
                    using (await owner.EnterAsync(timeout.Token)) owner.ReleaseAndIsLastUser();
                    var status = await replacement.RequestAsync("status", new Dictionary<string, object>(), timeout.Token);
                    Assert.That(status["sha256"], Is.EqualTo(manifest["sha256"]));
                    Assert.That(resident.WaitForExit(5000), Is.True);
                    Assert.That(File.Exists(Path.Combine(_home, "config.toml")), Is.False);
                }
                finally
                {
                    using (await owner.EnterAsync(CancellationToken.None)) owner.ReleaseAndIsLastUser();
                    if (!resident.HasExited)
                    {
                        var original = new LocalAuthenticationHost("Bundled", first, _home, _store);
                        await original.RequestAsync("status", new Dictionary<string, object>(), CancellationToken.None);
                        await original.ShutdownAsync(CancellationToken.None);
                        resident.WaitForExit(5000);
                    }
                    else await replacement.ShutdownAsync(CancellationToken.None);
                }
            }
        }

        [TestCase("normal")]
        [TestCase("extended")]
        [TestCase("unc")]
        public async Task IdleVerifiedResidentIsGracefullyStoppedBeforeReplacement(string paths)
        {
            if (paths == "unc")
            {
                _home = @"\\server\share\home";
                _store = @"\\server\share\store";
                _status["codexHome"] = @"\\?\UNC\server\share\home";
                _status["dataDir"] = @"\\?\UNC\server\share\store";
            }
            else if (paths == "extended")
            {
                _status["codexHome"] = @"\\?\" + _home;
                _status["dataDir"] = @"\\?\" + _store;
            }
            var calls = 0;
            await LocalAuthenticationHostRecovery.StopIdleResidentAsync(_status, _home, _store, false,
                (method, args, token) =>
                {
                    calls++;
                    Assert.That(method, Is.EqualTo("backend/shutdown"));
                    Assert.That(args["forUpdate"], Is.True);
                    Assert.That(args.ContainsKey("detach"), Is.False);
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "status", "shutting_down" } });
                }, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase("active-user")]
        [TestCase("wrong-home")]
        [TestCase("wrong-store")]
        [TestCase("wrong-identity")]
        [TestCase("tampered")]
        [TestCase("newer-version")]
        public void UnverifiedOrUsedResidentsAreNeverStopped(string scenario)
        {
            if (scenario == "wrong-home") _status["codexHome"] = _home + "-other";
            if (scenario == "wrong-store") _status["dataDir"] = _store + "-other";
            if (scenario == "wrong-identity") _status["sourceRevision"] = new string('b', 40);
            if (scenario == "tampered") File.AppendAllText((string)_status["executablePath"], "tampered");
            if (scenario == "newer-version") { _status["hostVersion"] = "9.0.0"; WriteManifest(); }
            var calls = 0;
            Assert.ThrowsAsync<AuthenticationCenterException>(() => LocalAuthenticationHostRecovery.StopIdleResidentAsync(
                _status, _home, _store, scenario == "active-user", (method, args, token) =>
                {
                    calls++;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
                }, CancellationToken.None));
            Assert.That(calls, Is.Zero);
        }

        [TestCase("login_in_progress")]
        [TestCase("route_requests_active")]
        public void BusyResidentRefusalIsPreserved(string code)
        {
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => LocalAuthenticationHostRecovery.StopIdleResidentAsync(
                _status, _home, _store, false, (method, args, token) => throw new AuthenticationCenterException(code), CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo(code));
        }

        [Test]
        public void ShutdownMustBeAcknowledgedBeforeReplacementCanProceed()
        {
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => LocalAuthenticationHostRecovery.StopIdleResidentAsync(
                _status, _home, _store, false, (method, args, token) =>
                    Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "status", "running" } }), CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo("invalid_response"));
        }

        private void WriteManifest()
        {
            File.WriteAllText(Path.Combine(_root, AuthenticationHostArtifact.ManifestFileName), new JavaScriptSerializer().Serialize(_status));
        }
    }
}
