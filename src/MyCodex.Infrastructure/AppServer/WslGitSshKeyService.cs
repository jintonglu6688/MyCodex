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
    public sealed class WslGitSshKeyService : IGitSshKeyService
    {
        private const string ReadKeysScript =
            "set +e\n" +
            "dir=\"$HOME/.ssh\"\n" +
            "printf 'DIR\\t%s\\n' \"$dir\"\n" +
            "if [ ! -d \"$dir\" ]; then exit 0; fi\n" +
            "for f in \"$dir\"/*.pub; do\n" +
            "  [ -e \"$f\" ] || continue\n" +
            "  name=$(basename \"$f\")\n" +
            "  private=${f%.pub}\n" +
            "  has_private=no\n" +
            "  [ -f \"$private\" ] && has_private=yes\n" +
            "  fingerprint=$(ssh-keygen -lf \"$f\" 2>/dev/null)\n" +
            "  public_key=$(cat \"$f\" 2>/dev/null)\n" +
            "  printf 'KEY\\t%s\\t%s\\t%s\\t%s\\t%s\\n' \"$name\" \"$f\" \"$private\" \"$has_private\" \"$fingerprint\"\n" +
            "  printf 'PUB\\t%s\\n' \"$public_key\"\n" +
            "done";

        private const string TestGitHubSshScript =
            "set +e\n" +
            "ssh -o BatchMode=yes -o ConnectTimeout=8 -o StrictHostKeyChecking=accept-new -T git@github.com";

        private readonly WslAppServerConfigSnapshot _config;
        private readonly IWslCommandExecutor _executor;

        public WslGitSshKeyService(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslGitSshKeyService(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _config = config ?? new WslAppServerConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public Task<GitSshKeySnapshot> ReadKeysAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => ReadKeys(cancellationToken), cancellationToken);
        }

        public Task<GitSshTestResult> TestGitHubSshAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => TestGitHubSsh(cancellationToken), cancellationToken);
        }

        private GitSshKeySnapshot ReadKeys(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _executor.ExecuteAsync(
                _config,
                ReadKeysScript,
                "mycodex-wsl-ssh-keys",
                10000).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();
            return ParseKeys(result);
        }

        private GitSshTestResult TestGitHubSsh(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _executor.ExecuteAsync(
                _config,
                TestGitHubSshScript,
                "mycodex-wsl-github-ssh",
                12000).GetAwaiter().GetResult();
            cancellationToken.ThrowIfCancellationRequested();

            var output = GetBestOutput(result);
            var succeeded = output.IndexOf("successfully authenticated", StringComparison.OrdinalIgnoreCase) >= 0;
            var details = new StringBuilder();
            AppendDetails(details, "ssh -T git@github.com", result);
            return new GitSshTestResult
            {
                Succeeded = succeeded,
                ExitCode = result.ExitCode,
                Output = output,
                Details = details.ToString()
            };
        }

        private static GitSshKeySnapshot ParseKeys(WslCommandExecutionResult result)
        {
            var keys = new List<GitSshKeyEntry>();
            var details = new StringBuilder();
            var sshDirectory = string.Empty;
            GitSshKeyEntry current = null;
            var output = result == null ? string.Empty : result.StandardOutput ?? string.Empty;
            var lines = output.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.Length == 0)
                {
                    continue;
                }

                if (line.StartsWith("DIR\t", StringComparison.Ordinal))
                {
                    sshDirectory = line.Substring(4);
                    details.AppendLine(string.Format(
                        SharedLocalization.Text("GitSshKeyService.Details.Directory.Template", "SSH directory: {0}"),
                        sshDirectory));
                    continue;
                }

                if (line.StartsWith("KEY\t", StringComparison.Ordinal))
                {
                    var parts = line.Split(new[] { '\t' }, 6);
                    if (parts.Length < 6)
                    {
                        continue;
                    }

                    current = new GitSshKeyEntry
                    {
                        Name = parts[1],
                        PublicKeyPath = parts[2],
                        PrivateKeyPath = parts[3],
                        HasPrivateKey = string.Equals(parts[4], "yes", StringComparison.OrdinalIgnoreCase),
                        Fingerprint = parts[5]
                    };
                    keys.Add(current);
                    continue;
                }

                if (line.StartsWith("PUB\t", StringComparison.Ordinal) && current != null)
                {
                    current.PublicKeyText = line.Substring(4);
                    ParsePublicKey(current.PublicKeyText, out var keyType, out var comment);
                    current.KeyType = keyType;
                    current.Comment = comment;
                    details.AppendLine(string.Format(
                        SharedLocalization.Text("GitSshKeyService.Details.Key.Template", "{0}: {1}; {2}; private: {3}"),
                        current.Name,
                        string.IsNullOrWhiteSpace(current.KeyType) ? SharedLocalization.Text("GitSshKeyService.Value.Unknown", "Unknown") : current.KeyType,
                        string.IsNullOrWhiteSpace(current.Fingerprint) ? SharedLocalization.Text("GitSshKeyService.Value.NoFingerprint", "No fingerprint") : current.Fingerprint,
                        current.HasPrivateKey
                            ? SharedLocalization.Text("GitSshKeyService.Value.Yes", "Yes")
                            : SharedLocalization.Text("GitSshKeyService.Value.No", "No")));
                }
            }

            if (keys.Count == 0)
            {
                details.AppendLine(SharedLocalization.Text("GitSshKeyService.Details.NoKeys", "No public SSH keys were found."));
            }

            if (result != null && !string.IsNullOrWhiteSpace(result.StandardError))
            {
                details.AppendLine(result.StandardError.TrimEnd());
            }

            return new GitSshKeySnapshot
            {
                SshDirectory = sshDirectory,
                Keys = keys,
                Details = details.ToString()
            };
        }

        private static void ParsePublicKey(string publicKeyText, out string keyType, out string comment)
        {
            keyType = string.Empty;
            comment = string.Empty;
            if (string.IsNullOrWhiteSpace(publicKeyText))
            {
                return;
            }

            var parts = publicKeyText.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
            {
                keyType = parts[0];
            }

            if (parts.Length > 2)
            {
                comment = parts[2];
            }
        }

        private static void AppendDetails(StringBuilder details, string title, WslCommandExecutionResult result)
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
        }

        private static string GetBestOutput(WslCommandExecutionResult result)
        {
            if (result == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(result.StandardOutput)
                ? result.StandardError ?? string.Empty
                : result.StandardOutput ?? string.Empty;
        }
    }
}
