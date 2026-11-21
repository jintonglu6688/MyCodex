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

        // What was on disk when an existing skill was loaded; null for a new skill.
        public SkillFileSource Source { get; set; }
    }

    internal sealed class SkillFileSource
    {
        public IList<string> FrontMatterLines { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        public string DisplayName { get; set; }

        public string ShortDescription { get; set; }

        public string DefaultPrompt { get; set; }
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

            model.Source = model.Source ?? new SkillFileSource();
            model.Source.DisplayName = model.DisplayName;
            model.Source.ShortDescription = model.ShortDescription;
            model.Source.DefaultPrompt = model.DefaultPrompt;
            return model;
        }

        // Returns only the agents/openai.yaml interface fields the user changed (empty value = remove).
        public static Dictionary<string, string> GetChangedInterfaceValues(SkillFileModel model)
        {
            var changes = new Dictionary<string, string>(StringComparer.Ordinal);
            if (model == null)
            {
                return changes;
            }

            var source = model.Source ?? new SkillFileSource();
            AddIfChanged(changes, "display_name", model.DisplayName, source.DisplayName);
            AddIfChanged(changes, "short_description", model.ShortDescription, source.ShortDescription);
            AddIfChanged(changes, "default_prompt", model.DefaultPrompt, source.DefaultPrompt);
            return changes;
        }

        public static string UpdateOpenAiInterfaceText(string existingText, IDictionary<string, string> changes)
        {
            var newLine = (existingText ?? string.Empty).Contains("\r\n") ? "\r\n" : "\n";
            var lines = string.IsNullOrEmpty(existingText)
                ? new List<string>()
                : existingText.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n').ToList();
            var interfaceIndex = lines.FindIndex(line => CountLeadingSpaces(line) == 0 &&
                string.Equals(line.Trim(), "interface:", StringComparison.Ordinal));
            if (interfaceIndex < 0)
            {
                lines.Add("interface:");
                interfaceIndex = lines.Count - 1;
            }

            var blockEnd = FindBlockEnd(lines, interfaceIndex, 0);
            var childIndent = 2;
            for (var i = interfaceIndex + 1; i < blockEnd; i++)
            {
                if (lines[i].Trim().Length > 0)
                {
                    childIndent = CountLeadingSpaces(lines[i]);
                    break;
                }
            }

            foreach (var change in changes ?? new Dictionary<string, string>())
            {
                var replacement = string.IsNullOrWhiteSpace(change.Value)
                    ? null
                    : new string(' ', childIndent) + change.Key + ": " + ToYamlScalar(change.Value.Trim());
                var keyIndex = FindKeyLine(lines, interfaceIndex + 1, blockEnd, childIndent, change.Key);
                if (keyIndex >= 0)
                {
                    var valueEnd = FindBlockEnd(lines, keyIndex, childIndent);
                    lines.RemoveRange(keyIndex, valueEnd - keyIndex);
                    blockEnd -= valueEnd - keyIndex;
                    if (replacement != null)
                    {
                        lines.Insert(keyIndex, replacement);
                        blockEnd++;
                    }
                }
                else if (replacement != null)
                {
                    var insertAt = blockEnd;
                    while (insertAt > interfaceIndex + 1 && lines[insertAt - 1].Trim().Length == 0)
                    {
                        insertAt--;
                    }

                    lines.Insert(insertAt, replacement);
                    blockEnd++;
                }
            }

            return string.Join(newLine, lines) + newLine;
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
                model.Source = new SkillFileSource
                {
                    FrontMatterLines = frontMatter,
                    Name = model.Name,
                    Description = model.Description
                };
            }

            return model;
        }

        private static string BuildSkillMarkdown(SkillFileModel model)
        {
            var builder = new StringBuilder();
            builder.AppendLine("---");
            var source = model.Source;
            if (source != null && source.FrontMatterLines != null)
            {
                // Keep every other front matter field (metadata, license, comments) exactly as loaded.
                var lines = source.FrontMatterLines.ToList();
                if (!string.Equals(NormalizeSingleLine(model.Name), NormalizeSingleLine(source.Name), StringComparison.Ordinal))
                {
                    ReplaceTopLevelKey(lines, "name", model.Name, true);
                }

                if (!string.Equals(NormalizeSingleLine(model.Description), NormalizeSingleLine(source.Description), StringComparison.Ordinal))
                {
                    ReplaceTopLevelKey(lines, "description", model.Description, false);
                }

                foreach (var line in lines)
                {
                    builder.AppendLine(line);
                }
            }
            else
            {
                builder.Append("name: ");
                builder.AppendLine(ToYamlScalar(model.Name));
                builder.Append("description: ");
                builder.AppendLine(ToYamlScalar(model.Description));
            }

            builder.AppendLine("---");
            builder.AppendLine();
            builder.AppendLine(NormalizeLineEndings(model.Body).Trim());
            return builder.ToString();
        }

        private static void ReplaceTopLevelKey(List<string> lines, string key, string value, bool insertFirst)
        {
            var replacement = key + ": " + ToYamlScalar(value);
            var keyIndex = FindKeyLine(lines, 0, lines.Count, 0, key);
            if (keyIndex < 0)
            {
                lines.Insert(insertFirst ? 0 : lines.Count, replacement);
                return;
            }

            lines.RemoveRange(keyIndex, FindBlockEnd(lines, keyIndex, 0) - keyIndex);
            lines.Insert(keyIndex, replacement);
        }

        private static int FindKeyLine(IList<string> lines, int start, int end, int indent, string key)
        {
            for (var i = start; i < end; i++)
            {
                var line = lines[i] ?? string.Empty;
                var trimmed = line.Trim();
                if (CountLeadingSpaces(line) == indent &&
                    trimmed.StartsWith(key + ":", StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        // A value or block continues while lines are indented deeper than its key (blank lines included).
        private static int FindBlockEnd(IList<string> lines, int keyIndex, int keyIndent)
        {
            var end = keyIndex + 1;
            for (var i = keyIndex + 1; i < lines.Count; i++)
            {
                var line = lines[i] ?? string.Empty;
                if (line.Trim().Length == 0)
                {
                    continue;
                }

                if (CountLeadingSpaces(line) <= keyIndent)
                {
                    break;
                }

                end = i + 1;
            }

            return end;
        }

        private static void AddIfChanged(IDictionary<string, string> changes, string key, string value, string original)
        {
            var current = (value ?? string.Empty).Trim();
            if (!string.Equals(current, (original ?? string.Empty).Trim(), StringComparison.Ordinal))
            {
                changes[key] = current;
            }
        }

        private static Dictionary<string, string> ParseKeyValueLines(IList<string> lines)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lines = lines ?? new List<string>();
            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i] ?? string.Empty;
                var trimmed = line.Trim();
                // Indented lines belong to the previous key (nested maps or block scalars).
                if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal) || CountLeadingSpaces(line) > 0)
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
                values[key] = value.StartsWith("|", StringComparison.Ordinal) || value.StartsWith(">", StringComparison.Ordinal)
                    ? ReadBlockScalar(lines, i, value[0] == '|')
                    : UnquoteYamlScalar(value);
            }

            return values;
        }

        private static string ReadBlockScalar(IList<string> lines, int keyIndex, bool literal)
        {
            var parts = new List<string>();
            var end = FindBlockEnd(lines, keyIndex, 0);
            for (var i = keyIndex + 1; i < end; i++)
            {
                parts.Add((lines[i] ?? string.Empty).Trim());
            }

            return literal
                ? string.Join("\n", parts).Trim()
                : NormalizeSingleLine(string.Join(" ", parts));
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
