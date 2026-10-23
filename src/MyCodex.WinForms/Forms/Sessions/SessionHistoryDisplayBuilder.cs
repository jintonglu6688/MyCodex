using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionHistoryDisplayBuilder
    {
        private readonly string _systemSender;
        private readonly string _errorSender;
        private readonly Func<ChatSession, string> _modelSummaryBuilder;
        private readonly Func<string, string, ChatHistoryCategory, ChatHistoryItem> _itemFactory;
        private readonly Func<string, ChatHistoryCategory> _defaultCategoryResolver;

        internal SessionHistoryDisplayBuilder(
            string systemSender,
            string errorSender,
            Func<ChatSession, string> modelSummaryBuilder,
            Func<string, string, ChatHistoryCategory, ChatHistoryItem> itemFactory,
            Func<string, ChatHistoryCategory> defaultCategoryResolver)
        {
            _systemSender = systemSender ?? string.Empty;
            _errorSender = errorSender ?? string.Empty;
            _modelSummaryBuilder = modelSummaryBuilder ?? throw new ArgumentNullException(nameof(modelSummaryBuilder));
            _itemFactory = itemFactory ?? throw new ArgumentNullException(nameof(itemFactory));
            _defaultCategoryResolver = defaultCategoryResolver ?? throw new ArgumentNullException(nameof(defaultCategoryResolver));
        }

        internal string BuildDisplayedSessionHeaderText(ChatHistoryItem firstItem, ChatSession session)
        {
            if (session == null)
            {
                return string.Empty;
            }

            var fullText = firstItem != null ? firstItem.FullText ?? string.Empty : string.Empty;
            var sessionDisplayTitle = SessionDisplayFormatter.BuildDisplayTitle(session);
            var switchedPrefix = UiLocalization.Text("MainForm.Message.SwitchedToSession.Prefix", "Switched to session:");
            var createdPrefix = UiLocalization.Text("MainForm.Message.CreatedNewSession.Prefix", "Created new session:");
            if (fullText.IndexOf(switchedPrefix, StringComparison.Ordinal) >= 0)
            {
                return string.Format(UiLocalization.Text("MainForm.Message.SwitchedToSession.Template", "Switched to session: {0}"), sessionDisplayTitle);
            }

            if (fullText.IndexOf(createdPrefix, StringComparison.Ordinal) >= 0)
            {
                return string.Format(UiLocalization.Text("MainForm.Message.CreatedNewSession.Template", "Created new session: {0}"), sessionDisplayTitle);
            }

            return string.Format(UiLocalization.Text("MainForm.Message.SwitchedSession.Template", "Switched session: {0}"), sessionDisplayTitle);
        }

        internal ChatHistoryItem[] BuildSessionHeaderItems(ChatSession session, bool isNewSession)
        {
            if (session == null)
            {
                return Array.Empty<ChatHistoryItem>();
            }

            var items = new List<ChatHistoryItem>
            {
                CreateSystemItem(isNewSession
                    ? string.Format(UiLocalization.Text("MainForm.Message.CreatedNewSession.Template", "Created new session: {0}"), SessionDisplayFormatter.BuildDisplayTitle(session))
                    : string.Format(UiLocalization.Text("MainForm.Message.SwitchedSession.Template", "Switched session: {0}"), SessionDisplayFormatter.BuildDisplayTitle(session))),
                CreateSystemItem(string.Format(UiLocalization.Text("MainForm.Message.CurrentBranch.Template", "Current branch: {0}"), SessionDisplayFormatter.BuildBranchLabel(session)))
            };

            if (session.IsPlanModeEnabled)
            {
                items.Add(CreateSystemItem(UiLocalization.Text("MainForm.Message.CurrentPlanModeEnabled", "Current mode: Plan")));
            }

            var goalSummary = SessionDisplayFormatter.BuildGoalSummaryText(session);
            if (!string.IsNullOrWhiteSpace(goalSummary))
            {
                items.Add(CreateSystemItem(goalSummary));
            }

            var forkSourceText = SessionDisplayFormatter.BuildForkSourceText(session);
            if (!string.IsNullOrWhiteSpace(forkSourceText))
            {
                items.Add(CreateSystemItem(string.Format(UiLocalization.Text("MainForm.Message.ForkedFrom.Template", "Forked from: {0}"), forkSourceText)));
            }

            var reviewSourceText = SessionDisplayFormatter.BuildReviewSourceText(session);
            if (!string.IsNullOrWhiteSpace(reviewSourceText))
            {
                items.Add(CreateSystemItem(string.Format(UiLocalization.Text("MainForm.Message.ReviewFrom.Template", "Review from: {0}"), reviewSourceText)));
            }

            if (!string.IsNullOrWhiteSpace(session.ThreadId))
            {
                items.Add(CreateSystemItem(string.Format(UiLocalization.Text("MainForm.Message.ThreadId.Template", "Thread ID: {0}"), session.ThreadId)));
            }
            else
            {
                items.Add(CreateSystemItem(
                    UiLocalization.Text(
                        "MainForm.Message.ThreadWillBeCreatedOnFirstSend",
                        "A remote thread will be created automatically when you send the first message.")));
            }

            items.Add(CreateSystemItem(string.Format(UiLocalization.Text("MainForm.Message.CurrentModel.Template", "Current model: {0}"), _modelSummaryBuilder(session))));
            return items.ToArray();
        }

        internal ChatHistoryItem[] BuildLoadingChatHistoryItems(ChatSession session, bool includeSystemHeader)
        {
            var items = new List<ChatHistoryItem>();
            AppendSessionHeaderItems(items, session, includeSystemHeader);
            items.Add(CreateSystemItem(UiLocalization.Text("MainForm.Status.LoadingSessionHistory", "Loading session history...")));
            return items.ToArray();
        }

        internal ChatHistoryItem[] BuildDraftSessionHistoryItems(ChatSession session, bool includeSystemHeader)
        {
            var items = new List<ChatHistoryItem>();
            AppendSessionHeaderItems(items, session, includeSystemHeader);
            items.Add(CreateSystemItem(
                UiLocalization.Text(
                    "MainForm.Message.DraftSessionReadyToSend",
                    "This session has no history messages yet. You can continue typing, and a remote thread will be created automatically when you send the first message.")));
            return items.ToArray();
        }

        internal ChatHistoryItem[] BuildDisplayChatHistoryItems(ChatSession session, bool includeSystemHeader, ChatHistoryItem[] historyItems)
        {
            var items = new List<ChatHistoryItem>();
            AppendSessionHeaderItems(items, session, includeSystemHeader);

            if (historyItems != null && historyItems.Length > 0)
            {
                items.AddRange(historyItems);
            }
            else
            {
                items.Add(CreateSystemItem(UiLocalization.Text("MainForm.Message.NoHistoryMessages", "No history messages.")));
            }

            return items.ToArray();
        }

        internal ChatHistoryItem[] BuildErrorChatHistoryItems(ChatSession session, bool includeSystemHeader, string errorMessage)
        {
            var items = new List<ChatHistoryItem>();
            AppendSessionHeaderItems(items, session, includeSystemHeader);
            items.Add(CreateErrorItem(errorMessage));
            return items.ToArray();
        }

        internal ChatHistoryItem[] BuildConnectivityUnavailableHistoryItems(ChatSession session, bool includeSystemHeader, string errorMessage)
        {
            var items = new List<ChatHistoryItem>();
            AppendSessionHeaderItems(items, session, includeSystemHeader);
            items.Add(CreateSystemItem(
                UiLocalization.Text("MainForm.Message.CannotConnectAppServerRetryEnter", "Unable to connect to App Server for now. Press Enter to retry loading full history.")));
            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                items.Add(CreateErrorItem(errorMessage));
            }

            return items.ToArray();
        }

        internal ChatHistoryItem[] BuildMissingThreadHistoryItems(ChatSession session, bool includeSystemHeader, string errorMessage)
        {
            var items = new List<ChatHistoryItem>();
            AppendSessionHeaderItems(items, session, includeSystemHeader);
            items.Add(CreateSystemItem(
                UiLocalization.Text("MainForm.Message.RemoteThreadMissingCanContinueSending", "Remote thread for current session is missing. You can continue sending messages, and a new thread will be created automatically.")));
            items.Add(CreateErrorItem(errorMessage));
            return items.ToArray();
        }

        internal ChatHistoryItem[] BuildHistoryChatItems(AppServerThreadDetails details)
        {
            var items = new List<ChatHistoryItem>();
            if (details != null && details.Messages.Count > 0)
            {
                foreach (var message in details.Messages)
                {
                    var item = _itemFactory(
                        message.Sender,
                        message.Content,
                        _defaultCategoryResolver(message.Sender));
                    item.ThreadId = message.ThreadId;
                    item.SourceItemId = message.SourceItemId;
                    item.TurnId = message.TurnId;
                    item.TurnIndex = message.TurnIndex;
                    item.TurnCount = details.TurnCount;
                    items.Add(item);
                }
            }

            items = VoiceHistoryProjection.Merge(items, details?.RealtimeHistory, _itemFactory);
            if (items.Count == 0)
            {
                items.Add(CreateSystemItem(UiLocalization.Text("MainForm.Message.NoHistoryMessages", "No history messages.")));
            }

            return items.ToArray();
        }

        private void AppendSessionHeaderItems(List<ChatHistoryItem> items, ChatSession session, bool includeSystemHeader)
        {
            if (!includeSystemHeader)
            {
                return;
            }

            items.AddRange(BuildSessionHeaderItems(session, false));
        }

        private ChatHistoryItem CreateSystemItem(string message)
        {
            return _itemFactory(_systemSender, message, ChatHistoryCategory.System);
        }

        private ChatHistoryItem CreateErrorItem(string message)
        {
            return _itemFactory(_errorSender, message, ChatHistoryCategory.Error);
        }
    }
}
