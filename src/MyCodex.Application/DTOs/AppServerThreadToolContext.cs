using System.Collections.Generic;
using MyCodex.Application.InternalTools;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerThreadToolContext
    {
        public IReadOnlyCollection<AppServerDynamicToolSpec> DynamicTools { get; set; }

        public InternalToolExecutionContext ExecutionContext { get; set; }
    }
}
