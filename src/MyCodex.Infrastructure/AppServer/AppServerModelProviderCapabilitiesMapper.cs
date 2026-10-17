using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerModelProviderCapabilitiesMapper
    {
        public static AppServerModelProviderCapabilities BuildCapabilities(IDictionary<string, object> result)
        {
            return new AppServerModelProviderCapabilities
            {
                ImageGeneration = GetBool(result, "imageGeneration"),
                NamespaceTools = GetBool(result, "namespaceTools"),
                WebSearch = GetBool(result, "webSearch")
            };
        }

        private static bool GetBool(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return false;
            }

            if (value is bool)
            {
                return (bool)value;
            }

            bool parsed;
            return bool.TryParse(Convert.ToString(value), out parsed) && parsed;
        }
    }
}
