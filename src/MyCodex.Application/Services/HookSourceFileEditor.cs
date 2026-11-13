using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    /// <summary>
    /// Adds or removes one handler in a hooks.json file. Other events, matcher groups, handlers, and unknown
    /// fields are kept; the file is rewritten with two-space indentation and its original line endings and BOM.
    /// </summary>
    public static class HookSourceFileEditor
    {
        public static readonly string[] EventNames =
        {
            "preToolUse", "postToolUse", "permissionRequest", "userPromptSubmit", "sessionStart", "sessionEnd",
            "stop", "interrupt", "subagentStart", "subagentStop", "preCompact", "postCompact",
            "preModelRequest", "postModelRequest"
        };

        /// <summary>User and project files can be opened for editing; plugin and managed sources stay read-only.</summary>
        public static bool CanEditSource(AppServerHookInfo hook)
        {
            return hook != null && !hook.IsManaged && !string.IsNullOrWhiteSpace(hook.SourcePath) &&
                (string.Equals(hook.Source, "user", StringComparison.Ordinal) ||
                 string.Equals(hook.Source, "project", StringComparison.Ordinal));
        }

        /// <summary>Removing a single definition is supported for hooks.json; inline config.toml hooks are edited as text.</summary>
        public static bool CanRemove(AppServerHookInfo hook)
        {
            return CanEditSource(hook) && hook.SourcePath.EndsWith("hooks.json", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetSiblingPath(string path, string fileName)
        {
            var separator = path.IndexOf('\\') < 0 && path.IndexOf('/') >= 0 ? '/' : '\\';
            var index = path.LastIndexOfAny(new[] { '/', '\\' });
            return (index < 0 ? string.Empty : path.Substring(0, index + 1)) + fileName.Replace('\\', separator);
        }

        public static string GetFileName(string path)
        {
            return path.Substring(path.LastIndexOfAny(new[] { '/', '\\' }) + 1);
        }

        public static string GetProjectHooksPath(string workspacePath)
        {
            var separator = workspacePath.IndexOf('\\') < 0 && workspacePath.IndexOf('/') >= 0 ? "/" : "\\";
            return workspacePath.TrimEnd('/', '\\') + separator + ".codex" + separator + "hooks.json";
        }

        public static Dictionary<string, object> BuildCommandHandler(string command, int? timeoutSec, string statusMessage)
        {
            var handler = new Dictionary<string, object> { { "type", "command" }, { "command", command } };
            AddOptional(handler, timeoutSec, statusMessage);
            return handler;
        }

        public static Dictionary<string, object> BuildMcpToolHandler(string server, string tool, int? timeoutSec, string statusMessage)
        {
            var handler = new Dictionary<string, object> { { "type", "mcp_tool" }, { "server", server }, { "tool", tool } };
            AddOptional(handler, timeoutSec, statusMessage);
            return handler;
        }

        /// <summary>Appends a new matcher group with the handler; null or empty text starts a new file.</summary>
        public static byte[] AddHandler(byte[] original, string eventName, string matcher, Dictionary<string, object> handler)
        {
            string newLine;
            bool hasBom;
            var root = Parse(original, out newLine, out hasBom);
            var hooks = GetOrAddObject(root, "hooks");
            var eventKey = GetEventKey(eventName);
            object groups;
            var list = hooks.TryGetValue(eventKey, out groups) && groups is object[]
                ? ((object[])groups).ToList()
                : new List<object>();
            var group = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(matcher))
            {
                group["matcher"] = matcher.Trim();
            }

            group["hooks"] = new object[] { handler };
            list.Add(group);
            hooks[eventKey] = list.ToArray();
            return Serialize(root, newLine, hasBom);
        }

        /// <summary>
        /// Removes the handler the hook key points at. Returns null when that position no longer holds the
        /// same definition, so a file changed since listing is never edited blindly.
        /// </summary>
        public static byte[] RemoveHandler(byte[] original, AppServerHookInfo hook)
        {
            string prefix;
            int groupIndex;
            int handlerIndex;
            if (!HookStateEditPlanner.TryParseKey(hook.Key, out prefix, out groupIndex, out handlerIndex))
            {
                return null;
            }

            string newLine;
            bool hasBom;
            var root = Parse(original, out newLine, out hasBom);
            object hooksValue;
            object groupsValue;
            var hooks = root.TryGetValue("hooks", out hooksValue) ? hooksValue as Dictionary<string, object> : null;
            var eventKey = GetEventKey(hook.EventName);
            var groups = hooks != null && hooks.TryGetValue(eventKey, out groupsValue) ? groupsValue as object[] : null;
            var group = groups != null && groupIndex < groups.Length ? groups[groupIndex] as Dictionary<string, object> : null;
            object handlersValue;
            var handlers = group != null && group.TryGetValue("hooks", out handlersValue) ? handlersValue as object[] : null;
            var handler = handlers != null && handlerIndex < handlers.Length ? handlers[handlerIndex] as Dictionary<string, object> : null;
            if (handler == null || !Matches(handler, hook))
            {
                return null;
            }

            var remainingHandlers = handlers.Where((value, index) => index != handlerIndex).ToArray();
            var remainingGroups = groups.ToList();
            if (remainingHandlers.Length == 0)
            {
                remainingGroups.RemoveAt(groupIndex);
            }
            else
            {
                group["hooks"] = remainingHandlers;
            }

            if (remainingGroups.Count == 0)
            {
                hooks.Remove(eventKey);
            }
            else
            {
                hooks[eventKey] = remainingGroups.ToArray();
            }

            return Serialize(root, newLine, hasBom);
        }

        private static bool Matches(Dictionary<string, object> handler, AppServerHookInfo hook)
        {
            switch (hook.HandlerType)
            {
                case "command":
                    return string.Equals(GetString(handler, "type"), "command", StringComparison.Ordinal) &&
                        (string.Equals(GetString(handler, "command"), hook.Command, StringComparison.Ordinal) ||
                         string.Equals(GetString(handler, "commandWindows"), hook.Command, StringComparison.Ordinal) ||
                         string.Equals(GetString(handler, "command_windows"), hook.Command, StringComparison.Ordinal));
                case "mcpTool":
                    return string.Equals(GetString(handler, "type"), "mcp_tool", StringComparison.Ordinal) &&
                        string.Equals(GetString(handler, "server"), hook.McpServer, StringComparison.Ordinal) &&
                        string.Equals(GetString(handler, "tool"), hook.McpTool, StringComparison.Ordinal);
                default:
                    return false;
            }
        }

        private static string GetEventKey(string eventName)
        {
            return HookEventNames.GetOfficialName(eventName);
        }

        private static void AddOptional(Dictionary<string, object> handler, int? timeoutSec, string statusMessage)
        {
            if (timeoutSec.HasValue)
            {
                handler["timeout"] = timeoutSec.Value;
            }

            if (!string.IsNullOrWhiteSpace(statusMessage))
            {
                handler["statusMessage"] = statusMessage.Trim();
            }
        }

        private static string GetString(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) ? value as string : null;
        }

        private static Dictionary<string, object> GetOrAddObject(Dictionary<string, object> parent, string key)
        {
            object value;
            var child = parent.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
            if (child == null)
            {
                child = new Dictionary<string, object>();
                parent[key] = child;
            }

            return child;
        }

        private static Dictionary<string, object> Parse(byte[] original, out string newLine, out bool hasBom)
        {
            hasBom = original != null && original.Length >= 3 && original[0] == 0xEF && original[1] == 0xBB && original[2] == 0xBF;
            var text = original == null ? string.Empty : new UTF8Encoding(false, true).GetString(original, hasBom ? 3 : 0, original.Length - (hasBom ? 3 : 0));
            newLine = text.Contains("\r\n") ? "\r\n" : "\n";
            if (string.IsNullOrWhiteSpace(text))
            {
                return new Dictionary<string, object>();
            }

            // Throws on malformed JSON, so a file the editor cannot read is never overwritten.
            var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(text) as Dictionary<string, object>;
            if (root == null)
            {
                throw new FormatException("hooks.json must contain a JSON object.");
            }

            return root;
        }

        private static byte[] Serialize(Dictionary<string, object> root, string newLine, bool hasBom)
        {
            var builder = new StringBuilder();
            Write(builder, root, 0, newLine);
            builder.Append(newLine);
            return new UTF8Encoding(hasBom).GetPreamble().Concat(new UTF8Encoding(false).GetBytes(builder.ToString())).ToArray();
        }

        private static void Write(StringBuilder builder, object value, int depth, string newLine)
        {
            var indent = new string(' ', (depth + 1) * 2);
            var closing = new string(' ', depth * 2);
            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                if (dictionary.Count == 0)
                {
                    builder.Append("{}");
                    return;
                }

                builder.Append('{').Append(newLine);
                var first = true;
                foreach (var pair in dictionary)
                {
                    builder.Append(first ? string.Empty : "," + newLine).Append(indent);
                    WriteString(builder, pair.Key);
                    builder.Append(": ");
                    Write(builder, pair.Value, depth + 1, newLine);
                    first = false;
                }

                builder.Append(newLine).Append(closing).Append('}');
                return;
            }

            var text = value as string;
            if (text != null)
            {
                WriteString(builder, text);
                return;
            }

            var list = value as IEnumerable;
            if (list != null)
            {
                var items = list.Cast<object>().ToList();
                if (items.Count == 0)
                {
                    builder.Append("[]");
                    return;
                }

                builder.Append('[').Append(newLine);
                for (var i = 0; i < items.Count; i++)
                {
                    builder.Append(i == 0 ? string.Empty : "," + newLine).Append(indent);
                    Write(builder, items[i], depth + 1, newLine);
                }

                builder.Append(newLine).Append(closing).Append(']');
                return;
            }

            if (value == null)
            {
                builder.Append("null");
            }
            else if (value is bool)
            {
                builder.Append((bool)value ? "true" : "false");
            }
            else
            {
                builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
        }

        private static void WriteString(StringBuilder builder, string value)
        {
            builder.Append('"');
            foreach (var ch in value)
            {
                switch (ch)
                {
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        if (ch < ' ')
                        {
                            builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(ch);
                        }

                        break;
                }
            }

            builder.Append('"');
        }
    }
}
