using System;
using System.Collections.Generic;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal enum ApprovalRequestActionKey
    {
        Generic,
        Command,
        Read,
        Create,
        Build,
        Test,
        Install,
        GitInspect,
        GitStage,
        GitLocalCommit,
        GitLocalWorkingTreeChange,
        GitRemoteRead,
        GitRemoteWrite,
        GitDestructive,
        GitInitialize,
        GitLocalConfiguration,
        Delete,
        Write,
        Modify,
        Move,
        Change,
        Permissions
    }

    internal sealed class ApprovalRequestSummary
    {
        public string SpeechSummary { get; set; }

        public string DialogSummary { get; set; }

        public string WindowTitle { get; set; }

        public string QuestionText { get; set; }

        public ApprovalRequestActionKey ActionKey { get; set; }

        public string DetailsText { get; set; }

        public string DecisionAccessibleName { get; set; }
    }

    internal static class ApprovalRequestSummaryBuilder
    {
        private const int MaximumSpeechLength = 220;

        internal static ApprovalRequestSummary Build(AppServerEventArgs request)
        {
            if (request == null)
            {
                return BuildGenericSummary(null);
            }

            switch (request.ApprovalKind)
            {
                case AppServerApprovalRequestKind.Command:
                    return BuildCommandSummary(request);
                case AppServerApprovalRequestKind.FileChange:
                    return BuildFileChangeSummary(request);
                case AppServerApprovalRequestKind.Permissions:
                    return BuildPermissionsSummary(request);
                default:
                    return BuildGenericSummary(request);
            }
        }

        private static ApprovalRequestSummary BuildCommandSummary(AppServerEventArgs request)
        {
            var classification = request.ApprovalClassification ?? ApprovalRequestClassifier.ClassifyCommand(request.CommandText);
            var actionKey = ResolveClassificationActionKey(classification);
            var action = ResolveCommandAction(actionKey);
            var targets = classification != null && classification.Targets != null && classification.Targets.Count > 0
                ? new List<string>(classification.Targets)
                : new List<string>();
            var targetText = BuildTargetText(targets);
            var dialogSummary = string.IsNullOrWhiteSpace(targetText)
                ? LF("ApprovalRequestSummary.Command.Dialog.NoTarget.Template", "Approval request: {0}.", action)
                : LF("ApprovalRequestSummary.Command.Dialog.Template", "Approval request: {0}. Target: {1}.", action, targetText);
            var speech = AppendSentence(dialogSummary, BuildWorkingDirectorySentence(request.WorkingDirectory));
            var displayCommand = classification == null || string.IsNullOrWhiteSpace(classification.DisplayCommand)
                ? request.CommandText
                : classification.DisplayCommand;
            var rawCommand = classification == null || string.IsNullOrWhiteSpace(classification.RawCommand)
                ? request.CommandText
                : classification.RawCommand;
            var details = new List<string>
            {
                dialogSummary
            };
            AddOptionalLine(details, L("ApprovalRequestSummary.Label.WorkingDirectory", "Working directory"), request.WorkingDirectory);
            AddOptionalLine(details, L("ApprovalRequestSummary.Label.Reason", "Reason"), request.Reason);
            AddOptionalBlock(details, request.Details);
            AddOptionalLine(details, L("ApprovalRequestSummary.Label.FullCommand", "Full command"), displayCommand);
            if (!string.Equals(displayCommand, rawCommand, StringComparison.Ordinal))
            {
                AddOptionalLine(details, L("ApprovalRequestSummary.Label.OriginalCommand", "Original command"), rawCommand);
            }

            return BuildResult(
                actionKey,
                BuildWindowTitle(actionKey),
                BuildQuestionText(actionKey, targetText, displayCommand),
                dialogSummary,
                speech,
                details);
        }

        private static ApprovalRequestSummary BuildFileChangeSummary(AppServerEventArgs request)
        {
            var writeRoot = ExtractKnownDetailValue(request.Details, "Write root:", "写入根目录：");
            var detailTargets = ExtractFileChangeTargets(request.Details);
            var classification = request.ApprovalClassification ?? ApprovalRequestClassifier.ClassifyFileChangeDetails(request.Details);
            var targetText = BuildFileChangeTargetText(classification, detailTargets, request.ChangedFiles);
            var reason = NormalizeWhitespace(request.Reason);
            var actionKey = ApprovalRequestActionKey.Change;
            string dialogSummary;
            if (!string.IsNullOrWhiteSpace(targetText))
            {
                dialogSummary = LF(
                    "ApprovalRequestSummary.FileChange.DialogWithTarget.Template",
                    "Approval request: {0}. Target: {1}.",
                    ResolveFileChangeAction(actionKey),
                    targetText);
            }
            else
            {
                dialogSummary = string.IsNullOrWhiteSpace(writeRoot)
                    ? L("ApprovalRequestSummary.FileChange.Dialog", "Approval request: file changes.")
                    : LF("ApprovalRequestSummary.FileChange.DialogWithRoot.Template", "Approval request: change files. Write root: {0}.", writeRoot);
            }

            var speech = AppendSentence(dialogSummary, string.IsNullOrWhiteSpace(reason) ? null : reason);
            var details = new List<string>
            {
                dialogSummary
            };
            AddOptionalLine(details, L("ApprovalRequestSummary.Label.Reason", "Reason"), request.Reason);
            AddOptionalBlock(details, request.Details);
            return BuildResult(
                actionKey,
                BuildWindowTitle(ApprovalRequestActionKey.Change),
                BuildFileChangeQuestionText(targetText, writeRoot),
                dialogSummary,
                speech,
                details);
        }

        private static ApprovalRequestSummary BuildPermissionsSummary(AppServerEventArgs request)
        {
            var compactDetails = BuildCompactDetails(request.Details);
            var dialogSummary = string.IsNullOrWhiteSpace(compactDetails)
                ? L("ApprovalRequestSummary.Permissions.Dialog", "Approval request: permissions.")
                : LF("ApprovalRequestSummary.Permissions.DialogWithDetails.Template", "Approval request: permissions. {0}.", compactDetails);
            var speech = AppendSentence(dialogSummary, NormalizeWhitespace(request.Reason));
            var details = new List<string>
            {
                dialogSummary
            };
            AddOptionalLine(details, L("ApprovalRequestSummary.Label.Reason", "Reason"), request.Reason);
            AddOptionalBlock(details, request.Details);
            return BuildResult(
                ApprovalRequestActionKey.Permissions,
                BuildWindowTitle(ApprovalRequestActionKey.Permissions),
                L("ApprovalRequestSummary.Question.Permissions", "Allow this permission request?"),
                dialogSummary,
                speech,
                details);
        }

        private static ApprovalRequestSummary BuildGenericSummary(AppServerEventArgs request)
        {
            var content = request == null ? null : request.Content;
            var dialogSummary = string.IsNullOrWhiteSpace(content)
                ? L("ApprovalRequestSummary.Generic.Dialog", "Approval request.")
                : NormalizeWhitespace(content);
            return BuildResult(
                ApprovalRequestActionKey.Generic,
                BuildWindowTitle(ApprovalRequestActionKey.Generic),
                L("ApprovalRequestSummary.Question.Generic", "Allow this request?"),
                dialogSummary,
                dialogSummary,
                new List<string> { dialogSummary });
        }

        private static ApprovalRequestSummary BuildResult(
            ApprovalRequestActionKey actionKey,
            string windowTitle,
            string questionText,
            string dialogSummary,
            string speechSummary,
            IList<string> details)
        {
            var speech = Truncate(NormalizeWhitespace(string.IsNullOrWhiteSpace(questionText) ? speechSummary : questionText), MaximumSpeechLength);
            return new ApprovalRequestSummary
            {
                ActionKey = actionKey,
                WindowTitle = windowTitle ?? string.Empty,
                QuestionText = questionText ?? string.Empty,
                DialogSummary = dialogSummary ?? string.Empty,
                SpeechSummary = speech,
                DetailsText = string.Join(Environment.NewLine + Environment.NewLine, details ?? new List<string>()).Trim(),
                DecisionAccessibleName = LF(
                    "ApprovalRequestSummary.Decision.Accessible.Template",
                    "Approval decision. {0}",
                    speech)
            };
        }

        private static ApprovalRequestActionKey ResolveClassificationActionKey(ApprovalRequestClassification classification)
        {
            if (classification == null)
            {
                return ApprovalRequestActionKey.Command;
            }

            switch (classification.Category)
            {
                case ApprovalRequestCategory.CommandBuild:
                    return ApprovalRequestActionKey.Build;
                case ApprovalRequestCategory.CommandTest:
                    return ApprovalRequestActionKey.Test;
                case ApprovalRequestCategory.CommandDependencyInstall:
                    return ApprovalRequestActionKey.Install;
                case ApprovalRequestCategory.GitInspect:
                    return ApprovalRequestActionKey.GitInspect;
                case ApprovalRequestCategory.GitStage:
                    return ApprovalRequestActionKey.GitStage;
                case ApprovalRequestCategory.GitLocalCommit:
                    return ApprovalRequestActionKey.GitLocalCommit;
                case ApprovalRequestCategory.GitLocalWorkingTreeChange:
                    return ApprovalRequestActionKey.GitLocalWorkingTreeChange;
                case ApprovalRequestCategory.GitRemoteRead:
                    return ApprovalRequestActionKey.GitRemoteRead;
                case ApprovalRequestCategory.GitRemoteWrite:
                    return ApprovalRequestActionKey.GitRemoteWrite;
                case ApprovalRequestCategory.GitDestructive:
                    return ApprovalRequestActionKey.GitDestructive;
                case ApprovalRequestCategory.GitInitialize:
                    return ApprovalRequestActionKey.GitInitialize;
                case ApprovalRequestCategory.GitLocalConfiguration:
                    return ApprovalRequestActionKey.GitLocalConfiguration;
                case ApprovalRequestCategory.FileRead:
                    return ApprovalRequestActionKey.Read;
                case ApprovalRequestCategory.FileWrite:
                    return ApprovalRequestActionKey.Write;
                case ApprovalRequestCategory.FileCreate:
                    return ApprovalRequestActionKey.Create;
                case ApprovalRequestCategory.FileModify:
                    return ApprovalRequestActionKey.Modify;
                case ApprovalRequestCategory.FileDelete:
                    return ApprovalRequestActionKey.Delete;
                case ApprovalRequestCategory.FileMove:
                    return ApprovalRequestActionKey.Move;
                case ApprovalRequestCategory.FileMixed:
                    return ApprovalRequestActionKey.Change;
                default:
                    return ApprovalRequestActionKey.Command;
            }
        }

        private static string ResolveCommandAction(ApprovalRequestActionKey actionKey)
        {
            switch (actionKey)
            {
                case ApprovalRequestActionKey.Read:
                    return L("ApprovalRequestSummary.Command.Action.Read", "read files");
                case ApprovalRequestActionKey.Create:
                    return L("ApprovalRequestSummary.Command.Action.Create", "create files");
                case ApprovalRequestActionKey.Build:
                    return L("ApprovalRequestSummary.Command.Action.Build", "run build");
                case ApprovalRequestActionKey.Test:
                    return L("ApprovalRequestSummary.Command.Action.Test", "run tests");
                case ApprovalRequestActionKey.Install:
                    return L("ApprovalRequestSummary.Command.Action.Install", "install dependencies");
                case ApprovalRequestActionKey.GitInspect:
                    return L("ApprovalRequestSummary.Command.Action.GitInspect", "inspect Git repository");
                case ApprovalRequestActionKey.GitStage:
                    return L("ApprovalRequestSummary.Command.Action.GitStage", "stage Git changes");
                case ApprovalRequestActionKey.GitLocalCommit:
                    return L("ApprovalRequestSummary.Command.Action.GitLocalCommit", "create local Git history");
                case ApprovalRequestActionKey.GitLocalWorkingTreeChange:
                    return L("ApprovalRequestSummary.Command.Action.GitLocalWorkingTreeChange", "change Git working tree");
                case ApprovalRequestActionKey.GitRemoteRead:
                    return L("ApprovalRequestSummary.Command.Action.GitRemoteRead", "read Git remote");
                case ApprovalRequestActionKey.GitRemoteWrite:
                    return L("ApprovalRequestSummary.Command.Action.GitRemoteWrite", "write Git remote");
                case ApprovalRequestActionKey.GitDestructive:
                    return L("ApprovalRequestSummary.Command.Action.GitDestructive", "run destructive Git operation");
                case ApprovalRequestActionKey.GitInitialize:
                    return L("ApprovalRequestSummary.Command.Action.GitInitialize", "initialize Git repository");
                case ApprovalRequestActionKey.GitLocalConfiguration:
                    return L("ApprovalRequestSummary.Command.Action.GitLocalConfiguration", "change Git configuration");
                case ApprovalRequestActionKey.Delete:
                    return L("ApprovalRequestSummary.Command.Action.Delete", "delete files");
                case ApprovalRequestActionKey.Write:
                    return L("ApprovalRequestSummary.Command.Action.Write", "write files");
                default:
                    return L("ApprovalRequestSummary.Command.Action.Generic", "execute command");
            }
        }

        private static string ResolveFileChangeAction(ApprovalRequestActionKey actionKey)
        {
            switch (actionKey)
            {
                case ApprovalRequestActionKey.Delete:
                    return L("ApprovalRequestSummary.FileChange.Action.Delete", "delete files");
                case ApprovalRequestActionKey.Create:
                    return L("ApprovalRequestSummary.FileChange.Action.Create", "create files");
                case ApprovalRequestActionKey.Write:
                    return L("ApprovalRequestSummary.FileChange.Action.Write", "write files");
                case ApprovalRequestActionKey.Modify:
                    return L("ApprovalRequestSummary.FileChange.Action.Modify", "modify files");
                case ApprovalRequestActionKey.Move:
                    return L("ApprovalRequestSummary.FileChange.Action.Move", "move files");
                default:
                    return L("ApprovalRequestSummary.FileChange.Action.Change", "change files");
            }
        }

        private static string BuildFileChangeQuestionText(string targetText, string writeRoot)
        {
            if (!string.IsNullOrWhiteSpace(targetText))
            {
                return LF(
                    "ApprovalRequestSummary.Question.FileChange.ChangeFiles.Template",
                    "Change these files: {0}?",
                    targetText);
            }

            if (!string.IsNullOrWhiteSpace(writeRoot))
            {
                return LF(
                    "ApprovalRequestSummary.Question.FileChange.ChangeRoot.Template",
                    "Change files under {0}?",
                    writeRoot);
            }

            return L("ApprovalRequestSummary.Question.FileChange.ChangeGeneric", "Change files?");
        }

        private static string BuildFileChangeTargetText(
            ApprovalRequestClassification classification,
            IList<FileChangeTarget> detailTargets,
            IReadOnlyList<string> changedFiles)
        {
            if (detailTargets != null && detailTargets.Count > 0)
            {
                return BuildTargetText(GetFileChangeTargetDescriptions(detailTargets));
            }

            if (classification != null && classification.Targets != null && classification.Targets.Count > 0)
            {
                return BuildTargetText(classification.Targets);
            }

            if (changedFiles != null && changedFiles.Count > 0)
            {
                return BuildTargetText(GetChangedFileTargets(changedFiles));
            }

            return string.Empty;
        }

        private static List<string> GetChangedFileTargets(IReadOnlyList<string> changedFiles)
        {
            var result = new List<string>();
            if (changedFiles == null)
            {
                return result;
            }

            for (var i = 0; i < changedFiles.Count; i++)
            {
                AddTarget(result, changedFiles[i]);
            }

            return result;
        }

        private static List<string> GetFileChangeTargetDescriptions(IList<FileChangeTarget> targets)
        {
            var result = new List<string>();
            if (targets == null)
            {
                return result;
            }

            for (var i = 0; i < targets.Count; i++)
            {
                var description = BuildFileChangeTargetDescription(targets[i]);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    result.Add(description);
                }
            }

            return result;
        }

        private static string BuildFileChangeTargetDescription(FileChangeTarget target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.Path))
            {
                return string.Empty;
            }

            switch (target.Kind)
            {
                case FileChangeTargetKind.Delete:
                    return LF("ApprovalRequestSummary.FileChange.Target.Delete.Template", "delete {0}", target.Path);
                case FileChangeTargetKind.Add:
                    return LF("ApprovalRequestSummary.FileChange.Target.Create.Template", "create {0}", target.Path);
                case FileChangeTargetKind.Move:
                    return LF("ApprovalRequestSummary.FileChange.Target.Move.Template", "move {0}", target.Path);
                case FileChangeTargetKind.Modify:
                    return LF("ApprovalRequestSummary.FileChange.Target.Modify.Template", "modify {0}", target.Path);
                default:
                    return target.Path;
            }
        }

        private static string BuildWindowTitle(ApprovalRequestActionKey actionKey)
        {
            switch (actionKey)
            {
                case ApprovalRequestActionKey.Read:
                    return L("ApprovalRequestForm.Title.Read", "Read Request");
                case ApprovalRequestActionKey.Create:
                    return L("ApprovalRequestForm.Title.Create", "Create Request");
                case ApprovalRequestActionKey.Delete:
                    return L("ApprovalRequestForm.Title.Delete", "Delete Request");
                case ApprovalRequestActionKey.Write:
                    return L("ApprovalRequestForm.Title.Write", "Write Request");
                case ApprovalRequestActionKey.Modify:
                    return L("ApprovalRequestForm.Title.Modify", "Modify Request");
                case ApprovalRequestActionKey.Change:
                    return L("ApprovalRequestForm.Title.Change", "Change Request");
                case ApprovalRequestActionKey.Move:
                    return L("ApprovalRequestForm.Title.Move", "Move Request");
                case ApprovalRequestActionKey.Permissions:
                    return L("ApprovalRequestForm.Title.Permissions", "Permission Request");
                case ApprovalRequestActionKey.Command:
                case ApprovalRequestActionKey.Build:
                case ApprovalRequestActionKey.Test:
                case ApprovalRequestActionKey.Install:
                case ApprovalRequestActionKey.GitInspect:
                case ApprovalRequestActionKey.GitStage:
                case ApprovalRequestActionKey.GitLocalCommit:
                case ApprovalRequestActionKey.GitLocalWorkingTreeChange:
                case ApprovalRequestActionKey.GitRemoteRead:
                case ApprovalRequestActionKey.GitRemoteWrite:
                case ApprovalRequestActionKey.GitDestructive:
                case ApprovalRequestActionKey.GitInitialize:
                case ApprovalRequestActionKey.GitLocalConfiguration:
                    return L("ApprovalRequestForm.Title.Command", "Command Request");
                default:
                    return L("ApprovalRequestForm.Title", "Approval Request");
            }
        }

        private static string BuildQuestionText(ApprovalRequestActionKey actionKey, string targetText, string fallbackText)
        {
            var value = !string.IsNullOrWhiteSpace(targetText) ? targetText : BuildFallbackQuestionValue(fallbackText);
            switch (actionKey)
            {
                case ApprovalRequestActionKey.Read:
                    return LF("ApprovalRequestSummary.Question.FileChange.Read.Template", "Read {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Files", "files") : value);
                case ApprovalRequestActionKey.Delete:
                    return LF("ApprovalRequestSummary.Question.FileChange.Delete.Template", "Delete {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Files", "files") : value);
                case ApprovalRequestActionKey.Create:
                    return LF("ApprovalRequestSummary.Question.FileChange.Create.Template", "Create {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Files", "files") : value);
                case ApprovalRequestActionKey.Write:
                    return LF("ApprovalRequestSummary.Question.FileChange.Write.Template", "Write {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Files", "files") : value);
                case ApprovalRequestActionKey.Modify:
                    return LF("ApprovalRequestSummary.Question.FileChange.Modify.Template", "Modify {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Files", "files") : value);
                case ApprovalRequestActionKey.Move:
                    return LF("ApprovalRequestSummary.Question.FileChange.Move.Template", "Move {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Files", "files") : value);
                case ApprovalRequestActionKey.Build:
                    return L("ApprovalRequestSummary.Question.Command.Build", "Run build?");
                case ApprovalRequestActionKey.Test:
                    return L("ApprovalRequestSummary.Question.Command.Test", "Run tests?");
                case ApprovalRequestActionKey.Install:
                    return L("ApprovalRequestSummary.Question.Command.Install", "Install dependencies?");
                case ApprovalRequestActionKey.GitInspect:
                    return LF("ApprovalRequestSummary.Question.Command.GitInspect.Template", "Inspect Git repository: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitStage:
                    return LF("ApprovalRequestSummary.Question.Command.GitStage.Template", "Stage Git changes: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitLocalCommit:
                    return LF("ApprovalRequestSummary.Question.Command.GitLocalCommit.Template", "Create local Git history: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitLocalWorkingTreeChange:
                    return LF("ApprovalRequestSummary.Question.Command.GitLocalWorkingTreeChange.Template", "Change Git working tree: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitRemoteRead:
                    return LF("ApprovalRequestSummary.Question.Command.GitRemoteRead.Template", "Read Git remote: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitRemoteWrite:
                    return LF("ApprovalRequestSummary.Question.Command.GitRemoteWrite.Template", "Write Git remote: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitDestructive:
                    return LF("ApprovalRequestSummary.Question.Command.GitDestructive.Template", "Run destructive Git operation: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitInitialize:
                    return LF("ApprovalRequestSummary.Question.Command.GitInitialize.Template", "Initialize Git repository: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.GitLocalConfiguration:
                    return LF("ApprovalRequestSummary.Question.Command.GitLocalConfiguration.Template", "Change Git configuration: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
                case ApprovalRequestActionKey.Change:
                    return LF("ApprovalRequestSummary.Question.FileChange.Change.Template", "Change {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Files", "files") : value);
                default:
                    return LF("ApprovalRequestSummary.Question.Command.Generic.Template", "Run command: {0}?", string.IsNullOrWhiteSpace(value) ? L("ApprovalRequestSummary.Value.Command", "command") : value);
            }
        }

        private static string BuildFallbackQuestionValue(string fallbackText)
        {
            return NormalizeWhitespace(fallbackText);
        }

        private static List<FileChangeTarget> ExtractFileChangeTargets(string details)
        {
            var result = new List<FileChangeTarget>();
            if (string.IsNullOrWhiteSpace(details))
            {
                return result;
            }

            var lines = details.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = NormalizeWhitespace(lines[i]);
                AddFileChangeTargetIfMatched(result, line, "Deleted ", FileChangeTargetKind.Delete);
                AddFileChangeTargetIfMatched(result, line, "已删除 ", FileChangeTargetKind.Delete);
                AddFileChangeTargetIfMatched(result, line, "Added ", FileChangeTargetKind.Add);
                AddFileChangeTargetIfMatched(result, line, "已新增 ", FileChangeTargetKind.Add);
                AddFileChangeTargetIfMatched(result, line, "Modified ", FileChangeTargetKind.Modify);
                AddFileChangeTargetIfMatched(result, line, "已修改 ", FileChangeTargetKind.Modify);
                AddFileChangeTargetIfMatched(result, line, "Moved ", FileChangeTargetKind.Move);
                AddFileChangeTargetIfMatched(result, line, "已移动 ", FileChangeTargetKind.Move);
                AddFileChangeTargetIfMatched(result, line, "Processed ", FileChangeTargetKind.Unknown);
                AddFileChangeTargetIfMatched(result, line, "已处理 ", FileChangeTargetKind.Unknown);
            }

            return result;
        }

        private static void AddFileChangeTargetIfMatched(ICollection<FileChangeTarget> targets, string line, string prefix, FileChangeTargetKind kind)
        {
            if (targets == null ||
                string.IsNullOrWhiteSpace(line) ||
                string.IsNullOrWhiteSpace(prefix) ||
                !line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var path = line.Substring(prefix.Length).Trim();
            if (kind == FileChangeTargetKind.Move)
            {
                path = NormalizeMoveTarget(path);
            }

            if (!string.IsNullOrWhiteSpace(path))
            {
                targets.Add(new FileChangeTarget(kind, path));
            }
        }

        private static string NormalizeMoveTarget(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = path.Trim();
            var separatorIndex = normalized.IndexOf(" -> ", StringComparison.Ordinal);
            return separatorIndex > 0 ? normalized.Substring(0, separatorIndex).Trim() : normalized;
        }

        private static void AddTarget(ICollection<string> targets, string token)
        {
            var normalized = NormalizeWhitespace(token);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                targets.Add(normalized);
            }
        }

        private static string BuildTargetText(IList<string> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                return string.Empty;
            }

            if (targets.Count == 1)
            {
                return targets[0];
            }

            var displayCount = Math.Min(3, targets.Count);
            var builder = new StringBuilder();
            builder.Append(targets[0]);
            for (var i = 1; i < displayCount; i++)
            {
                builder.Append("; ");
                builder.Append(targets[i]);
            }

            if (targets.Count > displayCount)
            {
                builder.Append("; ");
                builder.Append(LF("ApprovalRequestSummary.Targets.More.Template", "and {0} more", targets.Count - displayCount));
            }

            return builder.ToString();
        }

        private static string BuildWorkingDirectorySentence(string workingDirectory)
        {
            return string.IsNullOrWhiteSpace(workingDirectory)
                ? null
                : LF("ApprovalRequestSummary.WorkingDirectory.Sentence.Template", "Working directory: {0}.", workingDirectory.Trim());
        }

        private static string BuildCompactDetails(string details)
        {
            if (string.IsNullOrWhiteSpace(details))
            {
                return string.Empty;
            }

            var lines = details.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var parts = new List<string>();
            for (var i = 0; i < lines.Length && parts.Count < 3; i++)
            {
                var line = NormalizeWhitespace(lines[i]);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    parts.Add(line);
                }
            }

            return string.Join("; ", parts.ToArray());
        }

        private static string ExtractKnownDetailValue(string details, params string[] prefixes)
        {
            if (string.IsNullOrWhiteSpace(details) || prefixes == null || prefixes.Length == 0)
            {
                return string.Empty;
            }

            var lines = details.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                for (var j = 0; j < prefixes.Length; j++)
                {
                    var prefix = prefixes[j];
                    if (!string.IsNullOrWhiteSpace(prefix) &&
                        line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        return line.Substring(prefix.Length).Trim();
                    }
                }
            }

            return string.Empty;
        }

        private static void AddOptionalLine(ICollection<string> lines, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add(label + ": " + value.Trim());
            }
        }

        private static void AddOptionalBlock(ICollection<string> lines, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add(value.Trim());
            }
        }

        private static string AppendSentence(string first, string second)
        {
            if (string.IsNullOrWhiteSpace(second))
            {
                return first ?? string.Empty;
            }

            return (first ?? string.Empty).Trim() + " " + second.Trim();
        }

        private static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var previousWasWhitespace = false;
            foreach (var ch in text.Trim())
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                    }

                    previousWasWhitespace = true;
                    continue;
                }

                builder.Append(ch);
                previousWasWhitespace = false;
            }

            return builder.ToString();
        }

        private static string Truncate(string text, int maximumLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maximumLength)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, Math.Max(0, maximumLength - 3)).TrimEnd() + "...";
        }

        private static string L(string key, string fallback)
        {
            return UiLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(UiLocalization.Text(key, fallback), args);
        }

        private enum FileChangeTargetKind
        {
            Unknown,
            Add,
            Delete,
            Modify,
            Move
        }

        private sealed class FileChangeTarget
        {
            public FileChangeTarget(FileChangeTargetKind kind, string path)
            {
                Kind = kind;
                Path = path;
            }

            public FileChangeTargetKind Kind { get; private set; }

            public string Path { get; private set; }
        }
    }
}
