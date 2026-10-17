using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerSkillInfo
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string ShortDescription { get; set; }

        public string Description { get; set; }

        public bool Enabled { get; set; }

        public string Path { get; set; }

        public string Scope { get; set; }

        public string DefaultPrompt { get; set; }

        public string BrandColor { get; set; }

        public List<AppServerSkillToolDependency> ToolDependencies { get; set; }
    }
}
