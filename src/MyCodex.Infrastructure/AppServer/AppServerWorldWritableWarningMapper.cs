using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerWorldWritableWarningMapper
    {
        internal static AppServerWindowsWorldWritableWarningEvent BuildEvent(IDictionary<string, object> parameters)
        {
            var warning = new AppServerWindowsWorldWritableWarningEvent
            {
                ExtraCount = GetUInt(parameters, "extraCount"),
                FailedScan = GetBool(parameters, "failedScan")
            };

            foreach (var item in GetArray(parameters, "samplePaths"))
            {
                if (item == null)
                {
                    continue;
                }

                var path = Convert.ToString(item, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    warning.SamplePaths.Add(path);
                }
            }

            return warning;
        }

        private static uint GetUInt(IDictionary<string, object> source, string key)
        {
            object value;
            uint result;
            return source != null &&
                source.TryGetValue(key, out value) &&
                value != null &&
                uint.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out result)
                    ? result
                    : 0U;
        }

        private static bool GetBool(IDictionary<string, object> source, string key)
        {
            object value;
            bool result;
            return source != null &&
                source.TryGetValue(key, out value) &&
                value != null &&
                bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out result) &&
                result;
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                yield break;
            }

            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array)
                {
                    yield return item;
                }

                yield break;
            }

            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null || value is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                yield return item;
            }
        }
    }
}
