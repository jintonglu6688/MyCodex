using System;
using System.IO;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;

namespace MyCodex.Application.Services
{
    public sealed class RuntimeTargetIdentityService
    {
        private readonly OfficeRuntimeScopeService _officeRuntimeScopeService;

        public RuntimeTargetIdentityService()
            : this(new OfficeRuntimeScopeService())
        {
        }

        public RuntimeTargetIdentityService(OfficeRuntimeScopeService officeRuntimeScopeService)
        {
            _officeRuntimeScopeService = officeRuntimeScopeService ?? throw new ArgumentNullException(nameof(officeRuntimeScopeService));
        }

        public string ResolveIdentity(AppConfigSnapshot config)
        {
            config = config ?? new AppConfigSnapshot();
            var target = _officeRuntimeScopeService.ResolveTarget(config);
            if (target.HostPlatform != AppServerHostPlatform.Windows)
            {
                return target.RuntimeScopeId;
            }

            var source = NormalizeWindowsSource(config.CodexRuntimeSource);
            if (!string.Equals(source, CodexRuntimeSources.Custom, StringComparison.OrdinalIgnoreCase))
            {
                return "windows|" + source.ToLowerInvariant();
            }

            return "windows|custom|" + NormalizeWindowsExecutablePath(config.CodexCustomExecutablePath);
        }

        private static string NormalizeWindowsSource(string source)
        {
            if (string.Equals(source, CodexRuntimeSources.System, StringComparison.OrdinalIgnoreCase))
            {
                return CodexRuntimeSources.System;
            }

            if (string.Equals(source, CodexRuntimeSources.Custom, StringComparison.OrdinalIgnoreCase))
            {
                return CodexRuntimeSources.Custom;
            }

            return CodexRuntimeSources.Bundled;
        }

        private static string NormalizeWindowsExecutablePath(string path)
        {
            var value = string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Trim().Trim('"');
            try
            {
                value = Path.GetFullPath(value);
            }
            catch
            {
            }

            return value.Replace('/', '\\').ToLowerInvariant();
        }
    }
}
