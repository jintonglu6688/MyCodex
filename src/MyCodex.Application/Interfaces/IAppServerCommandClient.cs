using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerCommandClient
    {
        Task<AppServerCommandExecResult> ExecuteCommandAsync(string commandText, string workspacePath, ApprovalMode approvalMode, string processId, long timeoutMs);

        Task<AppServerCommandExecResult> ExecuteCommandAsync(string commandText, string workspacePath, ApprovalMode approvalMode, string processId, long timeoutMs, Action<string, string> outputReceived);

        Task TerminateCommandAsync(string processId);

        Task WriteCommandInputAsync(string processId, string input);

        Task ResizeCommandAsync(
            string processId,
            AppServerCommandTerminalSize size,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
