using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ApprovalRequestClassifierTests
    {
        [Test]
        public void ClassifyCommand_UnwrapsPowerShellAndRecognizesFileReadTarget()
        {
            var classification = ApprovalRequestClassifier.ClassifyCommand(
                "\"C:\\Users\\tester\\AppData\\Local\\Microsoft\\WindowsApps\\pwsh.exe\" -Command \"Get-Content -LiteralPath \\\"E:\\MyCodex\\custom-permission-file-test.txt\\\"\"");

            Assert.That(classification.Category, Is.EqualTo(ApprovalRequestCategory.FileRead));
            Assert.That(classification.Confidence, Is.EqualTo(ApprovalRequestConfidence.HighConfidenceCommand));
            Assert.That(classification.DisplayAction, Is.EqualTo("read file"));
            Assert.That(classification.DisplayCommand, Is.EqualTo("Get-Content -LiteralPath E:\\MyCodex\\custom-permission-file-test.txt"));
            Assert.That(classification.Targets, Is.EqualTo(new[] { @"E:\MyCodex\custom-permission-file-test.txt" }));
            Assert.That(classification.ShellKind, Is.EqualTo("pwsh"));
        }

        [Test]
        public void ClassifyCommand_QuotesDisplayArgumentsThatContainSpacesAfterShellUnwrap()
        {
            var classification = ApprovalRequestClassifier.ClassifyCommand(
                "pwsh.exe -Command \"Get-Content -LiteralPath \\\"E:\\My Codex\\notes.txt\\\"\"");

            Assert.That(classification.DisplayCommand, Is.EqualTo("Get-Content -LiteralPath \"E:\\My Codex\\notes.txt\""));
            Assert.That(classification.Targets, Is.EqualTo(new[] { @"E:\My Codex\notes.txt" }));
        }

        [Test]
        public void ClassifyCommand_NewItemTreatsItemTypeAsOptionValueNotTarget()
        {
            var classification = ApprovalRequestClassifier.ClassifyCommand(
                "New-Item -Path approval-classification-newitem-test.txt -ItemType File -Value \"上午好\"");

            Assert.That(classification.Category, Is.EqualTo(ApprovalRequestCategory.FileCreate));
            Assert.That(classification.Targets, Is.EqualTo(new[] { "approval-classification-newitem-test.txt" }));
        }

        [Test]
        public void ClassifyCommand_SplitsGitInspectAndDestructiveCommands()
        {
            var status = ApprovalRequestClassifier.ClassifyCommand("pwsh.exe -Command 'git status --short'");
            var clean = ApprovalRequestClassifier.ClassifyCommand("git clean -fdx");

            Assert.That(status.Category, Is.EqualTo(ApprovalRequestCategory.GitInspect));
            Assert.That(status.DisplayCommand, Is.EqualTo("git status --short"));
            Assert.That(clean.Category, Is.EqualTo(ApprovalRequestCategory.GitDestructive));
        }

        [Test]
        public void ClassifyCommand_SplitsGitInitializationAndConfigurationCommands()
        {
            var init = ApprovalRequestClassifier.ClassifyCommand("git init --bare ..\\approval-git-popup-remote.git");
            var config = ApprovalRequestClassifier.ClassifyCommand("git config user.name \"Approval Test\"");
            var remoteAdd = ApprovalRequestClassifier.ClassifyCommand("git remote add origin ..\\approval-git-popup-remote.git");
            var remoteList = ApprovalRequestClassifier.ClassifyCommand("git remote -v");

            Assert.That(init.Category, Is.EqualTo(ApprovalRequestCategory.GitInitialize));
            Assert.That(config.Category, Is.EqualTo(ApprovalRequestCategory.GitLocalConfiguration));
            Assert.That(remoteAdd.Category, Is.EqualTo(ApprovalRequestCategory.GitLocalConfiguration));
            Assert.That(remoteList.Category, Is.EqualTo(ApprovalRequestCategory.GitInspect));
        }

        [Test]
        public void ClassifyCommand_LeavesCompoundScriptsGeneric()
        {
            var classification = ApprovalRequestClassifier.ClassifyCommand(
                "git config user.email approval-test@example.invalid; git config user.name \"Approval Test\"; Set-Content -LiteralPath README.md -Value \"hello\" -Encoding UTF8");

            Assert.That(classification.Category, Is.EqualTo(ApprovalRequestCategory.CommandGeneric));
            Assert.That(classification.Confidence, Is.EqualTo(ApprovalRequestConfidence.Unknown));
            Assert.That(classification.Targets, Is.Empty);
        }

        [TestCase("git status | powershell -c calc")]
        [TestCase("git status & echo second")]
        [TestCase("git status\necho second")]
        [TestCase("git log $(curl x)")]
        [TestCase("git log `curl x`")]
        [TestCase("git status > README.md")]
        [TestCase("git status >> README.md")]
        [TestCase("git status 2>&1|sh")]
        [TestCase("git status > /dev/null;echo second")]
        [TestCase("cat a.txt | sh")]
        [TestCase("Get-Content a.txt | iex")]
        [TestCase("git log (Invoke-Expression calc)")]
        [TestCase("cat <(curl x)")]
        [TestCase("pwsh.exe -Command \"git status; calc\"")]
        [TestCase("bash -lc \"git log \\\"$(curl x)\\\"\"")]
        [TestCase("cmd.exe /c git status & calc")]
        [TestCase("cmd.exe /c \"git status 'x & calc'\"")]
        public void ClassifyCommand_TreatsCommandChainsPipesSubstitutionAndFileRedirectionAsGeneric(string command)
        {
            var classification = ApprovalRequestClassifier.ClassifyCommand(command);

            Assert.That(classification.Category, Is.EqualTo(ApprovalRequestCategory.CommandGeneric));
            Assert.That(classification.Confidence, Is.EqualTo(ApprovalRequestConfidence.Unknown));
        }

        [TestCase("git status 2>&1")]
        [TestCase("git status > /dev/null")]
        [TestCase("git status 2>$null")]
        [TestCase("git status > NUL")]
        [TestCase("pwsh.exe -Command \"& 'C:\\Program Files\\Git\\cmd\\git.exe' status\"")]
        [TestCase("bash -lc \"git log --grep 'a|b;c'\"")]
        public void ClassifyCommand_KeepsSingleCommandWithDiscardRedirectionOrQuotedOperators(string command)
        {
            var classification = ApprovalRequestClassifier.ClassifyCommand(command);

            Assert.That(classification.Category, Is.EqualTo(ApprovalRequestCategory.GitInspect));
        }

        [Test]
        public void ClassifyFileChange_DetectsMixedProtocolChanges()
        {
            var classification = ApprovalRequestClassifier.ClassifyFileChangeDetails(
                "Added src\\new.cs\r\nDeleted src\\old.cs");

            Assert.That(classification.Category, Is.EqualTo(ApprovalRequestCategory.FileMixed));
            Assert.That(classification.Confidence, Is.EqualTo(ApprovalRequestConfidence.ExactFromProtocol));
            Assert.That(classification.Targets, Is.EqualTo(new[] { @"src\new.cs", @"src\old.cs" }));
        }
    }
}
