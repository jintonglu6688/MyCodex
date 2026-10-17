using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerSubAgentClient
    {
        Task<List<AppServerThreadSummary>> ListSubAgentThreadsAsync(string parentThreadId, CancellationToken cancellationToken, bool includeArchived = false);

        Task SendSubAgentMessageAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, CancellationToken cancellationToken);

        Task InterruptSubAgentTurnAsync(string threadId, CancellationToken cancellationToken);

        void RememberSubAgentParent(string threadId, string parentThreadId);
    }
}
