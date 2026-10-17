using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SessionCommandTerminalHostTests
    {
        [Test]
        public void TerminalWindow_ForwardsScreenReaderHotkeysThroughItsHost()
        {
            var rootPath = Path.Combine(Path.GetTempPath(), "MyCodex.SessionCommandTerminalHostTests", Guid.NewGuid().ToString("N"));
            var appConfigService = new AppConfigService(rootPath);
            appConfigService.EnsureConfigDirectory();
            using (var client = new AppServerClient(
                new CodexRuntimeManager(appConfigService),
                new JsonAppConfigStore(appConfigService)))
            {
                var host = new SessionCommandTerminalHost(
                    client,
                    new ChatSession { Id = "session-1", ThreadId = "thread-1" },
                    new WorkspaceInfo { Id = "workspace-1", Name = "Workspace", RootPath = rootPath },
                    session => ApprovalMode.Auto,
                    () => Task.FromResult(true));
                Keys forwarded = Keys.None;
                host.ScreenReaderHotkeyPressed += (sender, e) =>
                {
                    forwarded = e.KeyData;
                    e.Handled = true;
                };

                host.Show();
                var form = host.Window as CommandTerminalForm;

                Assert.That(form, Is.Not.Null);
                Assert.That(form.ProcessTerminalShortcutForTesting(Keys.Alt | Keys.Down), Is.True);
                Assert.That(forwarded, Is.EqualTo(Keys.Alt | Keys.Down));

                host.Close();
            }
        }
    }
}
