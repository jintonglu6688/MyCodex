using System.Collections.Generic;

namespace MyCodex.Application.InternalTools
{
    public sealed class InternalToolDefinition
    {
        public string Namespace { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public IDictionary<string, object> InputSchema { get; set; }

        public bool DeferLoading { get; set; }

        public IReadOnlyCollection<string> Modes { get; set; }
    }
}
