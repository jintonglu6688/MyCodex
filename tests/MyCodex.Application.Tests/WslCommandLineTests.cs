using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WslCommandLineTests
    {
        [Test]
        public void BuildExecArguments_WithWindowsEscapingEdgeCases_ReturnsCompleteArgumentList()
        {
            var arguments = WslCommandLine.BuildExecArguments(
                "Ubuntu Dev",
                @"C:\Program Files\codex\codex.exe",
                string.Empty,
                "two\\\\slashes and space",
                "quote \" inside",
                @"C:\repo with space\");

            Assert.That(
                arguments,
                Is.EqualTo("-d \"Ubuntu Dev\" --exec \"C:\\Program Files\\codex\\codex.exe\" \"\" \"two\\\\slashes and space\" \"quote \\\" inside\" \"C:\\repo with space\\\\\""));
        }

        [Test]
        public void BuildHeadlessShellExecArguments_StartsCodexAppServerWithoutModelCatalogInjection()
        {
            var arguments = WslCommandLine.BuildHeadlessShellExecArguments(
                "Ubuntu",
                "/bin/bash",
                "codex",
                "app-server");

            Assert.That(arguments, Does.Contain("'codex' 'app-server'"));
            Assert.That(arguments, Does.Not.Contain("model_catalog_json"));
        }
    }
}
