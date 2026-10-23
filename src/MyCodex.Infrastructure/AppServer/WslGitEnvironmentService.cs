using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslGitEnvironmentService : IGitEnvironmentService
    {
        private const string RunCommandScript =
            "set +e\n" +
            "command_key=${1:-}\n" +
            "case \"$command_key\" in\n" +
            "  git-version) git --version ;;\n" +
            "  git-path) command -v git ;;\n" +
            "  user-name) git config --global --get user.name ;;\n" +
            "  user-email) git config --global --get user.email ;;\n" +
            "  credential-helper) git config --global --get credential.helper ;;\n" +
            "  gcm) git credential-manager --version ;;\n" +
            "  gcm-legacy) git-credential-manager --version ;;\n" +
            "  ssh-keys) for f in \"$HOME\"/.ssh/*.pub; do [ -e \"$f\" ] || continue; basename \"$f\"; done ;;\n" +
            "  github-ssh) ssh -o BatchMode=yes -o ConnectTimeout=5 -T git@github.com ;;\n" +
            "  *) exit 64 ;;\n" +
            "esac";

        private const string SetGlobalConfigScript =
            "set -u\n" +
            "key=${1:-}\n" +
            "value=${2:-}\n" +
            "if [ -z \"$key\" ] || [ -z \"$value\" ]; then exit 64; fi\n" +
            "git config --global \"$key\" \"$value\"";

        private readonly WslAppServerConfigSnapshot _config;
        private readonly IWslCommandExecutor _executor;

        public WslGitEnvironmentService(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslGitEnvironmentService(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _config = config ?? new WslAppServerConfigSnapshot();
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
                "mycodex-wsl-git-config",
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
            var gitVersion = ReadValue(details, "git --version", "git-version", 8000, cancellationToken);
            var gitPath = ReadValue(details, "command -v git", "git-path", 8000, cancellationToken);
            var userName = ReadValue(details, "git config --global --get user.name", "user-name", 8000, cancellationToken);
            var userEmail = ReadValue(details, "git config --global --get user.email", "user-email", 8000, cancellationToken);
            var credentialHelper = ReadValue(details, "git config --global --get credential.helper", "credential-helper", 8000, cancellationToken);
            var gcm = ReadFirstAvailable(
                details,
                cancellationToken,
                new CommandProbe("git credential-manager --version", "gcm"),
                new CommandProbe("git-credential-manager --version", "gcm-legacy"));
            var sshKeys = ReadValue(details, "find ~/.ssh/*.pub", "ssh-keys", 8000, cancellationToken);
            var githubSsh = ReadValue(details, "ssh -T git@github.com", "github-ssh", 8000, cancellationToken);

            return UnixGitEnvironmentText.BuildSnapshot(
                SharedLocalization.Text("GitEnvironmentService.Runtime.Wsl", "WSL"),
                gitVersion,
                gitPath,
                userName,
                userEmail,
                credentialHelper,
                gcm,
                sshKeys,
                githubSsh,
                details);
        }

        private string ReadFirstAvailable(StringBuilder details, CancellationToken cancellationToken, params CommandProbe[] commands)
        {
            for (var i = 0; i < commands.Length; i++)
            {
                var value = ReadValue(details, commands[i].Title, commands[i].Key, 8000, cancellationToken);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private string ReadValue(
            StringBuilder details,
            string title,
            string commandKey,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _executor.ExecuteAsync(
                _config,
                RunCommandScript,
                "mycodex-wsl-git-env",
                timeoutMilliseconds,
                cancellationToken,
                commandKey).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            AppendDetails(details, title, result);
            return GetBestOutput(result);
        }

        private static void AppendDetails(StringBuilder details, string title, WslCommandExecutionResult result)
        {
            UnixGitEnvironmentText.AppendDetails(details, title, result.ExitCode, result.StandardOutput, result.StandardError);
        }

        private static string GetBestOutput(WslCommandExecutionResult result)
        {
            return result == null ? string.Empty : UnixGitEnvironmentText.GetBestOutput(result.StandardOutput, result.StandardError);
        }

        private sealed class CommandProbe
        {
            public CommandProbe(string title, string key)
            {
                Title = title;
                Key = key;
            }

            public string Title { get; private set; }

            public string Key { get; private set; }
        }
    }
}
