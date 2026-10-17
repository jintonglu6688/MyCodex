using System.Collections.Generic;

namespace MyCodex.Application.Services
{
    public sealed class OfficeCliIntegrationPaths
    {
        public string InstallDirectory { get; set; }

        public string ExecutablePath { get; set; }

        public IReadOnlyList<string> SkillDirectories { get; set; }
    }
}
