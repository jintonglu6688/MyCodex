using System.Threading;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class LiveWorkStatusPresentationTests
    {
        [Test]
        public void SessionListItem_UpdatesTextInPlaceWithoutReplacingTheFocusedItem()
        {
            var entry = new SessionListEntry(new ChatSession { Title = "Feature" }, null);
            var presenter = new SessionListItem(entry, true, false);
            var item = presenter.CreateListViewItem();
            using (var form = new Form())
            using (var list = new ListView { Dock = DockStyle.Fill })
            {
                form.Controls.Add(list);
                list.Items.Add(item);
                form.Show();
                list.Focus();
                item.Selected = true;
                item.Focused = true;
                System.Windows.Forms.Application.DoEvents();

                Assert.That(item.Focused, Is.True, "Test setup must give the item keyboard focus.");

                presenter.UpdateWorkingState(item, true);

                Assert.That(list.Items[0], Is.SameAs(item));
                Assert.That(item.Selected, Is.True);
                Assert.That(item.Focused, Is.True);
                Assert.That(
                    item.Text,
                    Does.StartWith(UiLocalization.Text("MainForm.WindowStatus.Working", "Working")));
                Assert.That(
                    item.Text,
                    Does.EndWith(UiLocalization.Text("MainForm.ListMarker.Current", " (Current)")));
                Assert.That(item.ToolTipText, Is.EqualTo(item.Text));
            }
        }

        [Test]
        public void WorkspaceListItem_UpdatesOnlyTheNameCellInPlace()
        {
            var presenter = new WorkspaceListItem(
                new WorkspaceInfo { Name = "Repo", RootPath = @"E:\Repo", GitBranch = "main" },
                true,
                AppServerHostPlatform.Windows,
                null,
                false,
                0);
            var item = presenter.CreateListViewItem(false);
            using (var list = new ListView())
            {
                list.Items.Add(item);
                var branch = item.SubItems[1].Text;

                presenter.UpdateWorkingSessionCount(item, 3);

                Assert.That(list.Items[0], Is.SameAs(item));
                Assert.That(
                    item.Text,
                    Is.EqualTo(string.Format(
                        UiLocalization.Text("MainForm.ListStatus.Prefix.Template", "{0}: {1}"),
                        string.Format(
                            UiLocalization.Text("MainForm.WorkspaceList.WorkingSessionCount.Template", "{0} sessions working"),
                            3),
                        "Repo")));
                Assert.That(item.SubItems[1].Text, Is.EqualTo(branch));
            }
        }
    }
}
