using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal sealed class AppServerRealtimeOperations
    {
        private readonly AppServerRequestInvoker _send;

        public AppServerRealtimeOperations(AppServerRequestInvoker send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        public async Task<RealtimeVoiceCatalog> ListVoicesAsync(CancellationToken token)
        {
            await RequireChatGptAsync(token).ConfigureAwait(false);
            var result = await _send("thread/realtime/listVoices", new Dictionary<string, object>(), token, 30000).ConfigureAwait(false);
            var catalog = AppServerJsonValueReader.GetDictionary(result, "voices");
            var voices = AppServerJsonValueReader.GetArray(catalog, "v3").OfType<string>()
                .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).ToArray();
            var defaultVoice = AppServerJsonValueReader.GetString(catalog, "defaultV1");
            return new RealtimeVoiceCatalog
            {
                Voices = voices,
                DefaultVoice = voices.Contains(defaultVoice, StringComparer.Ordinal) ? defaultVoice : null
            };
        }

        public async Task StartAsync(RealtimeVoiceStart request, CancellationToken token)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ThreadId) ||
                string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.OfferSdp) ||
                string.IsNullOrWhiteSpace(request.Voice)) throw new ArgumentException(nameof(request));
            await RequireChatGptAsync(token).ConfigureAwait(false);
            if (!request.IsPreview) await UpdateBackendAsync(request, token).ConfigureAwait(false);
            var initialItems = request.IsPreview
                ? new List<Dictionary<string, object>>()
                : AppServerVoiceContext.BuildInitialItems(
                    await AppServerVoiceContext.ReadAsync(_send, request.ThreadId, null, token).ConfigureAwait(false));
            var parameters = new Dictionary<string, object>
            {
                { "threadId", request.ThreadId }, { "realtimeSessionId", request.SessionId },
                { "version", "v3" }, { "outputModality", "audio" }, { "voice", request.Voice },
                { "transport", new Dictionary<string, object> { { "type", "webrtc" }, { "sdp", request.OfferSdp } } }
            };
            // The official prompt and speaking pace are unchanged at the default speed.
            if (request.FasterSpeech)
                initialItems.Add(new Dictionary<string, object>
                {
                    { "role", "developer" },
                    { "text", "Speak faster than your default speaking pace while remaining clear and natural. Keep the same voice and pitch. Do not shorten answers merely to sound faster." }
                });
            if (request.IsPreview)
            {
                parameters["includeStartupContext"] = false;
                parameters["clientManagedHandoffs"] = true;
                initialItems.Add(new Dictionary<string, object>
                {
                    { "role", "developer" },
                    { "text", "This is only a short voice preview. Say this sample once, then remain silent. Do not delegate or perform tasks: " + request.PreviewText }
                });
            }
            if (initialItems.Count > 0) parameters["initialItems"] = initialItems.ToArray();
            await _send("thread/realtime/start", parameters, token, 30000).ConfigureAwait(false);
        }

        public async Task<string> CreatePreviewThreadAsync(string workspacePath, CancellationToken token)
        {
            await RequireChatGptAsync(token).ConfigureAwait(false);
            var result = await _send("thread/start", new Dictionary<string, object>
            {
                { "cwd", workspacePath }, { "ephemeral", true }, { "sandbox", "read-only" }, { "approvalPolicy", "never" },
                { "environments", new object[0] }, { "config", new Dictionary<string, object> { { "features.hooks", false } } },
                { "baseInstructions", "This is a temporary voice preview. Never perform work, inspect files, run commands or call tools. No user conversation belongs here." }
            }, token, 30000).ConfigureAwait(false);
            var id = AppServerJsonValueReader.GetString(AppServerJsonValueReader.GetDictionary(result, "thread"), "id");
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException(SharedLocalization.Text("VoiceChat.Error.PreviewThread", "The voice preview could not be prepared."));
            return id;
        }

        public Task AppendSpeechAsync(string threadId, string text, CancellationToken token)
        {
            return _send("thread/realtime/appendSpeech", new Dictionary<string, object> { { "threadId", threadId }, { "text", text } }, token, 10000);
        }

        public Task AppendContextAsync(string threadId, string text, CancellationToken token)
        {
            var context = "This request was typed into the existing text composer and is already being handled by the attached Codex agent. Wait for its result, then answer aloud. Do not delegate the same request again. Request: " +
                AppServerVoiceContext.TakeTail(text, AppServerVoiceContext.MaximumContextBytes);
            return _send("thread/realtime/appendText", new Dictionary<string, object>
            {
                { "threadId", threadId }, { "role", "user" }, { "text", context }
            }, token, 10000);
        }

        public Task UpdateBackendAsync(RealtimeVoiceStart request, CancellationToken token)
        {
            if (request?.BackendMode == null) return Task.CompletedTask;
            if (string.IsNullOrWhiteSpace(request.ThreadId) || string.IsNullOrWhiteSpace(request.BackendMode.ModelId) || string.IsNullOrWhiteSpace(request.BackendMode.Mode))
                throw new ArgumentException(nameof(request));
            var parameters = new Dictionary<string, object> { { "threadId", request.ThreadId } };
            AppServerCollaborationParameters.AddTo(parameters, request.BackendMode);
            return _send("thread/settings/update", parameters, token, 30000);
        }

        private async Task RequireChatGptAsync(CancellationToken token)
        {
            var result = await _send("account/read", new Dictionary<string, object> { { "refreshToken", false } }, token, 30000).ConfigureAwait(false);
            var type = AppServerJsonValueReader.GetString(AppServerJsonValueReader.GetDictionary(result, "account"), "type");
            if (!string.Equals(type, "chatgpt", StringComparison.Ordinal))
                throw new InvalidOperationException(SharedLocalization.Text("VoiceChat.Error.ChatGptRequired", "Sign in with ChatGPT to use official voice chat."));
        }

        public Task StopAsync(string threadId, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(threadId)) throw new ArgumentException(nameof(threadId));
            return _send("thread/realtime/stop", new Dictionary<string, object> { { "threadId", threadId } }, token, 6000);
        }

        public static RealtimeVoiceEvent MapNotification(string method, IDictionary<string, object> parameters)
        {
            if (method == "thread/realtime/item/completed")
            {
                var item = AppServerJsonValueReader.GetDictionary(parameters, "item");
                if (AppServerJsonValueReader.GetString(item, "type") != "transcriptSegment") return null;
                return new RealtimeVoiceEvent
                {
                    Kind = RealtimeVoiceEventKind.Transcript,
                    ThreadId = AppServerJsonValueReader.GetString(parameters, "threadId"),
                    SessionId = AppServerJsonValueReader.GetString(item, "realtimeSessionId"),
                    ItemId = AppServerJsonValueReader.GetString(item, "id"),
                    Role = AppServerJsonValueReader.GetString(item, "role"),
                    Text = AppServerJsonValueReader.GetString(item, "text")
                };
            }
            RealtimeVoiceEventKind kind;
            switch (method)
            {
                case "thread/realtime/started": kind = RealtimeVoiceEventKind.Started; break;
                case "thread/realtime/sdp": kind = RealtimeVoiceEventKind.Sdp; break;
                case "thread/realtime/error": kind = RealtimeVoiceEventKind.Error; break;
                case "thread/realtime/closed": kind = RealtimeVoiceEventKind.Closed; break;
                default: return null;
            }
            return new RealtimeVoiceEvent
            {
                Kind = kind,
                ThreadId = AppServerJsonValueReader.GetString(parameters, "threadId"),
                SessionId = AppServerJsonValueReader.GetString(parameters, "realtimeSessionId"),
                Sdp = AppServerJsonValueReader.GetString(parameters, "sdp"),
                Role = AppServerJsonValueReader.GetString(parameters, "role"),
                Text = AppServerJsonValueReader.GetString(parameters, kind == RealtimeVoiceEventKind.Error ? "message" :
                    kind == RealtimeVoiceEventKind.Closed ? "reason" : "text")
            };
        }
    }
}
