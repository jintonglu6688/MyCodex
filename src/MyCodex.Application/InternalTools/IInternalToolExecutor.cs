using System.Collections.Generic;
using System.Threading.Tasks;

namespace MyCodex.Application.InternalTools
{
    public interface IInternalToolExecutor
    {
        IReadOnlyCollection<InternalToolDefinition> Definitions { get; }

        Task<InternalToolResult> ExecuteAsync(
            InternalToolRequest request,
            InternalToolExecutionContext context);
    }
}
