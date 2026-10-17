using MyCodex.Domain.Enums;

namespace MyCodex.Application.DTOs
{
    public sealed class OfficeRuntimeTarget
    {
        public OfficeRuntimeTarget(
            string runtimeScopeId,
            AppServerHostPlatform hostPlatform,
            string runtimeSource)
        {
            RuntimeScopeId = runtimeScopeId;
            HostPlatform = hostPlatform;
            RuntimeSource = runtimeSource;
        }

        public string RuntimeScopeId { get; }

        public AppServerHostPlatform HostPlatform { get; }

        public string RuntimeSource { get; }

        public bool UsesUnixPaths => HostPlatform != AppServerHostPlatform.Windows;
    }
}
