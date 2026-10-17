using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Processes;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal static class AuthenticationHostDeployment
    {
        internal static readonly string ProbeScript = InPosixShell(
            "set -eu\n[ -n \"${HOME:-}\" ] || exit 12\n" +
            "codex_home=${CODEX_HOME:-$HOME/.codex}\n" +
            "case $codex_home in /*) ;; *) exit 19;; esac\n" +
            "[ -d \"$codex_home\" ] || exit 19\n" +
            "uname -s\nuname -m");

        // The archive is verified before it reaches this script and again before extraction.
        // Only the fixed MyCodex auth-core directory is ever replaced; CodexHome and data stay untouched.
        internal static readonly string InstallScript = InPosixShell(
            "set -eu\numask 077\n" +
            "[ -n \"${HOME:-}\" ] || exit 12\n" +
            "[ \"$(uname -s)\" = \"$1\" ] && [ \"$(uname -m)\" = \"$2\" ] || exit 13\n" +
            "root=\"$HOME/.local/share/MyCodex/auth-core\"\n" +
            "for dir in \"$HOME\" \"$HOME/.local\" \"$HOME/.local/share\" \"$HOME/.local/share/MyCodex\" \"$root\"; do\n" +
            "  [ ! -L \"$dir\" ] || exit 14\n" +
            "  [ -d \"$dir\" ] || mkdir -m 700 \"$dir\"\n" +
            "  [ -d \"$dir\" ] || exit 14\n" +
            "  if [ \"$1\" = Darwin ]; then owner=$(stat -f %u \"$dir\"); mode=$(stat -f %Lp \"$dir\");\n" +
            "  else owner=$(stat -c %u \"$dir\"); mode=$(stat -c %a \"$dir\"); fi\n" +
            "  [ \"$owner\" = \"$(id -u)\" ] || exit 14\n" +
            "  case $mode in ''|*[!0-7]*|*[2367][0-7]|*[0-7][2367]) exit 14;; esac\n" +
            "done\n" +
            "[ ! -L \"$root/bin\" ] && [ ! -L \"$root/.previous\" ] || exit 14\n" +
            "mkdir \"$root/.install-lock\" 2>/dev/null || exit 15\n" +
            "stage=\n" +
            "trap 'result=$?; if [ -n \"$stage\" ]; then rm -rf \"$stage\"; fi; rmdir \"$root/.install-lock\"; exit \"$result\"' 0\n" +
            "trap 'exit 1' HUP INT TERM\n" +
            "stage=$(mktemp -d \"$root/.stage.XXXXXXXX\")\n" +
            "if [ ! -e \"$root/bin\" ] && [ -d \"$root/.previous\" ]; then mv \"$root/.previous\" \"$root/bin\"; fi\n" +
            "if [ \"$1\" = Darwin ]; then tr -cd 'A-Za-z0-9+/=' | base64 -D > \"$stage/archive.tar.gz\";\n" +
            "else tr -cd 'A-Za-z0-9+/=' | base64 -d > \"$stage/archive.tar.gz\"; fi\n" +
            "if [ \"$1\" = Darwin ]; then digest() { shasum -a 256 \"$1\" | cut -c1-64; };\n" +
            "else digest() { sha256sum \"$1\" | cut -c1-64; }; fi\n" +
            "[ \"$(digest \"$stage/archive.tar.gz\")\" = \"$3\" ] || exit 16\n" +
            "mkdir \"$stage/bin\"\n" +
            "tar -xzf \"$stage/archive.tar.gz\" -C \"$stage/bin\"\n" +
            "[ -f \"$stage/bin/mycodex-auth-host.manifest.json\" ] && [ -f \"$stage/bin/LICENSE\" ] || exit 16\n" +
            "[ ! -L \"$stage/bin/mycodex-auth-host\" ] && [ \"$(digest \"$stage/bin/mycodex-auth-host\")\" = \"$4\" ] || exit 16\n" +
            "chmod 700 \"$stage/bin/mycodex-auth-host\"\n" +
            "\"$stage/bin/mycodex-auth-host\" --version-json > /dev/null || exit 17\n" +
            "if [ \"$5\" = missing ]; then\n" +
            "  [ ! -e \"$root/bin\" ] && [ ! -e \"$root/.previous\" ] || exit 18\n" +
            "  mv \"$stage/bin\" \"$root/bin\"\n" +
            "else\n" +
            "  [ -d \"$root/bin\" ] && [ ! -e \"$root/.previous\" ] || exit 18\n" +
            "  for entry in \"$root/bin\"/* \"$root/bin\"/.[!.]* \"$root/bin\"/..?*; do\n" +
            "    [ -e \"$entry\" ] || continue\n" +
            "    [ -f \"$entry\" ] && [ ! -L \"$entry\" ] || exit 14\n" +
            "    case ${entry##*/} in mycodex-auth-host|mycodex-auth-host.manifest.json|LICENSE|RUNTIME.md|dependencies.txt) ;; *) exit 18;; esac\n" +
            "  done\n" +
            "  mv \"$root/bin\" \"$root/.previous\"\n" +
            "  if ! mv \"$stage/bin\" \"$root/bin\"; then mv \"$root/.previous\" \"$root/bin\"; exit 18; fi\n" +
            "  if [ \"$(digest \"$root/bin/mycodex-auth-host\")\" != \"$4\" ]; then\n" +
            "    rm -rf \"$root/bin\"; mv \"$root/.previous\" \"$root/bin\"; exit 16\n" +
            "  fi\n" +
            "fi\n" +
            "printf 'installed\\n'");

        internal static readonly string FinalizeScript = InPosixShell(
            "set -eu\n[ -n \"${HOME:-}\" ] || exit 12\n" +
            "root=\"$HOME/.local/share/MyCodex/auth-core\"\n" +
            "for dir in \"$HOME\" \"$HOME/.local\" \"$HOME/.local/share\" \"$HOME/.local/share/MyCodex\" \"$root\"; do\n" +
            "  [ -d \"$dir\" ] && [ ! -L \"$dir\" ] || exit 14\n" +
            "done\n" +
            "[ -d \"$root\" ] && [ ! -L \"$root\" ] && [ -d \"$root/bin\" ] && [ ! -L \"$root/bin\" ] || exit 14\n" +
            "[ -d \"$root/.previous\" ] && [ ! -L \"$root/.previous\" ] || exit 18\n" +
            "if [ \"$(uname -s)\" = Darwin ]; then digest() { shasum -a 256 \"$1\" | cut -c1-64; };\n" +
            "else digest() { sha256sum \"$1\" | cut -c1-64; }; fi\n" +
            "if [ \"$1\" = commit ]; then\n" +
            "  [ \"$(digest \"$root/bin/mycodex-auth-host\")\" = \"$2\" ] || exit 16\n" +
            "  rm -rf \"$root/.previous\"\n" +
            "elif [ \"$1\" = rollback ]; then\n" +
            "  failed=\"$root/.failed-$$\"\n" +
            "  [ ! -e \"$failed\" ] || exit 18\n" +
            "  mv \"$root/bin\" \"$failed\"\n" +
            "  if ! mv \"$root/.previous\" \"$root/bin\"; then mv \"$failed\" \"$root/bin\"; exit 18; fi\n" +
            "  rm -rf \"$failed\"\n" +
            "else exit 18; fi");

        private static string InPosixShell(string script)
        {
            // Keep the configured login shell's environment, but use POSIX glob and trap semantics.
            return "exec /bin/sh -c " + RemoteSshCommandLine.QuoteRemoteShellArgument(script) + " \"$0\" \"$@\"";
        }

        internal static async Task<AuthenticationHostRelease.Archive> ProbeWslAsync(
            WslAppServerConfigSnapshot config, IWslCommandExecutor executor, CancellationToken token)
        {
            var result = await executor.ExecuteAsync(config, ProbeScript, "mycodex-auth-probe", 20000, token).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new AuthenticationCenterException(result.ExitCode == 19 ? "invalid_codex_home" : "backend_probe_failed");
            return ParseProbe(result.StandardOutput);
        }

        internal static async Task<AuthenticationHostRelease.Archive> ProbeRemoteAsync(
            RemoteMacSshConfigSnapshot config, RemoteSshCommandExecutor executor, CancellationToken token)
        {
            var result = await executor.ExecuteAsync(config, ProbeScript, "mycodex-auth-probe", 30000, token).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new AuthenticationCenterException(result.ExitCode == 19 ? "invalid_codex_home" : "backend_probe_failed");
            return ParseProbe(result.StandardOutput);
        }

        internal static AuthenticationHostRelease.Archive ParseProbe(string output)
        {
            var lines = (output ?? string.Empty).Trim().Split('\n');
            if (lines.Length != 2) throw new AuthenticationCenterException("backend_probe_failed");
            return AuthenticationHostRelease.ForTarget(lines[0].Trim(), lines[1].Trim());
        }

        internal static async Task InstallWslAsync(WslAppServerConfigSnapshot config,
            AuthenticationHostRelease.Archive archive, bool update, CancellationToken token, string verifiedBase64 = null)
        {
            var input = verifiedBase64 ?? await AuthenticationHostRelease.ReadVerifiedBase64Async(archive, token).ConfigureAwait(false);
            var start = new ProcessStartInfo
            {
                FileName = "wsl.exe",
                Arguments = WslCommandLine.BuildHeadlessExecArguments(config.DistributionName,
                    string.IsNullOrWhiteSpace(config.ShellCommand) ? WslAppServerConfigSnapshot.DefaultShellCommand : config.ShellCommand.Trim(),
                    "-c", InstallScript, "mycodex-auth-install", archive.System, archive.Architecture,
                    archive.Sha256, archive.ExecutableSha256, update ? "update" : "missing"),
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            var result = await Task.Run(() => ProcessExecutionRunner.Run(new ProcessExecutionRequest
            {
                StartInfo = start, StandardInput = input, CancellationToken = token,
                TimeoutMilliseconds = 180000, TimeoutMessage = "Authentication host deployment timed out."
            }), token).ConfigureAwait(false);
            if (result.ExitCode == 17) throw new AuthenticationCenterException("backend_dependencies_missing");
            if (result.ExitCode == 16) throw new AuthenticationCenterException("backend_integrity_failed");
            if (result.ExitCode == 14 || result.ExitCode == 18) throw new AuthenticationCenterException("backend_install_unsafe");
            if (result.ExitCode == 15) throw new AuthenticationCenterException("backend_install_busy");
            if (result.ExitCode != 0 || result.StandardOutput.Trim() != "installed")
                throw new AuthenticationCenterException("backend_install_failed");
        }

        internal static async Task InstallRemoteAsync(RemoteMacSshConfigSnapshot config,
            RemoteSshCommandExecutor executor, AuthenticationHostRelease.Archive archive, bool update, CancellationToken token,
            string verifiedBase64 = null)
        {
            var input = verifiedBase64 ?? await AuthenticationHostRelease.ReadVerifiedBase64Async(archive, token).ConfigureAwait(false);
            var result = await executor.ExecuteWithInputAsync(config, InstallScript, "mycodex-auth-install", 180000,
                token, input, archive.System, archive.Architecture, archive.Sha256, archive.ExecutableSha256,
                update ? "update" : "missing").ConfigureAwait(false);
            if (result.ExitCode == 17) throw new AuthenticationCenterException("backend_dependencies_missing");
            if (result.ExitCode == 16) throw new AuthenticationCenterException("backend_integrity_failed");
            if (result.ExitCode == 14 || result.ExitCode == 18) throw new AuthenticationCenterException("backend_install_unsafe");
            if (result.ExitCode == 15) throw new AuthenticationCenterException("backend_install_busy");
            if (result.ExitCode != 0 || result.StandardOutput.Trim() != "installed")
                throw new AuthenticationCenterException("backend_install_failed");
        }

        internal static async Task FinalizeWslAsync(WslAppServerConfigSnapshot config, IWslCommandExecutor executor,
            AuthenticationHostRelease.Archive archive, bool rollback, CancellationToken token)
        {
            var result = await executor.ExecuteAsync(config, FinalizeScript, "mycodex-auth-finalize", 30000,
                token, rollback ? "rollback" : "commit", archive.ExecutableSha256).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new AuthenticationCenterException("backend_update_unsafe");
        }

        internal static async Task FinalizeRemoteAsync(RemoteMacSshConfigSnapshot config, RemoteSshCommandExecutor executor,
            AuthenticationHostRelease.Archive archive, bool rollback, CancellationToken token)
        {
            var result = await executor.ExecuteAsync(config, FinalizeScript, "mycodex-auth-finalize", 30000,
                token, rollback ? "rollback" : "commit", archive.ExecutableSha256).ConfigureAwait(false);
            if (result.ExitCode != 0) throw new AuthenticationCenterException("backend_update_unsafe");
        }
    }
}
