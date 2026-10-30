using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CodexRuntimePackageClientTests
    {
        private const string Target = "x86_64-pc-windows-msvc";

        [TestCase(Target, "win32-x64")]
        [TestCase("aarch64-pc-windows-msvc", "win32-arm64")]
        public async Task OfficialStableVersionSelectsMatchingPlatformAndPinsItsIntegrity(string target, string suffix)
        {
            var requests = new List<string>();
            var client = new CodexRuntimePackageClient((url, token) =>
            {
                requests.Add(url);
                return Task.FromResult(Metadata(url, suffix));
            });
            var package = await client.CheckLatestAsync(target, CancellationToken.None);
            Assert.That(requests, Is.EqualTo(new[]
            {
                CodexRuntimePackageClient.OfficialRegistry + "/%40openai%2Fcodex/latest",
                CodexRuntimePackageClient.OfficialRegistry + "/%40openai%2Fcodex/0.165.0-" + suffix
            }));
            Assert.That(package.Version, Is.EqualTo("0.165.0"));
            Assert.That(package.TargetTriple, Is.EqualTo(target));
            Assert.That(package.FileName, Is.EqualTo("codex-0.165.0-" + suffix + ".tgz"));
            Assert.That(package.Integrity, Is.EqualTo(Integrity("verified")));
        }

        [Test]
        public async Task UnreachableOfficialRegistryFallsBackToMirrorMetadata()
        {
            var requests = new List<string>();
            var client = new CodexRuntimePackageClient((url, token) =>
            {
                requests.Add(url);
                if (url.StartsWith(CodexRuntimePackageClient.OfficialRegistry, StringComparison.Ordinal))
                    throw new WebException("offline");
                return Task.FromResult(Metadata(url));
            });
            Assert.That((await client.CheckLatestAsync(Target, CancellationToken.None)).Version, Is.EqualTo("0.165.0"));
            Assert.That(requests.Count, Is.EqualTo(4));
            Assert.That(requests[1], Does.StartWith(CodexRuntimePackageClient.MirrorRegistry));
            Assert.That(requests[3], Does.EndWith("/0.165.0-win32-x64"));
        }

        [TestCase("0.166.0-alpha.1")]
        [TestCase("../0.165.0")]
        [TestCase("v0.165.0")]
        [TestCase("")]
        public void LatestMustBeAStableVersion(string version)
        {
            var calls = 0;
            var client = new CodexRuntimePackageClient((url, token) =>
            {
                calls++;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "version", version } });
            });
            Assert.ThrowsAsync<InvalidOperationException>(() => client.CheckLatestAsync(Target, CancellationToken.None));
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase("http://registry.npmjs.org/@openai/codex/-/codex-0.165.0-win32-x64.tgz")]
        [TestCase("https://untrusted.example/@openai/codex/-/codex-0.165.0-win32-x64.tgz")]
        [TestCase("https://registry.npmjs.org/@openai/codex/-/codex-0.165.0-win32-arm64.tgz")]
        public void RejectsUnexpectedPackageAddresses(string address)
        {
            var client = new CodexRuntimePackageClient((url, token) =>
            {
                var metadata = Metadata(url);
                if (!url.EndsWith("/latest", StringComparison.Ordinal))
                    ((IDictionary<string, object>)metadata["dist"])["tarball"] = address;
                return Task.FromResult(metadata);
            });
            Assert.ThrowsAsync<InvalidOperationException>(() => client.CheckLatestAsync(Target, CancellationToken.None));
        }

        [Test]
        public async Task CheckedPackageDownloadsFromMirrorWithoutRequeryingLatest()
        {
            await WithDownloadAsync(false, false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MirrorFailureOrCorruptionFallsBackToSameOfficialPackage(bool corrupt)
        {
            await WithDownloadAsync(true, corrupt);
        }

        private static async Task WithDownloadAsync(bool failMirror, bool corrupt)
        {
            var root = TemporaryDirectory();
            try
            {
                var downloads = new List<string>();
                var reads = 0;
                var client = new CodexRuntimePackageClient((url, token) =>
                {
                    if (++reads > 2) throw new AssertionException("Do not query latest while downloading.");
                    return Task.FromResult(Metadata(url));
                }, (url, path, progress, token) =>
                {
                    downloads.Add(url);
                    if (failMirror && downloads.Count == 1)
                    {
                        File.WriteAllText(path, "corrupt");
                        if (!corrupt) throw new WebException("not synchronized yet");
                    }
                    else
                    {
                        Assert.That(File.Exists(path), Is.False, "Discard the failed download before retrying.");
                        File.WriteAllText(path, "verified");
                    }
                    return Task.CompletedTask;
                });
                var package = await client.CheckLatestAsync(Target, CancellationToken.None);
                var destination = Path.Combine(root, "package.tgz");
                await client.DownloadVerifiedAsync(package, destination, null, CancellationToken.None);
                Assert.That(reads, Is.EqualTo(2));
                Assert.That(downloads, Is.EqualTo(failMirror
                    ? new[] { package.MirrorUrl, package.OfficialUrl }
                    : new[] { package.MirrorUrl }));
                Assert.That(File.ReadAllText(destination), Is.EqualTo("verified"));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void BothCorruptCopiesFailWithoutLeavingAnArchive()
        {
            var root = TemporaryDirectory();
            try
            {
                var requests = 0;
                var client = new CodexRuntimePackageClient(download: (url, path, progress, token) =>
                {
                    requests++;
                    File.WriteAllText(path, "corrupt");
                    return Task.CompletedTask;
                });
                var path = Path.Combine(root, "package.tgz");
                var package = new CodexRuntimePackage("0.165.0", Target, "codex-0.165.0-win32-x64.tgz", Integrity("verified"), null);
                Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadVerifiedAsync(package, path, null, CancellationToken.None));
                Assert.That(requests, Is.EqualTo(2));
                Assert.That(File.Exists(path), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void CancelledMetadataRequestNeverFallsBack()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var calls = 0;
                var client = new CodexRuntimePackageClient((url, token) =>
                {
                    calls++;
                    cancellation.Cancel();
                    throw new OperationCanceledException(token);
                });
                Assert.CatchAsync<OperationCanceledException>(() => client.CheckLatestAsync(Target, cancellation.Token));
                Assert.That(calls, Is.EqualTo(1));
            }
        }

        [Test]
        public void CancelledDownloadNeverFallsBack()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var calls = 0;
                var client = new CodexRuntimePackageClient(download: (url, path, progress, token) =>
                {
                    calls++;
                    cancellation.Cancel();
                    throw new OperationCanceledException(token);
                });
                var package = new CodexRuntimePackage("0.165.0", Target, "codex-0.165.0-win32-x64.tgz", Integrity("verified"), null);
                Assert.CatchAsync<OperationCanceledException>(() => client.DownloadVerifiedAsync(package, "unused", null, cancellation.Token));
                Assert.That(calls, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task OlderRegistryVersionNeverDowngradesAnInstalledRuntime()
        {
            var root = TemporaryDirectory();
            try
            {
                var paths = new AppConfigService(root);
                var target = new CodexRuntimeUpdateService(paths).GetInstalledInfo().TargetTriple;
                var exe = paths.GetBundledCodexExecutablePath("0.166.0", target);
                Directory.CreateDirectory(Path.GetDirectoryName(exe));
                File.WriteAllText(exe, "installed");
                var client = new CodexRuntimePackageClient((url, token) => Task.FromResult(Metadata(url,
                    target.StartsWith("aarch64", StringComparison.Ordinal) ? "win32-arm64" : "win32-x64")),
                    (url, path, progress, token) => throw new AssertionException("Must not download an older version."));
                var service = new CodexRuntimeUpdateService(paths, client, () => new string[0]);
                var check = await service.CheckLatestAsync(CancellationToken.None);
                Assert.That(check.IsUpdateAvailable, Is.False);
                var result = await service.InstallLatestAsync(null, CancellationToken.None);
                Assert.That(result.Version, Is.EqualTo("0.166.0"));
                Assert.That(result.WasUpdated, Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void VerifyPackageIntegrity_StrongSriMismatchIsNotRescuedBySha1()
        {
            var rootPath = TemporaryDirectory();
            try
            {
                var archivePath = Path.Combine(rootPath, "package.tgz");
                File.WriteAllText(archivePath, "runtime package");
                var bytes = File.ReadAllBytes(archivePath);
                string sha1Hex;
                string sha512Base64;
                using (var sha1 = System.Security.Cryptography.SHA1.Create())
                {
                    sha1Hex = BitConverter.ToString(sha1.ComputeHash(bytes)).Replace("-", string.Empty);
                }

                using (var sha512 = System.Security.Cryptography.SHA512.Create())
                {
                    sha512Base64 = Convert.ToBase64String(sha512.ComputeHash(bytes));
                }

                var wrongSri = "sha512-" + Convert.ToBase64String(new byte[64]);

                Assert.That(CodexRuntimePackageClient.VerifyPackageIntegrity(archivePath, "sha512-" + sha512Base64, sha1Hex), Is.True);
                Assert.That(CodexRuntimePackageClient.VerifyPackageIntegrity(archivePath, wrongSri, sha1Hex), Is.False);
                Assert.That(CodexRuntimePackageClient.VerifyPackageIntegrity(archivePath, null, sha1Hex), Is.True);
                Assert.That(CodexRuntimePackageClient.VerifyPackageIntegrity(archivePath, null, new string('0', 40)), Is.False);
            }
            finally
            {
                Directory.Delete(rootPath, true);
            }
        }

        private static IDictionary<string, object> Metadata(string url, string suffix = "win32-x64")
        {
            if (url.EndsWith("/latest", StringComparison.Ordinal))
                return new Dictionary<string, object> { { "version", "0.165.0" } };
            return new Dictionary<string, object>
            {
                { "version", "0.165.0-" + suffix },
                { "dist", new Dictionary<string, object>
                    {
                        { "tarball", CodexRuntimePackageClient.OfficialRegistry + "/@openai/codex/-/codex-0.165.0-" + suffix + ".tgz" },
                        { "integrity", Integrity("verified") }
                    }
                }
            };
        }

        private static string Integrity(string content)
        {
            using (var hash = SHA512.Create())
                return "sha512-" + Convert.ToBase64String(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content)));
        }

        private static string TemporaryDirectory()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "runtime-packages-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
