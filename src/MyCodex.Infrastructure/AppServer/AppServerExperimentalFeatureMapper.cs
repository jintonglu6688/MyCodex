using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerExperimentalFeatureMapper
    {
        public static List<AppServerExperimentalFeature> BuildFeatures(IDictionary<string, object> result)
        {
            var features = new List<AppServerExperimentalFeature>();
            foreach (var item in GetArray(result, "data"))
            {
                var feature = BuildFeature(item as IDictionary<string, object>);
                if (feature != null)
                {
                    features.Add(feature);
                }
            }

            return features
                .OrderBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static AppServerExperimentalFeature BuildFeature(IDictionary<string, object> source)
        {
            if (source == null)
            {
                return null;
            }

            var name = AppServerJsonValueReader.GetString(source, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            return new AppServerExperimentalFeature
            {
                Name = name.Trim(),
                Stage = AppServerJsonValueReader.GetString(source, "stage"),
                Enabled = GetBool(source, "enabled"),
                DefaultEnabled = GetBool(source, "defaultEnabled"),
                DisplayName = AppServerJsonValueReader.GetString(source, "displayName"),
                Description = AppServerJsonValueReader.GetString(source, "description"),
                Announcement = AppServerJsonValueReader.GetString(source, "announcement")
            };
        }

        public static string GetNextCursor(IDictionary<string, object> result)
        {
            return AppServerJsonValueReader.GetString(result, "nextCursor");
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                return Enumerable.Empty<object>();
            }

            var array = value as IEnumerable<object>;
            return array ?? Enumerable.Empty<object>();
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
