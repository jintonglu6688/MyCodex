using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class UnixCommandResult
    {
        public int ExitCode { get; set; }

        public string StandardOutput { get; set; }

        public string StandardError { get; set; }
    }

    internal delegate Task<UnixCommandResult> UnixCommandInvoker(
        string script,
        string operationName,
        int timeoutMilliseconds,
        params string[] arguments);

    // For long operations the user can cancel, such as a clone: the token stops the remote command.
    internal delegate Task<UnixCommandResult> UnixCancellableCommandInvoker(
        string script,
        string operationName,
        int timeoutMilliseconds,
        CancellationToken cancellationToken,
        params string[] arguments);

    internal static class UnixCommandInvokers
    {
        internal static UnixCancellableCommandInvoker ForWslCancellable(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }

            var target = config ?? new WslAppServerConfigSnapshot();
            return async (script, operationName, timeoutMilliseconds, cancellationToken, arguments) =>
            {
                var result = await executor.ExecuteAsync(
                    target,
                    script,
                    operationName,
                    timeoutMilliseconds,
                    cancellationToken,
                    arguments).ConfigureAwait(false);
                return result == null ? null : Create(result.ExitCode, result.StandardOutput, result.StandardError);
            };
        }

        internal static UnixCancellableCommandInvoker ForRemoteCancellable(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }

            var target = config ?? new RemoteMacSshConfigSnapshot();
            return async (script, operationName, timeoutMilliseconds, cancellationToken, arguments) =>
            {
                var result = await executor.ExecuteAsync(
                    target,
                    script,
                    operationName,
                    timeoutMilliseconds,
                    cancellationToken,
                    arguments).ConfigureAwait(false);
                return result == null ? null : Create(result.ExitCode, result.StandardOutput, result.StandardError);
            };
        }

        internal static UnixCommandInvoker ForWsl(WslAppServerConfigSnapshot config, IWslCommandExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }

            var target = config ?? new WslAppServerConfigSnapshot();
            return async (script, operationName, timeoutMilliseconds, arguments) =>
            {
                var result = await executor.ExecuteAsync(
                    target,
                    script,
                    operationName,
                    timeoutMilliseconds,
                    arguments).ConfigureAwait(false);
                return result == null ? null : Create(result.ExitCode, result.StandardOutput, result.StandardError);
            };
        }

        internal static UnixCommandInvoker ForRemote(RemoteMacSshConfigSnapshot config, IRemoteSshCommandExecutor executor)
        {
            if (executor == null)
            {
                throw new ArgumentNullException(nameof(executor));
            }

            var target = config ?? new RemoteMacSshConfigSnapshot();
            return async (script, operationName, timeoutMilliseconds, arguments) =>
            {
                var result = await executor.ExecuteAsync(
                    target,
                    script,
                    operationName,
                    timeoutMilliseconds,
                    arguments).ConfigureAwait(false);
                return result == null ? null : Create(result.ExitCode, result.StandardOutput, result.StandardError);
            };
        }

        private static UnixCommandResult Create(int exitCode, string standardOutput, string standardError)
        {
            return new UnixCommandResult
            {
                ExitCode = exitCode,
                StandardOutput = standardOutput,
                StandardError = standardError
            };
        }
    }
}
