using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerHookClient
    {
        /// <summary>Lists a loaded thread's hooks when threadId is set, otherwise the workspace's configured hooks.</summary>
        Task<AppServerHooksListResult> ListHooksAsync(string workspacePath, string threadId);

        Task<AppServerConfigWriteResult> WriteHookStateAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string expectedVersion);
    }
}
