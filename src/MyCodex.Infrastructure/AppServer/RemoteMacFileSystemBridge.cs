using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class RemoteMacFileSystemBridge : IRemoteMacFileSystemBridge
    {
        private const int DirectoryExistsTimeoutMilliseconds = 8000;
        private const int ListDirectoryTimeoutMilliseconds = 15000;
        private const int DeleteTimeoutMilliseconds = 10000;
        private const int TextFileTimeoutMilliseconds = 10000;
        private const int SkillWriteTimeoutMilliseconds = 15000;
        private const int GitInfoTimeoutMilliseconds = 15000;
        private const int GitDiffTimeoutMilliseconds = 25000;
        private const int UnsupportedSessionPathExitCode = 73;

        private const string ListDirectoryScript =
            "emulate -L sh 2>/dev/null || true\n" +
            "set -u\n" +
            "dir=${1:-}\n" +
            "if [ -z \"$dir\" ]; then exit 64; fi\n" +
            "case \"$dir\" in\n" +
            "  \"~\") dir=$HOME ;;\n" +
            "  \"~/\"*) dir=\"$HOME/${dir#~/}\" ;;\n" +
            "esac\n" +
            "if [ ! -d \"$dir\" ]; then exit 66; fi\n" +
            "MYCODEX_LIST_DIR=\"$dir\" perl -MMIME::Base64=encode_base64 -e '\n" +
            "use strict;\n" +
            "use warnings;\n" +
            "my $dir = $ENV{MYCODEX_LIST_DIR} || q{};\n" +
            "opendir(my $dh, $dir) or exit 66;\n" +
            "while (defined(my $name = readdir($dh))) {\n" +
            "  next if $name eq q{.} || $name eq q{..};\n" +
            "  my $path = $dir eq q{/} ? q{/} . $name : $dir . q{/} . $name;\n" +
            "  my @st = stat($path);\n" +
            "  my $is_dir = @st ? (-d _ ? 1 : 0) : 0;\n" +
            "  if (!@st) { @st = lstat($path); next unless @st; }\n" +
            "  my $hidden = index($name, q{.}) == 0 ? 1 : 0;\n" +
            "  my $kind = $is_dir ? q{dir} : q{file};\n" +
            "  my $size = defined $st[7] ? $st[7] : q{};\n" +
            "  my $mtime = defined $st[9] ? $st[9] : q{};\n" +
            "  print join(qq{\\t}, $kind, encode_base64($name, q{}), encode_base64($path, q{}), $hidden, $size, $mtime), qq{\\n};\n" +
            "}\n" +
            "closedir($dh);\n" +
            "'";
        private const string DeleteCodexSessionScript =
            "emulate -L sh 2>/dev/null || true\n" +
            "set -eu\n" +
            "target=${1:-}\n" +
            "if [ -z \"$target\" ]; then exit 64; fi\n" +
            "case \"$target\" in /*) ;; *) exit 73 ;; esac\n" +
            "case \"$target\" in */../*|*/..|*/./*|*/.) exit 73 ;; esac\n" +
            "codex_home=${CODEX_HOME:-$HOME/.codex}\n" +
            "case \"$codex_home\" in /*) ;; *) codex_home=\"$HOME/$codex_home\" ;; esac\n" +
            "codex_home=${codex_home%/}\n" +
            "sessions_dir=\"$codex_home/sessions\"\n" +
            "archived_dir=\"$codex_home/archived_sessions\"\n" +
            "case \"$target\" in\n" +
            "  \"$sessions_dir\"/*|\"$archived_dir\"/*) ;;\n" +
            "  *) exit 73 ;;\n" +
            "esac\n" +
            "if [ ! -e \"$target\" ] && [ ! -L \"$target\" ]; then exit 0; fi\n" +
            "target_real=$(perl -MCwd=abs_path -e '$p=abs_path(shift); exit 66 unless defined $p; print $p' \"$target\")\n" +
            "sessions_real=$(perl -MCwd=abs_path -e '$p=abs_path(shift); print $p if defined $p' \"$sessions_dir\")\n" +
            "archived_real=$(perl -MCwd=abs_path -e '$p=abs_path(shift); print $p if defined $p' \"$archived_dir\")\n" +
            "case \"$target_real\" in\n" +
            "  \"$sessions_real\"/*|\"$archived_real\"/*) rm -rf \"$target\" ;;\n" +
            "  *) exit 73 ;;\n" +
            "esac";
        private const string ReadTextFileScript = "test -f \"$1\" && cat \"$1\"";
        private const string WriteSkillFilesScript =
            "set -eu\n" +
            "skill_dir=${1:-}\n" +
            "skill_text=${2-}\n" +
            "agent_text=${3-}\n" +
            "agent_action=${4:-none}\n" +
            "if [ -z \"$skill_dir\" ]; then exit 64; fi\n" +
            "mkdir -p \"$skill_dir\"\n" +
            "printf '%s' \"$skill_text\" > \"$skill_dir/SKILL.md\"\n" +
            "agents_dir=\"$skill_dir/agents\"\n" +
            "agent_file=\"$agents_dir/openai.yaml\"\n" +
            "case \"$agent_action\" in\n" +
            "  write) mkdir -p \"$agents_dir\"; printf '%s' \"$agent_text\" > \"$agent_file\" ;;\n" +
            "  delete) rm -f \"$agent_file\"; rmdir \"$agents_dir\" 2>/dev/null || true ;;\n" +
            "  none) ;;\n" +
            "  *) exit 65 ;;\n" +
            "esac";

        private const string GitStatusAndDiffScript =
            "workspace_path=${1:-}\n" +
            "if [ -z \"$workspace_path\" ]; then exit 64; fi\n" +
            "if ! git -C \"$workspace_path\" rev-parse --is-inside-work-tree >/dev/null 2>&1; then exit 2; fi\n" +
            "MYCODEX_WORKSPACE=\"$workspace_path\" perl -MMIME::Base64=encode_base64 -e '\n" +
            "use strict;\n" +
            "use warnings;\n" +
            "my $workdir = $ENV{MYCODEX_WORKSPACE} || exit 64;\n" +
            "sub enc { return encode_base64(defined $_[0] ? $_[0] : q{}, q{}); }\n" +
            "sub capture {\n" +
            "  my @cmd = @_;\n" +
            "  open(my $fh, q{-|}, @cmd) or return q{};\n" +
            "  local $/;\n" +
            "  my $out = <$fh>;\n" +
            "  close($fh);\n" +
            "  return defined $out ? $out : q{};\n" +
            "}\n" +
            "open(my $status_fh, q{-|}, q{git}, q{-C}, $workdir, q{status}, q{--short}, q{-z}, q{--untracked-files=all}, q{--no-renames}) or exit 67;\n" +
            "local $/ = qq{\\0};\n" +
            "while (defined(my $record = <$status_fh>)) {\n" +
            "  chomp $record;\n" +
            "  next if length($record) < 3;\n" +
            "  my $raw = substr($record, 0, 2);\n" +
            "  my $path = length($record) > 3 ? substr($record, 3) : q{};\n" +
            "  next if $path eq q{};\n" +
            "  my $staged = q{};\n" +
            "  my $unstaged = q{};\n" +
            "  if ($raw !~ /\\?\\?/) {\n" +
            "    $staged = capture(q{git}, q{-C}, $workdir, q{diff}, q{--cached}, q{--no-ext-diff}, q{--binary}, q{--}, $path);\n" +
            "    $unstaged = capture(q{git}, q{-C}, $workdir, q{diff}, q{--no-ext-diff}, q{--binary}, q{--}, $path);\n" +
            "  }\n" +
            "  print join(qq{\\t}, q{file}, enc($record), enc($staged), enc($unstaged)), qq{\\n};\n" +
            "}\n" +
            "close($status_fh);\n" +
            "my $code = $? >> 8;\n" +
            "exit $code if $code != 0;\n" +
            "'";

        private readonly IRemoteSshCommandExecutor _executor;

        public RemoteMacFileSystemBridge()
            : this(new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacFileSystemBridge(IRemoteSshCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public async Task<string> ResolveHomeDirectoryAsync(RemoteMacSshConfigSnapshot config)
        {
            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.ResolveHomeDirectoryScript,
                "mycodex-resolve-remote-home",
                DirectoryExistsTimeoutMilliseconds).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                throw BuildRemoteCommandException(result);
            }

            var homePath = (result.StandardOutput ?? string.Empty).Trim();
            if (homePath.IndexOf('\r') >= 0 ||
                homePath.IndexOf('\n') >= 0 ||
                !WorkspacePathUtilities.LooksLikeUnixAbsolutePath(homePath))
            {
                throw new InvalidOperationException("Remote home directory was not returned as an absolute path.");
            }

            return WorkspacePathUtilities.NormalizeUnixAbsolutePath(homePath);
        }

        public async Task<bool> DirectoryExistsAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.DirectoryExistsScript,
                "mycodex-test-remote-directory",
                DirectoryExistsTimeoutMilliseconds,
                path.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return true;
            }

            if (result.ExitCode == 1)
            {
                return false;
            }

            throw BuildRemoteCommandException(result);
        }

        public async Task<bool> FileExistsAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.FileExistsScript,
                "mycodex-test-remote-file",
                DirectoryExistsTimeoutMilliseconds,
                path.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return true;
            }

            if (result.ExitCode == 1)
            {
                return false;
            }

            throw BuildRemoteCommandException(result);
        }

        public async Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Remote directory path cannot be empty.", nameof(path));
            }

            var result = await _executor.ExecuteAsync(
                config,
                ListDirectoryScript,
                "mycodex-list-remote-directory",
                ListDirectoryTimeoutMilliseconds,
                path.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return UnixFileSystemBridgeParsers.ParseDirectoryListing(result.StandardOutput);
            }

            if (result.ExitCode == 66)
            {
                throw new InvalidOperationException("Remote directory was not found.");
            }

            throw BuildRemoteCommandException(result);
        }

        public async Task DeleteCodexSessionPathAsync(RemoteMacSshConfigSnapshot config, string sessionPath)
        {
            if (string.IsNullOrWhiteSpace(sessionPath))
            {
                throw new ArgumentException(
                    SharedLocalization.Text("AppServerClient.Error.ThreadFilePathRequired", "Thread file path cannot be empty."),
                    nameof(sessionPath));
            }

            var result = await _executor.ExecuteAsync(
                config,
                DeleteCodexSessionScript,
                "mycodex-delete-remote-codex-session",
                DeleteTimeoutMilliseconds,
                sessionPath.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return;
            }

            if (result.ExitCode == UnsupportedSessionPathExitCode)
            {
                throw new InvalidOperationException(
                    SharedLocalization.Text(
                        "AppServerClient.Error.ThreadFilePathUnsupported",
                        "Current thread file path is outside supported Codex state directories and cannot be fully removed."));
            }

            throw BuildRemoteCommandException(result);
        }

        public async Task<string> ReadTextFileAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Remote file path cannot be empty.", nameof(path));
            }

            var result = await _executor.ExecuteAsync(
                config,
                ReadTextFileScript,
                "mycodex-read-remote-text-file",
                TextFileTimeoutMilliseconds,
                path.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return result.StandardOutput ?? string.Empty;
            }

            throw BuildRemoteCommandException(result);
        }

        public async Task WriteSkillFilesAsync(
            RemoteMacSshConfigSnapshot config,
            string skillDirectoryPath,
            string skillMarkdownText,
            string openAiAgentText,
            RemoteSkillAgentFileAction agentFileAction)
        {
            if (string.IsNullOrWhiteSpace(skillDirectoryPath))
            {
                throw new ArgumentException("Remote skill directory path cannot be empty.", nameof(skillDirectoryPath));
            }

            var result = await _executor.ExecuteAsync(
                config,
                WriteSkillFilesScript,
                "mycodex-write-remote-skill-files",
                SkillWriteTimeoutMilliseconds,
                skillDirectoryPath.Trim(),
                skillMarkdownText ?? string.Empty,
                openAiAgentText ?? string.Empty,
                ToAgentFileActionArgument(agentFileAction)).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return;
            }

            throw BuildRemoteCommandException(result);
        }

        public async Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(RemoteMacSshConfigSnapshot config, string workspacePath)
        {
            var snapshot = new WorkspaceGitInfoSnapshot();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return snapshot;
            }

            var result = await _executor.ExecuteAsync(
                config,
                UnixFileSystemBridgeScripts.GitInfoScript,
                "mycodex-read-remote-git-info",
                GitInfoTimeoutMilliseconds,
                workspacePath.Trim()).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                return snapshot;
            }

            var values = UnixFileSystemBridgeParsers.ParseKeyValueOutput(result.StandardOutput);
            snapshot.IsGitRepository = true;
            snapshot.Branch = UnixFileSystemBridgeParsers.GetValue(values, "branch");
            snapshot.Sha = UnixFileSystemBridgeParsers.GetValue(values, "sha");
            snapshot.OriginUrl = UnixFileSystemBridgeParsers.GetValue(values, "origin");
            return snapshot;
        }

        public async Task<GitDiffSnapshot> ReadUncommittedChangesAsync(RemoteMacSshConfigSnapshot config, string workspacePath, CancellationToken cancellationToken = default(CancellationToken))
        {
            var snapshot = new GitDiffSnapshot
            {
                WorkspacePath = workspacePath
            };
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                return snapshot;
            }

            var normalizedWorkspacePath = workspacePath.Trim();
            var statusResult = await _executor.ExecuteAsync(
                config,
                GitStatusAndDiffScript,
                "mycodex-read-remote-git-status-and-diffs",
                GitDiffTimeoutMilliseconds,
                cancellationToken,
                normalizedWorkspacePath).ConfigureAwait(false);
            if (statusResult.ExitCode != 0)
            {
                return snapshot;
            }

            snapshot.IsGitRepository = true;
            foreach (var entry in ParseStatusAndDiffOutput(statusResult.StandardOutput))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry == null || string.IsNullOrWhiteSpace(entry.FilePath))
                {
                    continue;
                }

                UnixFileSystemBridgeParsers.PopulateDiffLineCounts(entry);
                snapshot.Files.Add(entry);
            }

            return snapshot;
        }

        private static IReadOnlyList<GitDiffFileEntry> ParseStatusAndDiffOutput(string output)
        {
            var entries = new List<GitDiffFileEntry>();
            if (string.IsNullOrWhiteSpace(output))
            {
                return entries;
            }

            var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(new[] { '\t' }, 4);
                if (parts.Length < 4 || !string.Equals(parts[0], "file", StringComparison.Ordinal))
                {
                    continue;
                }

                var entry = UnixFileSystemBridgeParsers.ParseStatusEntry(UnixFileSystemBridgeParsers.DecodeBase64Field(parts[1]));
                if (entry == null)
                {
                    continue;
                }

                entry.DiffText = UnixFileSystemBridgeParsers.BuildDiffText(
                    entry,
                    UnixFileSystemBridgeParsers.DecodeBase64Field(parts[2]),
                    UnixFileSystemBridgeParsers.DecodeBase64Field(parts[3]));
                entries.Add(entry);
            }

            return entries;
        }

        private static InvalidOperationException BuildRemoteCommandException(RemoteSshCommandExecutionResult result)
        {
            var message = result != null && !string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardError.Trim()
                : SharedLocalization.Text("RemoteMacFileSystemBridge.Error.CommandFailed", "Remote SSH command failed.");
            return new InvalidOperationException(message);
        }

        private static string ToAgentFileActionArgument(RemoteSkillAgentFileAction action)
        {
            switch (action)
            {
                case RemoteSkillAgentFileAction.Write:
                    return "write";
                case RemoteSkillAgentFileAction.Delete:
                    return "delete";
                default:
                    return "none";
            }
        }
    }
}
