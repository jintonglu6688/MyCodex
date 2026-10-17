using System;
using System.Collections.Generic;
using MyCodex.Application.InternalTools;

namespace MyCodex.Infrastructure.AppServer
{
    public static class AppServerDynamicToolCallMapper
    {
        public static InternalToolRequest FromParams(IDictionary<string, object> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return new InternalToolRequest
            {
                ThreadId = GetString(parameters, "threadId"),
                TurnId = GetString(parameters, "turnId"),
                CallId = GetString(parameters, "callId"),
                Namespace = GetString(parameters, "namespace"),
                Name = GetString(parameters, "tool"),
                Arguments = GetValue(parameters, "arguments")
            };
        }

        private static object GetValue(IDictionary<string, object> values, string key)
        {
            object value;
            return values.TryGetValue(key, out value) ? value : null;
        }

        private static string GetString(IDictionary<string, object> values, string key)
        {
            var value = GetValue(values, key);
            return value == null ? null : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
