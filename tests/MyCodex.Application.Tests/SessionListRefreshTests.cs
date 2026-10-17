using System;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SessionListRefreshTests
    {
        [TestCase("same")]
        [TestCase("rename")]
        [TestCase("insert")]
        [TestCase("remove")]
        [TestCase("display-order")]
        public void BackgroundRefreshKeepsBrowsedSessionAndViewportWithoutReselecting(string change)
        {
            using (var form = new Form())
            using (var list = new ListView { Dock = DockStyle.Fill, View = View.Details, MultiSelect = false })
            {
                list.Columns.Add("Sessions", 240);
                form.Controls.Add(list);
                form.Show();
                SessionListItem.RefreshList(list, Items(), 0);
                list.Items[0].Selected = false;
                list.Items[35].Selected = true;
                list.Items[35].Focused = true;
                list.TopItem = list.Items[30];
                var selected = list.SelectedItems[0];
                var top = list.TopItem;
                var changed = 0;
                list.SelectedIndexChanged += (sender, args) => changed++;
                var next = Items().ToList();
                if (change == "rename") next[1].Text = "Renamed session";
                if (change == "insert") next.Insert(1, new SessionListItem(new SessionListEntry(new ChatSession { Id = "new" }, null), false, false).CreateListViewItem());
                if (change == "remove") next.RemoveAt(1);
                if (change == "display-order") next = Items(displayOrder: SessionListDisplayOrder.TitleTimeAgents).ToList();
                SessionListItem.RefreshList(list, next.ToArray(), 0);
                SessionListItem.RefreshList(list, next.ToArray(), 0);
                Assert.Multiple(() =>
                {
                    Assert.That(list.SelectedItems.Cast<ListViewItem>().Single(), Is.SameAs(selected));
                    Assert.That(list.TopItem, Is.SameAs(top));
                    Assert.That(changed, Is.Zero);
                    Assert.That(list.Items.Count, Is.EqualTo(next.Count));
                    Assert.That(list.FocusedItem, Is.SameAs(selected));
                    if (change == "display-order")
                    {
                        Assert.That(selected.Text, Is.EqualTo(next[35].Text));
                        Assert.That(selected.ToolTipText, Is.EqualTo(selected.Text));
                    }
                    if (change == "rename") Assert.That(list.Items[1].Text, Is.EqualTo("Renamed session"));
                });
            }
        }

        [Test]
        public void ExplicitCurrentSessionChangeSelectsNewSessionAndDeletionRemovesOldSelection()
        {
            using (var form = new Form())
            using (var list = new ListView { Dock = DockStyle.Fill, View = View.Details })
            {
                list.Columns.Add("Sessions", 240);
                form.Controls.Add(list);
                form.Show();
                SessionListItem.RefreshList(list, Items(), 0);
                var next = Items(40);
                SessionListItem.RefreshList(list, next, 40);
                Assert.That(((SessionListItem)list.SelectedItems[0].Tag).Session.Id, Is.EqualTo("40"));
                SessionListItem.RefreshList(list, Items().Where(item => ((SessionListItem)item.Tag).Session.Id != "40").ToArray(), 0);
                Assert.That(((SessionListItem)list.SelectedItems[0].Tag).Session.Id, Is.EqualTo("0"));
            }
        }

        private static ListViewItem[] Items(int current = 0, string displayOrder = SessionListDisplayOrder.AgentsTimeTitle)
        {
            return Enumerable.Range(0, 60).Select(i => new SessionListItem(
                new SessionListEntry(new ChatSession { Id = i.ToString(), Title = "Session " + i, CreatedAt = new DateTime(2026, 1, 1) }, null),
                i == current, false, displayOrder).CreateListViewItem()).ToArray();
        }
    }
}
