using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslFileSystemBridge : IWslFileSystemBridge
    {
        private const int DirectoryExistsTimeoutMilliseconds = 5000;
        private const int ListDirectoryTimeoutMilliseconds = 12000;
        private const int DeleteTimeoutMilliseconds = 10000;
        private const int GitInfoTimeoutMilliseconds = 15000;

        private const string ListDirectoryScript =
            "set -u\n" +
            "dir=${1:-}\n" +
            "if [ -z \"$dir\" ]; then exit 64; fi\n" +
            "case \"$dir\" in\n" +
            "  \"~\") dir=$HOME ;;\n" +
            "  \"~/\"*) dir=\"$HOME/${dir#~/}\" ;;\n" +
            "esac\n" +
            "if [ ! -d \"$dir\" ]; then exit 66; fi\n" +
            "enc() { printf '%s' \"$1\" | base64 | tr -d '\\n'; }\n" +
            "emit_entry() {\n" +
            "  p=$1\n" +
            "  if [ ! -e \"$p\" ] && [ ! -L \"$p\" ]; then return; fi\n" +
            "  name=${p##*/}\n" +
            "  kind=file\n" +
            "  if [ -d \"$p\" ]; then kind=dir; fi\n" +
            "  hidden=0\n" +
            "  case \"$name\" in .*) hidden=1 ;; esac\n" +
            "  size=$(stat -c %s \"$p\" 2>/dev/null || stat -f %z \"$p\" 2>/dev/null || printf '')\n" +
            "  mtime=$(stat -c %Y \"$p\" 2>/dev/null || stat -f %m \"$p\" 2>/dev/null || printf '')\n" +
            "  printf '%s\\t%s\\t%s\\t%s\\t%s\\t%s\\n' \"$kind\" \"$(enc \"$name\")\" \"$(enc \"$p\")\" \"$hidden\" \"$size\" \"$mtime\"\n" +
            "}\n" +
            "for p in \"$dir\"/* \"$dir\"/.[!.]* \"$dir\"/..?*; do emit_entry \"$p\"; done";

        private const string DeleteCodexSessionScript =
            "set -eu\n" +
            "target=${1:-}\n" +
            "if [ -z \"$target\" ]; then exit 64; fi\n" +
            "target_real=$(realpath -m -- \"$target\")\n" +
            "codex_home=${CODEX_HOME:-$HOME/.codex}\n" +
            "home_real=$(realpath -m -- \"$codex_home\")\n" +
            "sessions_real=$(realpath -m -- \"$home_real/sessions\")\n" +
            "archived_real=$(realpath -m -- \"$home_real/archived_sessions\")\n" +
            "case \"$target_real\" in\n" +
            "  \"$sessions_real\"/*|\"$archived_real\"/*) ;;\n" +
            "  *) echo \"Path is outside supported Codex session directories.\" >&2; exit 73 ;;\n" +
            "esac\n" +
            "if [ -e \"$target_real\" ] || [ -L \"$target_real\" ]; then rm -rf -- \"$target_real\"; fi";

        private readonly IWslCommandExecutor _executor;
        private readonly UnixFileSystemBridgeCore _core = new UnixFileSystemBridgeCore(new UnixFileSystemBridgeProfile
        {
            TargetLabel = "WSL",
            CommandFailedMessage = () => SharedLocalization.Text("WslFileSystemBridge.Error.CommandFailed", "WSL command failed."),
            ListDirectoryScript = ListDirectoryScript,
            DeleteCodexSessionScript = DeleteCodexSessionScript,
            ResolveHomeOperation = "mycodex-resolve-wsl-home",
            TestDirectoryOperation = "mycodex-test-directory",
            TestFileOperation = "mycodex-test-file",
            ListDirectoryOperation = "mycodex-list-directory",
            DeleteCodexSessionOperation = "mycodex-delete-codex-session",
            ReadGitInfoOperation = "mycodex-read-git-info",
            ProbeTimeoutMilliseconds = DirectoryExistsTimeoutMilliseconds,
            ListDirectoryTimeoutMilliseconds = ListDirectoryTimeoutMilliseconds,
            DeleteTimeoutMilliseconds = DeleteTimeoutMilliseconds,
            GitInfoTimeoutMilliseconds = GitInfoTimeoutMilliseconds
        });

        public WslFileSystemBridge()
            : this(new WslCommandExecutor())
        {
        }

        internal WslFileSystemBridge(IWslCommandExecutor executor)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public Task<string> ResolveHomeDirectoryAsync(WslAppServerConfigSnapshot config)
        {
            return _core.ResolveHomeDirectoryAsync(Invoker(config));
        }

        public Task<bool> DirectoryExistsAsync(WslAppServerConfigSnapshot config, string path)
        {
            return _core.DirectoryExistsAsync(Invoker(config), path);
        }

        public Task<bool> FileExistsAsync(WslAppServerConfigSnapshot config, string path)
        {
            return _core.FileExistsAsync(Invoker(config), path);
        }

        public Task<IReadOnlyList<RuntimeFileSystemEntrySnapshot>> ListDirectoryAsync(WslAppServerConfigSnapshot config, string path)
        {
            return _core.ListDirectoryAsync(Invoker(config), path);
        }

        public Task DeleteCodexSessionPathAsync(WslAppServerConfigSnapshot config, string sessionPath)
        {
            return _core.DeleteCodexSessionPathAsync(Invoker(config), sessionPath);
        }

        public Task<WorkspaceGitInfoSnapshot> ReadGitInfoAsync(WslAppServerConfigSnapshot config, string workspacePath)
        {
            return _core.ReadGitInfoAsync(Invoker(config), workspacePath);
        }

        private UnixCommandInvoker Invoker(WslAppServerConfigSnapshot config)
        {
            return UnixCommandInvokers.ForWsl(config, _executor);
        }
    }
}
