using System;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPathMappingPolicyTests
    {
        [Test]
        public void ForWsl_MapsRequiredClientPathToServer()
        {
            var policy = AppServerPathMappingPolicy.ForHostPlatform(Domain.Enums.AppServerHostPlatform.Wsl);

            var mapped = policy.MapRequiredClientPathToServer(@"E:\MyCodex\MyCodex", "workspace");

            Assert.That(mapped, Is.EqualTo("/mnt/e/MyCodex/MyCodex"));
        }

        [Test]
        public void ForWsl_ThrowsWhenRequiredClientPathCannotBeMapped()
        {
            var policy = AppServerPathMappingPolicy.ForHostPlatform(Domain.Enums.AppServerHostPlatform.Wsl);

            Assert.Throws<InvalidOperationException>(() =>
                policy.MapRequiredClientPathToServer("relative/repo", "workspace"));
        }

        [Test]
        public void ForWsl_AllowsNativeWslRequiredPath()
        {
            var policy = AppServerPathMappingPolicy.ForHostPlatform(Domain.Enums.AppServerHostPlatform.Wsl);

            var mapped = policy.MapRequiredClientPathToServer("/home/tester/../tester/repo", "workspace");

            Assert.That(mapped, Is.EqualTo("/home/tester/repo"));
        }

        [Test]
        public void ForWsl_AllowsServerNativePathWhenRoundTrippingServerPath()
        {
            var policy = AppServerPathMappingPolicy.ForHostPlatform(Domain.Enums.AppServerHostPlatform.Wsl);

            var mapped = policy.MapClientOrServerPathToServer("/home/tester/.codex/skills/demo/SKILL.md");

            Assert.That(mapped, Is.EqualTo("/home/tester/.codex/skills/demo/SKILL.md"));
        }

        [Test]
        public void ForWsl_MapsServerMountedPathBackToClient()
        {
            var policy = AppServerPathMappingPolicy.ForHostPlatform(Domain.Enums.AppServerHostPlatform.Wsl);

            var mapped = policy.MapServerPathToClient("/mnt/e/MyCodex/MyCodex");

            Assert.That(mapped, Is.EqualTo(@"E:\MyCodex\MyCodex"));
        }

        [Test]
        public void ForWindows_KeepsPathTrimmed()
        {
            var policy = AppServerPathMappingPolicy.ForHostPlatform(Domain.Enums.AppServerHostPlatform.Windows);

            var mapped = policy.MapRequiredClientPathToServer(@"  E:\MyCodex\MyCodex  ", "workspace");

            Assert.That(mapped, Is.EqualTo(@"E:\MyCodex\MyCodex"));
        }
    }
}
