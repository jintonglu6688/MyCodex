using System.IO;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class MainFormTargetIntegrationTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public void ConnectedBundledRuntimeCleansOlderVersionsWithTargetManagementEnabled(bool justUpdated)
        {
            using (var fixture = new TargetFormFixture())
            {
                var target = new CodexRuntimeUpdateService(fixture.Paths).GetInstalledInfo().TargetTriple;
                var old = CreateRuntimeFixture(fixture.Paths, "0.162.1", target);
                var current = CreateRuntimeFixture(fixture.Paths, "0.163.0", target);
                Assert.That(Get<TargetCatalogStore>(fixture.Form, "_targetCatalog"), Is.Not.Null);
                if (justUpdated)
                    Set(fixture.Form, "_installedBundledRuntimeUpdate", new CodexRuntimeUpdateResult { Version = "0.163.0", TargetTriple = target });
                Invoke(fixture.Form, "CleanupConnectedBundledRuntimeUpdate");
                Assert.That(File.Exists(old), Is.False);
                Assert.That(Directory.Exists(fixture.Paths.GetBundledCodexVersionDirectory("0.162.1")), Is.False);
                Assert.That(File.Exists(current), Is.True);
                Assert.That(Get<CodexRuntimeUpdateResult>(fixture.Form, "_installedBundledRuntimeUpdate"), Is.Null);
            }
        }

        private static string CreateRuntimeFixture(AppConfigService paths, string version, string target)
        {
            var executable = paths.GetBundledCodexExecutablePath(version, target);
            Directory.CreateDirectory(Path.GetDirectoryName(executable));
            File.WriteAllText(executable, "synthetic runtime fixture");
            return executable;
        }
    }
}
