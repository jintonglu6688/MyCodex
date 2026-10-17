using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    public static class AppServerDynamicToolSpecMapper
    {
        public static IDictionary<string, object> ToWire(AppServerDynamicToolSpec spec)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            var item = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "name", spec.Name },
                { "description", spec.Description },
                { "inputSchema", spec.InputSchema ?? new Dictionary<string, object>(StringComparer.Ordinal) }
            };

            if (!string.IsNullOrWhiteSpace(spec.Namespace))
            {
                item["namespace"] = spec.Namespace;
            }

            if (spec.DeferLoading)
            {
                item["deferLoading"] = true;
            }

            return item;
        }

        public static object[] ToWireArray(IEnumerable<AppServerDynamicToolSpec> specs)
        {
            var items = new List<object>();
            if (specs == null)
            {
                return items.ToArray();
            }

            foreach (var spec in specs)
            {
                items.Add(ToWire(spec));
            }

            return items.ToArray();
        }
    }
}
