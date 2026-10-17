using MyCodex.Application.Services;

namespace MyCodex.Application.Interfaces
{
    public interface IGitDiffCommandRunner
    {
        GitCommandResult Run(string workspacePath, string arguments, int timeoutMilliseconds);
    }
}
