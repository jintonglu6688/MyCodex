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
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class FormLayoutAdjustmentTests
    {
        [Test]
        public void GitRemoteRepositoriesForm_ShowsCloseButtonInFooter()
        {
            using (var form = new GitRemoteRepositoriesForm(
                       new GitHostingProviderService(new[] { new FakeGitHostingProvider() }),
                       null))
            {
                var closeAccessibleName = UiLocalization.Text("GitRemoteRepositoriesForm.Accessible.Close", "Close");

                Assert.That(
                    FindButtons(form).Any(button => string.Equals(button.AccessibleName, closeAccessibleName, StringComparison.Ordinal)),
                    Is.True);
            }
        }

        [Test]
        public void GitRemoteRepositoriesForm_FilterInputsAndButtonsFitBothRows()
        {
            using (var form = new GitRemoteRepositoriesForm(
                       new GitHostingProviderService(new[] { new FakeGitHostingProvider() }),
                       null))
            {
                form.ShowInTaskbar = false;
                form.Show();
                GuiConsistencyTests.AssertInputRowsFit(form);
            }
        }

        [Test]
        public void WslAppServerSettingsForm_UsesResizableRoomierWindow()
        {
            using (var form = CreateWslSettingsForm())
            {
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(760));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(520));
                Assert.That(form.Size.Width, Is.GreaterThanOrEqualTo(860));
                Assert.That(form.Size.Height, Is.GreaterThanOrEqualTo(640));
            }
        }

        [Test]
        public void WslAppServerSettingsForm_RemotePrivateKeyDiagnosticsExplainsOpenSshKnownHostsPolicy()
        {
            using (var form = CreateWslSettingsForm(
                CodexRuntimeSources.RemoteUnixSsh,
                new RemoteMacSshConfigSnapshot
                {
                    HostName = "192.168.50.2",
                    UserName = "tester",
                    AuthenticationMode = RemoteSshAuthenticationMode.PrivateKey,
                    PrivateKeyPath = @"C:\Users\tester\.ssh\id_ed25519",
                    HostKeyFingerprint = "SHA256:abc123"
                }))
            {
                var diagnostics = FormTestDriver.Field<TextBox>(form, "_diagnosticsTextBox");

                Assert.That(diagnostics.Text, Does.Contain("OpenSSH"));
                Assert.That(diagnostics.Text, Does.Contain("known_hosts"));
                Assert.That(diagnostics.Text, Does.Contain("Plink"));
            }
        }

        [TestCase(null, "zsh", "bash")]
        [TestCase("mac.example", "zsh", "zsh")]
        [TestCase(null, "/bin/sh", "/bin/sh")]
        public void WslAppServerSettingsForm_InitialRemoteShellPreservesConfiguredTargets(
            string hostName, string shell, string expectedShell)
        {
            using (var form = CreateWslSettingsForm(CodexRuntimeSources.RemoteUnixSsh,
                new RemoteMacSshConfigSnapshot { HostName = hostName, ShellCommand = shell }))
            {
                Assert.That(FormTestDriver.Field<TextBox>(form, "_remoteShellTextBox").Text, Is.EqualTo(expectedShell));
            }
        }

        [Test]
        public void ToolUserInputForm_UsesResizableRoomierWindowForDynamicPrompts()
        {
            using (var form = new ToolUserInputForm(new[]
                   {
                       new AppServerToolUserInputQuestion
                       {
                           Id = "goal",
                           Question = "Goal"
                       }
                   }))
            {
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(620));
                Assert.That(form.MinimumSize.Height, Is.GreaterThanOrEqualTo(420));
                Assert.That(form.Size.Width, Is.GreaterThanOrEqualTo(760));
                Assert.That(form.Size.Height, Is.GreaterThanOrEqualTo(560));
            }
        }

        [Test]
        public void MessageSearchForm_UsesRoomierDefaultSizeForWrappedTipAndButtons()
        {
            using (var form = new MessageSearchForm("keyword"))
            {
                Assert.That(form.ClientSize.Width, Is.GreaterThanOrEqualTo(560));
                Assert.That(form.ClientSize.Height, Is.GreaterThanOrEqualTo(220));
            }
        }

        private static WslAppServerSettingsForm CreateWslSettingsForm()
        {
            return CreateWslSettingsForm(CodexRuntimeSources.Bundled, new RemoteMacSshConfigSnapshot());
        }

        private static WslAppServerSettingsForm CreateWslSettingsForm(string runtimeSource, RemoteMacSshConfigSnapshot remoteConfig)
        {
            var rootPath = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, "layout-wsl-form-" + System.IO.Path.GetRandomFileName());
            System.IO.Directory.CreateDirectory(rootPath);
            var appConfigService = new AppConfigService(rootPath);
            return new WslAppServerSettingsForm(
                runtimeSource,
                string.Empty,
                string.Empty,
                new WslAppServerConfigSnapshot(),
                remoteConfig ?? new RemoteMacSshConfigSnapshot(),
                new List<RemoteUnixSshProfileConfigSnapshot>(),
                null,
                new CodexRuntimeManager(appConfigService),
                new CodexRuntimeUpdateService(appConfigService),
                null,
                text => { });
        }

        private static IEnumerable<Button> FindButtons(Control root)
        {
            if (root == null)
            {
                yield break;
            }

            foreach (Control child in root.Controls)
            {
                var button = child as Button;
                if (button != null)
                {
                    yield return button;
                }

                foreach (var descendant in FindButtons(child))
                {
                    yield return descendant;
                }
            }
        }

        private sealed class FakeGitHostingProvider : IGitHostingProvider
        {
            public string Id
            {
                get { return "test"; }
            }

            public string DisplayName
            {
                get { return "Test"; }
            }

            public GitHostingProviderCapabilities Capabilities
            {
                get { return GitHostingProviderCapabilities.RepositoryList; }
            }

            public bool Supports(GitHostingProviderCapabilities capability)
            {
                return capability == GitHostingProviderCapabilities.None ||
                    (Capabilities & capability) == capability;
            }

            public Task<IReadOnlyList<RemoteRepositoryEntry>> ListRepositoriesAsync(
                string keyword,
                int limit,
                CancellationToken cancellationToken)
            {
                return Task.FromResult<IReadOnlyList<RemoteRepositoryEntry>>(new RemoteRepositoryEntry[0]);
            }

            public TCapability GetCapability<TCapability>() where TCapability : class
            {
                return this as TCapability;
            }
        }
    }
}
