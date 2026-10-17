using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(System.Threading.ApartmentState.STA)]
    public sealed class WslAppServerSettingsFormWindowsSandboxTests
    {
        [Test]
        public void Constructor_DoesNotAnnounceInitialWindowsSandboxStatus()
        {
            var announcements = new List<string>();

            using (var form = CreateForm(new FakeWindowsSandboxClient(), announcements.Add))
            {
                Assert.That(
                    form.WindowsSandboxStatusTextForTests,
                    Does.Contain(UiLocalization.Text(
                        "WslAppServerSettingsForm.WindowsSandbox.Status.NotChecked",
                        "Windows Sandbox: not checked")));
                Assert.That(announcements, Is.Empty);
            }
        }

        [TestCase(AppServerWindowsSandboxReadinessStatus.Ready, "ready", false)]
        [TestCase(AppServerWindowsSandboxReadinessStatus.NotConfigured, "notConfigured", true)]
        [TestCase(AppServerWindowsSandboxReadinessStatus.UpdateRequired, "updateRequired", true)]
        public async Task DetectWindowsSandbox_UpdatesStatusAndSetupAvailability(
            AppServerWindowsSandboxReadinessStatus status,
            string rawStatus,
            bool expectedSetupEnabled)
        {
            var client = new FakeWindowsSandboxClient
            {
                Readiness = new AppServerWindowsSandboxReadiness
                {
                    Status = status,
                    RawStatus = rawStatus
                }
            };

            using (var form = CreateForm(client))
            {
                await form.DetectWindowsSandboxForTestsAsync();

                Assert.That(client.ReadinessCalls, Is.EqualTo(1));
                Assert.That(form.WindowsSandboxStatusTextForTests, Does.Contain(rawStatus).IgnoreCase);
                Assert.That(form.WindowsSandboxSetupEnabledForTests, Is.EqualTo(expectedSetupEnabled));
            }
        }

        [Test]
        public void WindowsSandboxActions_DisabledWhenRuntimeSettingsChanged()
        {
            var client = new FakeWindowsSandboxClient();

            using (var form = CreateForm(client))
            {
                form.SelectRuntimeSourceForTests(CodexRuntimeSources.Wsl);

                Assert.That(form.WindowsSandboxDetectEnabledForTests, Is.False);
                Assert.That(form.WindowsSandboxSetupEnabledForTests, Is.False);
                Assert.That(
                    form.WindowsSandboxDetailsTextForTests,
                    Does.Contain(UiLocalization.Text(
                        "WslAppServerSettingsForm.WindowsSandbox.Detail.SaveRuntimeFirst",
                        "Save and restart App Server before checking Windows Sandbox.")));
            }
        }

        [Test]
        public async Task StartWindowsSandboxSetup_UsesUnelevatedModeAndWaitsForCompletion()
        {
            var client = new FakeWindowsSandboxClient
            {
                Readiness = new AppServerWindowsSandboxReadiness
                {
                    Status = AppServerWindowsSandboxReadinessStatus.NotConfigured,
                    RawStatus = "notConfigured"
                }
            };

            using (var form = CreateForm(client))
            {
                await form.DetectWindowsSandboxForTestsAsync();
                await form.StartWindowsSandboxSetupForTestsAsync();

                Assert.That(client.SetupCalls, Is.EqualTo(1));
                Assert.That(client.LastSetupMode, Is.EqualTo(AppServerWindowsSandboxSetupMode.Unelevated));
                Assert.That(form.WindowsSandboxDetectEnabledForTests, Is.False);
                Assert.That(form.WindowsSandboxSetupEnabledForTests, Is.False);

                form.ApplyWindowsSandboxSetupCompletedForTests(new AppServerWindowsSandboxSetupCompletedEvent
                {
                    Mode = AppServerWindowsSandboxSetupMode.Unelevated,
                    RawMode = "unelevated",
                    Success = true
                });

                Assert.That(form.WindowsSandboxDetailsTextForTests, Does.Contain("completed").Or.Contain("完成"));
            }
        }

        private static WslAppServerSettingsForm CreateForm(FakeWindowsSandboxClient client)
        {
            return CreateForm(client, text => { });
        }

        private static WslAppServerSettingsForm CreateForm(FakeWindowsSandboxClient client, System.Action<string> announce)
        {
            var rootPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, "sandbox-form-" + Path.GetRandomFileName());
            Directory.CreateDirectory(rootPath);
            var appConfigService = new AppConfigService(rootPath);
            return new WslAppServerSettingsForm(
                CodexRuntimeSources.Bundled,
                string.Empty,
                string.Empty,
                new WslAppServerConfigSnapshot(),
                new RemoteMacSshConfigSnapshot(),
                new List<RemoteUnixSshProfileConfigSnapshot>(),
                null,
                new CodexRuntimeManager(appConfigService),
                new CodexRuntimeUpdateService(appConfigService),
                client,
                announce);
        }

        private sealed class FakeWindowsSandboxClient : IAppServerWindowsSandboxClient
        {
            public int ReadinessCalls { get; private set; }

            public int SetupCalls { get; private set; }

            public AppServerWindowsSandboxSetupMode LastSetupMode { get; private set; }

            public string LastSetupCwd { get; private set; }

            public AppServerWindowsSandboxReadiness Readiness { get; set; }

            public AppServerWindowsSandboxSetupStartResult SetupResult { get; set; }

            public Task<AppServerWindowsSandboxReadiness> ReadWindowsSandboxReadinessAsync()
            {
                ReadinessCalls++;
                return Task.FromResult(Readiness ?? new AppServerWindowsSandboxReadiness
                {
                    Status = AppServerWindowsSandboxReadinessStatus.Ready,
                    RawStatus = "ready"
                });
            }

            public Task<AppServerWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(
                AppServerWindowsSandboxSetupMode mode,
                string cwd = null)
            {
                SetupCalls++;
                LastSetupMode = mode;
                LastSetupCwd = cwd;
                return Task.FromResult(SetupResult ?? new AppServerWindowsSandboxSetupStartResult { Started = true });
            }
        }
    }
}
