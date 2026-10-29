using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class BrowserControlOptionsTests
    {
        [Test]
        public void OptionsPageKeepsSwitchPendingAndBrowserActionsExplicit()
        {
            var service = new Service();
            service.Edge.Profiles.Add(new BrowserExtensionProfileStatus { ProfileDirectory = "Default", State = BrowserExtensionInstallState.NotInstalled });
            service.Chrome.Profiles.Add(new BrowserExtensionProfileStatus { ProfileDirectory = "Default", State = BrowserExtensionInstallState.Disabled });
            using (var page = new BrowserControlOptionsPage(service, true, false))
            using (var form = new OptionsForm(new AppConfigSnapshot(), null))
            {
                form.AddBrowserControlPage(page);
                var enabled = FormTestDriver.Field<CheckBox>(page, "_enabled");
                enabled.Checked = true;
                Assert.That(service.Preparations, Is.Zero);
                Assert.That(service.Opens, Is.Empty);
                FormTestDriver.InvokeAndWait(page, "RefreshAsync");
                Assert.That(service.Checks, Is.EqualTo(new[] { BrowserExtensionBrowser.Edge, BrowserExtensionBrowser.Chrome }));
                Assert.That(form.BrowserControlEnabled, Is.True);
                FormTestDriver.Invoke(page, "OpenPage", 0);
                FormTestDriver.Invoke(page, "OpenPage", 1);
                Assert.That(service.Opens, Is.EqualTo(new[] { "Edge:Store", "Chrome:Extensions" }));
                var actions = FormTestDriver.Field<Button[]>(page, "_actions");
                Assert.That(actions[0].TabIndex, Is.EqualTo(1));
                Assert.That(actions[1].TabIndex, Is.EqualTo(2));
                Assert.That(enabled.TabIndex, Is.Zero);
                Assert.That(actions.All(button => !string.IsNullOrWhiteSpace(button.AccessibleName)), Is.True);
                Assert.That(FormTestDriver.Descendants<Panel>(page).All(panel => !panel.TabStop), Is.True);
                form.DialogResult = DialogResult.Cancel;
                Assert.That(service.Preparations, Is.Zero, "Cancel must not prepare or register anything.");
            }
        }

        [Test]
        public void DetectionUpdatesButtonsInPlaceAndKeepsUnknownSeparateFromMissing()
        {
            var service = new Service();
            using (var page = new BrowserControlOptionsPage(service, true, true))
            {
                FormTestDriver.InvokeAndWait(page, "RefreshAsync");
                var actions = FormTestDriver.Field<Button[]>(page, "_actions");
                var original = actions[0];
                var installText = actions[0].Text;
                service.Edge.Profiles.Add(new BrowserExtensionProfileStatus { State = BrowserExtensionInstallState.Enabled });
                service.Chrome.BrowserInstalled = null;
                FormTestDriver.InvokeAndWait(page, "RefreshAsync");
                Assert.That(actions[0], Is.SameAs(original));
                Assert.That(actions[0].Text, Is.Not.EqualTo(installText));
                Assert.That(actions[1].Enabled, Is.False);
                Assert.That(service.Preparations, Is.Zero);
                service.Edge.Profiles.Clear();
                service.Edge.Profiles.Add(new BrowserExtensionProfileStatus { State = BrowserExtensionInstallState.NotInstalled });
                FormTestDriver.InvokeAndWait(page, "RefreshAsync");
                Assert.That(actions[0].Text, Is.EqualTo(installText));
                FormTestDriver.Invoke(page, "OpenPage", 0);
                service.Edge.Profiles[0].State = BrowserExtensionInstallState.Unknown;
                FormTestDriver.InvokeAndWait(page, "RefreshAsync");
                FormTestDriver.Invoke(page, "OpenPage", 0);
                Assert.That(service.Opens, Is.EqualTo(new[] { "Edge:Store", "Edge:Store" }));
            }
        }

        [Test]
        public void UnsupportedTargetPreservesSettingAndDoesNotDetectOrInstall()
        {
            var service = new Service();
            using (var page = new BrowserControlOptionsPage(service, false, true))
            {
                Assert.That(page.Selection, Is.Null);
                Assert.That(FormTestDriver.Field<CheckBox>(page, "_enabled").Enabled, Is.False);
                FormTestDriver.InvokeAndWait(page, "RefreshAsync");
                Assert.That(service.Checks, Is.Empty);
                Assert.That(service.Preparations, Is.Zero);
            }
        }

        [Test]
        public async Task DisposingPageCancelsDetectionAndIgnoresLateResults()
        {
            var service = new Service { Pending = new TaskCompletionSource<BrowserExtensionSetupReport>() };
            var page = new BrowserControlOptionsPage(service, true, false);
            var pending = FormTestDriver.Invoke<Task>(page, "RefreshAsync");
            page.Dispose();
            Assert.That(service.Token.IsCancellationRequested, Is.True);
            service.Pending.SetResult(service.Edge);
            await pending;
            Assert.That(service.Checks.Count, Is.EqualTo(1));
        }

        [TestCase(false, false, 0)]
        [TestCase(false, true, 1)]
        [TestCase(true, false, 1)]
        public async Task PreparationReconnectsOnlyForChanges(bool packageChanged, bool settingChanged, int expected)
        {
            var service = new Service { Changed = packageChanged };
            var reconnects = 0;
            var released = 0;
            using (var owner = new Form())
            using (var coordinator = new BrowserControlOptionsCoordinator(owner, null, service,
                () => new Lease(() => released++), () => { reconnects++; return Task.FromResult(true); }, _ => { }, run => run()))
            {
                await coordinator.ApplyAsync(true, settingChanged);
                Assert.That(service.Preparations, Is.EqualTo(1));
                Assert.That(reconnects, Is.EqualTo(expected));
                Assert.That(released, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task ActiveWorkDefersPreparationAndTurningOffReplacesPendingEnable()
        {
            var service = new Service();
            var busy = true;
            var reconnects = 0;
            using (var owner = new Form())
            using (var coordinator = new BrowserControlOptionsCoordinator(owner, null, service,
                () => busy ? null : new Lease(() => { }), () => { reconnects++; return Task.FromResult(true); }, _ => { }, run => run()))
            {
                await coordinator.ApplyAsync(true, true);
                Assert.That(service.Preparations, Is.Zero);
                Assert.That(reconnects, Is.Zero);
                await coordinator.ApplyAsync(false, true);
                busy = false;
                await FormTestDriver.Invoke<Task>(coordinator, "TryApplyAsync");
                Assert.That(service.Preparations, Is.Zero);
                Assert.That(reconnects, Is.EqualTo(1));
            }
        }

        private sealed class Lease : IDisposable
        {
            private readonly Action _release;
            internal Lease(Action release) { _release = release; }
            public void Dispose() { _release(); }
        }

        private sealed class Service : IBrowserExtensionSetupService
        {
            internal readonly BrowserExtensionSetupReport Edge = new BrowserExtensionSetupReport { BrowserInstalled = true };
            internal readonly BrowserExtensionSetupReport Chrome = new BrowserExtensionSetupReport { BrowserInstalled = true };
            internal readonly List<BrowserExtensionBrowser> Checks = new List<BrowserExtensionBrowser>();
            internal readonly List<string> Opens = new List<string>();
            internal int Preparations;
            internal bool Changed;
            internal CancellationToken Token;
            internal TaskCompletionSource<BrowserExtensionSetupReport> Pending;
            public Task<BrowserExtensionSetupReport> CheckAsync(BrowserExtensionBrowser browser, CancellationToken token)
            {
                Checks.Add(browser);
                Token = token;
                return Pending?.Task ?? Task.FromResult(browser == BrowserExtensionBrowser.Edge ? Edge : Chrome);
            }
            public Task<bool> PrepareAsync(CancellationToken token) { Preparations++; return Task.FromResult(Changed); }
            public void OpenPage(BrowserExtensionBrowser browser, BrowserExtensionSetupPage page) { Opens.Add(browser + ":" + page); }
        }
    }
}
