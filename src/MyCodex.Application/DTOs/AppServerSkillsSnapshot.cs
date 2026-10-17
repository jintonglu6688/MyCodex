using System.Collections.Generic;

namespace MyCodex.Application.DTOs
{
    public class AppServerSkillsSnapshot
    {
        public string WorkspacePath { get; set; }

        public List<AppServerSkillInfo> Skills { get; set; }

        public List<AppServerSkillError> Errors { get; set; }
    }
}
