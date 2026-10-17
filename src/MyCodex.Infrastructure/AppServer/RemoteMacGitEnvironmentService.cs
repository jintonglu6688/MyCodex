using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class RemoteMacGitEnvironmentService : IGitEnvironmentService
    {
        private const string ReadStatusScript =
            "set +e\n" +
            "encode_output() { perl -MMIME::Base64=encode_base64 -e 'local $/; print encode_base64(<STDIN>, \"\")'; }\n" +
            "emit_probe() {\n" +
            "  key=$1\n" +
            "  shift\n" +
            "  output=$(\"$@\" 2>&1)\n" +
            "  code=$?\n" +
            "  encoded=$(printf '%s' \"$output\" | encode_output)\n" +
            "  printf '%s\\t%s\\t%s\\n' \"$key\" \"$code\" \"$encoded\"\n" +
            "}\n" +
            "emit_shell_probe() {\n" +
            "  key=$1\n" +
            "  command_text=$2\n" +
            "  output=$(sh -c \"$command_text\" 2>&1)\n" +
            "  code=$?\n" +
            "  encoded=$(printf '%s' \"$output\" | encode_output)\n" +
            "  printf '%s\\t%s\\t%s\\n' \"$key\" \"$code\" \"$encoded\"\n" +
            "}\n" +
            "emit_probe git-version git --version\n" +
            "emit_probe git-path command -v git\n" +
            "emit_probe user-name git config --global --get user.name\n" +
            "emit_probe user-email git config --global --get user.email\n" +
            "emit_probe credential-helper git config --global --get credential.helper\n" +
            "emit_probe gcm git credential-manager --version\n" +
            "emit_probe gcm-legacy git-credential-manager --version\n" +
            "emit_shell_probe ssh-keys 'for f in \"$HOME\"/.ssh/*.pub; do [ -e \"$f\" ] || continue; basename \"$f\"; done'\n" +
            "emit_probe github-ssh ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com";

        private const string SetGlobalConfigScript =
            "set -u\n" +
            "key=${1:-}\n" +
            "value=${2:-}\n" +
            "if [ -z \"$key\" ] || [ -z \"$value\" ]; then exit 64; fi\n" +
            "git config --global \"$key\" \"$value\"";

        private readonly RemoteMacSshConfigSnapshot _config;
        private readonly IRemoteSshCommandExecutor _executor;

        public RemoteMacGitEnvironmentService(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacGitEnvironmentService(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _config = config ?? new RemoteMacSshConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public Task<GitEnvironmentSnapshot> ReadStatusAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => ReadStatus(cancellationToken), cancellationToken);
        }

        public Task SetGlobalUserNameAsync(string value, CancellationToken cancellationToken)
        {
            return SetGlobalConfigAsync("user.name", value, cancellationToken);
        }

        public Task SetGlobalUserEmailAsync(string value, CancellationToken cancellationToken)
        {
            return SetGlobalConfigAsync("user.email", value, cancellationToken);
        }

        private async Task SetGlobalConfigAsync(string key, string value, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(SharedLocalization.Text("GitEnvironmentService.Error.ValueRequired", "Value cannot be empty."));
            }

            cancellationToken.ThrowIfCancellationRequested();
            var result = await _executor.ExecuteAsync(
                _config,
                SetGlobalConfigScript,
                "mycodex-remote-git-config",
                10000,
                cancellationToken,
                key,
                value.Trim()).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(GetBestOutput(result));
            }
        }

        private GitEnvironmentSnapshot ReadStatus(CancellationToken cancellationToken)
        {
            var details = new StringBuilder();
            cancellationToken.ThrowIfCancellationRequested();
            var result = _executor.ExecuteAsync(
                _config,
                ReadStatusScript,
                "mycodex-remote-git-env",
                12000,
                cancellationToken).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();

            var probes = ParseProbeOutput(result.StandardOutput);
            AppendDetails(details, "git --version", GetProbe(probes, "git-version"));
            AppendDetails(details, "command -v git", GetProbe(probes, "git-path"));
            AppendDetails(details, "git config --global --get user.name", GetProbe(probes, "user-name"));
            AppendDetails(details, "git config --global --get user.email", GetProbe(probes, "user-email"));
            AppendDetails(details, "git config --global --get credential.helper", GetProbe(probes, "credential-helper"));
            AppendDetails(details, "git credential-manager --version", GetProbe(probes, "gcm"));
            AppendDetails(details, "git-credential-manager --version", GetProbe(probes, "gcm-legacy"));
            AppendDetails(details, "find ~/.ssh/*.pub", GetProbe(probes, "ssh-keys"));
            AppendDetails(details, "ssh -T git@github.com", GetProbe(probes, "github-ssh"));

            var gitVersion = GetBestOutput(GetProbe(probes, "git-version"));
            var gitPath = GetBestOutput(GetProbe(probes, "git-path"));
            var userName = GetBestOutput(GetProbe(probes, "user-name"));
            var userEmail = GetBestOutput(GetProbe(probes, "user-email"));
            var credentialHelper = GetBestOutput(GetProbe(probes, "credential-helper"));
            var gcm = FirstNonEmpty(GetBestOutput(GetProbe(probes, "gcm")), GetBestOutput(GetProbe(probes, "gcm-legacy")));
            var sshKeys = GetBestOutput(GetProbe(probes, "ssh-keys"));
            var githubSsh = GetBestOutput(GetProbe(probes, "github-ssh"));

            return new GitEnvironmentSnapshot
            {
                Runtime = SharedLocalization.Text("GitEnvironmentService.Runtime.RemoteUnixSsh", "Remote Unix SSH"),
                GitVersion = NormalizeMultilineValue(gitVersion),
                GitPath = NormalizeMultilineValue(gitPath),
                GlobalUserName = NormalizeMultilineValue(userName),
                GlobalUserEmail = NormalizeMultilineValue(userEmail),
                CredentialHelper = NormalizeMultilineValue(credentialHelper),
                GitCredentialManager = NormalizeMultilineValue(gcm),
                SshKeys = NormalizeMultilineValue(sshKeys),
                GitHubSsh = NormalizeMultilineValue(githubSsh),
                DetailedOutput = details.ToString()
            };
        }

        private static Dictionary<string, RemoteSshCommandExecutionResult> ParseProbeOutput(string output)
        {
            var result = new Dictionary<string, RemoteSshCommandExecutionResult>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(output))
            {
                return result;
            }

            var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var parts = line.Split(new[] { '\t' }, 3);
                if (parts.Length < 3 || string.IsNullOrWhiteSpace(parts[0]))
                {
                    continue;
                }

                int exitCode;
                if (!int.TryParse(parts[1], out exitCode))
                {
                    exitCode = 1;
                }

                result[parts[0]] = new RemoteSshCommandExecutionResult
                {
                    ExitCode = exitCode,
                    StandardOutput = DecodeBase64(parts[2]),
                    StandardError = string.Empty
                };
            }

            return result;
        }

        private static RemoteSshCommandExecutionResult GetProbe(
            IDictionary<string, RemoteSshCommandExecutionResult> probes,
            string key)
        {
            RemoteSshCommandExecutionResult result;
            return probes != null && probes.TryGetValue(key, out result)
                ? result
                : new RemoteSshCommandExecutionResult { ExitCode = 1 };
        }

        private static void AppendDetails(StringBuilder details, string title, RemoteSshCommandExecutionResult result)
        {
            details.AppendLine(">> " + title);
            details.AppendLine("ExitCode: " + result.ExitCode);
            if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                details.AppendLine(result.StandardOutput.TrimEnd());
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                details.AppendLine(result.StandardError.TrimEnd());
            }

            details.AppendLine();
        }

        private static string GetBestOutput(RemoteSshCommandExecutionResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError ?? string.Empty
                : result.StandardOutput ?? string.Empty;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (var i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(values[i]))
                {
                    return values[i];
                }
            }

            return string.Empty;
        }

        private static string DecodeBase64(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static string NormalizeMultilineValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }
    }
}
