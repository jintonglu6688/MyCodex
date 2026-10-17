using System.Collections.Generic;

namespace MyCodex.Application.InternalTools
{
    public interface IInternalToolRegistry
    {
        IReadOnlyList<InternalToolDefinition> ListDefinitions(string mode);

        bool TryGetExecutor(string toolNamespace, string toolName, out IInternalToolExecutor executor);
    }
}
