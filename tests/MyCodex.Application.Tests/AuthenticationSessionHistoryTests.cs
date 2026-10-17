using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationSessionHistoryTests
    {
        [Test]
        public async Task ClientSendsOnlyTheSettingAndReadsTheMove()
        {
            IDictionary<string, object> sent = null;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/sessionHistory/set"));
                sent = parameters;
                return Reply(State(true, false, new Dictionary<string, object>
                {
                    { "globalApplied", true },
                    { "moved", new Dictionary<string, object> { { "sessions", 2 }, { "indexRows", 3 } } }
                }));
            });
            var result = await client.SetSessionHistoryAsync(true, CancellationToken.None);
            Assert.That(sent.Keys, Is.EquivalentTo(new[] { "unified" }));
            Assert.That(result.State.Unified, Is.True);
            Assert.That(result.State.Pending, Is.False);
            Assert.That(result.GlobalApplied, Is.True);
            Assert.That(result.Moved.Sessions, Is.EqualTo(2));
            Assert.That(result.Moved.IndexRows, Is.EqualTo(3));
            Assert.That(result.Moved.Moved, Is.True);
            Assert.That(result.Moved.Error, Is.Null);
        }

        [Test]
        public void ClientRejectsAnOutcomeThatDoesNotMatchTheRequest()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                Reply(State(false, false, new Dictionary<string, object> { { "globalApplied", true } })));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                client.SetSessionHistoryAsync(true, CancellationToken.None)).Code, Is.EqualTo("operation_unknown"));
            var incomplete = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                Reply(new Dictionary<string, object> { { "unified", true } }));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                incomplete.ReadSessionHistoryAsync(CancellationToken.None)).Code, Is.EqualTo("invalid_response"));
        }

        [Test]
        public async Task PreviewReadsBothDirections()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/sessionHistory/preview"));
                return Reply(new Dictionary<string, object> { { "toMigrate", 12 }, { "toRestore", 4 } });
            });
            var preview = await client.PreviewSessionHistoryAsync(CancellationToken.None);
            Assert.That(preview.ToMigrate, Is.EqualTo(12));
            Assert.That(preview.ToRestore, Is.EqualTo(4));
        }

        [Test]
        public void BusyTargetChangesNothing()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) => throw new AssertionException("Backend must not be called."));
            var coordinator = new AuthenticationApplyCoordinator(client, () => null, () => throw new AssertionException("Must not restart."));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                coordinator.SetSessionHistoryAsync(true, CancellationToken.None)).Code, Is.EqualTo("runtime_busy"));
        }

        [TestCase(false, null, false)]
        [TestCase(true, null, true)]
        [TestCase(false, 1, true)]
        [TestCase(false, 0, false)]
        public async Task CodexRestartsOnlyWhenTheRouteOrSessionsChanged(bool globalApplied, int? moved, bool restarts)
        {
            var gate = new RuntimeOperationGate();
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                // Moving sessions must not overlap a turn that writes them.
                Assert.That(Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation()).Code, Is.EqualTo("runtime_busy"));
                var extra = new Dictionary<string, object> { { "globalApplied", globalApplied } };
                if (moved.HasValue)
                    extra["moved"] = new Dictionary<string, object> { { "sessions", moved.Value }, { "indexRows", 0 } };
                return Reply(State(true, false, extra));
            });
            var restartCount = 0;
            var coordinator = new AuthenticationApplyCoordinator(client, gate.TryEnterTransition, () =>
            {
                restartCount++;
                return Task.FromResult(true);
            });
            var result = await coordinator.SetSessionHistoryAsync(true, CancellationToken.None);
            Assert.That(restartCount, Is.EqualTo(restarts ? 1 : 0));
            Assert.That(result.Reconnected, Is.True);
        }

        [Test]
        public async Task FailedMoveStillRestartsAndLostReplyIsUnknown()
        {
            var failed = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                Reply(State(true, true, new Dictionary<string, object>
                {
                    { "globalApplied", false },
                    { "moved", new Dictionary<string, object> { { "sessions", 0 }, { "indexRows", 0 }, { "error", "history_move_failed" } } }
                })));
            var restarts = 0;
            var coordinator = new AuthenticationApplyCoordinator(failed, new RuntimeOperationGate().TryEnterTransition, () =>
            {
                restarts++;
                return Task.FromResult(false);
            });
            var result = await coordinator.SetSessionHistoryAsync(true, CancellationToken.None);
            Assert.That(restarts, Is.EqualTo(1));
            Assert.That(result.Reconnected, Is.False);
            Assert.That(result.State.Pending, Is.True);

            var lost = new AuthenticationCenterClient("Test", (method, parameters, token) => throw new AuthenticationCenterException("ipc_disconnected"));
            var unknown = new AuthenticationApplyCoordinator(lost, new RuntimeOperationGate().TryEnterTransition, () => throw new AssertionException("Must not restart."));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                unknown.SetSessionHistoryAsync(false, CancellationToken.None)).Code, Is.EqualTo("operation_unknown"));
        }

        [Test]
        public void ConfirmationShowsTheExplanationReadOnly()
        {
            using (var form = new AuthenticationSessionHistoryConfirmForm("Title", "Message") { ShowInTaskbar = false })
            {
                var details = Descendants(form).OfType<TextBox>().Single();
                Assert.That(details.ReadOnly, Is.True);
                Assert.That(details.Text, Is.EqualTo("Message"));
                Assert.That(details.TabIndex, Is.EqualTo(0));
                Assert.That(form.AcceptButton, Is.Not.Null);
                Assert.That(form.CancelButton, Is.Not.Null);
                Assert.That(Descendants(form).OfType<CheckBox>(), Is.Empty);
            }
        }

        private static Dictionary<string, object> State(bool unified, bool pending, Dictionary<string, object> extra)
        {
            var value = new Dictionary<string, object> { { "unified", unified }, { "pending", pending } };
            foreach (var pair in extra) value[pair.Key] = pair.Value;
            return value;
        }

        private static Task<IDictionary<string, object>> Reply(Dictionary<string, object> value)
        {
            return Task.FromResult<IDictionary<string, object>>(value);
        }

        private static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                yield return control;
                foreach (var child in Descendants(control)) yield return child;
            }
        }
    }
}
