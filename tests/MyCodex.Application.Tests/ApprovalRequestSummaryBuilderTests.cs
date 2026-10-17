using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ApprovalRequestSummaryBuilderTests
    {
        [Test]
        public void Build_CommandDeletionSummaryIncludesTargetAndFullDetails()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: Remove-Item",
                requestId: "approval-1",
                commandText: "Remove-Item -LiteralPath \"src\\old file.txt\" -Force",
                workingDirectory: @"E:\MyCodex",
                details: "Command actions: 1",
                approvalKind: AppServerApprovalRequestKind.Command);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.SpeechSummary, Does.Contain("src\\old file.txt"));
            Assert.That(summary.SpeechSummary.Length, Is.LessThanOrEqualTo(220));
            Assert.That(summary.DialogSummary, Does.Contain("src\\old file.txt"));
            Assert.That(summary.DetailsText, Does.Contain("Remove-Item -LiteralPath \"src\\old file.txt\" -Force"));
            Assert.That(summary.DetailsText, Does.Contain(@"E:\MyCodex"));
            Assert.That(summary.DetailsText, Does.Contain("Command actions: 1"));
            Assert.That(summary.DecisionAccessibleName, Does.Contain("src\\old file.txt"));
        }

        [Test]
        public void Build_WrappedPowerShellDeletionCommandSpeaksDeleteTarget()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: powershell",
                requestId: "approval-wrapped-delete",
                commandText: "powershell.exe -NoProfile -Command \"Remove-Item -LiteralPath 'docs\\superpowers' -Recurse\"",
                workingDirectory: @"E:\MyCodex",
                details: "Command actions: 1",
                approvalKind: AppServerApprovalRequestKind.Command);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.WindowTitle, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Delete", "Delete Request")));
            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Delete));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.Delete.Template", "Delete {0}?"),
                "docs\\superpowers")));
            Assert.That(summary.SpeechSummary, Is.EqualTo(summary.QuestionText));
            Assert.That(summary.DetailsText, Does.Contain("powershell.exe -NoProfile -Command"));
        }

        [Test]
        public void Build_SetContentCommandSpeaksWriteTarget()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: Set-Content",
                requestId: "approval-write",
                commandText: "Set-Content -LiteralPath approval-flow-test.txt -Value \"approval flow test\"",
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.WindowTitle, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Write", "Write Request")));
            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Write));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.Write.Template", "Write {0}?"),
                "approval-flow-test.txt")));
        }

        [Test]
        public void Build_WrappedPowerShellGetContentCommandSpeaksReadTargetWithoutShellNoise()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: pwsh",
                requestId: "approval-read",
                commandText: "\"C:\\Users\\tester\\AppData\\Local\\Microsoft\\WindowsApps\\pwsh.exe\" -Command \"Get-Content -LiteralPath \\\"E:\\MyCodex\\custom-permission-file-test.txt\\\"\"",
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.WindowTitle, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Read", "Read Request")));
            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Read));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.Read.Template", "Read {0}?"),
                @"E:\MyCodex\custom-permission-file-test.txt")));
            Assert.That(summary.SpeechSummary, Is.EqualTo(summary.QuestionText));
            Assert.That(summary.SpeechSummary, Does.Not.Contain("pwsh.exe"));
            Assert.That(summary.SpeechSummary, Does.Not.Contain("-Command"));
            Assert.That(summary.DetailsText, Does.Contain("Get-Content -LiteralPath E:\\MyCodex\\custom-permission-file-test.txt"));
            Assert.That(summary.DetailsText, Does.Contain(UiLocalization.Text("ApprovalRequestSummary.Label.OriginalCommand", "Original command")));
            Assert.That(summary.DetailsText, Does.Contain("pwsh.exe"));
        }

        [Test]
        public void Build_GitWorkingTreeCommandUsesSpecificGitAction()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: git restore",
                requestId: "approval-git-worktree",
                commandText: "pwsh.exe -Command \"git restore src\\file.txt\"",
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.GitLocalWorkingTreeChange));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.Command.GitLocalWorkingTreeChange.Template", "Change Git working tree: {0}?"),
                "git restore src\\file.txt")));
            Assert.That(summary.SpeechSummary, Does.Not.Contain("pwsh.exe"));
            Assert.That(summary.SpeechSummary, Does.Not.Contain("-Command"));
            Assert.That(summary.DetailsText, Does.Contain("git restore src\\file.txt"));
            Assert.That(summary.DetailsText, Does.Contain("pwsh.exe"));
        }

        [Test]
        public void Build_GitRemoteAddCommandUsesConfigurationAction()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: git remote add",
                requestId: "approval-git-remote-add",
                commandText: "git remote add origin ..\\approval-git-popup-remote.git",
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.GitLocalConfiguration));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.Command.GitLocalConfiguration.Template", "Change Git configuration: {0}?"),
                "git remote add origin ..\\approval-git-popup-remote.git")));
        }

        [Test]
        public void Build_CompoundCommandDoesNotInventFileTargets()
        {
            var commandText = "$workspace = (Resolve-Path .).Path; $root = Join-Path $workspace 'temp\\approval-git-popup-test'; if (Test-Path -LiteralPath $root) { $full = (Resolve-Path -LiteralPath $root).Path; Remove-Item -LiteralPath $full -Recurse -Force }; New-Item -ItemType Directory -Path $root | Out-Null; git init $root";
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received command approval request: pwsh",
                requestId: "approval-compound-script",
                commandText: commandText,
                workingDirectory: @"E:\MyCodex",
                approvalKind: AppServerApprovalRequestKind.Command);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Command));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.Command.Generic.Template", "Run command: {0}?"),
                commandText)));
            Assert.That(summary.QuestionText, Does.Contain("$full"));
            Assert.That(summary.QuestionText, Does.Contain("New-Item"));
            Assert.That(summary.QuestionText, Does.Contain("git init $root"));
            Assert.That(summary.DialogSummary, Does.Not.Contain("Target:"));
            Assert.That(summary.DialogSummary, Does.Not.Contain("目标："));
            Assert.That(summary.DetailsText, Does.Contain("New-Item"));
        }

        [Test]
        public void Build_PermissionsSummaryIncludesReadWriteAndNetworkWithoutFullDetailsInSpeech()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received permission approval request: Need broader access",
                requestId: "approval-2",
                reason: "Need broader access",
                details: "Read: E:\\ReadOnly\r\nWrite: E:\\Writable\r\nNetwork: allowed",
                approvalKind: AppServerApprovalRequestKind.Permissions);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.SpeechSummary, Is.EqualTo(UiLocalization.Text("ApprovalRequestSummary.Question.Permissions", "Allow this permission request?")));
            Assert.That(summary.SpeechSummary.Length, Is.LessThanOrEqualTo(220));
            Assert.That(summary.DetailsText, Does.Contain("Need broader access"));
            Assert.That(summary.DetailsText, Does.Contain(@"Read: E:\ReadOnly"));
            Assert.That(summary.DetailsText, Does.Contain(@"Write: E:\Writable"));
        }

        [Test]
        public void Build_FileChangeSummaryIncludesReasonAndWriteRoot()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request: Update generated files",
                requestId: "approval-3",
                reason: "Update generated files",
                details: @"Write root: E:\MyCodex\src",
                approvalKind: AppServerApprovalRequestKind.FileChange);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.WindowTitle, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Change", "Change Request")));
            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Change));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeRoot.Template", "Change files under {0}?"),
                @"E:\MyCodex\src")));
            Assert.That(summary.SpeechSummary, Does.Contain(@"E:\MyCodex\src"));
            Assert.That(summary.DialogSummary, Does.Contain(UiLocalization.Text("ApprovalRequestSummary.FileChange.Action.Change", "change files")));
            Assert.That(summary.DetailsText, Does.Contain("Update generated files"));
            Assert.That(summary.DetailsText, Does.Contain(@"Write root: E:\MyCodex\src"));
        }

        [Test]
        public void Build_FileChangeSummaryUsesProvidedClassificationTarget()
        {
            var classification = new ApprovalRequestClassification
            {
                Category = ApprovalRequestCategory.FileDelete,
                Confidence = ApprovalRequestConfidence.ExactFromProtocol,
                DisplayAction = "delete file"
            };
            classification.Targets.Add(@"src\obsolete.txt");
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request.",
                requestId: "approval-classified-file-change",
                approvalKind: AppServerApprovalRequestKind.FileChange,
                approvalClassification: classification);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.WindowTitle, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Change", "Change Request")));
            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Change));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template", "Change these files: {0}?"),
                @"src\obsolete.txt")));
        }

        [Test]
        public void Build_FileChangeSummarySpeaksDeletedFileWhenDetailsContainChangedFiles()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request.",
                requestId: "approval-4",
                details: "Deleted src\\old file.txt\r\nWrite root: E:\\MyCodex",
                approvalKind: AppServerApprovalRequestKind.FileChange);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.SpeechSummary, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template", "Change these files: {0}?"),
                string.Format(
                    UiLocalization.Text("ApprovalRequestSummary.FileChange.Target.Delete.Template", "delete {0}"),
                    "src\\old file.txt"))));
            Assert.That(summary.WindowTitle, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Change", "Change Request")));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template", "Change these files: {0}?"),
                string.Format(
                    UiLocalization.Text("ApprovalRequestSummary.FileChange.Target.Delete.Template", "delete {0}"),
                    "src\\old file.txt"))));
            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Change));
            Assert.That(summary.DialogSummary, Does.Contain("src\\old file.txt"));
            Assert.That(summary.DetailsText, Does.Contain("Deleted src\\old file.txt"));
        }

        [Test]
        public void Build_FileChangeSummarySpeaksCreatedFileWhenDetailsContainAddedFile()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request.",
                requestId: "approval-added-file",
                details: "Added approval-flow-test.txt",
                approvalKind: AppServerApprovalRequestKind.FileChange);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.WindowTitle, Is.EqualTo(UiLocalization.Text("ApprovalRequestForm.Title.Change", "Change Request")));
            Assert.That(summary.ActionKey, Is.EqualTo(ApprovalRequestActionKey.Change));
            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template", "Change these files: {0}?"),
                string.Format(
                    UiLocalization.Text("ApprovalRequestSummary.FileChange.Target.Create.Template", "create {0}"),
                    "approval-flow-test.txt"))));
            Assert.That(summary.SpeechSummary, Is.EqualTo(summary.QuestionText));
        }

        [Test]
        public void Build_FileChangeSummaryUsesChangedFilesWhenDetailsAreMissing()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request.",
                requestId: "approval-changed-files",
                approvalKind: AppServerApprovalRequestKind.FileChange,
                changedFiles: new[] { "src\\App.cs", "tests\\AppTests.cs" });

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template", "Change these files: {0}?"),
                "src\\App.cs; tests\\AppTests.cs")));
            Assert.That(summary.DialogSummary, Does.Contain("src\\App.cs"));
            Assert.That(summary.DialogSummary, Does.Contain("tests\\AppTests.cs"));
            Assert.That(summary.DetailsText, Does.Contain("src\\App.cs"));
            Assert.That(summary.DetailsText, Does.Contain("tests\\AppTests.cs"));
        }

        [Test]
        public void BuildDisplayRequest_UsesTurnChangedFilesForFileChangeApprovalWhenRequestHasNoTargets()
        {
            var state = new TurnProgressSummaryState();
            state.ChangedFiles.Add("src\\App.cs");
            state.ChangedFiles.Add("tests\\AppTests.cs");
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request.",
                requestId: "approval-display-request",
                approvalKind: AppServerApprovalRequestKind.FileChange);

            var displayRequest = ApprovalRequestDisplayRequestBuilder.Build(request, state);
            var summary = ApprovalRequestSummaryBuilder.Build(displayRequest);

            Assert.That(summary.QuestionText, Is.EqualTo(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template", "Change these files: {0}?"),
                "src\\App.cs; tests\\AppTests.cs")));
        }

        [Test]
        public void Build_FileChangeSummaryCompactsMultipleTargets()
        {
            var request = new AppServerEventArgs(
                "approval_requested",
                "Received file change approval request.",
                requestId: "approval-5",
                details: "Deleted src\\a.txt\r\nDeleted src\\b.txt\r\nDeleted src\\c.txt\r\nDeleted src\\d.txt\r\nDeleted src\\e.txt",
                approvalKind: AppServerApprovalRequestKind.FileChange);

            var summary = ApprovalRequestSummaryBuilder.Build(request);

            Assert.That(summary.QuestionText, Does.Contain("src\\a.txt"));
            Assert.That(summary.QuestionText, Does.Contain("src\\b.txt"));
            Assert.That(summary.QuestionText, Does.Contain("src\\c.txt"));
            Assert.That(summary.QuestionText, Does.Contain(string.Format(
                UiLocalization.Text("ApprovalRequestSummary.Targets.More.Template", "and {0} more"),
                2)));
            Assert.That(summary.QuestionText, Does.Not.Contain("src\\d.txt"));
            Assert.That(summary.QuestionText, Does.Not.Contain("src\\e.txt"));
        }
    }
}
