using System.Collections.Generic;
using System.Threading;

namespace MyCodex.Application.Services
{
    public interface IGitCommandRunner
    {
        GitCommandResult Run(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMilliseconds);

        GitCommandResult Run(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            int timeoutMilliseconds,
            CancellationToken cancellationToken);
    }
}
