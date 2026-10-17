using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerInteractionClient
    {
        bool HasInterruptibleTurn(string threadId);

        Task InterruptAsync(string threadId, string turnId = null);

        Task ResolveApprovalRequestAsync(string requestId, AppServerApprovalDecision decision);

        Task ResolveToolUserInputRequestAsync(string requestId, IDictionary<string, string[]> answers);

        Task CancelToolUserInputRequestAsync(string requestId);
    }
}
