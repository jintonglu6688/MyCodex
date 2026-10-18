using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnixRuntimeFileSystemScriptsTests
    {
        [Test]
        public void EnsureDirectoryScript_RequiresAbsolutePathWithoutEval()
        {
            var script = UnixRuntimeFileSystemScripts.EnsureDirectory(false);

            Assert.That(script, Does.StartWith("set -eu\n"));
            Assert.That(script, Does.Contain("if [ -z \"$target\" ]; then exit 64; fi"));
            Assert.That(script, Does.Contain("case \"$target\" in /*) ;; *) exit 65 ;; esac"));
            Assert.That(script, Does.Contain("mkdir -p -- \"$1\""));
            Assert.That(script, Does.Not.Contain("eval"));
        }

        [Test]
        public void RemoteScripts_AddZshCompatibilityPrefix()
        {
            var script = UnixRuntimeFileSystemScripts.EnsureDirectory(true);

            Assert.That(script, Does.StartWith("emulate -L sh 2>/dev/null || true\nset -eu\n"));
        }
    }
}
