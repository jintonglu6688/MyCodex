namespace MyCodex.Infrastructure.Codex
{
    public sealed class CodexRuntimeUpdateResult
    {
        public string TargetTriple { get; set; }

        public string Version { get; set; }

        public string ExecutablePath { get; set; }

        public bool WasUpdated { get; set; }

        public string PreviousVersion { get; set; }
    }
}
