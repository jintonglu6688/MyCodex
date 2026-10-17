using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerCodingRemoteThreadMessageReader : ICodingRemoteThreadMessageReader
    {
        private readonly IAppStateStore _appStateStore;
        private readonly IAppServerThreadClient _threadClient;
        private readonly IAppServerInteractionClient _interactionClient;
        private readonly CodexSessionFileThreadDetailsReader _sessionFileReader;
        private readonly AppInteractionMode _interactionMode;
        private readonly RemoteConversationSnapshotCache _snapshotCache;

        public AppServerCodingRemoteThreadMessageReader(IAppStateStore appStateStore, IAppServerThreadClient threadClient)
            : this(appStateStore, threadClient, new CodexSessionFileThreadDetailsReader(), AppInteractionMode.Coding, null)
        {
        }

        public AppServerCodingRemoteThreadMessageReader(
            IAppStateStore appStateStore,
            IAppServerThreadClient threadClient,
            AppInteractionMode interactionMode)
            : this(appStateStore, threadClient, new CodexSessionFileThreadDetailsReader(), interactionMode, null)
        {
        }

        public AppServerCodingRemoteThreadMessageReader(IAppStateStore appStateStore, IAppServerThreadClient threadClient, AppInteractionMode interactionMode, RemoteConversationSnapshotCache snapshotCache)
            : this(appStateStore, threadClient, new CodexSessionFileThreadDetailsReader(), interactionMode, snapshotCache)
        {
        }

        internal AppServerCodingRemoteThreadMessageReader(
            IAppStateStore appStateStore,
            IAppServerThreadClient threadClient,
            CodexSessionFileThreadDetailsReader sessionFileReader)
            : this(appStateStore, threadClient, sessionFileReader, AppInteractionMode.Coding, null)
        {
        }

        internal AppServerCodingRemoteThreadMessageReader(
            IAppStateStore appStateStore,
            IAppServerThreadClient threadClient,
            CodexSessionFileThreadDetailsReader sessionFileReader,
            AppInteractionMode interactionMode,
            RemoteConversationSnapshotCache snapshotCache = null)
        {
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _threadClient = threadClient ?? throw new ArgumentNullException(nameof(threadClient));
            _interactionClient = threadClient as IAppServerInteractionClient;
            _sessionFileReader = sessionFileReader ?? throw new ArgumentNullException(nameof(sessionFileReader));
            _interactionMode = interactionMode;
            _snapshotCache = snapshotCache;
        }

        public async Task<CodingRemoteMessagePage> ListMessagesAsync(CodingRemoteMessagesListRequest request, CancellationToken cancellationToken)
        {
            request = request ?? new CodingRemoteMessagesListRequest();
            var session = FindCodingSession(request.SessionId);
            var page = new CodingRemoteMessagePage
            {
                SessionId = request.SessionId
            };

            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return page;
            }

            page.IsGenerating = _interactionClient != null && _interactionClient.HasInterruptibleTurn(session.ThreadId);

            var details = await ReadDetailsAsync(session, cancellationToken).ConfigureAwait(false);
            if (details != null && details.Messages.Count > 0)
            {
                AddMessages(page, session.Id, details, request, ShouldExposeTurnActions());
                return page;
            }

            AppServerThreadDetails fileDetails;
            if (session.IsImported && _sessionFileReader.TryRead(session, out fileDetails))
            {
                AddMessages(page, session.Id, fileDetails, request, false);
                return page;
            }

            AddMessages(page, session.Id, details, request, true);

            return page;
        }

        private async Task<AppServerThreadDetails> TryReadAppServerDetailsAsync(string threadId, CancellationToken cancellationToken)
        {
            try
            {
                return await _threadClient.ReadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        public async Task<CodingRemoteMessageContentPage> ReadMessageContentAsync(CodingRemoteMessageContentRequest request, CancellationToken cancellationToken)
        {
            request = request ?? new CodingRemoteMessageContentRequest();
            var session = FindCodingSession(request.SessionId);
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                throw new CodingRemoteOperationException("message_not_found", "The message is not available.");
            }

            var details = await ReadDetailsAsync(session, cancellationToken).ConfigureAwait(false);
            if (details == null && session.IsImported)
            {
                _sessionFileReader.TryRead(session, out details);
            }

            var message = MapMessages(details, 0, ShouldExposeTurnActions()).FirstOrDefault(candidate =>
                string.Equals(candidate.MessageId, request.MessageId, StringComparison.Ordinal));
            if (message == null)
            {
                throw new CodingRemoteOperationException("message_not_found", "The message is not available.");
            }

            if (!string.IsNullOrWhiteSpace(request.ContentVersion) &&
                !string.Equals(request.ContentVersion, message.ContentVersion, StringComparison.Ordinal))
            {
                throw new CodingRemoteOperationException("message_content_changed", "The message content changed.");
            }

            long offset;
            if (!TryReadContentCursor(request.Cursor, request.SessionId, message.MessageId, message.ContentVersion, out offset))
            {
                throw new CodingRemoteOperationException("invalid_cursor", "The message cursor is invalid.");
            }

            long bytesRead;
            long totalBytes;
            var text = RemoteConversationUtf8Slice.Slice(message.Text, offset, RemoteConversationUtf8Slice.ContentChunkByteLimit, out bytesRead, out totalBytes);
            var nextOffset = offset + bytesRead;
            return new CodingRemoteMessageContentPage
            {
                SessionId = request.SessionId,
                MessageId = message.MessageId,
                ContentVersion = message.ContentVersion,
                Text = text,
                LoadedUtf8Bytes = nextOffset,
                TotalUtf8Bytes = totalBytes,
                Complete = nextOffset >= totalBytes,
                NextCursor = nextOffset >= totalBytes ? null : CreateContentCursor(request.SessionId, message.MessageId, message.ContentVersion, nextOffset)
            };
        }

        private static void AddMessages(CodingRemoteMessagePage page, string sessionId, AppServerThreadDetails details, CodingRemoteMessagesListRequest request, bool allowTurnActions)
        {
            var messages = MapMessages(details, 0, allowTurnActions).ToList();
            var revision = RemoteConversationStableIdentity.CreateHistoryRevision(messages);
            var anchor = ResolveAnchor(messages, request == null ? null : request.AnchorMessageId);
            var cursor = ResolveCursor(request, sessionId, revision);
            if (cursor != null && messages.All(candidate => !string.Equals(candidate.MessageId, cursor.Item2, StringComparison.Ordinal)))
            {
                throw new CodingRemoteOperationException("history_changed", "The message history changed.");
            }

            if (cursor == null && request != null && (!string.IsNullOrWhiteSpace(request.BeforeCursor) || !string.IsNullOrWhiteSpace(request.AfterCursor)))
            {
                throw new CodingRemoteOperationException("invalid_cursor", "The message cursor is invalid.");
            }

            var limit = request != null && request.PagingVersion > 0
                ? Math.Max(1, Math.Min(100, request.Limit <= 0 ? 100 : request.Limit))
                : request == null || request.Limit <= 0 ? messages.Count : Math.Min(200, request.Limit);
            var window = SelectWindow(messages, limit, anchor, cursor);
            var first = window.FirstOrDefault();
            var last = window.LastOrDefault();
            page.HistoryRevision = revision;
            page.AnchorFound = string.IsNullOrWhiteSpace(request == null ? null : request.AnchorMessageId) || anchor != null;
            page.ResolvedAnchorMessageId = anchor == null ? null : anchor.MessageId;
            page.HasOlder = first != null && messages.IndexOf(first) > 0;
            page.HasNewer = last != null && messages.IndexOf(last) < messages.Count - 1;
            page.TailConnected = !page.HasNewer;
            page.TailMessageId = messages.Count == 0 ? null : messages[messages.Count - 1].MessageId;
            page.LatestAssistantMessageId = messages.LastOrDefault(candidate => string.Equals(candidate.Role, CodingRemoteMessageRoles.Assistant, StringComparison.Ordinal))?.MessageId;
            page.OlderCursor = first == null ? null : CreateCursor(sessionId, "older", first.MessageId, revision);
            page.NewerCursor = last == null ? null : CreateCursor(sessionId, "newer", last.MessageId, revision);
            page.NextCursor = page.OlderCursor;

            foreach (var message in window)
            {
                var preview = RemoteConversationUtf8Slice.Take(message.Text, RemoteConversationUtf8Slice.PreviewByteLimit, out var truncated, out var totalBytes);
                message.Text = preview;
                message.ContentTruncated = truncated;
                message.ContentUtf8ByteCount = totalBytes;
                message.SessionId = sessionId;
                page.Messages.Add(message);
            }
        }

        private async Task<AppServerThreadDetails> ReadDetailsAsync(ChatSession session, CancellationToken cancellationToken)
        {
            AppServerThreadDetails details;
            var mode = _interactionMode.ToString();
            if (_snapshotCache != null && _snapshotCache.TryGet(mode, session.Id, out details))
            {
                return details;
            }

            details = session.IsImported
                ? await TryReadAppServerDetailsAsync(session.ThreadId, cancellationToken).ConfigureAwait(false)
                : await _threadClient.ReadThreadAsync(session.ThreadId, cancellationToken).ConfigureAwait(false);
            if (details != null && _snapshotCache != null)
            {
                _snapshotCache.Put(mode, session.Id, details);
            }

            return details;
        }

        private ChatSession FindCodingSession(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                return null;
            }

            var snapshot = _appStateStore.Load() ?? new AppStateSnapshot();
            return (snapshot.Sessions ?? new List<ChatSession>())
                .FirstOrDefault(session =>
                    session != null &&
                    session.Mode == _interactionMode &&
                    string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldExposeTurnActions()
        {
            return _interactionMode == AppInteractionMode.Coding;
        }

        private static IReadOnlyList<CodingRemoteMessageItem> MapMessages(AppServerThreadDetails details, int limit, bool allowTurnActions)
        {
            var source = details != null && details.Messages != null
                ? details.Messages.Where(message => message != null && !string.IsNullOrWhiteSpace(message.Content)).ToList()
                : new List<AppServerThreadMessage>();
            var turnCount = details == null ? 0 : details.TurnCount;
            var effectiveLimit = limit <= 0 ? source.Count : Math.Min(limit, 200);
            if (effectiveLimit >= 0 && source.Count > effectiveLimit)
            {
                source = source.Skip(source.Count - effectiveLimit).ToList();
            }

            var result = new List<CodingRemoteMessageItem>();
            for (var index = 0; index < source.Count; index++)
            {
                var message = source[index];
                var canCopy = !string.IsNullOrWhiteSpace(message.Content);
                var hasValidTurn = allowTurnActions && message.TurnIndex > 0 && turnCount >= message.TurnIndex && !string.IsNullOrWhiteSpace(message.TurnId);
                var trimTurnsAfterTurn = hasValidTurn ? turnCount - message.TurnIndex : 0;
                result.Add(new CodingRemoteMessageItem
                {
                    MessageId = RemoteConversationStableIdentity.CreateMessageId(message),
                    Role = MapRole(message.Sender),
                    Text = message.Content,
                    ContentVersion = RemoteConversationStableIdentity.CreateContentVersion(message.Content),
                    Status = CodingRemoteMessageStatuses.Complete,
                    ThreadId = message.ThreadId,
                    TurnId = message.TurnId,
                    TurnIndex = message.TurnIndex,
                    TurnCount = turnCount,
                    TurnPreview = BuildTurnPreview(source, message.TurnIndex),
                    Actions = new CodingRemoteMessageActions
                    {
                        Copy = canCopy,
                        ForkFromTurn = hasValidTurn,
                    },
                    OperationPreview = new CodingRemoteMessageOperationPreview
                    {
                        TrimTurnsAfterTurn = trimTurnsAfterTurn,
                    }
                });
            }

            return result;
        }

        private static string BuildTurnPreview(IReadOnlyList<AppServerThreadMessage> messages, int turnIndex)
        {
            if (messages == null || turnIndex <= 0)
            {
                return string.Empty;
            }

            var userMessage = messages.FirstOrDefault(candidate =>
                candidate != null &&
                candidate.TurnIndex == turnIndex &&
                string.Equals(candidate.Sender, "user", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate.Content));
            if (userMessage == null)
            {
                return string.Empty;
            }

            var content = userMessage.Content.Trim();
            return content.Length <= 80 ? content : content.Substring(0, 80);
        }

        private static List<CodingRemoteMessageItem> SelectWindow(
            List<CodingRemoteMessageItem> messages,
            int limit,
            CodingRemoteMessageItem anchor,
            Tuple<string, string> cursor)
        {
            if (messages.Count == 0)
            {
                return new List<CodingRemoteMessageItem>();
            }

            if (cursor != null)
            {
                var index = messages.FindIndex(candidate => string.Equals(candidate.MessageId, cursor.Item2, StringComparison.Ordinal));
                if (string.Equals(cursor.Item1, "older", StringComparison.Ordinal))
                {
                    var start = Math.Max(0, index - limit);
                    return messages.Skip(start).Take(index - start).ToList();
                }

                return messages.Skip(index + 1).Take(limit).ToList();
            }

            if (anchor != null)
            {
                var anchorIndex = messages.IndexOf(anchor);
                var start = Math.Max(0, anchorIndex - 50);
                var count = Math.Min(limit, messages.Count - start);
                if (count < limit)
                {
                    start = Math.Max(0, messages.Count - limit);
                    count = Math.Min(limit, messages.Count - start);
                }

                return messages.Skip(start).Take(count).ToList();
            }

            return messages.Skip(Math.Max(0, messages.Count - limit)).Take(limit).ToList();
        }

        private static CodingRemoteMessageItem ResolveAnchor(List<CodingRemoteMessageItem> messages, string anchorMessageId)
        {
            if (string.IsNullOrWhiteSpace(anchorMessageId)) return null;
            var direct = messages.FirstOrDefault(candidate => string.Equals(candidate.MessageId, anchorMessageId, StringComparison.Ordinal));
            if (direct != null) return direct;
            for (var index = 0; index < messages.Count; index++)
            {
                var message = messages[index];
                var legacyId = (message.TurnId ?? "turn-" + message.TurnIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)) + ":message-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (string.Equals(legacyId, anchorMessageId, StringComparison.Ordinal)) return message;
            }

            return null;
        }

        private static Tuple<string, string> ResolveCursor(CodingRemoteMessagesListRequest request, string sessionId, string revision)
        {
            if (request == null) return null;
            var direction = !string.IsNullOrWhiteSpace(request.BeforeCursor) ? "older" : !string.IsNullOrWhiteSpace(request.AfterCursor) ? "newer" : null;
            var cursor = direction == "older" ? request.BeforeCursor : request.AfterCursor;
            if (direction == null) return null;
            var parts = FromBase64Url(cursor).Split('|');
            if (parts.Length != 5 || parts[0] != "v1" || !string.Equals(parts[1], sessionId, StringComparison.Ordinal) || !string.Equals(parts[2], direction, StringComparison.Ordinal)) return null;
            if (!string.Equals(parts[4], revision, StringComparison.Ordinal)) throw new CodingRemoteOperationException("history_changed", "The message history changed.");
            return Tuple.Create(direction, parts[3]);
        }

        private static string CreateCursor(string sessionId, string direction, string messageId, string revision)
        {
            return ToBase64Url("v1|" + sessionId + "|" + direction + "|" + messageId + "|" + revision);
        }

        private static string CreateContentCursor(string sessionId, string messageId, string contentVersion, long offset)
        {
            return ToBase64Url("c1|" + sessionId + "|" + messageId + "|" + contentVersion + "|" + offset.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        private static bool TryReadContentCursor(string cursor, string sessionId, string messageId, string contentVersion, out long offset)
        {
            offset = 0;
            if (string.IsNullOrWhiteSpace(cursor)) return true;
            var parts = FromBase64Url(cursor).Split('|');
            return parts.Length == 5 && parts[0] == "c1" && string.Equals(parts[1], sessionId, StringComparison.Ordinal) && string.Equals(parts[2], messageId, StringComparison.Ordinal) && string.Equals(parts[3], contentVersion, StringComparison.Ordinal) && long.TryParse(parts[4], out offset) && offset >= 0;
        }

        private static string ToBase64Url(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        private static string FromBase64Url(string value)
        {
            try
            {
                value = (value ?? string.Empty).Replace('-', '+').Replace('_', '/');
                switch (value.Length % 4) { case 2: value += "=="; break; case 3: value += "="; break; }
                return Encoding.UTF8.GetString(Convert.FromBase64String(value));
            }
            catch (FormatException)
            {
                return string.Empty;
            }
        }

        private static string MapRole(string sender)
        {
            var value = (sender ?? string.Empty).Trim().ToLowerInvariant();
            if (value == "user" || value == "me")
            {
                return CodingRemoteMessageRoles.User;
            }

            if (value == "codex" || value == "assistant")
            {
                return CodingRemoteMessageRoles.Assistant;
            }

            if (value == "tool")
            {
                return CodingRemoteMessageRoles.Tool;
            }

            return CodingRemoteMessageRoles.System;
        }
    }
}
