using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnixRuntimeFileSystemScriptsTests
    {
        [Test]
        public void CreateDirectoryScript_UsesCommonNameValidationWithoutEval()
        {
            var script = UnixRuntimeFileSystemScripts.CreateDirectory(false);

            Assert.That(script, Does.StartWith("set -eu\n"));
            Assert.That(script, Does.Contain("case \"$parent\" in /*) ;; *) exit 65 ;; esac"));
            Assert.That(script, Does.Contain("case \"$name\" in */*|*\\\\*|.|..) exit 65 ;; esac"));
            Assert.That(script, Does.Contain("mkdir -- \"$parent/$name\""));
            Assert.That(script, Does.Not.Contain("eval"));
        }

        [Test]
        public void NamedInputScripts_RejectBackslashInNames()
        {
            var createScript = UnixRuntimeFileSystemScripts.CreateDirectory(false);
            var renameScript = UnixRuntimeFileSystemScripts.Rename(
                false,
                UnixRuntimeRenameParentMode.Dirname);

            Assert.That(createScript, Does.Contain("case \"$name\" in */*|*\\\\*|.|..) exit 65 ;; esac"));
            Assert.That(renameScript, Does.Contain("case \"$name\" in */*|*\\\\*|.|..) exit 65 ;; esac"));
        }

        [Test]
        public void RemoteScripts_AddZshCompatibilityPrefix()
        {
            var script = UnixRuntimeFileSystemScripts.Delete(true);

            Assert.That(script, Does.StartWith("emulate -L sh 2>/dev/null || true\nset -eu\n"));
        }

        [Test]
        public void RenameScript_KeepsHostParentResolutionVisible()
        {
            var wslScript = UnixRuntimeFileSystemScripts.Rename(false, UnixRuntimeRenameParentMode.Dirname);
            var remoteScript = UnixRuntimeFileSystemScripts.Rename(true, UnixRuntimeRenameParentMode.ParameterExpansion);

            Assert.That(wslScript, Does.Contain("parent=$(dirname -- \"$target\")"));
            Assert.That(remoteScript, Does.Contain("parent=${target%/*}"));
            Assert.That(remoteScript, Does.Contain("if [ -z \"$parent\" ]; then parent=/; fi"));
            Assert.That(wslScript, Does.Contain("[ ! -e \"$parent/$name\" ]"));
            Assert.That(remoteScript, Does.Contain("[ ! -e \"$parent/$name\" ]"));
        }

        [Test]
        public void DeleteScript_PreservesSafeDeleteGuards()
        {
            var script = UnixRuntimeFileSystemScripts.Delete(false);

            Assert.That(script, Does.Contain("case \"$target\" in /|/.) exit 65 ;; esac"));
            Assert.That(script, Does.Contain("case \"$target\" in */../*|*/..|*/./*|*/.) exit 65 ;; esac"));
            Assert.That(script, Does.Contain("if [ \"$mode\" = \"recursive\" ]; then rm -rf -- \"$target\"; else rmdir -- \"$target\"; fi"));
            Assert.That(script, Does.Not.Contain("eval"));
        }
    }
}
