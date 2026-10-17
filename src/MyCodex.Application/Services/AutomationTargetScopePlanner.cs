using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AutomationTargetScopePlanner
    {
        private readonly string _runtimeScopeId;
        private readonly bool _usesUnixPaths;

        public AutomationTargetScopePlanner(string runtimeScopeId, bool usesUnixPaths)
        {
            if (string.IsNullOrWhiteSpace(runtimeScopeId))
            {
                throw new ArgumentException(nameof(runtimeScopeId));
            }

            _runtimeScopeId = runtimeScopeId;
            _usesUnixPaths = usesUnixPaths;
        }

        public bool Matches(AutomationDefinitionSnapshot definition)
        {
            var context = definition == null ? null : definition.Context;
            if (context == null)
            {
                return false;
            }

            var scope = context.InteractionMode == AppInteractionMode.Office
                ? context.OfficeRuntimeScopeId : context.RuntimeProfileId;
            return string.Equals(scope, _runtimeScopeId, StringComparison.OrdinalIgnoreCase) &&
                IsCompatiblePath(context.WorkspacePath, _usesUnixPaths) &&
                (string.IsNullOrWhiteSpace(context.OfficeRootPath) ||
                    IsCompatiblePath(context.OfficeRootPath, _usesUnixPaths));
        }

        public void Validate(AutomationDefinitionSnapshot definition)
        {
            if (!Matches(definition))
            {
                throw new InvalidOperationException(SharedLocalization.Text(
                    "Automation.Target.ScopeMismatch", "This automation does not belong to the current target or uses an incompatible working directory."));
            }
        }

        public static bool IsCompatiblePath(string path, bool usesUnixPaths)
        {
            if (path == null || Array.Exists(path.ToCharArray(), char.IsControl))
            {
                return false;
            }

            path = path.Trim();
            if (path.Length == 0) return false;

            if (usesUnixPaths)
            {
                return path.StartsWith("/", StringComparison.Ordinal);
            }

            return
                (path.Length > 2 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')) ||
                path.Length > 2 && path.StartsWith("\\\\", StringComparison.Ordinal) &&
                !path.StartsWith("\\\\wsl$\\", StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("\\\\wsl.localhost\\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
