using System;
using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormTrayNotificationSourceTests
    {
        [Test]
        public void TargetEventsAndTrayHide_DoNotCreateDesktopNotifications()
        {
            var formsDirectory = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms",
                "Forms"));
            var mainSource = File.ReadAllText(Path.Combine(formsDirectory, "MainForm.cs"));
            var traySource = File.ReadAllText(Path.Combine(formsDirectory, "MainForm.UpdatesTray.cs"));
            var hideMethod = ExtractMethod(traySource, "private void HideMainWindowToTray()");
            Assert.That(hideMethod, Does.Contain("CaptureFocusBeforeTrayHide();"));
            Assert.That(hideMethod, Does.Contain("Hide();"));
            Assert.That(mainSource, Does.Not.Contain("_desktopNotificationChannel"));
            Assert.That(traySource, Does.Contain("_trayIcon.Visible = true;"));
            foreach (var path in Directory.GetFiles(formsDirectory, "*.cs"))
            {
                var source = File.ReadAllText(path);
                Assert.That(source, Does.Not.Contain("ShowBalloonTip("), path);
                Assert.That(source, Does.Not.Contain("DesktopNotificationChannel"), path);
            }
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature + " should exist.");
            var nextMethod = source.IndexOf("\r\n        private ", start + signature.Length, StringComparison.Ordinal);
            if (nextMethod < 0)
            {
                nextMethod = source.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
            }

            Assert.That(nextMethod, Is.GreaterThan(start), signature + " should be followed by another method.");
            return source.Substring(start, nextMethod - start);
        }
    }
}
