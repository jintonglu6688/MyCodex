using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.Codex
{
    public sealed class CodexRuntimeStatus
    {
        public string PreferredSource { get; set; }

        public string EffectiveSource { get; set; }

        public bool CanLaunch { get; set; }

        public string Message { get; set; }

        public string SystemExecutablePath { get; set; }

        public string BundledExecutablePath { get; set; }

        public string CustomExecutablePath { get; set; }

        public string WslExecutablePath { get; set; }

        public string WslDistributionName { get; set; }

        public string WslCodexCommand { get; set; }

        public string RemoteMacHostName { get; set; }

        public string RemoteMacUserName { get; set; }

        public string RemoteMacCodexCommand { get; set; }

        public string EffectiveExecutablePath { get; set; }

        public string EffectiveAdditionalPathDirectory { get; set; }

        public string Version { get; set; }

        public AppServerHostPlatform EffectiveHostPlatform { get; set; }
    }
}
