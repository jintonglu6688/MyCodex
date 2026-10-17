using System;
using System.Collections.Generic;

namespace MyCodex.Application.InternalTools
{
    public sealed class InternalToolRegistry : IInternalToolRegistry
    {
        private readonly List<InternalToolDefinition> _definitions = new List<InternalToolDefinition>();
        private readonly Dictionary<string, IInternalToolExecutor> _executors =
            new Dictionary<string, IInternalToolExecutor>(StringComparer.OrdinalIgnoreCase);

        public InternalToolRegistry(IEnumerable<IInternalToolExecutor> executors)
        {
            if (executors == null)
            {
                return;
            }

            foreach (var executor in executors)
            {
                if (executor == null || executor.Definitions == null)
                {
                    continue;
                }

                foreach (var definition in executor.Definitions)
                {
                    if (definition == null)
                    {
                        continue;
                    }

                    var key = BuildKey(definition.Namespace, definition.Name);
                    if (_executors.ContainsKey(key))
                    {
                        throw new InvalidOperationException("Internal tool is already registered: " + key);
                    }

                    _definitions.Add(definition);
                    _executors.Add(key, executor);
                }
            }
        }

        public IReadOnlyList<InternalToolDefinition> ListDefinitions(string mode)
        {
            var results = new List<InternalToolDefinition>();
            foreach (var definition in _definitions)
            {
                if (MatchesMode(definition, mode))
                {
                    results.Add(definition);
                }
            }

            return results;
        }

        public bool TryGetExecutor(string toolNamespace, string toolName, out IInternalToolExecutor executor)
        {
            if (string.IsNullOrWhiteSpace(toolName))
            {
                executor = null;
                return false;
            }

            return _executors.TryGetValue(BuildKey(toolNamespace, toolName), out executor);
        }

        private static bool MatchesMode(InternalToolDefinition definition, string mode)
        {
            if (definition.Modes == null || definition.Modes.Count == 0)
            {
                return true;
            }

            foreach (var candidate in definition.Modes)
            {
                if (string.Equals(candidate, mode, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildKey(string toolNamespace, string toolName)
        {
            return (toolNamespace ?? string.Empty).Trim() + "/" + (toolName ?? string.Empty).Trim();
        }
    }
}
