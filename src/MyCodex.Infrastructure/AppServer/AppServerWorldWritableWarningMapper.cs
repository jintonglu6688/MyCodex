using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerWorldWritableWarningMapper
    {
        internal static AppServerWindowsWorldWritableWarningEvent BuildEvent(IDictionary<string, object> parameters)
        {
            var warning = new AppServerWindowsWorldWritableWarningEvent
            {
                ExtraCount = GetUInt(parameters, "extraCount"),
                FailedScan = JsonValueReader.GetBool(parameters, "failedScan")
            };

            foreach (var item in AppServerJsonValueReader.GetArray(parameters, "samplePaths"))
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
    }
}
