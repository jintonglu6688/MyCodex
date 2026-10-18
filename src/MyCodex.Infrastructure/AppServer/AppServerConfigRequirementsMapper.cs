using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerConfigRequirementsMapper
    {
        public static AppServerConfigRequirementsSnapshot BuildRequirements(IDictionary<string, object> result)
        {
            var requirements = AppServerJsonValueReader.GetDictionary(result, "requirements");
            if (requirements.Count == 0)
            {
                return new AppServerConfigRequirementsSnapshot
                {
                    HasRequirements = false,
                    AllowedApprovalPolicies = new List<string>(),
                    AllowedSandboxModes = new List<string>(),
                    AllowedWebSearchModes = new List<string>(),
                    FeatureRequirements = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                };
            }

            return new AppServerConfigRequirementsSnapshot
            {
                HasRequirements = true,
                AllowedApprovalPolicies = BuildApprovalPolicies(requirements),
                AllowedSandboxModes = BuildStringList(requirements, "allowedSandboxModes"),
                AllowedWebSearchModes = BuildStringList(requirements, "allowedWebSearchModes"),
                FeatureRequirements = BuildBoolDictionary(requirements, "featureRequirements"),
                EnforceResidency = AppServerJsonValueReader.GetString(requirements, "enforceResidency"),
                NewThreadModelDefaults = BuildNewThreadModelDefaults(requirements)
            };
        }

        private static AppServerNewThreadModelDefaults BuildNewThreadModelDefaults(IDictionary<string, object> requirements)
        {
            var models = AppServerJsonValueReader.GetDictionary(requirements, "models");
            var newThread = AppServerJsonValueReader.GetDictionary(models, "newThread");
            if (newThread.Count == 0)
            {
                return null;
            }

            return new AppServerNewThreadModelDefaults
            {
                ModelId = AppServerJsonValueReader.GetString(newThread, "model"),
                ModelReasoningEffort = AppServerJsonValueReader.GetString(newThread, "modelReasoningEffort"),
                ServiceTier = AppServerJsonValueReader.GetString(newThread, "serviceTier")
            };
        }

        private static List<string> BuildApprovalPolicies(IDictionary<string, object> requirements)
        {
            var result = new List<string>();
            foreach (var item in GetArray(requirements, "allowedApprovalPolicies"))
            {
                var text = item as string;
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AddUnique(result, text.Trim());
                    continue;
                }

                var policy = item as IDictionary<string, object>;
                if (policy == null)
                {
                    continue;
                }

                if (policy.ContainsKey("granular"))
                {
                    AddUnique(result, "granular");
                    continue;
                }

                foreach (var key in policy.Keys.Where(key => !string.IsNullOrWhiteSpace(key)))
                {
                    AddUnique(result, key.Trim());
                }
            }

            return result;
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

        private static List<string> BuildStringList(IDictionary<string, object> source, string key)
        {
            var result = new List<string>();
            foreach (var item in GetArray(source, key))
            {
                var text = item == null ? null : Convert.ToString(item);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AddUnique(result, text.Trim());
                }
            }

            return result;
        }

        private static IDictionary<string, bool> BuildBoolDictionary(IDictionary<string, object> source, string key)
        {
            var dictionary = AppServerJsonValueReader.GetDictionary(source, key);
            var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in dictionary)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                bool parsed;
                if (pair.Value is bool)
                {
                    result[pair.Key.Trim()] = (bool)pair.Value;
                }
                else if (pair.Value != null && bool.TryParse(Convert.ToString(pair.Value), out parsed))
                {
                    result[pair.Key.Trim()] = parsed;
                }
            }

            return result;
        }

        private static void AddUnique(List<string> target, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            if (!target.Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
            {
                target.Add(value);
            }
        }
    }
}
