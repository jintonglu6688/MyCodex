using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Skills
{
    internal enum SkillSaveLocationKind
    {
        User,
        Workspace
    }

    internal sealed class SkillSaveLocation
    {
        public SkillSaveLocation(SkillSaveLocationKind kind, string displayName, string rootPath)
        {
            Kind = kind;
            DisplayName = displayName ?? string.Empty;
            RootPath = rootPath ?? string.Empty;
        }

        public SkillSaveLocationKind Kind { get; private set; }

        public string DisplayName { get; private set; }

        public string RootPath { get; private set; }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    internal sealed class SkillFileModel
    {
        public string Name { get; set; }

        public string DisplayName { get; set; }

        public string ShortDescription { get; set; }

        public string Description { get; set; }

        public string DefaultPrompt { get; set; }

        public string Body { get; set; }

        public string SkillPath { get; set; }

        public SkillSaveLocation Location { get; set; }
    }

    internal sealed class SkillValidationResult
    {
        private readonly List<string> _errors = new List<string>();

        public IList<string> Errors
        {
            get { return _errors; }
        }

        public bool IsValid
        {
            get { return _errors.Count == 0; }
        }

        public void AddError(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                _errors.Add(error.Trim());
            }
        }

        public string BuildMessage()
        {
            return string.Join(Environment.NewLine, _errors.ToArray());
        }
    }

    internal static class SkillFileEditor
    {
        internal const string SkillFileName = "SKILL.md";
        internal const string AgentsDirectoryName = "agents";
        internal const string OpenAiAgentFileName = "openai.yaml";
        private const int MaximumSkillNameLength = 64;
        private const int MaximumDescriptionLength = 1024;

        private static readonly Regex SkillNameRegex = new Regex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
        public static SkillFileModel CreateNewModel(IList<SkillSaveLocation> locations)
        {
            locations = locations ?? new List<SkillSaveLocation>();
            return new SkillFileModel
            {
                Name = string.Empty,
                DisplayName = string.Empty,
                ShortDescription = string.Empty,
                Description = string.Empty,
                DefaultPrompt = string.Empty,
                Body = UiLocalization.Text(
                    "SkillEditForm.DefaultBody",
                    "# Overview\r\n\r\nDescribe when this skill should be used and the workflow it should follow.\r\n\r\n# Workflow\r\n\r\n1. Add the first step here."),
                Location = locations.Count > 0 ? locations[0] : null,
                SkillPath = null
            };
        }

        public static SkillFileModel LoadForEdit(
            AppServerSkillInfo skill,
            string skillPath,
            string skillFileText,
            string openAiAgentFileText,
            SkillSaveLocation location)
        {
            if (skill == null)
            {
                throw new ArgumentNullException(nameof(skill));
            }

            if (string.IsNullOrWhiteSpace(skillPath))
            {
                throw new ArgumentException("Skill path cannot be empty.", nameof(skillPath));
            }

            var model = ParseSkillMarkdown(skillFileText, GetPortableFileName(skillPath));
            model.SkillPath = skillPath;
            model.Location = location;

            var interfaceValues = ParseOpenAiInterfaceValues(openAiAgentFileText);
            model.DisplayName = GetFirstNonEmpty(
                GetDictionaryValue(interfaceValues, "display_name"),
                skill.DisplayName,
                model.DisplayName);
            model.ShortDescription = GetFirstNonEmpty(
                GetDictionaryValue(interfaceValues, "short_description"),
                skill.ShortDescription,
                model.ShortDescription);
            model.DefaultPrompt = GetFirstNonEmpty(
                GetDictionaryValue(interfaceValues, "default_prompt"),
                skill.DefaultPrompt,
                model.DefaultPrompt);

            if (string.IsNullOrWhiteSpace(model.Description))
            {
                model.Description = skill.Description ?? string.Empty;
            }

            return model;
        }

        public static SkillValidationResult ValidateContent(SkillFileModel model)
        {
            var result = new SkillValidationResult();
            if (model == null)
            {
                result.AddError(UiLocalization.Text("SkillFileEditor.Validation.ModelMissing", "Skill content is missing."));
                return result;
            }

            var name = NormalizeSingleLine(model.Name).ToLowerInvariant();
            var description = NormalizeSingleLine(model.Description);
            if (string.IsNullOrWhiteSpace(name))
            {
                result.AddError(UiLocalization.Text("SkillFileEditor.Validation.NameRequired", "Enter an internal skill name."));
            }
            else
            {
                if (name.Length > MaximumSkillNameLength)
                {
                    result.AddError(string.Format(
                        UiLocalization.Text("SkillFileEditor.Validation.NameTooLong", "The internal skill name must be no longer than {0} characters."),
                        MaximumSkillNameLength));
                }

                if (!SkillNameRegex.IsMatch(name))
                {
                    result.AddError(UiLocalization.Text("SkillFileEditor.Validation.NameInvalid", "The internal skill name can only use lowercase letters, numbers, and single hyphens."));
                }
            }

            if (string.IsNullOrWhiteSpace(description))
            {
                result.AddError(UiLocalization.Text("SkillFileEditor.Validation.DescriptionRequired", "Enter the trigger description."));
            }
            else
            {
                if (description.Length > MaximumDescriptionLength)
                {
                    result.AddError(string.Format(
                        UiLocalization.Text("SkillFileEditor.Validation.DescriptionTooLong", "The trigger description must be no longer than {0} characters."),
                        MaximumDescriptionLength));
                }

                if (description.IndexOf('<') >= 0 || description.IndexOf('>') >= 0)
                {
                    result.AddError(UiLocalization.Text("SkillFileEditor.Validation.DescriptionAngleBrackets", "The trigger description cannot contain angle brackets."));
                }
            }

            if (string.IsNullOrWhiteSpace(model.Body))
            {
                result.AddError(UiLocalization.Text("SkillFileEditor.Validation.BodyRequired", "Enter the skill body."));
            }

            return result;
        }

        public static string BuildSkillMarkdownText(SkillFileModel model)
        {
            return BuildSkillMarkdown(model);
        }

        public static Dictionary<string, string> ParseOpenAiInterfaceValues(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var lines = NormalizeLineEndings(text).Split('\n');
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var inInterface = false;
            var interfaceIndent = 0;

            foreach (var rawLine in lines)
            {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var indent = CountLeadingSpaces(line);
                if (!inInterface && string.Equals(trimmed, "interface:", StringComparison.OrdinalIgnoreCase))
                {
                    inInterface = true;
                    interfaceIndent = indent;
                    continue;
                }

                if (inInterface && indent <= interfaceIndent)
                {
                    inInterface = false;
                }

                if (!inInterface)
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();
                if (string.Equals(key, "display_name", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "short_description", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, "default_prompt", StringComparison.OrdinalIgnoreCase))
                {
                    values[key] = UnquoteYamlScalar(value);
                }
            }

            return values;
        }

        private static SkillFileModel ParseSkillMarkdown(string text, string fallbackName)
        {
            var model = new SkillFileModel
            {
                Name = fallbackName ?? string.Empty,
                Body = text ?? string.Empty
            };

            if (string.IsNullOrEmpty(text))
            {
                return model;
            }

            using (var reader = new StringReader(text))
            {
                var firstLine = reader.ReadLine();
                if (!string.Equals((firstLine ?? string.Empty).Trim(), "---", StringComparison.Ordinal))
                {
                    return model;
                }

                var frontMatter = new List<string>();
                string line;
                var foundEnd = false;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                    {
                        foundEnd = true;
                        break;
                    }

                    frontMatter.Add(line);
                }

                if (!foundEnd)
                {
                    return model;
                }

                var fields = ParseKeyValueLines(frontMatter);
                model.Name = GetFirstNonEmpty(GetDictionaryValue(fields, "name"), fallbackName);
                model.Description = GetDictionaryValue(fields, "description") ?? string.Empty;
                model.Body = (reader.ReadToEnd() ?? string.Empty).TrimStart('\r', '\n');
            }

            return model;
        }

        private static string BuildSkillMarkdown(SkillFileModel model)
        {
            var builder = new StringBuilder();
            builder.AppendLine("---");
            builder.Append("name: ");
            builder.AppendLine(ToYamlScalar(model.Name));
            builder.Append("description: ");
            builder.AppendLine(ToYamlScalar(model.Description));
            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine(NormalizeLineEndings(model.Body).Trim());
            return builder.ToString();
        }

        private static Dictionary<string, string> ParseKeyValueLines(IEnumerable<string> lines)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in lines ?? Enumerable.Empty<string>())
            {
                var line = rawLine ?? string.Empty;
                var trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                var separatorIndex = trimmed.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var key = trimmed.Substring(0, separatorIndex).Trim();
                var value = trimmed.Substring(separatorIndex + 1).Trim();
                values[key] = UnquoteYamlScalar(value);
            }

            return values;
        }

        private static string GetPortableFileName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            var normalized = path.Trim().Replace('\\', '/').TrimEnd('/');
            var separatorIndex = normalized.LastIndexOf('/');
            return separatorIndex >= 0 ? normalized.Substring(separatorIndex + 1) : normalized;
        }

        private static int CountLeadingSpaces(string text)
        {
            var count = 0;
            while (count < (text ?? string.Empty).Length && text[count] == ' ')
            {
                count++;
            }

            return count;
        }

        private static string NormalizeSingleLine(string text)
        {
            return Regex.Replace(text ?? string.Empty, "\\s+", " ").Trim();
        }

        private static string NormalizeLineEndings(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", Environment.NewLine);
        }

        private static string GetDictionaryValue(IDictionary<string, string> values, string key)
        {
            string value;
            return values != null && values.TryGetValue(key, out value) ? value : null;
        }

        private static string GetFirstNonEmpty(params string[] values)
        {
            foreach (var value in values ?? new string[0])
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private static string ToYamlScalar(string value)
        {
            return "\"" + (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r\n", "\n")
                .Replace("\r", "\n")
                .Replace("\n", "\\n") + "\"";
        }

        private static string UnquoteYamlScalar(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            value = value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"')
            {
                return value.Substring(1, value.Length - 2)
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\\"", "\"")
                    .Replace("\\\\", "\\");
            }

            if (value.Length >= 2 && value[0] == '\'' && value[value.Length - 1] == '\'')
            {
                return value.Substring(1, value.Length - 2).Replace("''", "'");
            }

            var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                value = value.Substring(0, commentIndex).TrimEnd();
            }

            return value;
        }
    }
}
