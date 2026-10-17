using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationHostReleaseTests
    {
        [Test]
        public async Task CompletedInstallCleansOnlySamePlatformArchivesUpToTheInstalledVersion()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-cache-" + Guid.NewGuid().ToString("N"));
            var cache = Path.Combine(root, "MyCodex", "auth-core", "release-cache");
            var archive = AuthenticationHostRelease.ForTarget("Darwin", "arm64");
            var current = Path.Combine(cache, AuthenticationHostRelease.Tag, archive.FileName);
            var previous = Path.Combine(cache, "auth-core-v0.2.0", "mycodex-auth-host-0.2.0-" + archive.Target + ".tar.gz");
            var otherPlatform = Path.Combine(cache, AuthenticationHostRelease.Tag,
                AuthenticationHostRelease.ForTarget("Linux", "x86_64").FileName);
            var newer = Path.Combine(cache, "auth-core-v99.0.0", "mycodex-auth-host-99.0.0-" + archive.Target + ".tar.gz");
            var database = Path.Combine(root, "MyCodex", "auth-core", "targets", "fixture", "cc-switch.db");
            var partial = current + ".fixture.download";
            var unrelated = Path.Combine(cache, AuthenticationHostRelease.Tag, "keep.txt");
            try
            {
                foreach (var path in new[] { current, previous, otherPlatform, newer, database, partial, unrelated })
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllText(path, "fixture");
                }
                await AuthenticationHostRelease.CleanupCacheAsync("../../targets", root);
                Assert.That(File.Exists(current), Is.True);
                await AuthenticationHostRelease.CleanupCacheAsync(archive.Target, root);
                Assert.That(File.Exists(current), Is.False);
                Assert.That(Directory.Exists(Path.GetDirectoryName(previous)), Is.False);
                foreach (var path in new[] { otherPlatform, newer, database, partial, unrelated })
                    Assert.That(File.ReadAllText(path), Is.EqualTo("fixture"), path);
                await AuthenticationHostRelease.CleanupCacheAsync(archive.Target, root);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public async Task LockedCacheDoesNotFailAnInstallationAndCanBeCleanedOnRetry()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-cache-" + Guid.NewGuid().ToString("N"));
            var archive = AuthenticationHostRelease.ForTarget("Linux", "x86_64");
            var folder = Path.Combine(root, "MyCodex", "auth-core", "release-cache", AuthenticationHostRelease.Tag);
            var path = Path.Combine(folder, archive.FileName);
            Directory.CreateDirectory(folder);
            try
            {
                using (File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await AuthenticationHostRelease.CleanupCacheAsync(archive.Target, root);
                    Assert.That(File.Exists(path), Is.True);
                }
                await AuthenticationHostRelease.CleanupCacheAsync(archive.Target, root);
                Assert.That(Directory.Exists(folder), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        [Explicit("Manual integration against a disposable Mac backend installation.")]
        public async Task MacReleaseCanBeInstalledThroughSshTransport()
        {
            var host = Environment.GetEnvironmentVariable("MYCODEX_AUTH_REMOTE_TEST_HOST");
            var user = Environment.GetEnvironmentVariable("MYCODEX_AUTH_REMOTE_TEST_USER");
            Assert.That(host, Is.Not.Empty);
            Assert.That(user, Is.Not.Empty);
            var config = new RemoteMacSshConfigSnapshot { HostName = host, UserName = user };
            var archive = AuthenticationHostRelease.ForTarget("Darwin", "arm64");
            var input = await AuthenticationHostRelease.ReadVerifiedBase64Async(archive, CancellationToken.None);
            var result = await new RemoteSshCommandExecutor().ExecuteWithInputAsync(config,
                AuthenticationHostDeployment.InstallScript, "mycodex-auth-install", 180000,
                CancellationToken.None, input, archive.System, archive.Architecture,
                archive.Sha256, archive.ExecutableSha256, "missing");
            Assert.That(result.ExitCode, Is.EqualTo(0), "Remote install failed: " + result.StandardError.Trim());
            Assert.That(result.StandardOutput.Trim(), Is.EqualTo("installed"));
        }

        [TestCase("Linux", "x86_64", "x86_64-unknown-linux-gnu", "e54658c5cee0d0b068a8f315ab86821f3130fbb7304e86284fdefb722ed3e312")]
        [TestCase("Linux", "aarch64", "aarch64-unknown-linux-gnu", "d0b24044dfabfad193a72175cea621d38d69bf88d201a01f17b7d1db3cd4d877")]
        [TestCase("Darwin", "arm64", "aarch64-apple-darwin", "b405099d132e2c41de3da75d1bcff2e051e694babf6bebad21cd79bc4d91a01f")]
        public void SupportedTargetsSelectThePinnedGitCodeArchive(string system, string architecture,
            string target, string hash)
        {
            var release = AuthenticationHostDeployment.ParseProbe(system + "\n" + architecture + "\n");
            Assert.That(release.Target, Is.EqualTo(target));
            Assert.That(release.Sha256, Is.EqualTo(hash));
            Assert.That(release.FileName, Is.EqualTo("mycodex-auth-host-0.3.8-" + target + ".tar.gz"));
        }

        [Test]
        public void IntelMacIsRejectedBecauseNoReleaseWasPublished()
        {
            var error = Assert.Throws<AuthenticationCenterException>(() =>
                AuthenticationHostDeployment.ParseProbe("Darwin\nx86_64\n"));
            Assert.That(error.Code, Is.EqualTo("unsupported_platform"));
        }

        [TestCase("0.2.3", 2, "e0f70019b2758f5b6b9a04dd60e4689481a0c0ac")]
        [TestCase("0.3.0", 3, "2db86e94da13365caae55bb08d09295e31500d21")]
        [TestCase("0.3.1", 3, "2db86e94da13365caae55bb08d09295e31500d21")]
        [TestCase("0.3.2", 3, "2db86e94da13365caae55bb08d09295e31500d21")]
        [TestCase("0.3.4", 3, "790ed8009df809bedaf6c31b5ced05da46bb1f1f")]
        [TestCase("0.3.5", 3, "790ed8009df809bedaf6c31b5ced05da46bb1f1f")]
        [TestCase("0.3.6", 3, "790ed8009df809bedaf6c31b5ced05da46bb1f1f")]
        [TestCase("0.3.7", 3, "790ed8009df809bedaf6c31b5ced05da46bb1f1f")]
        public void DifferentInstalledBuildIsOfferedAnUpdateOnlyWhenItsPackageIsCoherent(string hostVersion, int protocol, string upstream)
        {
            var archive = AuthenticationHostRelease.ForTarget("Linux", "x86_64");
            var manifest = new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "hostVersion", hostVersion }, { "protocolVersion", protocol },
                { "upstreamRevision", upstream },
                { "sourceRevision", new string('a', 40) }, { "sourceDirty", false },
                { "target", archive.Target }, { "executable", "mycodex-auth-host" },
                { "sha256", new string('b', 64) }
            };
            var text = new JavaScriptSerializer().Serialize(manifest);
            Assert.That(Assert.Throws<AuthenticationCenterException>(() =>
                AuthenticationHostArtifact.LoadTarget(text, new string('b', 64), archive.Target)).Code,
                Is.EqualTo("backend_incompatible"));
            Assert.DoesNotThrow(() => AuthenticationHostArtifact.LoadTargetForUpdate(text, new string('b', 64), archive.Target));
            manifest["target"] = "aarch64-apple-darwin";
            text = new JavaScriptSerializer().Serialize(manifest);
            var error = Assert.Throws<AuthenticationCenterException>(() =>
                AuthenticationHostArtifact.LoadTargetForUpdate(text, new string('b', 64), archive.Target));
            Assert.That(error.Code, Is.EqualTo("backend_incompatible"));
        }

        [TestCase(2, "2db86e94da13365caae55bb08d09295e31500d21")]
        [TestCase(3, "e0f70019b2758f5b6b9a04dd60e4689481a0c0ac")]
        public void MismatchedProtocolAndUpstreamCannotBeOfferedAnUpdate(int protocol, string upstream)
        {
            var archive = AuthenticationHostRelease.ForTarget("Linux", "x86_64");
            var text = new JavaScriptSerializer().Serialize(new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "hostVersion", "0.2.3" }, { "protocolVersion", protocol },
                { "upstreamRevision", upstream },
                { "sourceRevision", new string('a', 40) }, { "sourceDirty", false },
                { "target", archive.Target }, { "executable", "mycodex-auth-host" },
                { "sha256", new string('b', 64) }
            });
            Assert.That(Assert.Throws<AuthenticationCenterException>(() =>
                AuthenticationHostArtifact.LoadTargetForUpdate(text, new string('b', 64), archive.Target)).Code,
                Is.EqualTo("backend_incompatible"));
        }

        [Test]
        public void MatchingProtocolAloneDoesNotAcceptAnUnpinnedBuild()
        {
            var archive = AuthenticationHostRelease.ForTarget("Linux", "x86_64");
            var manifest = new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "hostVersion", "0.3.8" }, { "protocolVersion", 3 },
                { "upstreamRevision", "790ed8009df809bedaf6c31b5ced05da46bb1f1f" },
                { "sourceRevision", new string('a', 40) }, { "sourceDirty", false },
                { "target", archive.Target }, { "executable", "mycodex-auth-host" },
                { "sha256", archive.ExecutableSha256 }
            };
            var serializer = new JavaScriptSerializer();
            Assert.That(AuthenticationHostRelease.IsInstalledRelease(serializer.Serialize(manifest), archive.ExecutableSha256, archive), Is.False);
            manifest["sourceRevision"] = AuthenticationHostRelease.SourceRevision;
            Assert.That(AuthenticationHostRelease.IsInstalledRelease(serializer.Serialize(manifest), archive.ExecutableSha256, archive), Is.True);
        }
    }
}
