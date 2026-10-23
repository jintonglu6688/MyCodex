using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RealtimeVoiceTests
    {
        [Test]
        public async Task OfficialDefault_DoesNotSendSpeedOrPromptOverrides()
        {
            IDictionary<string, object> sent = null;
            var operations = Operations((method, values) => { if (method == "thread/realtime/start") sent = values; });
            await operations.StartAsync(Request(), CancellationToken.None);
            Assert.That(sent["version"], Is.EqualTo("v3"));
            Assert.That(sent["voice"], Is.EqualTo("cove"));
            Assert.That(sent.ContainsKey("prompt"), Is.False);
            Assert.That(sent.ContainsKey("initialItems"), Is.False);
            Assert.That(sent.ContainsKey("speed"), Is.False);
            Assert.That(sent.ContainsKey("model"), Is.False);
            Assert.That(((IDictionary<string, object>)sent["transport"])["type"], Is.EqualTo("webrtc"));
        }

        [Test]
        public async Task FasterSpeech_IsOnlyRequestedWhenExplicitlyEnabled()
        {
            IDictionary<string, object> sent = null;
            var operations = Operations((method, values) => { if (method == "thread/realtime/start") sent = values; });
            var request = Request();
            request.FasterSpeech = true;
            await operations.StartAsync(request, CancellationToken.None);
            var item = ((IDictionary<string, object>[])sent["initialItems"])[0];
            Assert.That(item["role"], Is.EqualTo("developer"));
            Assert.That((string)item["text"], Does.Contain("faster than your default"));
            Assert.That(sent.ContainsKey("prompt"), Is.False, "Speed must not replace the official conversation instructions.");
        }

        [Test]
        public void ApiKeyAccount_IsNotSilentlyUsedForSubscriptionVoice()
        {
            var methods = new List<string>();
            var operations = Operations((method, values) => methods.Add(method), "apiKey");
            Assert.ThrowsAsync<InvalidOperationException>(async () => await operations.StartAsync(Request(), CancellationToken.None));
            Assert.That(methods, Is.EqualTo(new[] { "account/read" }));
        }

        [Test]
        public async Task VoiceChoicesAndDefault_ComeFromTheRuntimeCatalog()
        {
            var operations = new AppServerRealtimeOperations((method, args, token, timeout) => Task.FromResult<IDictionary<string, object>>(
                new Dictionary<string, object> { { "account", new Dictionary<string, object> { { "type", "chatgpt" } } }, { "voices", new Dictionary<string, object>
                { { "v3", new object[] { "juniper", "cove" } }, { "v2", new object[] { "marin" } }, { "defaultV1", "cove" } } } }));
            var catalog = await operations.ListVoicesAsync(CancellationToken.None);
            Assert.That(catalog.Voices, Is.EqualTo(new[] { "juniper", "cove" }));
            Assert.That(catalog.DefaultVoice, Is.EqualTo("cove"));
        }

        [Test]
        public void FinalTranscript_PreservesItsActualSpeakerAndText()
        {
            var mapped = AppServerRealtimeOperations.MapNotification("thread/realtime/item/completed", new Dictionary<string, object>
                { { "threadId", "thread" }, { "item", new Dictionary<string, object>
                    { { "type", "transcriptSegment" }, { "id", "canonical-voice-id" }, { "realtimeSessionId", "voice-session" }, { "role", "user" }, { "text", "你好" } } } });
            Assert.That(mapped.Role, Is.EqualTo("user"));
            Assert.That(mapped.Text, Is.EqualTo("你好"));
            Assert.That(mapped.ItemId, Is.EqualTo("canonical-voice-id"));
            Assert.That(AppServerRealtimeOperations.MapNotification("thread/realtime/transcript/done", null), Is.Null, "Raw transcripts must not duplicate canonical history.");
            Assert.That(AppServerRealtimeOperations.MapNotification("thread/realtime/outputAudio/delta", null), Is.Null);
        }

        [Test]
        public async Task CachedPreview_PlaysWithoutStartingAnotherServerSession()
        {
            var client = new VoiceClient();
            var audio = new Audio { CanReplayPreview = true };
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                var request = Request();
                request.IsPreview = true;
                request.OutputLevel = VoiceOutputLevel.StrongBoost;
                await session.StartAsync(request);
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active));
                Assert.That(client.PreviewThreads, Is.Zero);
                Assert.That(client.Starts, Is.Zero);
                Assert.That(audio.OutputLevel, Is.EqualTo(VoiceOutputLevel.StrongBoost));
                audio.FinishPreview();
                await session.StopAsync();
                Assert.That(client.Stops, Is.Zero);
            }
        }

        [Test]
        public async Task Preview_PreparesAudioWhileItsServerThreadIsStillLoading()
        {
            var pending = new TaskCompletionSource<string>();
            var client = new VoiceClient { PendingPreviewCreation = pending };
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                var request = Request();
                request.IsPreview = true;
                var start = session.StartAsync(request);
                try { Assert.That(audio.Preview, Is.True, "Independent local audio preparation must not wait for the network thread creation."); }
                finally { pending.TrySetResult("preview-thread"); await start; await session.StopAsync(); }
            }
        }

        [Test]
        public async Task StartAndStop_ConnectMediaAndReleaseItExactlyOnce()
        {
            var client = new VoiceClient();
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                await session.StartAsync(Request());
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active));
                Assert.That(audio.Answer, Is.EqualTo("answer"));
                await session.StopAsync();
                await session.StopAsync();
                Assert.That(audio.Stops, Is.EqualTo(1));
                Assert.That(client.Stops, Is.EqualTo(1));
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Idle));
            }
            Assert.That(audio.Disposed, Is.True);
        }

        [Test]
        public async Task StopWhileConnecting_CannotBeUndoneByALateAnswer()
        {
            var client = new VoiceClient();
            var audio = new Audio { PendingConnect = new TaskCompletionSource<bool>() };
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                var start = session.StartAsync(Request());
                await session.StopAsync();
                audio.PendingConnect.SetResult(true);
                await start;
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Idle));
                Assert.That(audio.Stops, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ForeignThreadOrOldSessionEvents_CannotConnectOrStopCurrentVoice()
        {
            var client = new VoiceClient { AutoAnswer = false };
            using (var session = new RealtimeVoiceSession(client, new Audio()))
            {
                var start = session.StartAsync(Request());
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Error, ThreadId = "other", Text = "error" });
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Started, ThreadId = "thread", SessionId = "old" });
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Sdp, ThreadId = "thread", Sdp = "wrong" });
                Assert.That(start.IsCompleted, Is.False);
                client.Answer();
                await start;
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active));
                await session.StopAsync();
            }
        }

        [Test]
        public async Task MutedRestart_KeepsTheMicrophoneMutedBeforeConnection()
        {
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(new VoiceClient(), audio))
            {
                var request = Request();
                request.MicrophoneMuted = true;
                await session.StartAsync(request);
                Assert.That(audio.PreparedMuted, Is.True);
                await session.SetMutedAsync(false);
                Assert.That(audio.Muted, Is.False);
                await session.StopAsync();
            }
        }

        [Test]
        public async Task RuntimeDisconnect_ClosesAudioAndAllowsAnExplicitNewStart()
        {
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(new VoiceClient(), audio))
            {
                await session.StartAsync(Request());
                await session.ConnectionLostAsync("Disconnected");
                Assert.That(audio.Stops, Is.EqualTo(1));
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Failed));
                await session.StartAsync(Request());
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active));
                await session.StopAsync();
            }
        }

        [Test]
        public async Task ServerErrorDuringStartup_ReleasesAudioAndReportsFailure()
        {
            var client = new VoiceClient { AutoAnswer = false };
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                var start = session.StartAsync(Request());
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Error, ThreadId = "thread", Text = "Voice unavailable" });
                await start;
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Failed));
                Assert.That(audio.Stops, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ASecondStart_DoesNotCreateConcurrentCalls()
        {
            var client = new VoiceClient();
            using (var session = new RealtimeVoiceSession(client, new Audio()))
            {
                await session.StartAsync(Request());
                Assert.ThrowsAsync<InvalidOperationException>(async () => await session.StartAsync(Request()));
                Assert.That(client.Starts, Is.EqualTo(1));
                await session.StopAsync();
            }
        }

        [Test]
        public async Task FailingUiObserver_CannotPreventMicrophoneCleanup()
        {
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(new VoiceClient(), audio))
            {
                session.StateChanged += (sender, args) => { throw new InvalidOperationException("Observer failure"); };
                await session.StartAsync(Request());
                await session.StopAsync();
                Assert.That(audio.Stops, Is.EqualTo(1));
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Idle));
            }
        }

        [Test]
        public void SavedVoiceSettings_RoundTripWithoutChangingDefaultSpeed()
        {
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "voice-settings-" + Guid.NewGuid().ToString("N"));
            var store = new JsonAppConfigStore(new AppConfigService(directory));
            var config = store.Load();
            Assert.That(config.VoiceChat, Is.Null);
            config.VoiceChat = new VoiceChatSettings { Voice = "cove" };
            store.Save(config);
            var loaded = store.Load();
            Assert.That(loaded.VoiceChat.Voice, Is.EqualTo("cove"));
            Assert.That(loaded.VoiceChat.FasterSpeech, Is.False);
            Assert.That(loaded.VoiceChat.OutputLevel, Is.Null, "Existing settings must keep the original output level.");
            loaded.VoiceChat.OutputLevel = VoiceOutputLevel.StrongBoost;
            store.Save(loaded);
            Assert.That(store.Load().VoiceChat.OutputLevel, Is.EqualTo(VoiceOutputLevel.StrongBoost));
        }

        [Test]
        public async Task VoiceContext_UsesOneBoundedMixedTimelinePageForTheExactThread()
        {
            var requests = new List<string>();
            IDictionary<string, object> parameters = null;
            var page = await AppServerVoiceContext.ReadAsync((method, args, token, timeout) =>
            {
                requests.Add(method);
                parameters = (IDictionary<string, object>)args;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "data", new object[]
                        {
                            new Dictionary<string, object> { { "type", "item" }, { "item", new Dictionary<string, object>
                                { { "type", "userMessage" }, { "id", "text-1" }, { "content", new object[]
                                    { new Dictionary<string, object> { { "type", "text" }, { "text", "继续刚才的项目" } } } } } } },
                            new Dictionary<string, object> { { "type", "realtime" }, { "item", new Dictionary<string, object>
                                { { "type", "transcriptSegment" }, { "id", "voice-1" }, { "role", "assistant" }, { "text", "正在继续。" } } } }
                        }
                    },
                    { "nextCursor", "older-page" }
                });
            }, "exact-thread", null, CancellationToken.None);
            Assert.That(requests, Is.EqualTo(new[] { "thread/timeline/list" }));
            Assert.That(parameters["threadId"], Is.EqualTo("exact-thread"));
            Assert.That(parameters["limit"], Is.EqualTo(64));
            Assert.That(page.Items.Select(item => item.Role), Is.EqualTo(new[] { "user", "assistant" }));
            Assert.That(page.Items[1].IsVoice, Is.True);
            Assert.That(page.NextCursor, Is.EqualTo("older-page"));
            var initial = AppServerVoiceContext.BuildInitialItems(page);
            Assert.That(initial[1]["text"], Is.EqualTo("继续刚才的项目"));
            Assert.That(initial[2]["text"], Is.EqualTo("正在继续。"));
        }

        [Test]
        public async Task PreviewThread_IsEphemeralReadOnlyAndDoesNotLoadConversationHistory()
        {
            var calls = new List<string>();
            IDictionary<string, object> create = null;
            IDictionary<string, object> start = null;
            var operations = new AppServerRealtimeOperations((method, args, token, timeout) =>
            {
                calls.Add(method);
                if (method == "thread/start") create = (IDictionary<string, object>)args;
                if (method == "thread/realtime/start") start = (IDictionary<string, object>)args;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "account", new Dictionary<string, object> { { "type", "chatgpt" } } },
                    { "thread", new Dictionary<string, object> { { "id", "preview" } } }
                });
            });
            var id = await operations.CreatePreviewThreadAsync(@"D:\workspace", CancellationToken.None);
            var request = Request();
            request.ThreadId = id;
            request.IsPreview = true;
            request.PreviewText = "你好，这是试听。";
            await operations.StartAsync(request, CancellationToken.None);
            Assert.That(create["ephemeral"], Is.True);
            Assert.That(create["approvalPolicy"], Is.EqualTo("never"));
            Assert.That(start["includeStartupContext"], Is.False);
            Assert.That(calls, Does.Not.Contain("thread/timeline/list"));
        }

        [Test]
        public async Task CancellingPreviewBeforeItsThreadExists_ReleasesTheLateThread()
        {
            var client = new VoiceClient { PendingPreviewCreation = new TaskCompletionSource<string>() };
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                var request = Request();
                request.ThreadId = null;
                request.IsPreview = true;
                request.PreviewText = "preview";
                var start = session.StartAsync(request);
                await session.StopAsync();
                client.PendingPreviewCreation.SetResult("late-preview");
                await start;
                Assert.That(client.Starts, Is.Zero);
                Assert.That(client.ReleasedPreviewThreads, Is.EqualTo(1));
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Idle));
            }
        }

        [Test]
        public void RecentVoiceContext_IsBoundedAndDoesNotBreakUnicode()
        {
            var text = string.Concat(Enumerable.Repeat("中😀", 20000));
            var tail = AppServerVoiceContext.TakeTail(text, 31);
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(tail), Is.LessThanOrEqualTo(31));
            Assert.That(char.IsLowSurrogate(tail[0]), Is.False);
            Assert.That(tail.EndsWith("中😀", StringComparison.Ordinal), Is.True);
        }

        private static RealtimeVoiceStart Request()
        {
            return new RealtimeVoiceStart { ThreadId = "thread", SessionId = "session", OfferSdp = "offer", Voice = "cove" };
        }

        private static AppServerRealtimeOperations Operations(Action<string, IDictionary<string, object>> observe, string account = "chatgpt")
        {
            return new AppServerRealtimeOperations((method, args, token, timeout) =>
            {
                observe(method, (IDictionary<string, object>)args);
                if (method == "thread/timeline/list")
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "data", new object[0] }, { "nextCursor", null } });
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    { { "account", new Dictionary<string, object> { { "type", account } } } });
            });
        }

        internal sealed class VoiceClient : IAppServerRealtimeClient
        {
            public event EventHandler<RealtimeVoiceEvent> RealtimeVoiceEventReceived;
            public event EventHandler<RealtimeVoiceInputEvent> RealtimeTextInputSubmitted;
            public string AppendedContext;
            public RealtimeVoiceStart UpdatedBackend;
            public TaskCompletionSource<bool> PendingBackendUpdate;
            public Task UpdateRealtimeBackendAsync(RealtimeVoiceStart request, CancellationToken token) { UpdatedBackend = request; return PendingBackendUpdate?.Task ?? Task.CompletedTask; }
            public void EmitText(string threadId, string text) { RealtimeTextInputSubmitted?.Invoke(this, new RealtimeVoiceInputEvent { ThreadId = threadId, Text = text }); }
            public RealtimeVoiceStart LastRequest;
            public bool AutoAnswer = true;
            public int Starts;
            public int Stops;
            public int PreviewThreads;
            public int ReleasedPreviewThreads;
            public string SpokenPreview;
            public TaskCompletionSource<string> PendingPreviewCreation;
            public void Emit(RealtimeVoiceEvent value) { RealtimeVoiceEventReceived?.Invoke(this, value); }
            public void Answer()
            {
                Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Started, ThreadId = LastRequest.ThreadId, SessionId = LastRequest.SessionId });
                Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Sdp, ThreadId = LastRequest.ThreadId, Sdp = "answer" });
            }
            public Task<RealtimeVoiceCatalog> ListRealtimeVoicesAsync(CancellationToken token)
            {
                return Task.FromResult(new RealtimeVoiceCatalog { Voices = new[] { "cove", "juniper" }, DefaultVoice = "cove" });
            }
            public Task StartRealtimeVoiceAsync(RealtimeVoiceStart request, CancellationToken token)
            {
                LastRequest = request;
                Starts++;
                if (AutoAnswer) Answer();
                return Task.CompletedTask;
            }
            public Task StopRealtimeVoiceAsync(string threadId, CancellationToken token)
            {
                Stops++;
                Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Closed, ThreadId = threadId });
                return Task.CompletedTask;
            }
            public Task<string> CreateVoicePreviewThreadAsync(string workspacePath, CancellationToken token)
            {
                PreviewThreads++;
                return PendingPreviewCreation?.Task ?? Task.FromResult("preview-thread");
            }
            public Task ReleaseVoicePreviewThreadAsync(string threadId, CancellationToken token) { ReleasedPreviewThreads++; return Task.CompletedTask; }
            public Task AppendRealtimeSpeechAsync(string threadId, string text, CancellationToken token) { SpokenPreview = text; return Task.CompletedTask; }
            public Task AppendRealtimeContextAsync(string threadId, string text, CancellationToken token) { AppendedContext = text; return Task.CompletedTask; }
        }

        internal sealed class Audio : IRealtimeVoiceAudio
        {
            public bool CanReplayPreview;
            public bool? SystemEchoCancellationEnabled => true;
            public event EventHandler<VoiceAudioEvent> Failed { add { } remove { } }
            public event EventHandler<VoiceAudioEvent> PreviewCompleted;
            public TaskCompletionSource<bool> PendingConnect;
            public TaskCompletionSource<bool> PendingMute;
            public string Answer;
            public int Stops;
            public bool PreparedMuted;
            public bool Muted;
            public bool PlaybackMuted;
            public Action OnMuting;
            public bool Disposed;
            public bool Preview;
            public VoiceOutputLevel OutputLevel;
            public string SessionId;
            public Task<bool> TryReplayPreviewAsync(string id, VoiceAudioOptions options, CancellationToken token)
            {
                if (CanReplayPreview) { SessionId = id; Preview = true; OutputLevel = options.OutputLevel; }
                return Task.FromResult(CanReplayPreview);
            }
            public Task<string> PrepareAsync(string id, VoiceAudioOptions options, CancellationToken token)
            {
                SessionId = id; PreparedMuted = options.Muted; Preview = options.Preview; OutputLevel = options.OutputLevel;
                return Task.FromResult("offer");
            }
            public Task ConnectAsync(string id, string answer, CancellationToken token) { Answer = answer; return PendingConnect?.Task ?? Task.CompletedTask; }
            public Task SetMutedAsync(string id, bool muted) { Muted = muted; OnMuting?.Invoke(); return PendingMute?.Task ?? Task.CompletedTask; }
            public Task SetPlaybackMutedAsync(string id, bool muted) { PlaybackMuted = muted; return Task.CompletedTask; }
            public Task SetOutputLevelAsync(string id, VoiceOutputLevel level) { OutputLevel = level; return Task.CompletedTask; }
            public void FinishPreview() { PreviewCompleted?.Invoke(this, new VoiceAudioEvent { SessionId = SessionId }); }
            public Task StopAsync(string id) { Stops++; return Task.CompletedTask; }
            public void Dispose() { Disposed = true; }
        }
    }
}
