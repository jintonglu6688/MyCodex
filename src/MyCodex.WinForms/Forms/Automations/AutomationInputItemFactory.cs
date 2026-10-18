using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    internal static class AutomationInputItemFactory
    {
        public static AutomationInputItemSnapshot FromSkill(AppServerSkillInfo skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.Path))
            {
                return null;
            }

            var displayName = ComposeAttachmentState.GetSkillInsertionDisplayName(skill);
            return new AutomationInputItemSnapshot
            {
                Type = AutomationInputItemKind.Skill,
                Name = string.IsNullOrWhiteSpace(skill.Name) ? displayName : skill.Name.Trim(),
                DisplayName = displayName,
                Path = skill.Path.Trim()
            };
        }

        public static AutomationInputItemSnapshot FromPlugin(AppServerPluginSummary plugin)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.SourcePath))
            {
                return null;
            }

            var displayName = ComposeAttachmentState.GetPluginInsertionDisplayName(plugin);
            return new AutomationInputItemSnapshot
            {
                Type = AutomationInputItemKind.Plugin,
                Name = string.IsNullOrWhiteSpace(plugin.Name) ? displayName : plugin.Name.Trim(),
                DisplayName = displayName,
                Path = plugin.SourcePath.Trim()
            };
        }
    }
}
