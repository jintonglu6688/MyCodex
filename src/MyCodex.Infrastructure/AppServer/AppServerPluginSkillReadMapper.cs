using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerPluginSkillReadMapper
    {
        public static AppServerPluginSkillReadResult BuildResult(IDictionary<string, object> result)
        {
            var skill = AppServerJsonValue.GetDictionary(result, "skill");
            var source = skill.Count == 0 ? result : skill;
            return new AppServerPluginSkillReadResult
            {
                SkillName = EmptyIfNull(GetFirstNonEmptyString(source, "skillName", "name", "id")),
                Markdown = EmptyIfNull(GetFirstNonEmptyString(source, "contents", "markdown", "content", "text", "description")),
                SourceLabel = EmptyIfNull(GetFirstNonEmptyString(source, "sourceLabel", "marketplaceName", "source"))
            };
        }

        private static string GetFirstNonEmptyString(IDictionary<string, object> source, params string[] keys)
        {
            if (keys == null)
            {
                return null;
            }

            for (var i = 0; i < keys.Length; i++)
            {
                var value = AppServerJsonValue.GetString(source, keys[i]);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return null;
        }

        private static string EmptyIfNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
