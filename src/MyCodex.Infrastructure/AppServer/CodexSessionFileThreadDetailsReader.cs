using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class CodexSessionFileThreadDetailsReader
    {
        private readonly JavaScriptSerializer _serializer;

        public CodexSessionFileThreadDetailsReader()
        {
            _serializer = new JavaScriptSerializer();
        }

        public bool TryRead(ChatSession session, out AppServerThreadDetails details)
        {
            details = null;
            if (session == null ||
                string.IsNullOrWhiteSpace(session.ThreadId) ||
                string.IsNullOrWhiteSpace(session.SessionPath) ||
                !File.Exists(session.SessionPath))
            {
                return false;
            }

            try
            {
                details = Read(session);
                return details.Messages.Count > 0;
            }
            catch (IOException)
            {
                details = null;
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                details = null;
                return false;
            }
            catch (ArgumentException)
            {
                details = null;
                return false;
            }
        }

        private AppServerThreadDetails Read(ChatSession session)
        {
            var details = new AppServerThreadDetails
            {
                ThreadId = session.ThreadId,
                Title = session.Title,
                Preview = session.Preview,
                ThreadSource = session.ThreadSource,
                ParentThreadId = session.ParentThreadId,
                SubagentDepth = session.SubAgentDepth,
                AgentNickname = session.SubAgentNickname,
                AgentRole = session.SubAgentRole,
                CreatedAt = session.CreatedAt,
                UpdatedAt = session.LastActiveAt,
                SessionPath = session.SessionPath
            };

            var turnIndex = 0;
            string activeTurnId = null;
            var explicitTurn = false;
            foreach (var line in File.ReadLines(session.SessionPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var root = TryDeserialize(line);
                if (string.Equals(AppServerJsonValueReader.GetString(root, "type"), "event_msg", StringComparison.Ordinal))
                {
                    var payload = AppServerJsonValueReader.GetDictionary(root, "payload");
                    var eventType = AppServerJsonValueReader.GetString(payload, "type");
                    if (eventType == "task_started" || eventType == "turn_started")
                    {
                        var turnId = AppServerJsonValueReader.GetString(payload, "turn_id");
                        if (!explicitTurn || !string.Equals(activeTurnId, turnId, StringComparison.Ordinal))
                        {
                            turnIndex++;
                        }
                        activeTurnId = turnId;
                        explicitTurn = true;
                    }
                    else if (eventType == "task_complete" || eventType == "turn_complete" || eventType == "turn_aborted")
                    {
                        explicitTurn = false;
                    }
                    else if (eventType == "thread_rolled_back")
                    {
                        // Old rollout files retain this marker. It removes turns, not messages;
                        // explicit turn boundaries keep mid-turn steering in the same group.
                        object countValue;
                        uint count;
                        if (payload != null && payload.TryGetValue("num_turns", out countValue) &&
                            uint.TryParse(Convert.ToString(countValue, System.Globalization.CultureInfo.InvariantCulture), out count) && count > 0)
                        {
                            var retainedTurns = (int)Math.Max(0L, (long)turnIndex - count);
                            details.Messages.RemoveAll(messageItem => messageItem.TurnIndex > retainedTurns);
                            turnIndex = retainedTurns;
                            activeTurnId = null;
                            explicitTurn = false;
                        }
                    }
                    continue;
                }
                var message = TryReadMessage(root);
                if (message == null)
                {
                    continue;
                }

                if (!explicitTurn && (turnIndex == 0 || message.Sender == "User"))
                {
                    turnIndex++;
                    activeTurnId = null;
                }
                details.Messages.Add(new AppServerThreadMessage
                {
                    ThreadId = session.ThreadId,
                    TurnId = activeTurnId ?? "session-file-" + turnIndex.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    TurnIndex = turnIndex,
                    Sender = message.Sender,
                    Content = message.Content
                });
            }

            details.TurnCount = turnIndex;
            return details;
        }

        private IDictionary<string, object> TryDeserialize(string line)
        {
            try
            {
                return _serializer.DeserializeObject(line) as IDictionary<string, object>;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static SessionFileMessage TryReadMessage(IDictionary<string, object> root)
        {
            if (!string.Equals(AppServerJsonValueReader.GetString(root, "type"), "response_item", StringComparison.Ordinal))
            {
                return null;
            }

            var payload = AppServerJsonValueReader.GetDictionary(root, "payload");
            if (!string.Equals(AppServerJsonValueReader.GetString(payload, "type"), "message", StringComparison.Ordinal))
            {
                return null;
            }

            var role = (AppServerJsonValueReader.GetString(payload, "role") ?? string.Empty).Trim().ToLowerInvariant();
            string sender;
            if (role == "user")
            {
                sender = "User";
            }
            else if (role == "assistant")
            {
                sender = "Codex";
            }
            else
            {
                return null;
            }

            var content = ExtractContentText(payload);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            return new SessionFileMessage
            {
                Sender = sender,
                Content = content
            };
        }

        private static string ExtractContentText(IDictionary<string, object> payload)
        {
            var parts = new List<string>();
            foreach (var itemObject in GetArray(payload, "content"))
            {
                var item = itemObject as IDictionary<string, object>;
                if (item == null)
                {
                    continue;
                }

                var type = AppServerJsonValueReader.GetString(item, "type");
                if (!string.Equals(type, "input_text", StringComparison.Ordinal) &&
                    !string.Equals(type, "output_text", StringComparison.Ordinal) &&
                    !string.Equals(type, "text", StringComparison.Ordinal))
                {
                    continue;
                }

                var text = AppServerJsonValueReader.GetString(item, "text");
                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text.Trim());
                }
            }

            return parts.Count == 0
                ? null
                : string.Join(Environment.NewLine, parts.ToArray());
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null || value is string)
            {
                yield break;
            }

            var enumerable = value as IEnumerable;
            if (enumerable == null)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                yield return item;
            }
        }

        private sealed class SessionFileMessage
        {
            public string Sender { get; set; }

            public string Content { get; set; }
        }
    }
}
