using System;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MinimizedActivationRestorerTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void RestoresMinimizedWindowOnlyWhileItIsForeground()
        {
            using (var form = new Form { ShowInTaskbar = false })
            {
                var restorer = new MinimizedActivationRestorer(form);
                var foreground = new IntPtr(-1);
                restorer.ForegroundWindowProvider = () => foreground;
                form.Show();
                form.WindowState = FormWindowState.Minimized;

                restorer.RestoreIfStillMinimized();
                Assert.That(form.WindowState, Is.EqualTo(FormWindowState.Minimized), "Another window keeps the foreground.");

                foreground = form.Handle;
                restorer.RestoreIfStillMinimized();
                Assert.That(form.WindowState, Is.EqualTo(FormWindowState.Normal));
                form.Hide();
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void RestoresMaximizedWindowToMaximized()
        {
            using (var form = new Form { ShowInTaskbar = false })
            {
                var restorer = new MinimizedActivationRestorer(form) { ForegroundWindowProvider = () => form.Handle };
                form.Show();
                form.WindowState = FormWindowState.Maximized;
                form.WindowState = FormWindowState.Minimized;

                restorer.RestoreIfStillMinimized();

                Assert.That(form.WindowState, Is.EqualTo(FormWindowState.Maximized));
                form.Hide();
            }
        }
    }
}
