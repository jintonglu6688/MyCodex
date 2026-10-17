using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class EmbeddedRelayFirewallFormTests
    {
        [Test]
        public async Task MissingRule_OnlyExplicitAllowRequestsPrivateSubnetAccess()
        {
            var service = new FakeFirewall();
            using (var form = new EmbeddedRelayFirewallForm(service))
            {
                await form.RefreshAsync();
                Assert.That(service.AllowCalls, Is.Zero);
                Assert.That(Get<Button>(form, "_allow").Enabled, Is.True);
                await form.AllowAsync();
                Assert.That(service.AllowCalls, Is.EqualTo(1));
                Assert.That(Get<Button>(form, "_allow").Enabled, Is.False);
                Assert.That(Get<Label>(form, "_status").Text, Does.Contain(UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.FirewallAllowed", "Windows Firewall allows the built-in Remote Service to receive connections from the private local subnet.")));
            }
        }

        [Test]
        public async Task CanceledElevation_ReportsNoChangeAndAllowsRetry()
        {
            var service = new FakeFirewall
            {
                Result = new EmbeddedRelayFirewallAllowResult { Canceled = true, Status = Status(EmbeddedRelayFirewallState.RuleMissing) }
            };
            using (var form = new EmbeddedRelayFirewallForm(service))
            {
                await form.RefreshAsync();
                await form.AllowAsync();
                Assert.That(Get<Label>(form, "_status").Text, Is.EqualTo(UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.FirewallCanceled", "Windows Firewall was not changed.")));
                Assert.That(Get<Button>(form, "_allow").Enabled, Is.True);
                Assert.That(service.AllowCalls, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task FailedRefresh_ShowsErrorAndDoesNotRetainStalePermissionToAllow()
        {
            var service = new FakeFirewall();
            using (var form = new EmbeddedRelayFirewallForm(service))
            {
                await form.RefreshAsync();
                service.FailRead = true;
                await form.RefreshAsync();
                Assert.That(Get<Label>(form, "_status").Text, Does.Contain("inspection failed"));
                Assert.That(Get<Button>(form, "_allow").Enabled, Is.False);
                await form.AllowAsync();
                Assert.That(service.AllowCalls, Is.Zero);
            }
        }

        [Test]
        public async Task FailedAllowWithoutDetails_ReportsFailureAndKeepsRetryAvailable()
        {
            var service = new FakeFirewall
            {
                Result = new EmbeddedRelayFirewallAllowResult { Success = false, Status = Status(EmbeddedRelayFirewallState.RuleMissing) }
            };
            using (var form = new EmbeddedRelayFirewallForm(service))
            {
                await form.RefreshAsync();
                await form.AllowAsync();
                Assert.That(Get<Label>(form, "_status").Text, Does.Contain(UiLocalization.Text(
                    "RemoteServiceManagementForm.Message.FirewallAllowFailed", "Failed to allow local-network access through Windows Firewall.")));
                Assert.That(Get<Button>(form, "_allow").Enabled, Is.True);
            }
        }

        [Test]
        public async Task BusyAllow_DisablesCloseAndIgnoresDuplicateAction()
        {
            var service = new FakeFirewall { Pending = new TaskCompletionSource<EmbeddedRelayFirewallAllowResult>() };
            using (var form = new EmbeddedRelayFirewallForm(service))
            {
                await form.RefreshAsync();
                var applying = form.AllowAsync();
                await form.AllowAsync();
                var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
                typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { closing });
                Assert.That(closing.Cancel, Is.True);
                Assert.That(Get<Button>(form, "_allow").Enabled, Is.False);
                Assert.That(Get<Button>(form, "_close").Enabled, Is.False);
                Assert.That(service.AllowCalls, Is.EqualTo(1));
                service.Pending.SetResult(service.Result);
                await applying;
                Assert.That(Get<Button>(form, "_close").Enabled, Is.True);
            }
        }

        [TestCase(EmbeddedRelayFirewallState.RuleMissing, "_allow")]
        [TestCase(EmbeddedRelayFirewallState.NonPrivateNetwork, "_close")]
        public void ShownDialog_HasNativeFocusAndAccessibleResizableControls(EmbeddedRelayFirewallState state, string focusField)
        {
            var service = new FakeFirewall { Current = Status(state) };
            using (var form = new EmbeddedRelayFirewallForm(service))
            using (var closer = new System.Windows.Forms.Timer { Interval = 20 })
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-2000, -2000);
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(480));
                Assert.That(Get<Button>(form, "_refresh").TabStop, Is.False);
                Assert.That(Get<Label>(form, "_status").TabStop, Is.False);
                Assert.That(form.CancelButton, Is.SameAs(Get<Button>(form, "_close")));
                foreach (var field in new[] { "_allow", "_close" })
                    Assert.That(Get<Button>(form, field).AccessibleName, Is.Not.Empty);
                var focused = false;
                var timeout = Stopwatch.StartNew();
                closer.Tick += (s, e) =>
                {
                    if (Get<bool>(form, "_busy"))
                    {
                        if (timeout.Elapsed > TimeSpan.FromSeconds(5)) form.Dispose();
                        return;
                    }
                    focused = Get<Button>(form, focusField).Focused;
                    form.Close();
                };
                closer.Start();
                form.ShowDialog();
                closer.Stop();
                Assert.That(focused, Is.True);
                Assert.That(service.AllowCalls, Is.Zero);
            }
        }

        private static T Get<T>(object target, string name)
        {
            return (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static EmbeddedRelayFirewallStatus Status(EmbeddedRelayFirewallState state)
        {
            return new EmbeddedRelayFirewallStatus { State = state };
        }

        private sealed class FakeFirewall : IEmbeddedRelayFirewallService
        {
            public EmbeddedRelayFirewallStatus Current = Status(EmbeddedRelayFirewallState.RuleMissing);
            public EmbeddedRelayFirewallAllowResult Result = new EmbeddedRelayFirewallAllowResult { Success = true, Status = Status(EmbeddedRelayFirewallState.Allowed) };
            public TaskCompletionSource<EmbeddedRelayFirewallAllowResult> Pending;
            public bool FailRead;
            public int AllowCalls;

            public EmbeddedRelayFirewallStatus GetStatus()
            {
                if (FailRead) throw new InvalidOperationException("inspection failed");
                return Current;
            }

            public Task<EmbeddedRelayFirewallAllowResult> AllowPrivateLocalSubnetAsync(CancellationToken cancellationToken)
            {
                AllowCalls++;
                return Pending?.Task ?? Task.FromResult(Result);
            }
        }
    }
}
