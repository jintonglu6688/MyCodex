using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class RemoteMacGitCommandRunner : IGitCommandRunner
    {
        private const string GitScript =
            "emulate -L sh 2>/dev/null || true\n" +
            "set -u\n" +
            "workdir=${1:-}\n" +
            "if [ -z \"$workdir\" ]; then exit 64; fi\n" +
            "if [ ! -d \"$workdir\" ]; then exit 66; fi\n" +
            "shift\n" +
            "git -C \"$workdir\" \"$@\"";

        private const string GitStatusBatchScript =
            "emulate -L sh 2>/dev/null || true\n" +
            "set -u\n" +
            "workdir=${1:-}\n" +
            "if [ -z \"$workdir\" ]; then exit 64; fi\n" +
            "if [ ! -d \"$workdir\" ]; then exit 66; fi\n" +
            "encode_file() { perl -MMIME::Base64=encode_base64 -e 'local $/; print encode_base64(<STDIN>, \"\")' < \"$1\"; }\n" +
            "emit() {\n" +
            "  key=$1\n" +
            "  shift\n" +
            "  stdout_file=$(mktemp)\n" +
            "  stderr_file=$(mktemp)\n" +
            "  \"$@\" >\"$stdout_file\" 2>\"$stderr_file\"\n" +
            "  code=$?\n" +
            "  stdout=$(encode_file \"$stdout_file\")\n" +
            "  stderr=$(encode_file \"$stderr_file\")\n" +
            "  rm -f \"$stdout_file\" \"$stderr_file\"\n" +
            "  printf '%s\\t%s\\t%s\\t%s\\n' \"$key\" \"$code\" \"$stdout\" \"$stderr\"\n" +
            "}\n" +
            "emit status git -C \"$workdir\" status --porcelain=v1 -z --branch --untracked-files=all --no-renames\n" +
            "emit diff-numstat git -C \"$workdir\" diff --numstat --no-renames -z --no-ext-diff\n" +
            "emit diff-cached-numstat git -C \"$workdir\" diff --cached --numstat --no-renames -z --no-ext-diff\n" +
            "emit head-sha git -C \"$workdir\" rev-parse --short HEAD\n" +
            "emit origin-url git -C \"$workdir\" config --get remote.origin.url\n" +
            "emit upstream git -C \"$workdir\" rev-parse --abbrev-ref --symbolic-full-name '@{u}'\n" +
            "emit ahead-behind git -C \"$workdir\" rev-list --left-right --count 'HEAD...@{u}'\n" +
            "emit stash-list git -C \"$workdir\" stash list --format=%gd\n" +
            "emit cherry-pick-head git -C \"$workdir\" rev-parse -q --verify CHERRY_PICK_HEAD\n" +
            "emit revert-head git -C \"$workdir\" rev-parse -q --verify REVERT_HEAD\n" +
            "emit merge-head git -C \"$workdir\" rev-parse -q --verify MERGE_HEAD\n" +
            "emit rebase-head git -C \"$workdir\" rev-parse -q --verify REBASE_HEAD";

        private readonly RemoteMacSshConfigSnapshot _config;
        private readonly IRemoteSshCommandExecutor _executor;
        private readonly object _statusCacheSyncRoot = new object();
        private StatusBatchCache _statusCache;

        public RemoteMacGitCommandRunner(RemoteMacSshConfigSnapshot config)
            : this(config, new RemoteSshCommandExecutor())
        {
        }

        internal RemoteMacGitCommandRunner(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            _config = config ?? new RemoteMacSshConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds)
        {
            return Run(workingDirectory, arguments, timeoutMilliseconds, CancellationToken.None);
        }

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsStatusBatchTrigger(arguments))
            {
                var batchResult = TryRunStatusBatch(workingDirectory, timeoutMilliseconds, cancellationToken);
                if (batchResult != null)
                {
                    return batchResult;
                }
            }

            GitCommandResult cachedResult;
            if (TryGetStatusBatchResult(workingDirectory, arguments, out cachedResult))
            {
                return cachedResult;
            }

            var commandArguments = new[] { workingDirectory ?? string.Empty }
                .Concat(arguments ?? new string[0])
                .ToArray();
            var result = _executor.ExecuteAsync(
                _config,
                GitScript,
                "mycodex-remote-git",
                timeoutMilliseconds,
                cancellationToken,
                commandArguments).GetAwaiter().GetResult();

            return new GitCommandResult
            {
                ExitCode = result.ExitCode,
                StandardOutput = GitOutputSanitizer.RedactSensitiveText(result.StandardOutput),
                StandardError = GitOutputSanitizer.RedactSensitiveText(result.StandardError)
            };
        }

        private GitCommandResult TryRunStatusBatch(string workingDirectory, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            var result = _executor.ExecuteAsync(
                _config,
                GitStatusBatchScript,
                "mycodex-remote-git-status-batch",
                timeoutMilliseconds,
                cancellationToken,
                workingDirectory ?? string.Empty).GetAwaiter().GetResult();
            if (result.ExitCode != 0)
            {
                return null;
            }

            var commands = ParseStatusBatchOutput(result.StandardOutput);
            if (commands.Count == 0)
            {
                return null;
            }

            var cache = new StatusBatchCache(workingDirectory, commands);
            lock (_statusCacheSyncRoot)
            {
                _statusCache = cache;
            }

            GitCommandResult statusResult;
            return commands.TryGetValue(StatusCommandKey, out statusResult) ? statusResult : null;
        }

        private bool TryGetStatusBatchResult(string workingDirectory, IReadOnlyList<string> arguments, out GitCommandResult result)
        {
            result = null;
            var key = BuildStatusBatchCommandKey(arguments);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            StatusBatchCache cache;
            lock (_statusCacheSyncRoot)
            {
                cache = _statusCache;
            }

            if (cache == null ||
                !string.Equals(cache.WorkingDirectory ?? string.Empty, workingDirectory ?? string.Empty, StringComparison.Ordinal) ||
                DateTime.UtcNow - cache.CreatedAtUtc > TimeSpan.FromSeconds(5))
            {
                return false;
            }

            return cache.Results.TryGetValue(key, out result);
        }

        private static Dictionary<string, GitCommandResult> ParseStatusBatchOutput(string output)
        {
            var results = new Dictionary<string, GitCommandResult>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(output))
            {
                return results;
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
                if (parts.Length < 4)
                {
                    continue;
                }

                int exitCode;
                if (!int.TryParse(parts[1], out exitCode))
                {
                    exitCode = 1;
                }

                results[parts[0]] = new GitCommandResult
                {
                    ExitCode = exitCode,
                    StandardOutput = GitOutputSanitizer.RedactSensitiveText(DecodeBase64(parts[2])),
                    StandardError = GitOutputSanitizer.RedactSensitiveText(DecodeBase64(parts[3]))
                };
            }

            return results;
        }

        private static bool IsStatusBatchTrigger(IReadOnlyList<string> arguments)
        {
            return string.Equals(BuildStatusBatchCommandKey(arguments), StatusCommandKey, StringComparison.Ordinal);
        }

        private const string StatusCommandKey = "status";

        private static string BuildStatusBatchCommandKey(IReadOnlyList<string> arguments)
        {
            if (ArgumentsEqual(arguments, "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all", "--no-renames"))
            {
                return StatusCommandKey;
            }

            if (ArgumentsEqual(arguments, "diff", "--numstat", "--no-renames", "-z", "--no-ext-diff"))
            {
                return "diff-numstat";
            }

            if (ArgumentsEqual(arguments, "diff", "--cached", "--numstat", "--no-renames", "-z", "--no-ext-diff"))
            {
                return "diff-cached-numstat";
            }

            if (ArgumentsEqual(arguments, "rev-parse", "--short", "HEAD"))
            {
                return "head-sha";
            }

            if (ArgumentsEqual(arguments, "config", "--get", "remote.origin.url"))
            {
                return "origin-url";
            }

            if (ArgumentsEqual(arguments, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}"))
            {
                return "upstream";
            }

            if (ArgumentsEqual(arguments, "rev-list", "--left-right", "--count", "HEAD...@{u}"))
            {
                return "ahead-behind";
            }

            if (ArgumentsEqual(arguments, "stash", "list", "--format=%gd"))
            {
                return "stash-list";
            }

            if (ArgumentsEqual(arguments, "rev-parse", "-q", "--verify", "CHERRY_PICK_HEAD"))
            {
                return "cherry-pick-head";
            }

            if (ArgumentsEqual(arguments, "rev-parse", "-q", "--verify", "REVERT_HEAD"))
            {
                return "revert-head";
            }

            if (ArgumentsEqual(arguments, "rev-parse", "-q", "--verify", "MERGE_HEAD"))
            {
                return "merge-head";
            }

            if (ArgumentsEqual(arguments, "rev-parse", "-q", "--verify", "REBASE_HEAD"))
            {
                return "rebase-head";
            }

            return null;
        }

        private static bool ArgumentsEqual(IReadOnlyList<string> actual, params string[] expected)
        {
            if (actual == null || actual.Count != expected.Length)
            {
                return false;
            }

            for (var i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(actual[i], expected[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
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

        private sealed class StatusBatchCache
        {
            public StatusBatchCache(string workingDirectory, Dictionary<string, GitCommandResult> results)
            {
                WorkingDirectory = workingDirectory;
                Results = results ?? new Dictionary<string, GitCommandResult>(StringComparer.Ordinal);
                CreatedAtUtc = DateTime.UtcNow;
            }

            public string WorkingDirectory { get; private set; }

            public Dictionary<string, GitCommandResult> Results { get; private set; }

            public DateTime CreatedAtUtc { get; private set; }
        }
    }
}
