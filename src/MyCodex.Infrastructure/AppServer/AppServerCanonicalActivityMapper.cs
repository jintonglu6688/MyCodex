using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerCanonicalActivityMapper
    {
        private const int MaxSummaryLength = 240;

        internal static AppServerEventArgs Map(
            IDictionary<string, object> item,
            bool completed,
            string threadId,
            string turnId,
            Func<string, string> mapServerPathToClient)
        {
            var itemType = AppServerJsonValue.GetString(item, "type");
            switch (itemType)
            {
                case "imageGeneration":
                    return MapImageGeneration(item, completed, threadId, turnId, mapServerPathToClient);
                case "subAgentActivity":
                case "collabAgentToolCall":
                    return AppServerAgentActivityMapper.Map(item, threadId, turnId);
                case "hookPrompt":
                default:
                    return null;
            }
        }

        private static AppServerEventArgs MapImageGeneration(
            IDictionary<string, object> item,
            bool completed,
            string threadId,
            string turnId,
            Func<string, string> mapServerPathToClient)
        {
            var status = AppServerJsonValue.GetString(item, "status");
            var revisedPrompt = Abbreviate(AppServerJsonValue.GetString(item, "revisedPrompt"));
            var summary = string.IsNullOrWhiteSpace(revisedPrompt)
                ? Abbreviate(status)
                : revisedPrompt;
            var content = completed
                ? Format(
                    "AppServerClient.ImageGeneration.Completed.Template",
                    "Image generation completed: {0}",
                    summary)
                : Format(
                    "AppServerClient.ImageGeneration.Started.Template",
                    "Image generation started: {0}",
                    summary);
            var savedPath = AppServerJsonValue.GetString(item, "savedPath");
            if (!string.IsNullOrWhiteSpace(savedPath) && mapServerPathToClient != null)
            {
                savedPath = mapServerPathToClient(savedPath);
            }

            return new AppServerEventArgs(
                completed ? "image_generation_completed" : "image_generation_started",
                content,
                threadId: threadId,
                turnId: turnId,
                itemId: AppServerJsonValue.GetString(item, "id"),
                details: savedPath,
                itemStatus: status);
        }

        private static string Abbreviate(string value)
        {
            var normalized = (value ?? string.Empty).Trim();
            return normalized.Length <= MaxSummaryLength
                ? normalized
                : normalized.Substring(0, MaxSummaryLength - 3) + "...";
        }

        private static string Format(string key, string fallback, params object[] values)
        {
            return string.Format(SharedLocalization.Text(key, fallback), values);
        }
    }
}
