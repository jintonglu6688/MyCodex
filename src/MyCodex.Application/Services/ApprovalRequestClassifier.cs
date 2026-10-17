using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public static class ApprovalRequestClassifier
    {
        public static ApprovalRequestClassification ClassifyCommand(string commandText)
        {
            var command = NormalizeCommand(commandText);
            var tokens = Tokenize(command.DisplayCommand);
            if (tokens.Count == 0)
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.CommandGeneric,
                    ApprovalRequestConfidence.Unknown,
                    "execute command",
                    command,
                    null);
            }

            tokens = RemoveInvocationOperator(tokens);
            if (tokens.Count == 0)
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.CommandGeneric,
                    ApprovalRequestConfidence.Unknown,
                    "execute command",
                    command,
                    null);
            }

            if (IsCompoundCommand(command))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.CommandGeneric,
                    ApprovalRequestConfidence.Unknown,
                    "execute command",
                    command,
                    null);
            }

            var git = ClassifyGit(tokens, command);
            if (git != null)
            {
                return git;
            }

            var file = ClassifyFileCommand(tokens, command);
            if (file != null)
            {
                return file;
            }

            var searchable = " " + BuildSearchableCommandText(command.DisplayCommand) + " ";
            if (ContainsAny(searchable, " dotnet build ", " msbuild ", " npm run build ", " pnpm build ", " yarn build ", " cargo build ", " go build ", " cmake --build "))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.CommandBuild,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "run build",
                    command,
                    null);
            }

            if (ContainsAny(searchable, " dotnet test ", " vstest ", " npm test ", " pnpm test ", " yarn test ", " cargo test ", " go test ", " pytest "))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.CommandTest,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "run tests",
                    command,
                    null);
            }

            if (ContainsAny(searchable, " npm install ", " pnpm install ", " yarn install ", " pip install ", " dotnet restore ", " nuget restore "))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.CommandDependencyInstall,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "install dependencies",
                    command,
                    null);
            }

            return BuildCommandClassification(
                ApprovalRequestCategory.CommandGeneric,
                ApprovalRequestConfidence.Unknown,
                "execute command",
                command,
                null);
        }

        public static ApprovalRequestClassification ClassifyFileChangeDetails(string details)
        {
            var targets = new List<string>();
            var categories = new HashSet<ApprovalRequestCategory>();

            if (!string.IsNullOrWhiteSpace(details))
            {
                var lines = details.Replace("\r", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < lines.Length; i++)
                {
                    AddFileChangeLine(categories, targets, NormalizeWhitespace(lines[i]));
                }
            }

            ApprovalRequestCategory category;
            if (categories.Count == 0)
            {
                category = ApprovalRequestCategory.FileUnknown;
            }
            else if (categories.Count == 1)
            {
                category = First(categories);
            }
            else
            {
                category = ApprovalRequestCategory.FileMixed;
            }

            return BuildClassification(
                category,
                ApprovalRequestConfidence.ExactFromProtocol,
                GetDisplayAction(category),
                null,
                null,
                null,
                targets);
        }

        public static ApprovalRequestClassification ClassifyPermissionsDetails(string details)
        {
            var categories = new HashSet<ApprovalRequestCategory>();
            if (!string.IsNullOrWhiteSpace(details))
            {
                if (details.IndexOf("Read:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    details.IndexOf("读取：", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    categories.Add(ApprovalRequestCategory.PermissionExtraReadRoots);
                }

                if (details.IndexOf("Write:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    details.IndexOf("写入：", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    categories.Add(ApprovalRequestCategory.PermissionExtraWriteRoots);
                }

                if (details.IndexOf("Network:", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    details.IndexOf("网络：", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    categories.Add(ApprovalRequestCategory.PermissionNetwork);
                }
            }

            var category = categories.Count == 0
                ? ApprovalRequestCategory.PermissionMixed
                : categories.Count == 1 ? First(categories) : ApprovalRequestCategory.PermissionMixed;
            return BuildClassification(
                category,
                ApprovalRequestConfidence.ExactFromProtocol,
                GetDisplayAction(category),
                null,
                null,
                null,
                null);
        }

        public static ApprovalRequestClassification Classify(
            AppServerApprovalRequestKind kind,
            string commandText,
            string details)
        {
            switch (kind)
            {
                case AppServerApprovalRequestKind.Command:
                    return ClassifyCommand(commandText);
                case AppServerApprovalRequestKind.FileChange:
                    return ClassifyFileChangeDetails(details);
                case AppServerApprovalRequestKind.Permissions:
                    return ClassifyPermissionsDetails(details);
                default:
                    return BuildClassification(
                        ApprovalRequestCategory.Unknown,
                        ApprovalRequestConfidence.Unknown,
                        "process request",
                        null,
                        null,
                        null,
                        null);
            }
        }

        private static ApprovalRequestClassification ClassifyGit(IReadOnlyList<string> tokens, NormalizedCommand command)
        {
            if (tokens == null || tokens.Count < 2 || !IsExecutable(tokens[0], "git"))
            {
                return null;
            }

            var subcommand = NormalizeToken(tokens[1]);
            if (IsGitDestructive(subcommand, tokens))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitDestructive,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "run destructive Git operation",
                    command,
                    null);
            }

            if (string.Equals(subcommand, "init", StringComparison.OrdinalIgnoreCase))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitInitialize,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "initialize Git repository",
                    command,
                    null);
            }

            if (string.Equals(subcommand, "config", StringComparison.OrdinalIgnoreCase) ||
                IsGitRemoteConfigurationChange(subcommand, tokens))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitLocalConfiguration,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "change Git configuration",
                    command,
                    null);
            }

            if (IsAny(subcommand, "status", "diff", "log", "show", "rev-parse", "ls-files", "grep", "remote"))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitInspect,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "inspect Git repository",
                    command,
                    null);
            }

            if (string.Equals(subcommand, "branch", StringComparison.OrdinalIgnoreCase))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitInspect,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "inspect Git repository",
                    command,
                    null);
            }

            if (string.Equals(subcommand, "add", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(subcommand, "restore", StringComparison.OrdinalIgnoreCase) && ContainsToken(tokens, "--staged")))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitStage,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "stage Git changes",
                    command,
                    null);
            }

            if (IsAny(subcommand, "commit", "tag"))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitLocalCommit,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "create local Git history",
                    command,
                    null);
            }

            if (IsAny(subcommand, "fetch", "pull", "ls-remote"))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitRemoteRead,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "read Git remote",
                    command,
                    null);
            }

            if (string.Equals(subcommand, "push", StringComparison.OrdinalIgnoreCase))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitRemoteWrite,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "write Git remote",
                    command,
                    null);
            }

            if (IsAny(subcommand, "checkout", "switch", "restore", "reset", "merge", "rebase", "stash"))
            {
                return BuildCommandClassification(
                    ApprovalRequestCategory.GitLocalWorkingTreeChange,
                    ApprovalRequestConfidence.HighConfidenceCommand,
                    "change Git working tree",
                    command,
                    null);
            }

            return BuildCommandClassification(
                ApprovalRequestCategory.CommandGeneric,
                ApprovalRequestConfidence.HeuristicCommand,
                "run Git operation",
                command,
                null);
        }

        private static bool IsGitRemoteConfigurationChange(string subcommand, IReadOnlyList<string> tokens)
        {
            if (!string.Equals(subcommand, "remote", StringComparison.OrdinalIgnoreCase) ||
                tokens == null ||
                tokens.Count < 3)
            {
                return false;
            }

            var remoteSubcommand = NormalizeToken(tokens[2]);
            return IsAny(remoteSubcommand, "add", "remove", "rm", "rename", "set-url", "set-head", "prune");
        }

        private static ApprovalRequestClassification ClassifyFileCommand(IReadOnlyList<string> tokens, NormalizedCommand command)
        {
            if (tokens == null || tokens.Count == 0)
            {
                return null;
            }

            var name = NormalizeToken(GetExecutableName(tokens[0]));
            ApprovalRequestCategory category;
            string action;

            if (IsAny(name, "get-content", "cat", "type", "get-childitem", "dir", "ls", "test-path"))
            {
                category = ApprovalRequestCategory.FileRead;
                action = "read file";
            }
            else if (IsAny(name, "set-content", "out-file"))
            {
                category = ApprovalRequestCategory.FileWrite;
                action = "write file";
            }
            else if (IsAny(name, "add-content", "clear-content"))
            {
                category = ApprovalRequestCategory.FileModify;
                action = "modify file";
            }
            else if (IsAny(name, "new-item", "touch", "mkdir", "md"))
            {
                category = ApprovalRequestCategory.FileCreate;
                action = "create file";
            }
            else if (IsAny(name, "remove-item", "rm", "del", "erase", "rmdir"))
            {
                category = ApprovalRequestCategory.FileDelete;
                action = "delete file";
            }
            else if (IsAny(name, "move-item", "rename-item", "mv", "move"))
            {
                category = ApprovalRequestCategory.FileMove;
                action = "move file";
            }
            else if (IsAny(name, "copy-item", "cp", "copy"))
            {
                category = ApprovalRequestCategory.FileCreate;
                action = "copy file";
            }
            else
            {
                return null;
            }

            var targets = ExtractFileCommandTargets(tokens);
            return BuildCommandClassification(
                category,
                ApprovalRequestConfidence.HighConfidenceCommand,
                action,
                command,
                targets);
        }

        private static NormalizedCommand NormalizeCommand(string commandText)
        {
            var raw = commandText ?? string.Empty;
            var result = new NormalizedCommand
            {
                RawCommand = raw,
                DisplayCommand = raw
            };
            var tokens = Tokenize(raw);
            if (tokens.Count < 3 || !IsKnownShellExecutable(tokens[0]))
            {
                return result;
            }

            for (var i = 1; i < tokens.Count - 1; i++)
            {
                if (!IsShellCommandSwitch(tokens[i]))
                {
                    continue;
                }

                var inner = JoinTokens(tokens, i + 1);
                result.ShellScript = inner;
                var innerTokens = RemoveInvocationOperator(Tokenize(inner));
                result.DisplayCommand = JoinTokensForDisplay(innerTokens, 0);
                if (string.IsNullOrWhiteSpace(result.DisplayCommand))
                {
                    result.DisplayCommand = inner;
                }

                result.ShellKind = GetShellKind(tokens[0]);
                return result;
            }

            return result;
        }

        private static List<string> ExtractFileCommandTargets(IReadOnlyList<string> tokens)
        {
            var targets = new List<string>();
            if (tokens == null || tokens.Count <= 1)
            {
                return targets;
            }

            for (var i = 1; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (IsCommandSeparator(token))
                {
                    break;
                }

                if (IsPathOption(token) || IsDestinationOption(token))
                {
                    if (i + 1 < tokens.Count)
                    {
                        AddTarget(targets, tokens[i + 1]);
                        i++;
                    }

                    continue;
                }

                if (IsValueOption(token))
                {
                    if (i + 1 < tokens.Count)
                    {
                        i++;
                    }

                    continue;
                }

                if (IsFlag(token))
                {
                    continue;
                }

                AddTarget(targets, token);
            }

            return targets;
        }

        private static void AddFileChangeLine(
            ISet<ApprovalRequestCategory> categories,
            ICollection<string> targets,
            string line)
        {
            AddFileChangeLineIfMatched(categories, targets, line, "Deleted ", ApprovalRequestCategory.FileDelete);
            AddFileChangeLineIfMatched(categories, targets, line, "已删除 ", ApprovalRequestCategory.FileDelete);
            AddFileChangeLineIfMatched(categories, targets, line, "Added ", ApprovalRequestCategory.FileCreate);
            AddFileChangeLineIfMatched(categories, targets, line, "已新增 ", ApprovalRequestCategory.FileCreate);
            AddFileChangeLineIfMatched(categories, targets, line, "Modified ", ApprovalRequestCategory.FileModify);
            AddFileChangeLineIfMatched(categories, targets, line, "已修改 ", ApprovalRequestCategory.FileModify);
            AddFileChangeLineIfMatched(categories, targets, line, "Moved ", ApprovalRequestCategory.FileMove);
            AddFileChangeLineIfMatched(categories, targets, line, "已移动 ", ApprovalRequestCategory.FileMove);
            AddFileChangeLineIfMatched(categories, targets, line, "Processed ", ApprovalRequestCategory.FileUnknown);
            AddFileChangeLineIfMatched(categories, targets, line, "已处理 ", ApprovalRequestCategory.FileUnknown);
        }

        private static void AddFileChangeLineIfMatched(
            ISet<ApprovalRequestCategory> categories,
            ICollection<string> targets,
            string line,
            string prefix,
            ApprovalRequestCategory category)
        {
            if (string.IsNullOrWhiteSpace(line) ||
                !line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            categories.Add(category);
            AddTarget(targets, NormalizeMoveTarget(line.Substring(prefix.Length).Trim()));
        }

        private static ApprovalRequestClassification BuildCommandClassification(
            ApprovalRequestCategory category,
            ApprovalRequestConfidence confidence,
            string action,
            NormalizedCommand command,
            IEnumerable<string> targets)
        {
            return BuildClassification(
                category,
                confidence,
                action,
                command == null ? null : command.DisplayCommand,
                command == null ? null : command.RawCommand,
                command == null ? null : command.ShellKind,
                targets);
        }

        private static ApprovalRequestClassification BuildClassification(
            ApprovalRequestCategory category,
            ApprovalRequestConfidence confidence,
            string action,
            string displayCommand,
            string rawCommand,
            string shellKind,
            IEnumerable<string> targets)
        {
            var result = new ApprovalRequestClassification
            {
                Category = category,
                Confidence = confidence,
                DisplayAction = action ?? GetDisplayAction(category),
                DisplayCommand = displayCommand,
                RawCommand = rawCommand,
                ShellKind = shellKind
            };

            if (targets != null)
            {
                foreach (var target in targets)
                {
                    AddTarget(result.Targets, target);
                }
            }

            return result;
        }

        private static string GetDisplayAction(ApprovalRequestCategory category)
        {
            switch (category)
            {
                case ApprovalRequestCategory.CommandBuild:
                    return "run build";
                case ApprovalRequestCategory.CommandTest:
                    return "run tests";
                case ApprovalRequestCategory.CommandDependencyInstall:
                    return "install dependencies";
                case ApprovalRequestCategory.GitInspect:
                    return "inspect Git repository";
                case ApprovalRequestCategory.GitStage:
                    return "stage Git changes";
                case ApprovalRequestCategory.GitLocalCommit:
                    return "create local Git history";
                case ApprovalRequestCategory.GitLocalWorkingTreeChange:
                    return "change Git working tree";
                case ApprovalRequestCategory.GitRemoteRead:
                    return "read Git remote";
                case ApprovalRequestCategory.GitRemoteWrite:
                    return "write Git remote";
                case ApprovalRequestCategory.GitDestructive:
                    return "run destructive Git operation";
                case ApprovalRequestCategory.GitInitialize:
                    return "initialize Git repository";
                case ApprovalRequestCategory.GitLocalConfiguration:
                    return "change Git configuration";
                case ApprovalRequestCategory.FileRead:
                    return "read file";
                case ApprovalRequestCategory.FileWrite:
                    return "write file";
                case ApprovalRequestCategory.FileCreate:
                    return "create file";
                case ApprovalRequestCategory.FileModify:
                    return "modify file";
                case ApprovalRequestCategory.FileDelete:
                    return "delete file";
                case ApprovalRequestCategory.FileMove:
                    return "move file";
                case ApprovalRequestCategory.FileMixed:
                    return "change files";
                case ApprovalRequestCategory.PermissionExtraReadRoots:
                    return "allow extra read roots";
                case ApprovalRequestCategory.PermissionExtraWriteRoots:
                    return "allow extra write roots";
                case ApprovalRequestCategory.PermissionNetwork:
                    return "allow network access";
                default:
                    return "execute command";
            }
        }

        private static bool IsGitDestructive(string subcommand, IReadOnlyList<string> tokens)
        {
            if (string.Equals(subcommand, "clean", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(subcommand, "reset", StringComparison.OrdinalIgnoreCase) && ContainsToken(tokens, "--hard"))
            {
                return true;
            }

            return string.Equals(subcommand, "branch", StringComparison.OrdinalIgnoreCase) &&
                   (ContainsToken(tokens, "-D") || ContainsToken(tokens, "--delete") || ContainsToken(tokens, "-d"));
        }

        private static bool IsKnownShellExecutable(string token)
        {
            return IsExecutable(token, "pwsh") ||
                   IsExecutable(token, "powershell") ||
                   IsExecutable(token, "cmd") ||
                   IsExecutable(token, "bash") ||
                   IsExecutable(token, "sh") ||
                   IsExecutable(token, "zsh");
        }

        private static bool IsShellCommandSwitch(string token)
        {
            return string.Equals(token, "-Command", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "-c", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "/c", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(token, "-lc", StringComparison.OrdinalIgnoreCase);
        }

        // Returns true when the command can run more than one program or write through a
        // shell redirection. Such commands must never inherit a single command's category
        // or a command-prefix approval.
        internal static bool IsCompoundCommand(string commandText)
        {
            return IsCompoundCommand(NormalizeCommand(commandText));
        }

        private static bool IsCompoundCommand(NormalizedCommand command)
        {
            return command != null &&
                   IsCompoundCommandText(command.ShellScript ?? command.RawCommand, command.ShellKind);
        }

        private static bool IsCompoundCommandText(string text, string shellKind)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            // cmd.exe does not treat single quotes as quoting; unknown shells are handled the same conservative way.
            var singleQuotesAreQuoting = !string.IsNullOrEmpty(shellKind) &&
                                         !string.Equals(shellKind, "cmd", StringComparison.OrdinalIgnoreCase);
            var quote = '\0';
            for (var i = SkipLeadingCallOperator(text); i < text.Length; i++)
            {
                var ch = text[i];
                if (quote == '\'')
                {
                    if (ch == '\'')
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (quote == '"')
                {
                    if (ch == '\\' && i + 1 < text.Length && text[i + 1] == '"')
                    {
                        i++;
                        continue;
                    }

                    if (ch == '"')
                    {
                        quote = '\0';
                        continue;
                    }

                    // Command substitution still runs inside double quotes in POSIX shells and PowerShell.
                    if (ch == '`' || (ch == '$' && i + 1 < text.Length && text[i + 1] == '('))
                    {
                        return true;
                    }

                    continue;
                }

                if (ch == '"' || (ch == '\'' && singleQuotesAreQuoting))
                {
                    quote = ch;
                    continue;
                }

                switch (ch)
                {
                    case ';':
                    case '{':
                    case '}':
                    case '(':
                    case ')':
                    case '|':
                    case '&':
                    case '`':
                    case '\r':
                    case '\n':
                        return true;
                    case '<':
                        if (i + 1 < text.Length && text[i + 1] == '(')
                        {
                            return true;
                        }

                        break;
                    case '>':
                        int end;
                        if (!TryConsumeDiscardRedirection(text, i, out end))
                        {
                            return true;
                        }

                        i = end - 1;
                        break;
                }
            }

            return false;
        }

        private static int SkipLeadingCallOperator(string text)
        {
            var index = 0;
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }

            // PowerShell's leading call operator (& 'tool.exe' args) invokes a single program.
            if (index + 1 < text.Length &&
                text[index] == '&' &&
                (char.IsWhiteSpace(text[index + 1]) || text[index + 1] == '"' || text[index + 1] == '\''))
            {
                return index + 1;
            }

            return index;
        }

        // Accepts only stream merges (2>&1) and redirections to the null device; any other
        // redirection writes a file and makes the command compound.
        private static bool TryConsumeDiscardRedirection(string text, int index, out int end)
        {
            var position = index + 1;
            if (position < text.Length && text[position] == '>')
            {
                position++;
            }

            if (position < text.Length && text[position] == '&')
            {
                position++;
                var digitsStart = position;
                while (position < text.Length && char.IsDigit(text[position]))
                {
                    position++;
                }

                end = position;
                return position > digitsStart &&
                       (position == text.Length || char.IsWhiteSpace(text[position]));
            }

            while (position < text.Length && (text[position] == ' ' || text[position] == '\t'))
            {
                position++;
            }

            var targetStart = position;
            while (position < text.Length && !char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            end = position;
            var target = text.Substring(targetStart, position - targetStart);
            return string.Equals(target, "/dev/null", StringComparison.Ordinal) ||
                   string.Equals(target, "$null", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(target, "nul", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetShellKind(string token)
        {
            var executable = NormalizeToken(GetExecutableName(token));
            if (executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                executable = executable.Substring(0, executable.Length - 4);
            }

            return executable;
        }

        private static bool IsExecutable(string token, string expectedName)
        {
            var executable = NormalizeToken(GetExecutableName(token));
            var expected = NormalizeToken(expectedName);
            return string.Equals(executable, expected, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(executable, expected + ".exe", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetExecutableName(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return string.Empty;
            }

            var normalized = token.Trim();
            var slashIndex = Math.Max(normalized.LastIndexOf('\\'), normalized.LastIndexOf('/'));
            return slashIndex >= 0 && slashIndex < normalized.Length - 1
                ? normalized.Substring(slashIndex + 1)
                : normalized;
        }

        private static List<string> RemoveInvocationOperator(IReadOnlyList<string> tokens)
        {
            var result = new List<string>();
            if (tokens == null || tokens.Count == 0)
            {
                return result;
            }

            var start = string.Equals(tokens[0], "&", StringComparison.Ordinal) ||
                        string.Equals(tokens[0], "call", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0;
            for (var i = start; i < tokens.Count; i++)
            {
                result.Add(tokens[i]);
            }

            return result;
        }

        private static string JoinTokens(IReadOnlyList<string> tokens, int startIndex)
        {
            if (tokens == null || startIndex >= tokens.Count)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (var i = startIndex; i < tokens.Count; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(tokens[i]);
            }

            return builder.ToString();
        }

        private static string JoinTokensForDisplay(IReadOnlyList<string> tokens, int startIndex)
        {
            if (tokens == null || startIndex >= tokens.Count)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            for (var i = startIndex; i < tokens.Count; i++)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(QuoteTokenForDisplay(tokens[i]));
            }

            return builder.ToString();
        }

        private static string QuoteTokenForDisplay(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return "\"\"";
            }

            if (token.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"' }) < 0)
            {
                return token;
            }

            return "\"" + token.Replace("\"", "\\\"") + "\"";
        }

        private static List<string> Tokenize(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var builder = new StringBuilder();
            var quote = '\0';
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (quote != '\0')
                {
                    if (ch == '\\' && i + 1 < text.Length && text[i + 1] == quote)
                    {
                        builder.Append(quote);
                        i++;
                        continue;
                    }

                    if (ch == quote)
                    {
                        quote = '\0';
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    continue;
                }

                if (ch == '"' || ch == '\'')
                {
                    quote = ch;
                    continue;
                }

                if (char.IsWhiteSpace(ch))
                {
                    AddToken(result, builder);
                    continue;
                }

                builder.Append(ch);
            }

            AddToken(result, builder);
            return result;
        }

        private static void AddToken(ICollection<string> result, StringBuilder builder)
        {
            if (builder.Length == 0)
            {
                return;
            }

            result.Add(builder.ToString());
            builder.Length = 0;
        }

        private static bool ContainsAny(string text, params string[] values)
        {
            if (string.IsNullOrEmpty(text) || values == null)
            {
                return false;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (text.IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsToken(IReadOnlyList<string> tokens, string expected)
        {
            if (tokens == null)
            {
                return false;
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                if (string.Equals(tokens[i], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsAny(string value, params string[] candidates)
        {
            if (candidates == null)
            {
                return false;
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                if (string.Equals(value, candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildSearchableCommandText(string commandText)
        {
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(commandText.Length);
            for (var i = 0; i < commandText.Length; i++)
            {
                var ch = commandText[i];
                builder.Append(ch == '"' || ch == '\'' ? ' ' : ch);
            }

            return builder.ToString();
        }

        private static bool IsPathOption(string token)
        {
            var normalized = NormalizeOption(token);
            return IsAny(normalized, "path", "literalpath", "filepath", "targetpath");
        }

        private static bool IsDestinationOption(string token)
        {
            var normalized = NormalizeOption(token);
            return IsAny(normalized, "destination", "destinationpath", "newname");
        }

        private static bool IsValueOption(string token)
        {
            var normalized = NormalizeOption(token);
            return IsAny(normalized, "value", "encoding", "filter", "include", "exclude", "itemtype");
        }

        private static bool IsFlag(string token)
        {
            return !string.IsNullOrWhiteSpace(token) &&
                   (token.StartsWith("-", StringComparison.Ordinal) ||
                    token.StartsWith("/", StringComparison.Ordinal));
        }

        private static bool IsCommandSeparator(string token)
        {
            return string.Equals(token, "|", StringComparison.Ordinal) ||
                   string.Equals(token, "&&", StringComparison.Ordinal) ||
                   string.Equals(token, ";", StringComparison.Ordinal);
        }

        private static string NormalizeOption(string token)
        {
            return (token ?? string.Empty).TrimStart('-', '/').Replace("-", string.Empty);
        }

        private static string NormalizeToken(string token)
        {
            return (token ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string NormalizeWhitespace(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var previousWasWhitespace = false;
            for (var i = 0; i < text.Length; i++)
            {
                var ch = text[i];
                if (char.IsWhiteSpace(ch))
                {
                    if (!previousWasWhitespace)
                    {
                        builder.Append(' ');
                        previousWasWhitespace = true;
                    }

                    continue;
                }

                builder.Append(ch);
                previousWasWhitespace = false;
            }

            return builder.ToString().Trim();
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

        private static void AddTarget(ICollection<string> targets, string target)
        {
            if (targets == null || string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            var normalized = target.Trim().Trim('"', '\'').Replace("\\\"", "\"");
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            foreach (var existing in targets)
            {
                if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            targets.Add(normalized);
        }

        private static ApprovalRequestCategory First(IEnumerable<ApprovalRequestCategory> categories)
        {
            foreach (var category in categories)
            {
                return category;
            }

            return ApprovalRequestCategory.Unknown;
        }

        private sealed class NormalizedCommand
        {
            public string RawCommand { get; set; }

            public string DisplayCommand { get; set; }

            public string ShellKind { get; set; }

            public string ShellScript { get; set; }
        }
    }
}
