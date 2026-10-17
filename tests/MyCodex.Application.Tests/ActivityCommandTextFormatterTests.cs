using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ActivityCommandTextFormatterTests
    {
        [Test]
        public void CompactCompletionText_UnwrapsPowerShellCommandAndKeepsCompletionDetails()
        {
            const string command =
                "\"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -Command \"rg -n commandExecution src\\MyCodex.Infrastructure; exit 0\"";
            const string completion =
                "命令执行完成：" + command + "，状态：completed，退出码：0";

            var result = ActivityCommandTextFormatter.CompactCompletionText(
                completion,
                command,
                @"E:\MyCodex");

            Assert.That(result, Is.EqualTo(
                "命令执行完成：rg -n commandExecution src\\MyCodex.Infrastructure; exit 0，状态：completed，退出码：0"));
            Assert.That(result, Does.Not.Contain("WindowsPowerShell"));
            Assert.That(result, Does.Not.Contain("powershell.exe"));
        }

        [TestCase(
            "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -NoProfile -Command \"dotnet test\"",
            "dotnet test")]
        [TestCase(
            "\"C:\\Windows\\System32\\cmd.exe\" /d /s /c \"git status\"",
            "git status")]
        [TestCase(
            "/usr/bin/bash -lc 'npm test'",
            "npm test")]
        public void CompactCommandText_UnwrapsKnownShellHosts(string command, string expected)
        {
            var result = ActivityCommandTextFormatter.CompactCommandText(command, null);

            Assert.That(result, Is.EqualTo(expected));
        }

        [Test]
        public void CompactCommandText_UsesFileNameForUnknownAbsoluteExecutable()
        {
            var result = ActivityCommandTextFormatter.CompactCommandText(
                "\"C:\\Tools\\custom-runner.exe\" --verbose",
                null);

            Assert.That(result, Is.EqualTo("\"custom-runner.exe\" --verbose"));
        }

        [Test]
        public void CompactCommandText_CompactsWorkingDirectoryAndPrompt()
        {
            Assert.That(
                ActivityCommandTextFormatter.CompactCommandText(
                    @"E:\MyCodex> dotnet test tests\MyCodex.Application.Tests",
                    @"E:\MyCodex"),
                Is.EqualTo(@"dotnet test tests\MyCodex.Application.Tests"));
            Assert.That(
                ActivityCommandTextFormatter.CompactCommandText(
                    @"E:\MyCodex\tools\verify.ps1 -Fast",
                    @"E:\MyCodex"),
                Is.EqualTo(@".\tools\verify.ps1 -Fast"));
            Assert.That(
                ActivityCommandTextFormatter.CompactCommandText(
                    @"E:\MyCodex2\tools\verify.ps1 -Fast",
                    @"E:\MyCodex"),
                Is.EqualTo(@"verify.ps1 -Fast"));
        }
    }
}
