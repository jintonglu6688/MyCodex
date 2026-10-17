using System.Collections.Generic;

namespace MyCodex.Application.Services
{
    public sealed class OfficeCliIntegrationStatus
    {
        public string InstallDirectory { get; set; }

        public string ExecutablePath { get; set; }

        public bool IsExecutableInstalled { get; set; }

        public bool IsUserPathConfigured { get; set; }

        public IReadOnlyList<string> SkillDirectories { get; set; }

        public IReadOnlyList<string> MissingSkillDirectories { get; set; }

        public bool AreSkillsInstalled
        {
            get { return MissingSkillDirectories == null || MissingSkillDirectories.Count == 0; }
        }

        public bool IsReady
        {
            get { return IsExecutableInstalled && IsUserPathConfigured && AreSkillsInstalled; }
        }
    }
}
