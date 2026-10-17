using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Application.DTOs;
using System;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerSubAgentOperationsTests
    {
        [TestCase("different", false)]
        [TestCase("child", true)]
        public void UntrustedIdentityIsRejected(string responseId, bool conflict)
        {
            Assert.Throws<InvalidOperationException>(() => AppServerSubAgentOperations.ValidateIdentity("child",
                new AppServerThreadDetails { ThreadId = responseId, ParentThreadId = "parent", ParentThreadIdConflict = conflict }));
        }

        [TestCase("idle", 1)]
        [TestCase("active", 2)]
        [TestCase("notLoaded", 1)]
        public void ContradictoryTurnStateCannotSteer(string status, int count)
        {
            var details = new AppServerThreadDetails { ThreadId = "child", ParentThreadId = "parent", ThreadStatus = new AppServerThreadStatus { Type = status } };
            for (var i = 0; i < count; i++) details.TurnStatuses[i.ToString()] = "inProgress";
            Assert.Throws<InvalidOperationException>(() => AppServerSubAgentOperations.BuildInputRequest(details, new object[] { "input" }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ManualInputPreservesConfigurationAndTargetsCurrentTurn(bool running)
        {
            var details = new AppServerThreadDetails
            {
                ThreadId = "child", ParentThreadId = "parent",
                ThreadStatus = new AppServerThreadStatus { Type = running ? "active" : "idle" }
            };
            if (running) details.TurnStatuses["active-turn"] = "inProgress";
            var request = AppServerSubAgentOperations.BuildInputRequest(details, new object[] { "input" });
            Assert.That(request.Key, Is.EqualTo(running ? "turn/steer" : "turn/start"));
            var parameters = (IDictionary<string, object>)request.Value;
            Assert.That(parameters.Keys, Is.EquivalentTo(running ? new[] { "threadId", "input", "expectedTurnId" } : new[] { "threadId", "input" }));
            if (running) Assert.That(parameters["expectedTurnId"], Is.EqualTo("active-turn"));
        }

        [TestCase("notLoaded")]
        [TestCase("systemError")]
        [TestCase("active")]
        [TestCase(null)]
        public void UnverifiedRuntimeStateDoesNotGuessSettingsOrCreateAnotherThread(string status)
        {
            Assert.Throws<InvalidOperationException>(() => AppServerSubAgentOperations.BuildInputRequest(
                new AppServerThreadDetails { ThreadId = "child", ParentThreadId = "parent", ThreadStatus = new AppServerThreadStatus { Type = status } },
                new object[] { "input" }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task DiscoveryPagesExplicitSpawnSourceWithoutAssumingExperimentalFilters(bool supportsFilters)
        {
            var calls = 0;
            var result = await AppServerSubAgentOperations.ListAsync("root", supportsFilters,
                (method, parameters, token, timeout) =>
                {
                    var request = (IDictionary<string, object>)parameters;
                    Assert.That(method, Is.EqualTo("thread/list"));
                    Assert.That(request["sourceKinds"], Is.EqualTo(new[] { "subAgentThreadSpawn" }));
                    Assert.That(request.ContainsKey("ancestorThreadId"), Is.EqualTo(supportsFilters));
                    Assert.That(request.ContainsKey("cursor"), Is.EqualTo(calls > 0));
                    calls++;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "data", new object[] { new Dictionary<string, object> { { "id", "child-" + calls }, { "parentThreadId", "root" } } } },
                        { "nextCursor", calls == 1 ? "next" : null }
                    });
                }, value => value, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(result.Count, Is.EqualTo(2));
        }
    }
}
