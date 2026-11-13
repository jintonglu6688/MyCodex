using System.Globalization;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class HooksManagementFormTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void Form_KeepsFocusableControlsNamedOrderedAndResizable()
        {
            using (var form = CreateForm())
            {
                var focusable = FormTestDriver.Descendants<Control>(form)
                    .Where(control => control.TabStop && (control is Button || control is ListView || control is TextBox))
                    .ToList();

                Assert.That(focusable.All(control => !string.IsNullOrWhiteSpace(control.AccessibleName)), Is.True);
                Assert.That(focusable.Select(control => control.TabIndex).Distinct().Count(), Is.EqualTo(focusable.Count));
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShowList_OffersTrustOnlyForUnmanagedHooksThatNeedReview()
        {
            using (var form = CreateForm())
            {
                var list = new AppServerHooksListResult();
                list.Hooks.Add(new AppServerHookInfo { Key = "user", EventName = "stop", TrustStatus = "modified", Enabled = true, CurrentHash = "h" });
                list.Hooks.Add(new AppServerHookInfo { Key = "policy", EventName = "stop", TrustStatus = "managed", Enabled = true, IsManaged = true });
                var listView = FormTestDriver.Field<ListView>(form, "_hookListView");
                var ignored = form.Handle;
                ignored = listView.Handle;
                var trust = FormTestDriver.Field<Button>(form, "_trustButton");
                var disable = FormTestDriver.Field<Button>(form, "_disableButton");

                FormTestDriver.Invoke(form, "ShowList", list, null);
                Assert.That(trust.Enabled && disable.Enabled, Is.True);

                listView.Items[1].Selected = true;
                Assert.That(trust.Enabled || disable.Enabled, Is.False);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ReviewForm_ListsPendingHooksWithFirstSelectedAndLaterAsCancel()
        {
            var pending = new[]
            {
                new AppServerHookInfo { Key = "a", EventName = "stop", TrustStatus = "untrusted", CurrentHash = "1" },
                new AppServerHookInfo { Key = "b", EventName = "preToolUse", TrustStatus = "modified", CurrentHash = "2" }
            }.ToList();
            using (var form = new HookReviewForm(CreateService(), @"E:\Repo", null, pending))
            {
                var ignored = form.Handle;
                var listView = FormTestDriver.Field<ListView>(form, "_hookListView");
                ignored = listView.Handle;

                Assert.That(listView.Items.Count, Is.EqualTo(2));
                Assert.That(listView.SelectedItems[0].Tag, Is.SameAs(pending[0]));
                Assert.That(listView.TabIndex, Is.EqualTo(0));
                Assert.That(form.CancelButton, Is.SameAs(FormTestDriver.Field<Button>(form, "_laterButton")));
                Assert.That(FormTestDriver.Field<Button>(form, "_trustAllButton").Enabled, Is.True);
                Assert.That(FormTestDriver.Descendants<Button>(form).All(button => !string.IsNullOrWhiteSpace(button.AccessibleName)), Is.True);
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void CreateItem_LeadsWithTrustThenSourceNameThenEvent()
        {
            var previous = Thread.CurrentThread.CurrentUICulture;
            Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
            try
            {
                var plugin = HooksManagementForm.CreateItem(new AppServerHookInfo
                {
                    TrustStatus = "untrusted", Enabled = false, Source = "plugin", PluginId = "ponytail@ponytail", EventName = "sessionStart"
                });
                var project = HooksManagementForm.CreateItem(new AppServerHookInfo
                {
                    TrustStatus = "trusted", Enabled = true, Source = "project", SourcePath = @"E:\MyCodex\.codex\hooks.json", EventName = "preToolUse"
                });

                Assert.That(plugin.Text, Is.EqualTo("Untrusted · Disabled · ponytail plugin · SessionStart"));
                Assert.That(project.Text, Is.EqualTo(@"Trusted · Project E:\MyCodex · PreToolUse"));
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = previous;
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShowList_PutsHooksNeedingReviewFirstAndKeepsServerOrderOtherwise()
        {
            using (var form = CreateForm())
            {
                var list = new AppServerHooksListResult();
                list.Hooks.Add(new AppServerHookInfo { Key = "t1", TrustStatus = "trusted" });
                list.Hooks.Add(new AppServerHookInfo { Key = "u1", TrustStatus = "untrusted" });
                list.Hooks.Add(new AppServerHookInfo { Key = "t2", TrustStatus = "trusted" });
                list.Hooks.Add(new AppServerHookInfo { Key = "m1", TrustStatus = "modified" });

                FormTestDriver.Invoke(form, "ShowList", list, null);

                var keys = FormTestDriver.Field<ListView>(form, "_hookListView").Items.Cast<ListViewItem>()
                    .Select(item => ((AppServerHookInfo)item.Tag).Key);
                Assert.That(keys, Is.EqualTo(new[] { "u1", "m1", "t1", "t2" }));
            }
        }

        private static HookManagementService CreateService()
        {
            return new HookManagementService(
                new HookManagementServiceTests.FakeHookClient(),
                new HookManagementServiceTests.FakeConfigClient());
        }

        private static HooksManagementForm CreateForm()
        {
            var service = new HookManagementService(
                new HookManagementServiceTests.FakeHookClient(),
                new HookManagementServiceTests.FakeConfigClient());
            return new HooksManagementForm(service, @"E:\Repo", null);
        }
    }
}
