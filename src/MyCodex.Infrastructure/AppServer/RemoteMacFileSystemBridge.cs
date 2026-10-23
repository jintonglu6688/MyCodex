using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
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
        private const int GitInfoTimeoutMilliseconds = 15000;

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

        private readonly IRemoteSshCommandExecutor _executor;
        private readonly UnixFileSystemBridgeCore _core = new UnixFileSystemBridgeCore(new UnixFileSystemBridgeProfile
        {
            TargetLabel = "Remote",
            CommandFailedMessage = () => SharedLocalization.Text("RemoteMacFileSystemBridge.Error.CommandFailed", "Remote SSH command failed."),
            ListDirectoryScript = ListDirectoryScript,
            DeleteCodexSessionScript = DeleteCodexSessionScript,
            ResolveHomeOperation = "mycodex-resolve-remote-home",
            TestDirectoryOperation = "mycodex-test-remote-directory",
            TestFileOperation = "mycodex-test-remote-file",
            ListDirectoryOperation = "mycodex-list-remote-directory",
            DeleteCodexSessionOperation = "mycodex-delete-remote-codex-session",
            ReadGitInfoOperation = "mycodex-read-remote-git-info",
            ProbeTimeoutMilliseconds = DirectoryExistsTimeoutMilliseconds,
            ListDirectoryTimeoutMilliseconds = ListDirectoryTimeoutMilliseconds,
            DeleteTimeoutMilliseconds = DeleteTimeoutMilliseconds,
            GitInfoTimeoutMilliseconds = GitInfoTimeoutMilliseconds
        });

        public RemoteMacFileSystemBridge()
            : this(new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacFileSystemBridge(IRemoteSshCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public Task<string> ResolveHomeDirectoryAsync(RemoteMacSshConfigSnapshot config)
        {
            return _core.ResolveHomeDirectoryAsync(Invoker(config));
        }

        public Task<bool> DirectoryExistsAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            return _core.DirectoryExistsAsync(Invoker(config), path);
        }

        public Task<bool> FileExistsAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            return _core.FileExistsAsync(Invoker(config), path);
        }

        public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            return _core.ListDirectoryAsync(Invoker(config), path);
        }

        public Task DeleteCodexSessionPathAsync(RemoteMacSshConfigSnapshot config, string sessionPath)
        {
            return _core.DeleteCodexSessionPathAsync(Invoker(config), sessionPath);
        }

        public async Task<string> ReadTextFileAsync(RemoteMacSshConfigSnapshot config, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Remote file path cannot be empty.", nameof(path));
            }

            var result = await Invoker(config)(
                ReadTextFileScript,
                "mycodex-read-remote-text-file",
                TextFileTimeoutMilliseconds,
                path.Trim()).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return result.StandardOutput ?? string.Empty;
            }

            throw _core.BuildCommandException(result);
        }

        public Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(RemoteMacSshConfigSnapshot config, string workspacePath)
        {
            return _core.ReadGitInfoAsync(Invoker(config), workspacePath);
        }

        private UnixCommandInvoker Invoker(RemoteMacSshConfigSnapshot config)
        {
            return UnixCommandInvokers.ForRemote(config, _executor);
        }
    }
}
