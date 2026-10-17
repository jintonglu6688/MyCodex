using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Theming;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class TargetWindowGroupTests
    {
        [Test]
        public void UnownedWindowsKeepTheirConstructionTargetAcrossRefreshRenameAndClose()
        {
            var name = "Ubuntu";
            using (var mainA = Window("MyCodex"))
            using (var groupA = new TargetWindowGroup(mainA, () => name))
            using (var childA = Window("README.md"))
            using (var mainB = Window("MyCodex"))
            using (var groupB = new TargetWindowGroup(mainB, () => "Development"))
            using (var childB = Window("README.md"))
            {
                childA.Show();
                childB.Show();
                Assert.That(childA.Owner, Is.Null);
                Assert.That(childA.Text, Is.EqualTo("Ubuntu - README.md"));
                Assert.That(childB.Text, Is.EqualTo("Development - README.md"));
                Assert.That(groupA.GetForms(), Is.EquivalentTo(new[] { childA }));
                Assert.That(groupB.GetForms(), Is.EquivalentTo(new[] { childB }));
                childA.Text = "* README.md - main";
                name = "Renamed";
                groupA.RefreshTitles();
                groupA.RefreshTitles();
                Assert.That(childA.Text, Is.EqualTo("Renamed - * README.md - main"));
                Assert.That(childB.Text, Is.EqualTo("Development - README.md"));
                var cancel = true;
                childA.FormClosing += (sender, args) => args.Cancel = cancel;
                childA.Close();
                Assert.That(childA.IsDisposed, Is.False, "Original save/cancel handlers must remain effective.");
                cancel = false;
                foreach (var child in groupA.GetForms()) child.Close();
                Assert.That(groupA.GetForms(), Is.Empty);
                Assert.That(childB.Visible, Is.True);
                Assert.That(mainA.IsDisposed, Is.False);
            }
        }

        [Test]
        public void WindowsGroupLeavesAllTitlesUnprefixedAndAvoidsDuplicateExistingPrefixes()
        {
            using (var main = Window("MyCodex - Coding Mode"))
            using (var group = new TargetWindowGroup(main, () => null))
            using (var files = Window("Files"))
            {
                files.Show();
                files.Text = "* README.md";
                group.RefreshTitles();
                Assert.That(main.Text, Is.EqualTo("MyCodex - Coding Mode"));
                Assert.That(files.Text, Is.EqualTo("* README.md"));
            }
            using (var main = Window("Ubuntu - Working - MyCodex - Office Mode"))
            using (var group = new TargetWindowGroup(main, () => "Ubuntu"))
            {
                group.RefreshTitles();
                Assert.That(main.Text, Is.EqualTo("Ubuntu - Working - MyCodex - Office Mode"));
            }
        }

        [Test]
        public void NestedModalOfUnownedAuxiliaryBelongsToItsLogicalGroup()
        {
            using (var main = Window("MyCodex"))
            using (var group = new TargetWindowGroup(main, () => "Ubuntu"))
            using (var auxiliary = Window("Repository"))
            using (var dialog = Window("Approval"))
            {
                auxiliary.Show();
                Exception failure = null;
                dialog.Shown += (sender, args) => dialog.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Assert.That(group.FindModal(), Is.SameAs(dialog));
                        Assert.That(group.TryActivateModal(), Is.True);
                        Assert.That(dialog.Text, Is.EqualTo("Ubuntu - Approval"));
                        Assert.That(IsWindowEnabled(auxiliary.Handle), Is.False);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { dialog.Close(); }
                }));
                dialog.ShowDialog(auxiliary);
                Assert.That(failure, Is.Null);
                Assert.That(IsWindowEnabled(auxiliary.Handle), Is.True);
            }
        }

        [Test]
        public void NestedModalSelectionUsesNativeEnabledStateAfterRegistrySlotReuse()
        {
            using (var main = Window("MyCodex"))
            using (var group = new TargetWindowGroup(main, () => "Ubuntu"))
            using (var temporary = Window("Temporary"))
            using (var outer = Window("Settings"))
            using (var inner = Window("Confirmation"))
            {
                main.Show();
                temporary.Show();
                Exception failure = null;
                inner.Shown += (sender, args) => inner.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        Assert.That(outer.Enabled, Is.True, "WinForms keeps the managed property enabled for modal owners.");
                        Assert.That(IsWindowEnabled(outer.Handle), Is.False);
                        Assert.That(IsWindowEnabled(inner.Handle), Is.True);
                        Assert.That(group.FindModal(), Is.SameAs(inner));
                        Assert.That(group.TryActivateModal(), Is.True);
                        // Form.ActiveForm follows the desktop foreground, which may belong to another app.
                        // Verify activation on this target's UI thread without requiring desktop focus.
                        var activationWait = Stopwatch.StartNew();
                        while (GetActiveWindow() != inner.Handle && activationWait.ElapsedMilliseconds < 2000)
                        {
                            System.Windows.Forms.Application.DoEvents();
                            Thread.Sleep(1);
                        }
                        Assert.That(GetActiveWindow(), Is.EqualTo(inner.Handle));
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { inner.Close(); }
                }));
                outer.Shown += (sender, args) => outer.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // The inner registration reuses an earlier dictionary slot than the outer.
                        temporary.Dispose();
                        inner.ShowDialog(outer);
                    }
                    catch (Exception ex) { failure = ex; }
                    finally { outer.Close(); }
                }));
                outer.ShowDialog(main);
                Assert.That(failure, Is.Null);
                Assert.That(IsWindowEnabled(main.Handle), Is.True);
            }
        }

        [TestCase(1)]
        [TestCase(30)]
        public void NativeErrorDialogGetsTargetTitleAndCanBeActivatedFromItsGroup(int interval)
        {
            using (var main = Window("MyCodex"))
            using (var group = new TargetWindowGroup(main, () => "Ubuntu"))
            using (var timer = new System.Windows.Forms.Timer { Interval = interval })
            {
                main.Show();
                group.TrackNativeDialogs();
                string caption = null;
                var activated = false;
                var deadline = Stopwatch.StartNew();
                timer.Tick += (sender, args) =>
                {
                    EnumThreadWindows(GetCurrentThreadId(), (handle, state) =>
                    {
                        var className = new StringBuilder(64);
                        GetClassName(handle, className, className.Capacity);
                        if (className.ToString() != "#32770" || GetWindow(handle, 4) != main.Handle ||
                            !IsWindowVisible(handle) || !IsWindowEnabled(handle)) return true;
                        timer.Stop();
                        var text = new StringBuilder(256);
                        GetWindowText(handle, text, text.Capacity);
                        caption = text.ToString();
                        activated = group.TryActivateModal();
                        PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                        return true;
                    }, IntPtr.Zero);
                    if (deadline.ElapsedMilliseconds > 3000) main.Close();
                };
                timer.Start();
                MessageBox.Show(main, "Failure details", "Failure", MessageBoxButtons.OK, MessageBoxIcon.Error);
                timer.Stop();
                Assert.That(caption, Is.EqualTo("Ubuntu - Failure"));
                Assert.That(activated, Is.True);
                Assert.That(group.HasNativeModal, Is.False);
            }
        }

        private static Form Window(string title)
        {
            var form = new Form { Text = title, ShowInTaskbar = false, StartPosition = FormStartPosition.Manual,
                Location = new Point(-2000, -2000), Size = new Size(260, 140) };
            UiTheme.ApplyForm(form);
            return form;
        }

        private delegate bool EnumWindow(IntPtr window, IntPtr state);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, EnumWindow callback, IntPtr state);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr GetActiveWindow();
    }
}
