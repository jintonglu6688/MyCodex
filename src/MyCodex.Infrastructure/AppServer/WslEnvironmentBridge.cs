using System;
using System.Collections.Generic;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class WslEnvironmentBridge
    {
        public static string BuildWslEnvValue(string existingValue, IEnumerable<string> variableNames)
        {
            var entries = new List<string>();
            var seenNames = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(existingValue))
            {
                foreach (var entry in existingValue.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var trimmed = entry.Trim();
                    if (trimmed.Length == 0)
                    {
                        continue;
                    }

                    entries.Add(trimmed);
                    var entryName = GetEntryName(trimmed);
                    if (entryName.Length > 0)
                    {
                        seenNames.Add(entryName);
                    }
                }
            }

            if (variableNames != null)
            {
                foreach (var variableName in variableNames)
                {
                    if (!IsForwardableVariableName(variableName) || seenNames.Contains(variableName))
                    {
                        continue;
                    }

                    entries.Add(variableName);
                    seenNames.Add(variableName);
                }
            }

            return string.Join(":", entries.ToArray());
        }

        private static string GetEntryName(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                return string.Empty;
            }

            var slashIndex = entry.IndexOf('/');
            return slashIndex < 0 ? entry : entry.Substring(0, slashIndex);
        }

        private static bool IsForwardableVariableName(string variableName)
        {
            if (string.IsNullOrWhiteSpace(variableName) ||
                string.Equals(variableName, "WSLENV", StringComparison.Ordinal))
            {
                return false;
            }

            var first = variableName[0];
            if (!((first >= 'A' && first <= 'Z') || (first >= 'a' && first <= 'z') || first == '_'))
            {
                return false;
            }

            for (var index = 1; index < variableName.Length; index++)
            {
                var ch = variableName[index];
                if (!((ch >= 'A' && ch <= 'Z') ||
                      (ch >= 'a' && ch <= 'z') ||
                      (ch >= '0' && ch <= '9') ||
                      ch == '_'))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
