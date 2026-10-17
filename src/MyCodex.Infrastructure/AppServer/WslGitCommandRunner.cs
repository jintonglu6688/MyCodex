using System;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    public sealed class WslGitCommandRunner : IGitCommandRunner
    {
        private const string GitScript =
            "set -u\n" +
            "workdir=${1:-}\n" +
            "if [ -z \"$workdir\" ]; then exit 64; fi\n" +
            "if [ ! -d \"$workdir\" ]; then exit 66; fi\n" +
            "shift\n" +
            "git -C \"$workdir\" \"$@\"";

        private readonly WslAppServerConfigSnapshot _config;
        private readonly IWslCommandExecutor _executor;

        public WslGitCommandRunner(WslAppServerConfigSnapshot config)
            : this(config, new WslCommandExecutor())
        {
        }

        internal WslGitCommandRunner(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            _config = config ?? new WslAppServerConfigSnapshot();
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds)
        {
            return Run(workingDirectory, arguments, timeoutMilliseconds, CancellationToken.None);
        }

        public GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var commandArguments = new[] { workingDirectory ?? string.Empty }
                .Concat(arguments ?? new string[0])
                .ToArray();
            var result = _executor.ExecuteAsync(
                _config,
                GitScript,
                "mycodex-wsl-git",
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
    }
}
