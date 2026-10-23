using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationStackTests
    {
        [Test]
        public async Task ClientReadsMembersEligibilityAndNotice()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/stack/get"));
                return Reply(State(true, "a", new Dictionary<string, object>
                {
                    { "notice", "routeOwnsCatalog" },
                    { "classicSubagents", true },
                    { "unifiedSessionHistory", true }
                }, Member("a", true), Member("b", false, "ccs-b/one", "ccs-b/two")));
            });
            var state = await client.ReadStackAsync(CancellationToken.None);
            Assert.That(state.Active, Is.True);
            Assert.That(state.DefaultProviderId, Is.EqualTo("a"));
            Assert.That(state.Members.Select(member => member.ProviderId), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(state.Members[0].IsDefault, Is.True);
            Assert.That(state.Members[1].ModelIds, Is.EqualTo(new[] { "ccs-b/one", "ccs-b/two" }));
            Assert.That(state.Providers.Single(provider => provider.ProviderId == "official").JoinRefusal, Is.EqualTo("stack_official_cannot_join"));
            Assert.That(state.Providers.Single(provider => provider.ProviderId == "a").DefaultRefusal, Is.Null);
            Assert.That(state.Notice, Is.EqualTo("routeOwnsCatalog"));
            Assert.That(state.ClassicSubagents, Is.True);
            Assert.That(state.UnifiedSessionHistory, Is.True);
            Assert.That(state.GlobalApplied, Is.False);
        }

        [Test]
        public async Task ClientSendsTheDefaultAndMembersWhenStarting()
        {
            IDictionary<string, object> sent = null;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/stack/enter"));
                sent = parameters;
                return Reply(State(true, "a", new Dictionary<string, object> { { "globalApplied", true } }, Member("a", true), Member("b", false, "ccs-b/one")));
            });
            var state = await client.EnterStackAsync("a", new[] { "b" }, CancellationToken.None);
            Assert.That(sent.Keys, Is.EquivalentTo(new[] { "defaultProviderId", "memberIds" }));
            Assert.That(sent["defaultProviderId"], Is.EqualTo("a"));
            Assert.That((IEnumerable<string>)sent["memberIds"], Is.EqualTo(new[] { "b" }));
            Assert.That(state.GlobalApplied, Is.True);
        }

        [Test]
        public void ClientRejectsAnOutcomeThatDoesNotMatchTheRequest()
        {
            var inactive = new AuthenticationCenterClient("Test", (method, parameters, token) => Reply(State(false, null, null)));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                inactive.EnterStackAsync("a", new string[0], CancellationToken.None)).Code, Is.EqualTo("operation_unknown"));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                inactive.SetStackMemberAsync("b", true, CancellationToken.None)).Code, Is.EqualTo("operation_unknown"));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                inactive.SetStackClassicSubagentsAsync(true, CancellationToken.None)).Code, Is.EqualTo("operation_unknown"));
            var active = new AuthenticationCenterClient("Test", (method, parameters, token) => Reply(State(true, "a", null, Member("a", true))));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                active.ExitStackAsync(CancellationToken.None)).Code, Is.EqualTo("operation_unknown"));
            var incomplete = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                Reply(new Dictionary<string, object> { { "members", new object[0] } }));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                incomplete.ReadStackAsync(CancellationToken.None)).Code, Is.EqualTo("invalid_response"));
        }

        [Test]
        public void PlannerKeepsTheDefaultListedAndRefusesIneligibleProviders()
        {
            var state = new AuthenticationStackState
            {
                Active = true,
                DefaultProviderId = "a",
                Members = new List<AuthenticationStackMember>
                {
                    new AuthenticationStackMember { ProviderId = "a", IsDefault = true },
                    new AuthenticationStackMember { ProviderId = "b", ModelIds = new List<string> { "ccs-b/one", "ccs-b/two" } },
                    new AuthenticationStackMember { ProviderId = "c" }
                },
                Providers = new List<AuthenticationStackEligibility>
                {
                    new AuthenticationStackEligibility { ProviderId = "official", JoinRefusal = "stack_official_cannot_join" },
                    new AuthenticationStackEligibility { ProviderId = "a" },
                    new AuthenticationStackEligibility { ProviderId = "b" },
                    new AuthenticationStackEligibility { ProviderId = "c" },
                    new AuthenticationStackEligibility { ProviderId = "d" },
                    new AuthenticationStackEligibility { ProviderId = "broken", JoinRefusal = "invalid_provider", DefaultRefusal = "invalid_provider" }
                }
            };
            Assert.That(AuthenticationStackPlanner.RoleOf(state, "a"), Is.EqualTo(AuthenticationStackRole.Default));
            Assert.That(AuthenticationStackPlanner.RoleOf(state, "b"), Is.EqualTo(AuthenticationStackRole.Member));
            Assert.That(AuthenticationStackPlanner.RoleOf(state, "d"), Is.EqualTo(AuthenticationStackRole.None));
            Assert.That(AuthenticationStackPlanner.RoleOf(state, "broken"), Is.EqualTo(AuthenticationStackRole.Unavailable));
            Assert.That(AuthenticationStackPlanner.CanLeave(state, "a"), Is.False);
            Assert.That(AuthenticationStackPlanner.CanLeave(state, "b"), Is.True);
            Assert.That(AuthenticationStackPlanner.CanJoin(state, "d"), Is.True);
            Assert.That(AuthenticationStackPlanner.CanJoin(state, "official"), Is.False);
            Assert.That(AuthenticationStackPlanner.CanJoin(state, "b"), Is.False);
            Assert.That(AuthenticationStackPlanner.CanBeDefault(state, "official"), Is.True);
            Assert.That(AuthenticationStackPlanner.DefaultRefusal(state, "broken"), Is.EqualTo("invalid_provider"));
            Assert.That(AuthenticationStackPlanner.DefaultCandidates(state), Is.EqualTo(new[] { "official", "a", "b", "c", "d" }));
            Assert.That(AuthenticationStackPlanner.MemberCandidates(state, "a"), Is.EqualTo(new[] { "b", "c", "d" }));
            // A member whose models could not be read adds none.
            Assert.That(AuthenticationStackPlanner.PublishingProviders(state), Is.EqualTo(1));
            Assert.That(AuthenticationStackPlanner.PublishedModels(state), Is.EqualTo(2));

            state.Active = false;
            Assert.That(AuthenticationStackPlanner.CanLeave(state, "a"), Is.True);
            Assert.That(AuthenticationStackPlanner.RoleOf(state, "a"), Is.EqualTo(AuthenticationStackRole.Member));
            Assert.That(AuthenticationStackPlanner.RoleOf(null, "a"), Is.EqualTo(AuthenticationStackRole.None));
        }

        [Test]
        public void BusyTargetChangesNothing()
        {
            var coordinator = new AuthenticationStackCoordinator(() => null, () => throw new AssertionException("Must not restart."));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                coordinator.RunAsync(token => throw new AssertionException("Backend must not be called."), CancellationToken.None)).Code,
                Is.EqualTo("runtime_busy"));
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public async Task CodexRestartsOnlyWhenTheClientFilesChanged(bool globalApplied, int restarts)
        {
            var gate = new RuntimeOperationGate();
            var restartCount = 0;
            var coordinator = new AuthenticationStackCoordinator(gate.TryEnterTransition, () =>
            {
                restartCount++;
                return Task.FromResult(true);
            });
            var result = await coordinator.RunAsync(token =>
            {
                // Switching the catalog must not overlap a turn.
                Assert.That(Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation()).Code, Is.EqualTo("runtime_busy"));
                return Task.FromResult(new AuthenticationStackState { Active = true, GlobalApplied = globalApplied });
            }, CancellationToken.None);
            Assert.That(restartCount, Is.EqualTo(restarts));
            Assert.That(result.Reconnected, Is.True);
            Assert.That(result.State.Active, Is.True);
        }

        [Test]
        public async Task FailedReconnectIsReportedAndUncertainWritesAreUnknown()
        {
            var coordinator = new AuthenticationStackCoordinator(new RuntimeOperationGate().TryEnterTransition, () => Task.FromResult(false));
            var result = await coordinator.RunAsync(token => Task.FromResult(new AuthenticationStackState { GlobalApplied = true }), CancellationToken.None);
            Assert.That(result.Reconnected, Is.False);
            foreach (var code in new[] { "stack_write_partial", "ipc_timeout", "backend_unavailable" })
            {
                Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                    coordinator.RunAsync(token => throw new AuthenticationCenterException(code), CancellationToken.None)).Code,
                    Is.EqualTo("operation_unknown"));
            }
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                coordinator.RunAsync(token => throw new AuthenticationCenterException("stack_official_cannot_join"), CancellationToken.None)).Code,
                Is.EqualTo("stack_official_cannot_join"));
        }

        [TestCase("stack_official_cannot_join")]
        [TestCase("stack_official_requires_unified_history")]
        [TestCase("stack_default_cannot_be_removed")]
        [TestCase("stack_member_rejected")]
        [TestCase("stack_operation_failed")]
        public void AggregationRefusalsAreExplained(string code)
        {
            var text = AuthenticationCenterUiText.Error(new AuthenticationCenterException(code));
            Assert.That(text, Is.Not.EqualTo(AuthenticationCenterUiText.Error(new AuthenticationCenterException("unmapped_code"))));
            Assert.That(text, Does.Not.Contain(code));
        }

        private static Dictionary<string, object> Member(string id, bool isDefault, params string[] models)
        {
            return new Dictionary<string, object> { { "providerId", id }, { "isDefault", isDefault }, { "modelIds", models.Cast<object>().ToArray() } };
        }

        private static Dictionary<string, object> State(bool active, string defaultId, Dictionary<string, object> extra,
            params Dictionary<string, object>[] members)
        {
            var value = new Dictionary<string, object>
            {
                { "active", active },
                { "defaultProviderId", defaultId },
                { "members", members.Cast<object>().ToArray() },
                { "providers", new object[]
                    {
                        new Dictionary<string, object> { { "providerId", "official" }, { "joinRefusal", "stack_official_cannot_join" }, { "defaultRefusal", null } },
                        new Dictionary<string, object> { { "providerId", "a" }, { "joinRefusal", null }, { "defaultRefusal", null } },
                        new Dictionary<string, object> { { "providerId", "b" } }
                    }
                }
            };
            if (extra != null)
            {
                foreach (var pair in extra) value[pair.Key] = pair.Value;
            }
            return value;
        }

        private static Task<IDictionary<string, object>> Reply(Dictionary<string, object> value)
        {
            return Task.FromResult<IDictionary<string, object>>(value);
        }
    }
}
