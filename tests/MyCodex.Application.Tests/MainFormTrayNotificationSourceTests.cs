using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormTrayNotificationSourceTests
    {
        [Test]
        public void TargetEventsAndTrayHide_DoNotCreateDesktopNotifications()
        {
            var mainSource = RepositorySource.ReadSourceFile("MainForm.cs");
            var traySource = RepositorySource.ReadSourceFile("MainForm.UpdatesTray.cs");
            var hideMethod = RepositorySource.ExtractUntilNextPrivateMember(traySource, "private void HideMainWindowToTray()");
            Assert.That(hideMethod, Does.Contain("CaptureFocusBeforeTrayHide();"));
            Assert.That(hideMethod, Does.Contain("Hide();"));
            Assert.That(mainSource, Does.Not.Contain("_desktopNotificationChannel"));
            Assert.That(traySource, Does.Contain("_trayIcon.Visible = true;"));
            foreach (var path in RepositorySource.FindSourceFiles("MyCodex.WinForms", "*.cs"))
            {
                var source = File.ReadAllText(path);
                Assert.That(source, Does.Not.Contain("ShowBalloonTip("), path);
                Assert.That(source, Does.Not.Contain("DesktopNotificationChannel"), path);
            }
        }
    }
}
