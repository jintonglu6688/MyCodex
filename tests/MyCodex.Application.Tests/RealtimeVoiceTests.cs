using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RealtimeVoiceTests
    {
        [Test]
        public async Task Start_SyncsBackendThenStartsV3WebRtcWithRecentContext()
        {
            var calls = new List<KeyValuePair<string, IDictionary<string, object>>>();
            var operations = Operations(calls, timeline: Timeline(
                Entry(2, "item", "agentMessage", text: "second"),
                Entry(1, "item", "userMessage", content: "first"),
                Entry(3, "realtime", "transcriptSegment", role: "user", text: "spoken"),
                Entry(4, "item", "reasoning", text: "hidden")));
            var request = Request();
            request.BackendMode = new AppServerCollaborationMode { Mode = "default", ModelId = "gpt-5.5" };

            await operations.StartAsync(request, CancellationToken.None);

            Assert.That(calls.Select(call => call.Key), Is.EqualTo(new[]
            {
                "account/read", "thread/settings/update", "thread/timeline/list", "thread/realtime/start"
            }));
            Assert.That(calls[1].Value.ContainsKey("collaborationMode"), Is.True);
            var start = calls[3].Value;
            Assert.That(start["version"], Is.EqualTo("v3"));
            Assert.That(start["outputModality"], Is.EqualTo("audio"));
            Assert.That(start["voice"], Is.EqualTo("cove"));
            Assert.That(start["realtimeSessionId"], Is.EqualTo("session"));
            var transport = (IDictionary<string, object>)start["transport"];
            Assert.That(transport["type"], Is.EqualTo("webrtc"));
            Assert.That(transport["sdp"], Is.EqualTo("offer"));
            var items = (Dictionary<string, object>[])start["initialItems"];
            Assert.That(items.Select(item => item["role"]), Is.EqualTo(new[] { "developer", "user", "assistant", "user" }));
            Assert.That(items.Skip(1).Select(item => item["text"]), Is.EqualTo(new[] { "first", "second", "spoken" }));
        }

        [Test]
        public async Task Start_WithoutBackendModeOrReadableTimeline_StillStartsVoice()
        {
            var calls = new List<KeyValuePair<string, IDictionary<string, object>>>();
            var operations = Operations(calls, timelineFailure: new InvalidOperationException("timeline unavailable"));

            await operations.StartAsync(Request(), CancellationToken.None);

            Assert.That(calls.Select(call => call.Key), Is.EqualTo(new[] { "account/read", "thread/timeline/list", "thread/realtime/start" }));
            Assert.That(calls[2].Value.ContainsKey("initialItems"), Is.False);
        }

        [Test]
        public void Start_WithApiKeyAccount_IsRefusedBeforeAnyVoiceRequest()
        {
            var calls = new List<KeyValuePair<string, IDictionary<string, object>>>();
            var operations = Operations(calls, accountType: "apiKey");

            Assert.ThrowsAsync<InvalidOperationException>(async () => await operations.StartAsync(Request(), CancellationToken.None));
            Assert.That(calls.Select(call => call.Key), Is.EqualTo(new[] { "account/read" }));
        }

        [TestCase("cove", "cove")]
        [TestCase("alloy", "juniper")]
        public async Task ListVoices_UsesV3CatalogAndFallsBackToItsFirstVoice(string defaultV1, string expected)
        {
            var operations = new AppServerRealtimeOperations((method, args, token, timeout) => Task.FromResult<IDictionary<string, object>>(
                new Dictionary<string, object>
                {
                    { "account", new Dictionary<string, object> { { "type", "chatgpt" } } },
                    { "voices", new Dictionary<string, object> { { "v3", new object[] { "juniper", "cove" } }, { "v1", new object[] { "alloy" } }, { "defaultV1", defaultV1 } } }
                }));

            var catalog = await operations.ListVoicesAsync(CancellationToken.None);

            Assert.That(catalog.Voices, Is.EqualTo(new[] { "juniper", "cove" }));
            Assert.That(catalog.DefaultVoice, Is.EqualTo(expected));
        }

        [Test]
        public void MapNotification_MapsLifecycleAndFinalTranscriptsOnly()
        {
            var sdp = AppServerRealtimeOperations.MapNotification("thread/realtime/sdp", new Dictionary<string, object> { { "threadId", "t" }, { "sdp", "answer" } });
            var started = AppServerRealtimeOperations.MapNotification("thread/realtime/started", new Dictionary<string, object> { { "threadId", "t" }, { "realtimeSessionId", "s" } });
            var error = AppServerRealtimeOperations.MapNotification("thread/realtime/error", new Dictionary<string, object> { { "threadId", "t" }, { "message", "boom" } });
            var transcript = AppServerRealtimeOperations.MapNotification("thread/realtime/item/completed", new Dictionary<string, object>
            {
                { "threadId", "t" },
                { "item", new Dictionary<string, object> { { "type", "transcriptSegment" }, { "id", "i" }, { "realtimeSessionId", "s" }, { "role", "user" }, { "text", "你好" } } }
            });

            Assert.That(sdp.Kind, Is.EqualTo(RealtimeVoiceEventKind.Sdp));
            Assert.That(sdp.Sdp, Is.EqualTo("answer"));
            Assert.That(started.SessionId, Is.EqualTo("s"));
            Assert.That(error.Text, Is.EqualTo("boom"));
            Assert.That(transcript.Kind, Is.EqualTo(RealtimeVoiceEventKind.Transcript));
            Assert.That(transcript.Text, Is.EqualTo("你好"));
            Assert.That(transcript.ItemId, Is.EqualTo("i"));
            Assert.That(AppServerRealtimeOperations.MapNotification("thread/realtime/item/completed",
                new Dictionary<string, object> { { "item", new Dictionary<string, object> { { "type", "handoff" } } } }), Is.Null);
            Assert.That(AppServerRealtimeOperations.MapNotification("thread/realtime/outputAudio/delta", null), Is.Null);
        }

        [Test]
        public void InitialItems_KeepTheNewestMessagesWithinTheByteBudget()
        {
            var entries = Enumerable.Range(1, 40)
                .Select(i => Entry(i, "item", "agentMessage", text: new string('x', 1000) + i))
                .ToArray();

            var items = AppServerVoiceContext.BuildInitialItems(Timeline(entries));

            Assert.That(items.Count - 1, Is.LessThanOrEqualTo(AppServerVoiceContext.MaximumContextItems));
            Assert.That(items.Skip(1).Sum(item => System.Text.Encoding.UTF8.GetByteCount((string)item["text"])),
                Is.LessThanOrEqualTo(AppServerVoiceContext.MaximumContextBytes));
            Assert.That((string)items.Last()["text"], Does.EndWith("40"));
            Assert.That(AppServerVoiceContext.BuildInitialItems(Timeline()), Is.Empty);
        }

        [Test]
        public void UserMessageText_ShowsOnlyTheRequestOfAVoiceDelegation()
        {
            bool delegation;
            var wrapped = Entry(1, "item", "userMessage",
                content: "<realtime_delegation>\n  <input>查一下 a &lt; b 的测试</input>\n  <transcript_delta>…</transcript_delta>\n</realtime_delegation>");
            var plain = Entry(2, "item", "userMessage", content: "<input>not a delegation</input>");
            var empty = Entry(3, "item", "userMessage", content: "<realtime_delegation><source>transcript_tail_flush</source></realtime_delegation>");

            Assert.That(AppServerThreadMapper.ExtractUserMessageText((IDictionary<string, object>)wrapped["item"], out delegation), Is.EqualTo("查一下 a < b 的测试"));
            Assert.That(delegation, Is.True);
            Assert.That(AppServerThreadMapper.ExtractUserMessageText((IDictionary<string, object>)plain["item"], out delegation), Is.EqualTo("<input>not a delegation</input>"));
            Assert.That(delegation, Is.False);
            Assert.That(AppServerThreadMapper.ExtractUserMessageText((IDictionary<string, object>)empty["item"]), Is.Null);
        }

        [Test]
        public void VoiceHistory_PutsSpokenTurnsBeforeTheirNextTypedMessageAndDropsRepeatedRequests()
        {
            var details = new AppServerThreadDetails();
            details.Messages.Add(new AppServerThreadMessage { SourceItemId = "item-1", Sender = "User", Content = "typed", TurnIndex = 1 });
            details.Messages.Add(new AppServerThreadMessage { SourceItemId = "item-4", Sender = "User", Content = "run  the tests", IsVoiceDelegation = true, TurnIndex = 2 });
            details.Messages.Add(new AppServerThreadMessage { SourceItemId = "item-5", Sender = "Codex", Content = "done", TurnIndex = 2 });
            details.Messages.Add(new AppServerThreadMessage { SourceItemId = "plan-without-timeline-entry", Sender = "Codex", Content = "plan", TurnIndex = 2 });

            AppServerVoiceHistory.Merge("t", details, new[]
            {
                Entry(6, "realtime", "transcriptSegment", "assistant", "all green"),
                Entry(5, "item", "agentMessage", text: "done"),
                Entry(1, "item", "userMessage", content: "typed"),
                Entry(2, "realtime", "transcriptSegment", "assistant", "hello"),
                Entry(3, "realtime", "transcriptSegment", "user", "Run the tests"),
                Entry(4, "item", "userMessage", content: "run the tests"),
                Entry(7, "realtime", "realtimeSessionClosed"),
                Entry(8, "realtime", "transcriptSegment", "system", "ignored")
            });

            Assert.That(details.Messages.Select(message => message.Sender + ":" + message.Content), Is.EqualTo(new[]
            {
                "User:typed", "Codex:hello", "User:Run the tests", "User:run  the tests", "Codex:done", "Codex:plan", "Codex:all green"
            }), "only an identical spoken request hides the delegated one");
            Assert.That(details.Messages.Where(message => message.Content == "hello").Single().TurnIndex, Is.Zero, "spoken turns are not rewind anchors");

            var repeated = new AppServerThreadDetails();
            repeated.Messages.Add(new AppServerThreadMessage { SourceItemId = "item-4", Sender = "User", Content = "run  the tests", IsVoiceDelegation = true });
            AppServerVoiceHistory.Merge("t", repeated, new[]
            {
                Entry(3, "realtime", "transcriptSegment", "user", "run the tests"),
                Entry(4, "item", "userMessage", content: "run the tests")
            });
            Assert.That(repeated.Messages.Select(message => message.Content), Is.EqualTo(new[] { "run the tests" }));
        }

        [Test]
        public async Task VoiceHistory_ReadsEveryTimelinePageAndKeepsHistoryWhenTheReadFails()
        {
            var cursors = new List<object>();
            var details = new AppServerThreadDetails();
            details.Messages.Add(new AppServerThreadMessage { SourceItemId = "item-2", Sender = "User", Content = "typed" });
            AppServerRequestInvoker send = (method, parameters, token, timeout) =>
            {
                var cursor = ((IDictionary<string, object>)parameters).TryGetValue("cursor", out var value) ? value : null;
                cursors.Add(cursor);
                var page = cursor == null
                    ? new Dictionary<string, object> { { "data", new object[] { Entry(2, "item", "userMessage", content: "typed") } }, { "nextCursor", "older" } }
                    : new Dictionary<string, object> { { "data", new object[] { Entry(1, "realtime", "transcriptSegment", "user", "spoken") } } };
                return Task.FromResult<IDictionary<string, object>>(page);
            };

            await AppServerVoiceHistory.MergeAsync(send, "t", details, CancellationToken.None);

            Assert.That(cursors, Is.EqualTo(new object[] { null, "older" }));
            Assert.That(details.Messages.Select(message => message.Content), Is.EqualTo(new[] { "spoken", "typed" }));

            await AppServerVoiceHistory.MergeAsync((method, parameters, token, timeout) => throw new TimeoutException(), "t", details, CancellationToken.None);
            Assert.That(details.Messages.Select(message => message.Content), Is.EqualTo(new[] { "spoken", "typed" }));
        }

        [Test]
        public void TakeTail_NeverSplitsASurrogatePair()
        {
            Assert.That(AppServerVoiceContext.TakeTail("a😀", 4), Is.EqualTo("😀"));
            Assert.That(AppServerVoiceContext.TakeTail("a😀", 3), Is.Empty);
        }

        [Test]
        public async Task Session_StartAndStop_ConnectsThenReleasesAudioAndServerOnce()
        {
            var client = new VoiceClient();
            var audio = new Audio();
            var states = new List<VoiceChatState>();
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                session.StateChanged += (sender, e) => states.Add(e.State);
                await session.StartAsync(Request());
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active));
                Assert.That(audio.Answer, Is.EqualTo("answer"));
                Assert.That(client.LastRequest.OfferSdp, Is.EqualTo("offer"));

                await session.StopAsync();
                await session.StopAsync();
                Assert.That(audio.Stops, Is.EqualTo(1));
                Assert.That(client.Stops, Is.EqualTo(1));
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Idle));
            }

            Assert.That(states, Is.EqualTo(new[] { VoiceChatState.Connecting, VoiceChatState.Active, VoiceChatState.Stopping, VoiceChatState.Idle }));
            Assert.That(audio.Disposed, Is.True);
        }

        [Test]
        public async Task Session_IgnoresEventsFromOtherThreadsAndSessions()
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
        public async Task Session_ServerErrorOrAudioFailure_StopsWithTheReason()
        {
            var client = new VoiceClient();
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                string error = null;
                session.StateChanged += (sender, e) => error = e.Error ?? error;
                await session.StartAsync(Request());
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Error, ThreadId = "thread", Text = "server failed" });
                await session.StopAsync();
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Failed));
                Assert.That(error, Is.EqualTo("server failed"));

                await session.StartAsync(Request());
                audio.Fail(client.LastRequest.SessionId, "mic lost");
                await session.StopAsync();
                Assert.That(error, Is.EqualTo("mic lost"));
                Assert.That(audio.Stops, Is.EqualTo(2));
            }
        }

        [Test]
        public async Task Session_StopWhileConnecting_IsNotUndoneByALateAnswer()
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
        public async Task Session_ServerClose_EndsVoiceAndAllowsANewStart()
        {
            var client = new VoiceClient();
            using (var session = new RealtimeVoiceSession(client, new Audio()))
            {
                await session.StartAsync(Request());
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Closed, ThreadId = "thread" });
                await session.StopAsync();
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Idle));

                await session.StartAsync(Request());
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active));
                await session.StopAsync();
            }
        }

        [TestCase(true, "https://mycodex-voice.local/voice.html", CoreWebView2PermissionKind.Microphone, true)]
        [TestCase(false, "https://mycodex-voice.local/voice.html", CoreWebView2PermissionKind.Microphone, false)]
        [TestCase(true, "https://example.com/voice.html", CoreWebView2PermissionKind.Microphone, false)]
        [TestCase(true, "https://mycodex-voice.local/voice.html", CoreWebView2PermissionKind.Camera, false)]
        [TestCase(true, "not a uri", CoreWebView2PermissionKind.Microphone, false)]
        public void Audio_GrantsOnlyTheMicrophoneToItsOwnPageWhileCapturing(bool capturing, string uri, CoreWebView2PermissionKind kind, bool expected)
        {
            Assert.That(WebViewVoiceAudio.IsMicrophoneAllowed(capturing, uri, kind), Is.EqualTo(expected));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Coordinator_FollowsAvailabilityAndTheSelectedConversation()
        {
            using (var form = new Form())
            using (var button = new Button())
            {
                var handle = form.Handle;
                var client = new VoiceClient();
                var coordinator = Coordinator(form, button, client, new Audio(), new VoiceChatSettings(), status => { });
                try
                {
                    coordinator.Refresh("a", false);
                    Assert.That(button.Enabled, Is.False);
                    Pump(coordinator.StartAsync());
                    Assert.That(client.Starts, Is.Zero, "Unsupported targets must not start voice.");

                    coordinator.Refresh("a", true);
                    Assert.That(button.Enabled, Is.True);
                    Pump(coordinator.StartAsync());
                    Assert.That(coordinator.ModeEnabled, Is.True);
                    Assert.That(coordinator.IsSpeakingForThread("thread"), Is.True);
                    Assert.That(button.AccessibleName, Is.Not.Empty);

                    coordinator.Refresh("b", true);
                    Assert.That(coordinator.ModeEnabled, Is.False);
                    Assert.That(coordinator.IsSpeakingForThread("thread"), Is.False);
                    Pump(coordinator.StopAsync());
                    Assert.That(client.Stops, Is.EqualTo(1));
                }
                finally
                {
                    coordinator.Dispose();
                }
            }
        }

        [Test]
        public async Task Session_MicrophoneToggle_OnlyReachesAnActiveConnection()
        {
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(new VoiceClient(), audio))
            {
                await session.SetMicrophoneEnabledAsync(true);
                Assert.That(audio.MicrophoneChanges, Is.Empty);

                await session.StartAsync(Request());
                await session.SetMicrophoneEnabledAsync(true);
                await session.SetMicrophoneEnabledAsync(false);
                Assert.That(audio.MicrophoneChanges, Is.EqualTo(new[] { true, false }));
                await session.StopAsync();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Coordinator_HoldTheDefaultF8ToTalkAndReleaseToSend()
        {
            using (var form = new Form())
            using (var button = new Button())
            {
                var handle = form.Handle;
                var audio = new Audio();
                var statuses = new List<string>();
                var coordinator = Coordinator(form, button, new VoiceClient(), audio, new VoiceChatSettings(), statuses.Add);
                try
                {
                    Assert.That(coordinator.HandleTalkKey(true), Is.False, "F8 must pass through while voice is off.");

                    coordinator.Refresh("a", true);
                    Pump(coordinator.StartAsync());
                    Assert.That(audio.MicrophoneChanges, Is.Empty, "The microphone stays off until F8 is held.");

                    Assert.That(coordinator.HandleTalkKey(true), Is.True);
                    Assert.That(coordinator.HandleTalkKey(true), Is.True, "Auto-repeat is consumed without toggling again.");
                    Assert.That(coordinator.HandleTalkKey(false), Is.True);
                    Pump(Task.Delay(50));
                    Assert.That(audio.MicrophoneChanges, Is.EqualTo(new[] { true, false }));

                    coordinator.HandleTalkKey(true);
                    FormTestDriver.Invoke(coordinator, "OnOwnerDeactivate", form, EventArgs.Empty);
                    Pump(Task.Delay(50));
                    Assert.That(audio.MicrophoneChanges, Is.EqualTo(new[] { true, false, true, false }), "Leaving the window releases the hold.");
                    Assert.That(statuses, Has.Some.Contains("F8"));
                    Pump(coordinator.StopAsync());
                }
                finally
                {
                    coordinator.Dispose();
                }
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Coordinator_ToggleModeUsesTheConfiguredKeyAndAppliesVoicePreferences()
        {
            using (var form = new Form())
            using (var button = new Button())
            {
                var handle = form.Handle;
                var client = new VoiceClient();
                var audio = new Audio();
                var statuses = new List<string>();
                var settings = new VoiceChatSettings
                {
                    TalkVirtualKey = (int)Keys.F9,
                    TalkMode = VoiceTalkMode.Toggle,
                    Voice = "marin",
                    FasterSpeech = true,
                    OutputVolumePercent = 150
                };
                var coordinator = Coordinator(form, button, client, audio, settings, statuses.Add);
                try
                {
                    coordinator.Refresh("a", true);
                    Pump(coordinator.StartAsync());
                    Assert.That(client.LastRequest.Voice, Is.EqualTo("marin"));
                    Assert.That(client.LastRequest.FasterSpeech, Is.True);
                    Assert.That(audio.OutputVolumePercent, Is.EqualTo(150));

                    Assert.That(coordinator.HandleTalkKey(true), Is.True);
                    Assert.That(coordinator.HandleTalkKey(true), Is.True, "Auto-repeat must not toggle again.");
                    Assert.That(coordinator.HandleTalkKey(false), Is.True, "Releasing keeps listening in toggle mode.");
                    Pump(Task.Delay(50));
                    Assert.That(audio.MicrophoneChanges, Is.EqualTo(new[] { true }));

                    coordinator.HandleTalkKey(true);
                    coordinator.HandleTalkKey(false);
                    Pump(Task.Delay(50));
                    Assert.That(audio.MicrophoneChanges, Is.EqualTo(new[] { true, false }));
                    Assert.That(statuses, Has.Some.Contains("F9"));

                    coordinator.HandleTalkKey(true);
                    settings.TalkMode = VoiceTalkMode.Hold;
                    coordinator.SettingsChanged();
                    Pump(Task.Delay(50));
                    Assert.That(audio.MicrophoneChanges, Is.EqualTo(new[] { true, false, true, false }), "Changing settings closes an open microphone.");
                    Pump(coordinator.StopAsync());
                }
                finally
                {
                    coordinator.Dispose();
                }
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Coordinator_FallsBackFromUnknownVoiceAndVolume()
        {
            using (var form = new Form())
            using (var button = new Button())
            {
                var handle = form.Handle;
                var client = new VoiceClient();
                var audio = new Audio();
                var coordinator = Coordinator(form, button, client, audio, new VoiceChatSettings { Voice = "retired", OutputVolumePercent = 130 }, status => { });
                try
                {
                    coordinator.Refresh("a", true);
                    Pump(coordinator.StartAsync());
                    Assert.That(client.LastRequest.Voice, Is.EqualTo("cove"));
                    Assert.That(audio.OutputVolumePercent, Is.EqualTo(100));
                    Pump(coordinator.StopAsync());
                }
                finally
                {
                    coordinator.Dispose();
                }
            }
        }

        [Test]
        public async Task Session_ForwardsTypedRequestsOnlyForTheActiveVoiceThread()
        {
            var client = new VoiceClient();
            using (var session = new RealtimeVoiceSession(client, new Audio()))
            {
                client.Type("thread", "before voice");
                await session.StartAsync(Request());
                client.Type("other", "elsewhere");
                client.Type("thread", "typed");
                await session.StopAsync();
                client.Type("thread", "after voice");
            }

            Assert.That(client.TypedRequests, Is.EqualTo(new[] { "thread:typed" }));
        }

        [Test]
        public async Task Operations_FasterSpeechAndTypedRequestsUseOfficialRequests()
        {
            var calls = new List<KeyValuePair<string, IDictionary<string, object>>>();
            var operations = Operations(calls);
            var request = Request();
            request.FasterSpeech = true;

            await operations.StartAsync(request, CancellationToken.None);
            await operations.AppendTypedRequestAsync("thread", "  run the tests  ", CancellationToken.None);

            var items = (Dictionary<string, object>[])calls.Single(call => call.Key == "thread/realtime/start").Value["initialItems"];
            Assert.That(items.Last()["role"], Is.EqualTo("developer"));
            Assert.That((string)items.Last()["text"], Does.Contain("faster"));
            var append = calls.Single(call => call.Key == "thread/realtime/appendText").Value;
            Assert.That(append["threadId"], Is.EqualTo("thread"));
            Assert.That(append["role"], Is.EqualTo("user"));
            Assert.That((string)append["text"], Does.EndWith("Request: run the tests"));
            Assert.That(AppServerRealtimeOperations.BuildTypedRequest(new[]
            {
                new AppServerUserInputItem { Type = "text", Text = " first " },
                new AppServerUserInputItem { Type = "localImage", Text = "ignored" },
                new AppServerUserInputItem { Type = "text", Text = "second" }
            }), Is.EqualTo("first\nsecond"));
        }

        [TestCase(0, 0x77)]
        [TestCase(0x78, 0x78)]
        [TestCase(0x79, 0x77)]
        [TestCase(0x73, 0x77)]
        public void Preferences_AcceptOnlyOfferedTalkKeys(int stored, int expected)
        {
            Assert.That(VoiceChatPreferences.NormalizeTalkVirtualKey(stored), Is.EqualTo(expected));
        }

        [Test]
        public void Preferences_DescribeKeysAndNormalizeVolumeAndVoice()
        {
            Assert.That(VoiceChatPreferences.TalkVirtualKeys.Select(VoiceChatPreferences.DescribeTalkKey),
                Is.EqualTo(new[] { "F6", "F7", "F8", "F9", "F11", "F12" }));
            Assert.That(VoiceChatPreferences.NormalizeOutputVolumePercent(200), Is.EqualTo(200));
            Assert.That(VoiceChatPreferences.NormalizeOutputVolumePercent(0), Is.EqualTo(100));
            Assert.That(VoiceChatPreferences.NormalizeVoice("marin", new[] { "cove", "marin" }), Is.EqualTo("marin"));
            Assert.That(VoiceChatPreferences.NormalizeVoice("retired", new[] { "cove" }), Is.Null);
        }

        [Test]
        public void Settings_RoundTripInTheTargetConfigAndDefaultWhenMissing()
        {
            var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppConfigSnapshot));
            var snapshot = new AppConfigSnapshot();
            snapshot.VoiceChat = new VoiceChatSettings { Voice = "marin", TalkMode = VoiceTalkMode.Toggle, TalkVirtualKey = 0x7B };
            using (var stream = new System.IO.MemoryStream())
            {
                serializer.WriteObject(stream, snapshot);
                stream.Position = 0;
                var restored = (AppConfigSnapshot)serializer.ReadObject(stream);
                Assert.That(restored.VoiceChat.Voice, Is.EqualTo("marin"));
                Assert.That(restored.VoiceChat.TalkMode, Is.EqualTo(VoiceTalkMode.Toggle));
                Assert.That(restored.VoiceChat.TalkVirtualKey, Is.EqualTo(0x7B));
            }

            using (var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")))
            {
                var legacy = (AppConfigSnapshot)serializer.ReadObject(stream);
                Assert.That(legacy.VoiceChat, Is.Not.Null);
                Assert.That(legacy.VoiceChat.TalkMode, Is.EqualTo(VoiceTalkMode.Hold));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void OptionsVoiceChatTab_ShowsSavedChoicesAndReturnsTheSelectionOnOk()
        {
            var saved = new VoiceChatSettings { TalkVirtualKey = 0x78, TalkMode = VoiceTalkMode.Toggle, Voice = "marin", FasterSpeech = true, OutputVolumePercent = 200 };
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                Assert.That(form.VoiceChat, Is.Null, "Without the tab, Options leaves voice settings alone.");
                form.AddVoiceChatPage(new VoiceChatOptionsPage(saved, null, null));
                var tabs = FormTestDriver.Field<TabControl>(form, "_tabControl");
                Assert.That(tabs.TabPages.IndexOfKey("voiceChat"), Is.EqualTo(tabs.TabPages.IndexOfKey("screenReader") + 1));
                Assert.That(tabs.TabPages["voiceChat"].Text, Is.EqualTo(UiLocalization.Text("OptionsForm.Category.VoiceChat", "Voice Chat")));

                var preview = FormTestDriver.Descendants<Button>(form).Single(button => button.Name == "VoicePreviewButton");
                Assert.That(preview.Enabled, Is.False, "Trying a voice needs official voice on this target.");
                Assert.That(preview.AccessibleName, Is.Not.Empty);
                var key = FormTestDriver.Descendants<ComboBox>(form).Single(list => list.Name == "VoiceTalkKey");
                var mode = FormTestDriver.Descendants<ComboBox>(form).Single(list => list.Name == "VoiceTalkMode");
                var voice = FormTestDriver.Descendants<ComboBox>(form).Single(list => list.Name == "VoiceName");
                var pace = FormTestDriver.Descendants<ComboBox>(form).Single(list => list.Name == "VoicePace");
                var volume = FormTestDriver.Descendants<ComboBox>(form).Single(list => list.Name == "VoiceVolume");
                var lists = new[] { key, mode, voice, pace, volume };
                Assert.That(lists.Select(list => list.AccessibleName), Has.All.Not.Empty);
                Assert.That(lists.Select(list => list.TabIndex), Is.Ordered.Ascending);
                Assert.That(key.Text, Is.EqualTo("F9"));
                Assert.That(voice.Text, Is.EqualTo("marin"), "A saved voice stays selectable before the catalog loads.");
                Assert.That(volume.Text, Is.EqualTo("200%"));

                key.SelectedIndex = 0;
                mode.SelectedIndex = 0;
                voice.SelectedIndex = 0;
                form.ValidateSelection = () => false;
                FormTestDriver.Invoke(form, "OkButton_Click", form, EventArgs.Empty);
                Assert.That(form.VoiceChat.TalkVirtualKey, Is.EqualTo(0x75));
                Assert.That(form.VoiceChat.TalkMode, Is.EqualTo(VoiceTalkMode.Hold));
                Assert.That(form.VoiceChat.Voice, Is.Null);
                Assert.That(form.VoiceChat.FasterSpeech, Is.True);
                Assert.That(form.VoiceChat.OutputVolumePercent, Is.EqualTo(200));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void OptionsVoiceChatTab_TabMovesFromTheTabStripIntoTheLists()
        {
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                form.AddVoiceChatPage(new VoiceChatOptionsPage(new VoiceChatSettings(), null, null));
                form.ShowInTaskbar = false;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var tabs = FormTestDriver.Field<TabControl>(form, "_tabControl");
                tabs.SelectedTab = tabs.TabPages["voiceChat"];
                tabs.Focus();
                var key = FormTestDriver.Descendants<ComboBox>(form).Single(list => list.Name == "VoiceTalkKey");
                var mode = FormTestDriver.Descendants<ComboBox>(form).Single(list => list.Name == "VoiceTalkMode");

                form.SelectNextControl(tabs, true, true, true, false);
                Assert.That(key.Focused, Is.True);
                form.SelectNextControl(key, true, true, true, false);
                Assert.That(mode.Focused, Is.True);
            }
        }

        [Test]
        public void Preferences_TreatStoredValuesByWhatTheyMean()
        {
            Assert.That(VoiceChatPreferences.AreEquivalent(null, new VoiceChatSettings { TalkVirtualKey = 0x77, OutputVolumePercent = 100, Voice = " " }), Is.True);
            Assert.That(VoiceChatPreferences.AreEquivalent(new VoiceChatSettings { OutputVolumePercent = 130 }, new VoiceChatSettings()), Is.True);
            Assert.That(VoiceChatPreferences.AreEquivalent(new VoiceChatSettings(), new VoiceChatSettings { TalkMode = VoiceTalkMode.Toggle }), Is.False);
            Assert.That(VoiceChatPreferences.AreEquivalent(new VoiceChatSettings(), new VoiceChatSettings { Voice = "marin" }), Is.False);
            Assert.That(VoiceChatPreferences.AreEquivalent(new VoiceChatSettings(), new VoiceChatSettings { FasterSpeech = true }), Is.False);
        }

        [Test]
        public async Task Session_PreviewListensOnlySpeaksTheSampleAndEndsWhenPlaybackFinishes()
        {
            var client = new VoiceClient();
            var audio = new Audio();
            using (var session = new RealtimeVoiceSession(client, audio))
            {
                var request = Request();
                request.PreviewText = "sample";
                await session.StartAsync(request);

                Assert.That(audio.ListenOnly, Is.True);
                Assert.That(client.Spoken, Is.EqualTo(new[] { "thread:sample" }));
                audio.Finish("other");
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active));

                audio.Finish(request.SessionId);
                await session.StopAsync();
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Idle));
                Assert.That(client.Stops, Is.EqualTo(1));

                await session.StartAsync(Request());
                Assert.That(audio.ListenOnly, Is.False);
                audio.Finish(client.LastRequest.SessionId);
                Assert.That(session.State, Is.EqualTo(VoiceChatState.Active), "voice chat does not end when the voice goes quiet");
                await session.StopAsync();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Coordinator_OptionsTabOffersVoicesWheneverTheRuntimeSupportsThem()
        {
            using (var form = new Form())
            using (var button = new Button())
            using (var coordinator = Coordinator(form, button, new VoiceClient(), new Audio(), new VoiceChatSettings(), status => { }))
            {
                coordinator.Refresh(null, false);
                Assert.That(button.Enabled, Is.False, "No usable workspace is selected, so voice chat cannot start.");
                using (var supported = coordinator.CreateOptionsPage(true))
                using (var unsupported = coordinator.CreateOptionsPage(false))
                {
                    Assert.That(FormTestDriver.Descendants<Button>(supported).Single(item => item.Name == "VoicePreviewButton").Enabled, Is.True);
                    Assert.That(FormTestDriver.Descendants<Button>(unsupported).Single(item => item.Name == "VoicePreviewButton").Enabled, Is.False);
                }
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void Coordinator_PreviewUsesATemporaryThreadAndKeepsVoiceChatOff()
        {
            var client = new VoiceClient();
            var audio = new Audio();
            var statuses = new List<string>();
            var settings = new VoiceChatSettings { Voice = "marin", FasterSpeech = true, OutputVolumePercent = 150 };
            using (var form = new Form())
            using (var button = new Button())
            using (var coordinator = Coordinator(form, button, client, audio, settings, statuses.Add))
            {
                coordinator.Refresh("a", true);
                var preview = coordinator.PreviewAsync(settings, CancellationToken.None);
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (client.Spoken.Count == 0 && timer.Elapsed < TimeSpan.FromSeconds(10))
                {
                    System.Windows.Forms.Application.DoEvents();
                }

                Assert.That(client.LastRequest.ThreadId, Is.EqualTo("preview-thread"));
                Assert.That(client.LastRequest.Voice, Is.EqualTo("marin"));
                Assert.That(client.LastRequest.FasterSpeech, Is.True);
                Assert.That(audio.OutputVolumePercent, Is.EqualTo(150));
                Assert.That(button.Enabled, Is.False);
                Assert.That(coordinator.ModeEnabled, Is.False);
                Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.PreviewAsync(settings, CancellationToken.None));

                audio.Finish(client.LastRequest.SessionId);
                Pump(preview);
                Assert.That(client.ReleasedPreviewThreads, Is.EqualTo(new[] { "preview-thread" }));
                Assert.That(button.Enabled, Is.True);
                Assert.That(statuses, Is.Empty, "the preview reports in the settings dialog, not the main status");
            }
        }

        [Test]
        public void Notifications_KeepTemporaryThreadsOutOfTheSessionList()
        {
            var events = new List<AppServerEventArgs>();
            var loaded = new List<string>();
            var forgotten = new List<string>();
            var processor = new AppServerNotificationEventProcessor(new AppServerNotificationEventContext(
                events.Add, loaded.Add, forgotten.Add, (_, __) => { }, _ => { }, path => path, _ => { }));

            processor.HandleNotification("thread/started", AppServerJsonValue.DeserializeDictionary("{\"thread\":{\"id\":\"preview\",\"ephemeral\":true}}"));
            processor.HandleNotification("thread/status/changed", AppServerJsonValue.DeserializeDictionary("{\"threadId\":\"preview\",\"status\":{\"type\":\"idle\"}}"));
            processor.HandleNotification("thread/closed", AppServerJsonValue.DeserializeDictionary("{\"threadId\":\"preview\"}"));
            Assert.That(events, Is.Empty);
            Assert.That(loaded, Is.EqualTo(new[] { "preview" }));
            Assert.That(forgotten, Is.EqualTo(new[] { "preview" }));

            processor.HandleNotification("thread/started", AppServerJsonValue.DeserializeDictionary("{\"thread\":{\"id\":\"real\"}}"));
            Assert.That(events.Select(e => e.ThreadId), Is.EqualTo(new[] { "real" }));
        }

        [TestCase(0x0100, Keys.F8, true, true, true)]
        [TestCase(0x0101, Keys.F8, true, true, false)]
        [TestCase(0x0104, Keys.F8, true, true, true)]
        [TestCase(0x0100, Keys.F7, true, false, null)]
        [TestCase(0x0100, Keys.F8, false, false, null)]
        [TestCase(0x0102, Keys.F8, true, false, null)]
        public void KeyFilter_RoutesOnlyTheTalkKeyWhileTheMainWindowIsActive(int message, Keys key, bool active, bool consumed, bool? pressed)
        {
            bool? seen = null;
            var filter = new PushToTalkKeyFilter(() => active, () => Keys.F8, down => { seen = down; return true; });
            var m = Message.Create(IntPtr.Zero, message, (IntPtr)(int)key, IntPtr.Zero);

            Assert.That(filter.PreFilterMessage(ref m), Is.EqualTo(consumed));
            Assert.That(seen, Is.EqualTo(pressed));
        }

        private static VoiceChatCoordinator Coordinator(Form form, Button button, VoiceClient client, Audio audio, VoiceChatSettings settings, Action<string> status)
        {
            return new VoiceChatCoordinator(form, button, client, null, audio,
                () => Task.FromResult(new VoiceConversationBinding { ConversationId = "a", Request = new RealtimeVoiceStart { ThreadId = "thread" } }),
                () => settings,
                status);
        }

        private static void Pump(Task task)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(10))
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }

            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }

        private static RealtimeVoiceStart Request()
        {
            return new RealtimeVoiceStart { ThreadId = "thread", SessionId = "session", OfferSdp = "offer", Voice = "cove" };
        }

        private static AppServerRealtimeOperations Operations(
            List<KeyValuePair<string, IDictionary<string, object>>> calls,
            string accountType = "chatgpt",
            IDictionary<string, object> timeline = null,
            Exception timelineFailure = null)
        {
            return new AppServerRealtimeOperations((method, args, token, timeout) =>
            {
                calls.Add(new KeyValuePair<string, IDictionary<string, object>>(method, (IDictionary<string, object>)args));
                if (method == "account/read")
                {
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "account", new Dictionary<string, object> { { "type", accountType } } }
                    });
                }

                if (method == "thread/timeline/list")
                {
                    return timelineFailure != null
                        ? Task.FromException<IDictionary<string, object>>(timelineFailure)
                        : Task.FromResult(timeline ?? Timeline());
                }

                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
            });
        }

        private static IDictionary<string, object> Timeline(params object[] entries)
        {
            return new Dictionary<string, object> { { "data", entries } };
        }

        private static IDictionary<string, object> Entry(long position, string entryType, string itemType, string role = null, string text = null, string content = null)
        {
            var item = new Dictionary<string, object> { { "type", itemType }, { "id", "item-" + position } };
            if (role != null)
            {
                item["role"] = role;
            }

            if (text != null)
            {
                item["text"] = text;
            }

            if (content != null)
            {
                item["content"] = new object[] { new Dictionary<string, object> { { "type", "text" }, { "text", content } } };
            }

            return new Dictionary<string, object> { { "type", entryType }, { "position", position }, { "item", item } };
        }

        private sealed class VoiceClient : IAppServerRealtimeClient
        {
            public bool AutoAnswer = true;
            public RealtimeVoiceStart LastRequest;
            public int Starts;
            public int Stops;

            public readonly List<string> TypedRequests = new List<string>();

            public event EventHandler<RealtimeVoiceEvent> RealtimeVoiceEventReceived;

            public event EventHandler<RealtimeVoiceEvent> TypedInputSubmitted;

            public void Emit(RealtimeVoiceEvent value)
            {
                RealtimeVoiceEventReceived?.Invoke(this, value);
            }

            public void Type(string threadId, string text)
            {
                TypedInputSubmitted?.Invoke(this, new RealtimeVoiceEvent { ThreadId = threadId, Text = text });
            }

            public Task AppendRealtimeTypedRequestAsync(string threadId, string text, CancellationToken cancellationToken)
            {
                TypedRequests.Add(threadId + ":" + text);
                return Task.CompletedTask;
            }

            public readonly List<string> Spoken = new List<string>();
            public readonly List<string> ReleasedPreviewThreads = new List<string>();

            public Task AppendRealtimeSpeechAsync(string threadId, string text, CancellationToken cancellationToken)
            {
                Spoken.Add(threadId + ":" + text);
                return Task.CompletedTask;
            }

            public Task<string> CreateVoicePreviewThreadAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult("preview-thread");
            }

            public Task ReleaseVoicePreviewThreadAsync(string threadId)
            {
                ReleasedPreviewThreads.Add(threadId);
                return Task.CompletedTask;
            }

            public void Answer()
            {
                Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Started, ThreadId = LastRequest.ThreadId, SessionId = LastRequest.SessionId });
                Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Sdp, ThreadId = LastRequest.ThreadId, Sdp = "answer" });
            }

            public Task<RealtimeVoiceCatalog> ListRealtimeVoicesAsync(CancellationToken cancellationToken)
            {
                return Task.FromResult(new RealtimeVoiceCatalog { Voices = new[] { "cove", "marin" }, DefaultVoice = "cove" });
            }

            public Task StartRealtimeVoiceAsync(RealtimeVoiceStart request, CancellationToken cancellationToken)
            {
                LastRequest = request;
                Starts++;
                if (AutoAnswer)
                {
                    Answer();
                }

                return Task.CompletedTask;
            }

            public Task StopRealtimeVoiceAsync(string threadId, CancellationToken cancellationToken)
            {
                Stops++;
                Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Closed, ThreadId = threadId });
                return Task.CompletedTask;
            }
        }

        private sealed class Audio : IRealtimeVoiceAudio
        {
            public TaskCompletionSource<bool> PendingConnect;
            public string Answer;
            public int OutputVolumePercent;
            public int Stops;
            public bool Disposed;
            public readonly List<bool> MicrophoneChanges = new List<bool>();

            public bool? SystemEchoCancellationEnabled => true;

            public event EventHandler<VoiceAudioEvent> Failed;

            public void Fail(string sessionId, string error)
            {
                Failed?.Invoke(this, new VoiceAudioEvent { SessionId = sessionId, Error = error });
            }

            public event EventHandler<VoiceAudioEvent> PlaybackFinished;

            public bool? ListenOnly;

            public void Finish(string sessionId)
            {
                PlaybackFinished?.Invoke(this, new VoiceAudioEvent { SessionId = sessionId });
            }

            public Task<string> PrepareAsync(string sessionId, int outputVolumePercent, bool listenOnly, CancellationToken cancellationToken)
            {
                OutputVolumePercent = outputVolumePercent;
                ListenOnly = listenOnly;
                return Task.FromResult("offer");
            }

            public async Task ConnectAsync(string sessionId, string answerSdp, CancellationToken cancellationToken)
            {
                Answer = answerSdp;
                if (PendingConnect != null)
                {
                    await PendingConnect.Task;
                }
            }

            public Task SetMicrophoneEnabledAsync(string sessionId, bool enabled)
            {
                MicrophoneChanges.Add(enabled);
                return Task.CompletedTask;
            }

            public Task StopAsync(string sessionId)
            {
                Stops++;
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
