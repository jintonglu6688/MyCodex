using System;
using System.Collections.Generic;
using MyCodex.Application.InternalTools;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerDynamicToolSpec
    {
        public string Namespace { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public IDictionary<string, object> InputSchema { get; set; }

        public bool DeferLoading { get; set; }

        public static AppServerDynamicToolSpec FromInternalDefinition(InternalToolDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            return new AppServerDynamicToolSpec
            {
                Namespace = definition.Namespace,
                Name = definition.Name,
                Description = definition.Description,
                InputSchema = definition.InputSchema,
                DeferLoading = definition.DeferLoading
            };
        }
    }
}
