using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class VoiceHistoryTests
    {
        [Test]
        public void ReopenedVoiceHistory_KeepsTheFullBackendAnswerAndOnlyOneVisibleReply()
        {
            var ordinary = new List<ChatHistoryItem> { TextItem("progress"), TextItem("answer") };
            ordinary.ForEach(item => item.Sender = "Codex");
            var page = new RealtimeVoiceContextPage
            {
                ThreadId = "thread", Items = new[]
                {
                    new RealtimeVoiceContextItem { ItemId = "progress", Role = "assistant", Text = "正在查询", RealtimeSessionId = "voice", IsVoiceBackend = true, MessagePhase = "commentary" },
                    new RealtimeVoiceContextItem { ItemId = "answer", Role = "assistant", Text = "完整答案", RealtimeSessionId = "voice", IsVoiceBackend = true, MessagePhase = "final_answer" },
                    new RealtimeVoiceContextItem { ItemId = "spoken", Role = "assistant", Text = "答案的语音转述", RealtimeSessionId = "voice", IsVoice = true }
                }
            };
            var result = VoiceHistoryProjection.Merge(ordinary, page, CreateItem);
            Assert.That(result.Count, Is.EqualTo(3), "Supporting records remain available instead of deleting history.");
            Assert.That(result.Where(item => item.Category == ChatHistoryCategory.Conversation).Select(item => item.SourceItemId), Is.EqualTo(new[] { "answer" }));
        }

        [Test]
        public async Task BackendSelection_UsesSettingsUpdateAndDoesNotOverrideTheSpeechModel()
        {
            IDictionary<string, object> update = null;
            IDictionary<string, object> realtime = null;
            var operations = new AppServerRealtimeOperations((method, args, token, timeout) =>
            {
                if (method == "thread/settings/update") update = (IDictionary<string, object>)args;
                if (method == "thread/realtime/start") realtime = (IDictionary<string, object>)args;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                { { "account", new Dictionary<string, object> { { "type", "chatgpt" } } }, { "data", new object[0] }, { "nextCursor", null } });
            });
            await operations.StartAsync(new RealtimeVoiceStart
            {
                ThreadId = "thread", SessionId = "voice", Voice = "cove", OfferSdp = "offer",
                BackendMode = new AppServerCollaborationMode { Mode = "default", ModelId = "chosen-model", ReasoningEffort = "high" }
            }, CancellationToken.None);
            var settings = (IDictionary<string, object>)((IDictionary<string, object>)update["collaborationMode"])["settings"];
            Assert.That(update["threadId"], Is.EqualTo("thread"));
            Assert.That(settings["model"], Is.EqualTo("chosen-model"));
            Assert.That(settings["reasoning_effort"], Is.EqualTo("high"));
            Assert.That(realtime.ContainsKey("model"), Is.False);
        }

        [Test]
        public void PreviewStatusEvents_DoNotCreateOrSelectAChatSession()
        {
            var events = new List<AppServerEventArgs>();
            var processor = new AppServerNotificationEventProcessor(new AppServerNotificationEventContext(
                events.Add, _ => { }, _ => { }, (_, __) => { }, _ => { }, path => path, _ => { }));
            processor.HandleNotification("thread/started", new Dictionary<string, object>
            { { "thread", new Dictionary<string, object> { { "id", "preview" }, { "ephemeral", true } } } });
            processor.HandleNotification("thread/status/changed", new Dictionary<string, object>
            { { "threadId", "preview" }, { "status", new Dictionary<string, object> { { "type", "idle" } } } });
            processor.HandleNotification("thread/closed", new Dictionary<string, object> { { "threadId", "preview" } });
            Assert.That(events, Is.Empty);
        }

        [Test]
        public void CanonicalTimeline_InterleavesVoiceWithTextAndPreservesRepeatedSpeech()
        {
            var ordinary = new List<ChatHistoryItem> { TextItem("t1"), TextItem("t2") };
            var page = new RealtimeVoiceContextPage
            {
                ThreadId = "thread",
                Items = new[] { Entry("t1", false), Entry("v1", true), Entry("t2", false), Entry("v2", true), Entry("v2", true) }
            };
            var result = VoiceHistoryProjection.Merge(ordinary, page, CreateItem);
            Assert.That(result.Select(item => item.SourceItemId), Is.EqualTo(new[] { "t1", "v1", "t2", "v2" }));
            Assert.That(result.Where(item => item.IsVoiceTranscript).All(item => item.ThreadId == "thread" && item.TurnId == null), Is.True);
            Assert.That(result[1].FullText, Is.EqualTo(result[3].FullText), "Repeated speech has distinct canonical identities.");
        }

        [Test]
        public void VoiceOnlyHistory_RemainsVisibleWhenThereAreNoTextTurns()
        {
            var page = new RealtimeVoiceContextPage { ThreadId = "thread", Items = new[] { Entry("v1", true), Entry("v2", true) } };
            var result = VoiceHistoryProjection.Merge(new List<ChatHistoryItem>(), page, CreateItem);
            Assert.That(result.Select(item => item.SourceItemId), Is.EqualTo(new[] { "v1", "v2" }));
        }

        [Test]
        public async Task TypedContext_UsesTheOfficialQuietAppendWithoutDispatchingAnotherTurn()
        {
            var calls = new List<string>();
            var operations = new AppServerRealtimeOperations((method, args, token, timeout) =>
            {
                calls.Add(method);
                var values = (IDictionary<string, object>)args;
                Assert.That(values["threadId"], Is.EqualTo("thread"));
                Assert.That(values["role"], Is.EqualTo("user"));
                Assert.That((string)values["text"], Does.Contain("Do not delegate the same request again").And.EndsWith("37 加 29"));
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
            });
            var text = AppServerVoiceContext.BuildTypedInput(new[]
            {
                new AppServerUserInputItem { Type = "image", Text = "not a text message" },
                new AppServerUserInputItem { Type = "text", Text = "37 加 29" }
            });
            await operations.AppendContextAsync("thread", text, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(new[] { "thread/realtime/appendText" }));
        }

        [Test]
        public void OptimisticMerge_UsesCanonicalIdsForRepeatedSpeech()
        {
            var local = new[] { TextItem("v1", true), TextItem("v2", true) };
            var result = SessionHistoryOptimisticMerge.PreserveLocalConversationItems(local, new[] { TextItem("v1", true) });
            Assert.That(result.Select(item => item.SourceItemId), Is.EqualTo(new[] { "v1", "v2" }));
        }

        [Test]
        public void HistoryRefresh_PreservesACompletedVoiceReplyThatArrivedAfterTheRemotePage()
        {
            var earlier = TextItem("v1", true);
            var arriving = TextItem("v2", true);
            arriving.Sender = "Codex";
            var result = SessionHistoryOptimisticMerge.PreserveLocalConversationItems(new[] { earlier, arriving }, new[] { earlier });
            Assert.That(result.Select(item => item.SourceItemId), Is.EqualTo(new[] { "v1", "v2" }));
        }

        [TestCase(true, 0)]
        [TestCase(false, 1)]
        public void TemporaryPreviewThread_DoesNotAppearAsANewConversation(bool ephemeral, int expectedEvents)
        {
            var events = new List<AppServerEventArgs>();
            string loaded = null;
            var processor = new AppServerNotificationEventProcessor(new AppServerNotificationEventContext(
                events.Add, id => loaded = id, _ => { }, (_, __) => { }, _ => { }, path => path, _ => { }));
            processor.HandleNotification("thread/started", new Dictionary<string, object>
            {
                { "thread", new Dictionary<string, object> { { "id", "thread" }, { "ephemeral", ephemeral } } }
            });
            Assert.That(loaded, Is.EqualTo("thread"));
            Assert.That(events, Has.Count.EqualTo(expectedEvents));
        }

        [Test]
        public async Task VoiceStart_ReadsOneBoundedPageOfItsOwnConversation()
        {
            var methods = new List<string>();
            IDictionary<string, object> start = null;
            var operations = new AppServerRealtimeOperations((method, args, token, timeout) =>
            {
                methods.Add(method);
                if (method == "thread/realtime/start") start = (IDictionary<string, object>)args;
                if (method == "thread/timeline/list")
                {
                    Assert.That(((IDictionary<string, object>)args)["threadId"], Is.EqualTo("thread"));
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "nextCursor", "older-page" },
                        { "data", new object[] { new Dictionary<string, object>
                            { { "type", "realtime" }, { "item", new Dictionary<string, object>
                                { { "id", "v1" }, { "type", "transcriptSegment" }, { "role", "user" }, { "text", "继续" } } } } } }
                    });
                }
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                { { "account", new Dictionary<string, object> { { "type", "chatgpt" } } } });
            });
            var request = new RealtimeVoiceStart
            {
                ThreadId = "thread", SessionId = "voice", Voice = "cove", OfferSdp = "offer"
            };
            await operations.StartAsync(request, CancellationToken.None);
            Assert.That(methods, Is.EqualTo(new[] { "account/read", "thread/timeline/list", "thread/realtime/start" }));
            var initial = (IDictionary<string, object>[])start["initialItems"];
            Assert.That(initial.Last()["role"], Is.EqualTo("user"));
            Assert.That(initial.Last()["text"], Is.EqualTo("继续"));
        }

        [Test]
        public async Task CanonicalTranscript_RejectsAnEarlierAudioSessionOnTheSameThread()
        {
            var client = new RealtimeVoiceTests.VoiceClient();
            using (var session = new RealtimeVoiceSession(client, new RealtimeVoiceTests.Audio()))
            {
                var received = new List<string>();
                session.TranscriptReceived += (sender, item) => received.Add(item.ItemId);
                await session.StartAsync(new RealtimeVoiceStart { ThreadId = "thread", Voice = "cove" });
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Transcript, ThreadId = "thread", SessionId = "old", ItemId = "old-message", Text = "old", Role = "user" });
                client.Emit(new RealtimeVoiceEvent { Kind = RealtimeVoiceEventKind.Transcript, ThreadId = "thread", SessionId = client.LastRequest.SessionId, ItemId = "current-message", Text = "current", Role = "user" });
                Assert.That(received, Is.EqualTo(new[] { "current-message" }));
            }
        }

        private static RealtimeVoiceContextItem Entry(string id, bool voice)
        {
            return new RealtimeVoiceContextItem { ItemId = id, IsVoice = voice, Role = "user", Text = "继续" };
        }

        private static ChatHistoryItem TextItem(string id, bool voice = false)
        {
            return new ChatHistoryItem { SourceItemId = id, IsVoiceTranscript = voice, Sender = "User", Category = ChatHistoryCategory.Conversation, FullText = "继续" };
        }

        private static ChatHistoryItem CreateItem(string sender, string text, ChatHistoryCategory category)
        {
            return new ChatHistoryItem { Sender = sender, FullText = text, Category = category };
        }
    }
}
