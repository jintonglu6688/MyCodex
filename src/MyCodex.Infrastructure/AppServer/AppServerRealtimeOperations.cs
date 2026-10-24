using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    // Official thread-scoped realtime voice (v3 over WebRTC): requests and notification mapping.
    internal sealed class AppServerRealtimeOperations
    {
        private const string RealtimeVersion = "v3";
        private readonly AppServerRequestInvoker _send;

        public AppServerRealtimeOperations(AppServerRequestInvoker send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public async Task<RealtimeVoiceCatalog> ListVoicesAsync(CancellationToken cancellationToken)
        {
            await RequireChatGptAsync(cancellationToken).ConfigureAwait(false);
            var result = await _send("thread/realtime/listVoices", new Dictionary<string, object>(), cancellationToken, 30000).ConfigureAwait(false);
            var catalog = AppServerJsonValueReader.GetDictionary(result, "voices");
            var voices = AppServerJsonValueReader.GetArray(catalog, RealtimeVersion)
                .OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            // The catalog has no v3 default; prefer the v1 default when v3 offers it.
            var defaultVoice = AppServerJsonValueReader.GetString(catalog, "defaultV1");
            return new RealtimeVoiceCatalog
            {
                Voices = voices,
                DefaultVoice = voices.Contains(defaultVoice, StringComparer.Ordinal) ? defaultVoice : voices.FirstOrDefault()
            };
        }

        public async Task StartAsync(RealtimeVoiceStart request, CancellationToken cancellationToken)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.ThreadId) ||
                string.IsNullOrWhiteSpace(request.SessionId) ||
                string.IsNullOrWhiteSpace(request.OfferSdp) ||
                string.IsNullOrWhiteSpace(request.Voice))
            {
                throw new ArgumentException("A thread, session, SDP offer and voice are required.", nameof(request));
            }

            await RequireChatGptAsync(cancellationToken).ConfigureAwait(false);
            await UpdateBackendAsync(request, cancellationToken).ConfigureAwait(false);
            var initialItems = await AppServerVoiceContext.ReadInitialItemsAsync(_send, request.ThreadId, cancellationToken).ConfigureAwait(false);
            var parameters = new Dictionary<string, object>
            {
                { "threadId", request.ThreadId },
                { "realtimeSessionId", request.SessionId },
                { "version", RealtimeVersion },
                { "outputModality", "audio" },
                { "voice", request.Voice },
                { "transport", new Dictionary<string, object> { { "type", "webrtc" }, { "sdp", request.OfferSdp } } }
            };
            if (initialItems.Count > 0)
            {
                parameters["initialItems"] = initialItems.ToArray();
            }

            await _send("thread/realtime/start", parameters, cancellationToken, 30000).ConfigureAwait(false);
        }

        public Task StopAsync(string threadId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                throw new ArgumentException("A thread is required.", nameof(threadId));
            }

            return _send("thread/realtime/stop", new Dictionary<string, object> { { "threadId", threadId } }, cancellationToken, 6000);
        }

        private Task UpdateBackendAsync(RealtimeVoiceStart request, CancellationToken cancellationToken)
        {
            if (request.BackendMode == null)
            {
                return Task.CompletedTask;
            }

            var parameters = new Dictionary<string, object> { { "threadId", request.ThreadId } };
            AppServerCollaborationParameters.AddTo(parameters, request.BackendMode);
            return parameters.ContainsKey("collaborationMode")
                ? _send("thread/settings/update", parameters, cancellationToken, 30000)
                : Task.CompletedTask;
        }

        private async Task RequireChatGptAsync(CancellationToken cancellationToken)
        {
            var result = await _send("account/read", new Dictionary<string, object> { { "refreshToken", false } }, cancellationToken, 30000).ConfigureAwait(false);
            var type = AppServerJsonValueReader.GetString(AppServerJsonValueReader.GetDictionary(result, "account"), "type");
            if (!string.Equals(type, "chatgpt", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(SharedLocalization.Text("VoiceChat.Error.ChatGptRequired", "Sign in with ChatGPT to use official voice chat."));
            }
        }

        public static RealtimeVoiceEvent MapNotification(string method, IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValueReader.GetString(parameters, "threadId");
            switch (method)
            {
                case "thread/realtime/started":
                    return Event(RealtimeVoiceEventKind.Started, threadId, AppServerJsonValueReader.GetString(parameters, "realtimeSessionId"));
                case "thread/realtime/sdp":
                    var sdp = Event(RealtimeVoiceEventKind.Sdp, threadId, null);
                    sdp.Sdp = AppServerJsonValueReader.GetString(parameters, "sdp");
                    return sdp;
                case "thread/realtime/error":
                    var error = Event(RealtimeVoiceEventKind.Error, threadId, null);
                    error.Text = AppServerJsonValueReader.GetString(parameters, "message");
                    return error;
                case "thread/realtime/closed":
                    var closed = Event(RealtimeVoiceEventKind.Closed, threadId, null);
                    closed.Text = AppServerJsonValueReader.GetString(parameters, "reason");
                    return closed;
                case "thread/realtime/item/completed":
                    var item = AppServerJsonValueReader.GetDictionary(parameters, "item");
                    if (AppServerJsonValueReader.GetString(item, "type") != "transcriptSegment")
                    {
                        return null;
                    }

                    var transcript = Event(RealtimeVoiceEventKind.Transcript, threadId, AppServerJsonValueReader.GetString(item, "realtimeSessionId"));
                    transcript.ItemId = AppServerJsonValueReader.GetString(item, "id");
                    transcript.Role = AppServerJsonValueReader.GetString(item, "role");
                    transcript.Text = AppServerJsonValueReader.GetString(item, "text");
                    return transcript;
                default:
                    return null;
            }
        }

        private static RealtimeVoiceEvent Event(RealtimeVoiceEventKind kind, string threadId, string sessionId)
        {
            return new RealtimeVoiceEvent { Kind = kind, ThreadId = threadId, SessionId = sessionId };
        }
    }
}
