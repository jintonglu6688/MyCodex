using System;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.DTOs
{
    public sealed class AppServerConversationSummaryRequest
    {
        private AppServerConversationSummaryRequest()
        {
        }

        public string ConversationId { get; private set; }

        public string RolloutPath { get; private set; }

        public static AppServerConversationSummaryRequest FromConversationId(string conversationId)
        {
            return new AppServerConversationSummaryRequest
            {
                ConversationId = conversationId
            };
        }

        public static AppServerConversationSummaryRequest FromRolloutPath(string rolloutPath)
        {
            return new AppServerConversationSummaryRequest
            {
                RolloutPath = rolloutPath
            };
        }

        public bool HasExactlyOneSource
        {
            get
            {
                var hasConversationId = !string.IsNullOrWhiteSpace(ConversationId);
                var hasRolloutPath = !string.IsNullOrWhiteSpace(RolloutPath);
                return hasConversationId ^ hasRolloutPath;
            }
        }

        public void Validate()
        {
            if (!HasExactlyOneSource)
            {
                throw new ArgumentException(SharedLocalization.Text(
                    "AppServerConversationSummaryRequest.Error.ExactlyOneSourceRequired",
                    "Exactly one conversation summary source is required."));
            }
        }
    }
}
