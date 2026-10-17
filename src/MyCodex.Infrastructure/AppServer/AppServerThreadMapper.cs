using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerThreadMapper
    {
        internal static AppServerThreadSummary BuildThreadSummary(
            IDictionary<string, object> thread,
            Func<string, string> mapServerPathToClient)
        {
            var source = GetDictionary(thread, "source");
            var subAgent = GetDictionary(source, "subAgent");
            if (subAgent.Count == 0)
            {
                subAgent = GetDictionary(source, "subagent");
            }
            var threadSpawn = GetDictionary(subAgent, "thread_spawn");
            var legacyParentThreadId = GetString(threadSpawn, "parent_thread_id");
            var parentThreadId = FirstNonBlank(GetString(thread, "parentThreadId"), legacyParentThreadId);
            var parentConflict = !string.IsNullOrWhiteSpace(legacyParentThreadId) &&
                !string.Equals(parentThreadId, legacyParentThreadId, StringComparison.Ordinal);
            var threadSource = GetString(thread, "thread_source");
            if (string.IsNullOrWhiteSpace(threadSource) && !string.IsNullOrWhiteSpace(parentThreadId))
            {
                threadSource = "subagent";
            }

            return new AppServerThreadSummary
            {
                ThreadId = GetString(thread, "id"),
                Title = GetString(thread, "name"),
                Preview = GetString(thread, "preview"),
                ModelProviderId = GetString(thread, "modelProvider"),
                WorkingDirectory = LocalSessionCatalog.NormalizeLocalThreadWorkingDirectory(
                    MapServerPathToClient(mapServerPathToClient, GetString(thread, "cwd"))),
                Source = ResolveSessionSourceText(thread, threadSource),
                ThreadSource = threadSource,
                ParentThreadId = parentThreadId,
                SubAgentSourceKind = threadSpawn.Count > 0 ? "thread_spawn" :
                    FirstNonBlank(GetValue(source, "subAgent") as string, GetValue(source, "subagent") as string),
                ParentThreadIdConflict = parentConflict,
                ThreadStatus = AppServerAgentActivityMapper.MapThreadStatus(GetDictionary(thread, "status")),
                SubagentDepth = GetNullableInt(threadSpawn, "depth"),
                AgentNickname = FirstNonBlank(GetString(thread, "agentNickname"), GetString(thread, "agent_nickname"), GetString(threadSpawn, "agent_nickname")),
                AgentRole = FirstNonBlank(GetString(thread, "agentRole"), GetString(thread, "agent_role"), GetString(threadSpawn, "agent_role")),
                CreatedAt = FromUnixTime(GetNullableLong(thread, "createdAt")),
                UpdatedAt = FromUnixTime(GetNullableLong(thread, "updatedAt")),
                SessionPath = MapServerPathToClient(mapServerPathToClient, GetString(thread, "path"))
            };
        }

        internal static AppServerThreadDetails BuildThreadDetails(
            IDictionary<string, object> thread,
            Func<string, string> mapServerPathToClient)
        {
            var summary = BuildThreadSummary(thread, mapServerPathToClient);
            var details = new AppServerThreadDetails
            {
                ThreadId = summary.ThreadId,
                Title = summary.Title,
                Preview = summary.Preview,
                WorkingDirectory = summary.WorkingDirectory,
                Source = summary.Source,
                ThreadSource = summary.ThreadSource,
                ParentThreadId = summary.ParentThreadId,
                SubAgentSourceKind = summary.SubAgentSourceKind,
                ParentThreadIdConflict = summary.ParentThreadIdConflict,
                ThreadStatus = summary.ThreadStatus,
                ModelProviderId = summary.ModelProviderId,
                SubagentDepth = summary.SubagentDepth,
                AgentNickname = summary.AgentNickname,
                AgentRole = summary.AgentRole,
                CreatedAt = summary.CreatedAt,
                UpdatedAt = summary.UpdatedAt,
                SessionPath = summary.SessionPath
            };

            var turnIndex = 0;
            foreach (var turnObject in GetArray(thread, "turns"))
            {
                var turn = turnObject as IDictionary<string, object>;
                if (turn == null)
                {
                    continue;
                }

                turnIndex++;
                var turnId = GetString(turn, "id");
                if (!string.IsNullOrWhiteSpace(turnId))
                {
                    details.TurnStatuses[turnId] = GetString(turn, "status");
                }
                var turnItemIndex = 0;
                foreach (var itemObject in GetArray(turn, "items"))
                {
                    turnItemIndex++;
                    var item = itemObject as IDictionary<string, object>;
                    if (item == null)
                    {
                        continue;
                    }

                    AppendThreadItem(details, item, summary.ThreadId, turnId, turnIndex, turnItemIndex);
                }
            }

            details.TurnCount = turnIndex;
            return details;
        }

        private static void AppendThreadItem(
            AppServerThreadDetails details,
            IDictionary<string, object> item,
            string threadId,
            string turnId,
            int turnIndex,
            int turnItemIndex)
        {
            var itemType = GetString(item, "type");
            if (string.IsNullOrWhiteSpace(itemType))
            {
                return;
            }

            var activity = AppServerAgentActivityMapper.Map(item, threadId, turnId, true);
            if (activity != null)
            {
                details.Activities.Add(activity);
                return;
            }

            switch (itemType)
            {
                case "userMessage":
                    var text = ExtractUserMessageText(item);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        details.Messages.Add(CreateMessage(threadId, turnId, turnIndex, turnItemIndex, item, "User", text));
                    }
                    break;
                case "agentMessage":
                    var agentText = GetString(item, "text");
                    if (AssistantMessageUtility.HasDisplayableContent(agentText))
                    {
                        details.Messages.Add(CreateMessage(threadId, turnId, turnIndex, turnItemIndex, item, "Codex", agentText));
                    }
                    break;
                case "plan":
                    var planText = GetString(item, "text");
                    if (!string.IsNullOrWhiteSpace(planText))
                    {
                        details.Messages.Add(CreateMessage(threadId, turnId, turnIndex, turnItemIndex, item, "Codex", BuildPlanMessageText(planText)));
                    }
                    break;
                case "enteredReviewMode":
                    var enteredReviewText = GetString(item, "review");
                    if (!string.IsNullOrWhiteSpace(enteredReviewText))
                    {
                        details.Messages.Add(CreateMessage(threadId, turnId, turnIndex, turnItemIndex, item, "System", L("AppServerClient.Review.Entered.Prefix", "Entered review mode: ") + enteredReviewText));
                    }
                    break;
                case "exitedReviewMode":
                    var exitedReviewText = GetString(item, "review");
                    if (!string.IsNullOrWhiteSpace(exitedReviewText))
                    {
                        details.Messages.Add(CreateMessage(threadId, turnId, turnIndex, turnItemIndex, item, "System", L("AppServerClient.Review.Exited.Prefix", "Exited review mode: ") + exitedReviewText));
                    }
                    break;
            }
        }

        private static AppServerThreadMessage CreateMessage(
            string threadId,
            string turnId,
            int turnIndex,
            int turnItemIndex,
            IDictionary<string, object> item,
            string sender,
            string content)
        {
            return new AppServerThreadMessage
            {
                ThreadId = threadId,
                TurnId = turnId,
                TurnIndex = turnIndex,
                TurnItemIndex = turnItemIndex,
                SourceItemId = GetString(item, "id"),
                Sender = sender,
                Content = content
            };
        }

        private static string ExtractUserMessageText(IDictionary<string, object> item)
        {
            var parts = new List<string>();
            var imageNames = new List<string>();
            var fileNames = new List<string>();

            foreach (var contentObject in GetArray(item, "content"))
            {
                var content = contentObject as IDictionary<string, object>;
                if (content == null)
                {
                    continue;
                }

                var contentType = GetString(content, "type");
                if (string.Equals(contentType, "text", StringComparison.OrdinalIgnoreCase))
                {
                    var text = GetString(content, "text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        parts.Add(text);
                    }
                }
                else if (string.Equals(contentType, "localImage", StringComparison.OrdinalIgnoreCase))
                {
                    var path = GetString(content, "path");
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        imageNames.Add(Path.GetFileName(path));
                    }
                }
                else if (string.Equals(contentType, "image", StringComparison.OrdinalIgnoreCase))
                {
                    var url = GetString(content, "url");
                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        imageNames.Add(ExtractImageDisplayName(url));
                    }
                }
                else if (string.Equals(contentType, "mention", StringComparison.OrdinalIgnoreCase))
                {
                    var path = GetString(content, "path");
                    var name = GetString(content, "name");
                    if (IsLikelyFileMention(path))
                    {
                        fileNames.Add(ExtractMentionDisplayName(name, path));
                    }
                }
            }

            if (imageNames.Count > 0)
            {
                parts.Add(BuildAttachmentSummaryText(imageNames));
            }

            if (fileNames.Count > 0)
            {
                parts.Add(BuildFileAttachmentSummaryText(fileNames));
            }

            return parts.Count == 0 ? null : string.Join(Environment.NewLine, parts.ToArray());
        }

        private static string BuildPlanMessageText(string planText)
        {
            return L("AppServerClient.Plan.MessagePrefix", "Plan:") +
                Environment.NewLine +
                (planText ?? string.Empty).Trim();
        }

        private static string ExtractImageDisplayName(string imageReference)
        {
            if (string.IsNullOrWhiteSpace(imageReference))
            {
                return L("AppServerClient.Attachment.Image.DefaultName", "Image");
            }

            Uri uri;
            if (Uri.TryCreate(imageReference, UriKind.Absolute, out uri))
            {
                var fileName = Path.GetFileName(uri.LocalPath);
                return string.IsNullOrWhiteSpace(fileName) ? imageReference : fileName;
            }

            var localFileName = Path.GetFileName(imageReference);
            return string.IsNullOrWhiteSpace(localFileName) ? imageReference : localFileName;
        }

        private static string BuildAttachmentSummaryText(List<string> imageNames)
        {
            if (imageNames == null || imageNames.Count == 0)
            {
                return string.Empty;
            }

            return imageNames.Count == 1
                ? LF("AppServerClient.Attachment.Image.Single.Template", "Attached 1 image: {0}", imageNames[0])
                : LF("AppServerClient.Attachment.Image.Multi.Template", "Attached {0} images: {1}", imageNames.Count, string.Join(", ", imageNames.ToArray()));
        }

        private static bool IsLikelyFileMention(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            return path.IndexOf("://", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static string ExtractMentionDisplayName(string name, string path)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return L("AppServerClient.Attachment.File.DefaultName", "File");
            }

            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }

        private static string BuildFileAttachmentSummaryText(List<string> fileNames)
        {
            if (fileNames == null || fileNames.Count == 0)
            {
                return string.Empty;
            }

            return fileNames.Count == 1
                ? LF("AppServerClient.Attachment.File.Single.Template", "Attached 1 file: {0}", fileNames[0])
                : LF("AppServerClient.Attachment.File.Multi.Template", "Attached {0} files: {1}", fileNames.Count, string.Join(", ", fileNames.ToArray()));
        }

        private static DateTime FromUnixTime(long? unixTime)
        {
            return unixTime.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(unixTime.Value).LocalDateTime
                : DateTime.Now;
        }

        private static string GetSessionSourceText(object value)
        {
            if (value == null)
            {
                return "unknown";
            }

            var text = value as string;
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var dictionary = value as IDictionary<string, object>;
            if (dictionary == null || dictionary.Count == 0)
            {
                return "unknown";
            }

            foreach (var pair in dictionary)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    return pair.Key + ":" + Convert.ToString(pair.Value, CultureInfo.CurrentCulture);
                }
            }

            return "unknown";
        }

        private static string ResolveSessionSourceText(IDictionary<string, object> thread, string threadSource)
        {
            return string.IsNullOrWhiteSpace(threadSource)
                ? GetSessionSourceText(GetValue(thread, "source"))
                : threadSource;
        }

        private static IDictionary<string, object> GetDictionary(IDictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value))
            {
                var dictionary = value as IDictionary<string, object>;
                if (dictionary != null)
                {
                    return dictionary;
                }
            }

            return new Dictionary<string, object>(StringComparer.Ordinal);
        }

        private static object GetValue(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value
                : null;
        }

        private static string GetString(IDictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value, CultureInfo.CurrentCulture)
                : null;
        }

        private static IEnumerable<object> GetArray(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                yield break;
            }

            var array = value as object[];
            if (array != null)
            {
                foreach (var item in array)
                {
                    yield return item;
                }

                yield break;
            }

            var enumerable = value as System.Collections.IEnumerable;
            if (enumerable == null || value is string)
            {
                yield break;
            }

            foreach (var item in enumerable)
            {
                yield return item;
            }
        }

        private static long? GetNullableLong(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            long result;
            return long.TryParse(Convert.ToString(value, CultureInfo.CurrentCulture), out result)
                ? (long?)result
                : null;
        }

        private static int? GetNullableInt(IDictionary<string, object> source, string key)
        {
            object value;
            if (source == null || !source.TryGetValue(key, out value) || value == null)
            {
                return null;
            }

            int result;
            return int.TryParse(Convert.ToString(value, CultureInfo.CurrentCulture), out result)
                ? (int?)result
                : null;
        }

        private static string MapServerPathToClient(Func<string, string> mapper, string path)
        {
            return mapper == null ? path : mapper(path);
        }

        private static string FirstNonBlank(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return null;
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? Array.Empty<object>());
        }
    }
}
