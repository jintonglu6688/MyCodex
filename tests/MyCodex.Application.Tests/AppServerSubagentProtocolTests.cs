using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerSubagentProtocolTests
    {
        // Synthetic, redacted minimal wire payloads matching codex-cli 0.154.0 experimental schema.
        [TestCase("subAgent")]
        [TestCase("subagent")]
        public void Identity_TopLevelWinsAndLegacyFallbackRemains(string sourceKey)
        {
            var thread = Json("{\"id\":\"child\",\"parentThreadId\":\"parent\",\"agentNickname\":\"Worker\",\"agentRole\":\"reviewer\",\"source\":{\"" + sourceKey + "\":{\"thread_spawn\":{\"parent_thread_id\":\"old\",\"depth\":2,\"agent_nickname\":\"Old\"}}},\"status\":{\"type\":\"active\",\"activeFlags\":[\"waitingOnApproval\",\"waitingOnUserInput\",\"futureFlag\"]}}");
            var result = AppServerThreadMapper.BuildThreadSummary(thread, null);
            Assert.That(result.ParentThreadId, Is.EqualTo("parent"));
            Assert.That(result.ParentThreadIdConflict, Is.True);
            Assert.That(result.AgentNickname, Is.EqualTo("Worker"));
            Assert.That(result.AgentRole, Is.EqualTo("reviewer"));
            Assert.That(result.ThreadStatus.ActiveFlags, Has.Count.EqualTo(3));
            var started = AppServerNotificationFactory.CreateThreadStarted(result.ThreadId, result);
            Assert.That(started.ThreadSummary, Is.SameAs(result));
            Assert.That(started.ThreadStatus.Type, Is.EqualTo("active"));
            thread.Remove("parentThreadId");
            thread.Remove("agentNickname");
            result = AppServerThreadMapper.BuildThreadSummary(thread, null);
            Assert.That(result.ParentThreadId, Is.EqualTo("old"));
            Assert.That(result.ParentThreadIdConflict, Is.False);
            Assert.That(result.AgentNickname, Is.EqualTo("Old"));
        }

        [Test]
        public void Identity_TopLevelOnlyAndMissingFieldsDoNotGuessRelationships()
        {
            var result = AppServerThreadMapper.BuildThreadSummary(Json("{\"id\":\"child\",\"parentThreadId\":\"parent\",\"unknown\":true}"), null);
            Assert.That(result.ParentThreadId, Is.EqualTo("parent"));
            Assert.That(result.ThreadSource, Is.EqualTo("subagent"));
            result = AppServerThreadMapper.BuildThreadSummary(Json("{\"id\":\"normal\",\"source\":{\"subAgent\":\"review\"}}"), null);
            Assert.That(result.ParentThreadId, Is.Null);
            Assert.That(result.ThreadStatus, Is.Null);
            Assert.That(result.ThreadSource, Is.Null);
        }

        [TestCase("spawnAgent")]
        [TestCase("followupTask")]
        [TestCase("sendMessage")]
        [TestCase("interruptAgent")]
        [TestCase("wait")]
        [TestCase("futureTool")]
        public void Collaboration_HistoryAndLivePreserveIndependentStatesAndStableIdentity(string tool)
        {
            var item = Json("{\"type\":\"collabAgentToolCall\",\"id\":\"call\",\"tool\":\"" + tool + "\",\"status\":\"completed\",\"senderThreadId\":\"parent\",\"receiverThreadIds\":[\"a\",\"b\"],\"agentsStates\":{\"a\":{\"status\":\"running\"},\"b\":{\"status\":\"notFound\",\"message\":\"missing\"}},\"prompt\":\"Check this\",\"model\":\"requested-model\",\"reasoningEffort\":\"high\"}");
            var live = AppServerCanonicalActivityMapper.Map(item, true, "parent", "turn", null);
            var thread = new Dictionary<string, object> { { "id", "parent" }, { "turns", new object[] { new Dictionary<string, object> { { "id", "turn" }, { "items", new object[] { item } } } } } };
            var history = AppServerThreadMapper.BuildThreadDetails(thread, null);
            Assert.That(history.Messages, Is.Empty);
            Assert.That(history.Activities, Has.Count.EqualTo(1));
            Assert.That(history.Activities[0].IsHistorical, Is.True);
            Assert.That(live.IsHistorical, Is.False);
            Assert.That(history.Activities[0].ItemId, Is.EqualTo(live.ItemId));
            Assert.That(live.Collaboration.ReceiverThreadIds, Is.EqualTo(new[] { "a", "b" }));
            Assert.That(live.Collaboration.AgentsStates["a"].Status, Is.EqualTo("running"));
            Assert.That(live.Collaboration.AgentsStates["b"].Status, Is.EqualTo("notFound"));
            Assert.That(live.Collaboration.Status, Is.EqualTo("completed"));
            Assert.That(live.ThreadStatus, Is.Null);
            Assert.That(live.Collaboration.RequestedModel, Is.EqualTo("requested-model"));
            Assert.That(history.Activities[0].Collaboration.Tool, Is.EqualTo(tool));
        }

        [TestCase("pendingInit")]
        [TestCase("running")]
        [TestCase("completed")]
        [TestCase("interrupted")]
        [TestCase("shutdown")]
        [TestCase("notFound")]
        [TestCase("errored")]
        public void AgentState_IsNotThreadOrToolState(string status)
        {
            var result = AppServerAgentActivityMapper.Map(Json("{\"type\":\"collabAgentToolCall\",\"agentsStates\":{\"a\":{\"status\":\"" + status + "\"}}}"), "parent", "turn");
            Assert.That(result.Collaboration.AgentsStates["a"].Status, Is.EqualTo(status));
            Assert.That(result.ItemStatus, Is.Null);
            Assert.That(result.ThreadStatus, Is.Null);
        }

        [TestCase("notLoaded")]
        [TestCase("idle")]
        [TestCase("systemError")]
        public void ThreadStateNotification_PreservesSeparateStatus(string type)
        {
            var result = AppServerNotificationFactory.CreateThreadStatusChanged("a", type);
            Assert.That(result.ThreadStatus.Type, Is.EqualTo(type));
            Assert.That(result.Collaboration, Is.Null);
        }

        [TestCase(AppServerHostPlatform.Windows)]
        [TestCase(AppServerHostPlatform.Wsl)]
        [TestCase(AppServerHostPlatform.RemoteMac)]
        public void Capabilities_UnknownTargetVersionDoesNotInheritHostSchema(AppServerHostPlatform platform)
        {
            var launch = new AppServerLaunchInfo("codex", "app-server", hostPlatform: platform);
            var unknown = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(null, launch, Json("{}"));
            Assert.That(unknown.SupportsSubagentThreadFilters, Is.False);
            Assert.That(unknown.SupportsCollabAgentToolCall, Is.False);
            var known = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(null, launch, Json("{\"userAgent\":\"codex/0.154.0\"}"));
            Assert.That(known.SupportsSubagentThreadFilters, Is.True);
            Assert.That(known.SupportsCollabAgentToolCall, Is.True);
        }

        [Test]
        public void Notifications_KeepThreadMetadataAndSeparateActivityIdentity()
        {
            var events = new List<AppServerEventArgs>();
            var processor = new AppServerNotificationEventProcessor(new AppServerNotificationEventContext(
                events.Add, _ => { }, _ => { }, (_, __) => { }, _ => { }, path => path, _ => { }));
            processor.HandleNotification("thread/started", Json("{\"thread\":{\"id\":\"a\",\"parentThreadId\":\"parent\",\"agentNickname\":\"Worker\"}}"));
            processor.HandleNotification("thread/status/changed", Json("{\"threadId\":\"a\",\"status\":{\"type\":\"active\",\"activeFlags\":[\"waitingOnUserInput\"]}}"));
            processor.HandleNotification("item/completed", Json("{\"threadId\":\"parent\",\"turnId\":\"turn\",\"item\":{\"id\":\"activity\",\"type\":\"subAgentActivity\",\"kind\":\"completed\",\"agentThreadId\":\"a\",\"agentPath\":\"worker\"}}"));
            Assert.That(events, Has.Count.EqualTo(3));
            Assert.That(events[0].ThreadSummary.ParentThreadId, Is.EqualTo("parent"));
            Assert.That(events[1].ThreadStatus.ActiveFlags, Is.EqualTo(new[] { "waitingOnUserInput" }));
            Assert.That(events[2].SubAgentActivity.AgentThreadId, Is.EqualTo("a"));
            Assert.That(events[2].Collaboration, Is.Null);
            Assert.That(events[2].ThreadStatus, Is.Null);
        }

        [Test]
        public void HistoricalRepeatWork_PreservesOneAgentAndDoesNotRaiseLiveEvents()
        {
            var details = AppServerThreadMapper.BuildThreadDetails(Json("{\"id\":\"parent\",\"turns\":[{\"id\":\"one\",\"items\":[{\"id\":\"spawn\",\"type\":\"collabAgentToolCall\",\"tool\":\"spawnAgent\",\"receiverThreadIds\":[\"a\"]}]},{\"id\":\"two\",\"items\":[{\"id\":\"followup\",\"type\":\"collabAgentToolCall\",\"tool\":\"followupTask\",\"receiverThreadIds\":[\"a\"]},{\"id\":\"activity\",\"type\":\"subAgentActivity\",\"kind\":\"started\",\"agentThreadId\":\"a\"}]}]}"), null);
            Assert.That(details.Activities, Has.Count.EqualTo(3));
            Assert.That(details.Activities[0].Collaboration.ReceiverThreadIds, Is.EqualTo(details.Activities[1].Collaboration.ReceiverThreadIds));
            Assert.That(details.Activities[2].SubAgentActivity.AgentThreadId, Is.EqualTo("a"));
            Assert.That(details.Activities.TrueForAll(activity => activity.IsHistorical && activity.ThreadStatus == null), Is.True);
            Assert.That(details.Activities[1].TurnId, Is.EqualTo("two"));
        }

        [TestCase("review")]
        [TestCase("compact")]
        [TestCase("futureKind")]
        public void Identity_PreservesInternalSourceEvenWithTopLevelParent(string kind)
        {
            var source = new Dictionary<string, object> { { "subAgent", kind } };
            var wire = new Dictionary<string, object> { { "id", "a" }, { "parentThreadId", "parent" }, { "source", source } };
            var details = AppServerThreadMapper.BuildThreadDetails(wire, null);
            Assert.That(details.SubAgentSourceKind, Is.EqualTo(kind));
            Assert.That(details.ParentThreadId, Is.EqualTo("parent"));
        }

        [Test]
        public void Observed0154_ReuseCanArriveOnlyAsActivityAndWaitHasNoReceivers()
        {
            // Captured 2026-09-12 from one isolated agent replying OK twice; identifiers redacted.
            var kinds = new[] { "started", "completed", "interacted", "completed" };
            var agentIds = new HashSet<string>();
            for (var index = 0; index < kinds.Length; index++)
            {
                var item = new Dictionary<string, object>
                {
                    { "type", "subAgentActivity" }, { "id", "activity-" + index },
                    { "agentThreadId", "same-agent" }, { "agentPath", "/root/smoke_test" }, { "kind", kinds[index] }
                };
                var live = AppServerCanonicalActivityMapper.Map(item, true, "parent", "turn", null);
                var history = AppServerAgentActivityMapper.Map(item, "parent", "turn", true);
                Assert.That(live.SubAgentActivity.Kind, Is.EqualTo(kinds[index]));
                Assert.That(history.SubAgentActivity.AgentThreadId, Is.EqualTo(live.SubAgentActivity.AgentThreadId));
                Assert.That(live.Collaboration, Is.Null);
                agentIds.Add(live.SubAgentActivity.AgentThreadId);
            }
            Assert.That(agentIds, Has.Count.EqualTo(1));
            var wait = AppServerAgentActivityMapper.Map(Json("{\"type\":\"collabAgentToolCall\",\"tool\":\"wait\",\"status\":\"completed\",\"senderThreadId\":\"parent\",\"receiverThreadIds\":[],\"agentsStates\":{}}"), "parent", "turn");
            Assert.That(wait.Collaboration.ReceiverThreadIds, Is.Empty);
            Assert.That(wait.Collaboration.AgentsStates, Is.Empty);
            Assert.That(wait.ThreadStatus, Is.Null);
        }

        private static IDictionary<string, object> Json(string json)
        {
            return AppServerJsonValue.DeserializeDictionary(json);
        }
    }
}
