using System;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class SessionStatisticsFormTests
    {
        [Test]
        public void Constructor_UsesListViewForDetailsWithoutExtraTitleNode()
        {
            using (var form = new SessionStatisticsForm(new ChatSession
            {
                Title = "Token stats",
                ThreadId = "thread-1"
            }))
            {
                var details = FindFirst<ListView>(form);

                Assert.That(details, Is.Not.Null);
                Assert.That(FindFirst<ListBox>(form), Is.Null);
                Assert.That(details.TabStop, Is.True);
                Assert.That(details.TabIndex, Is.EqualTo(0));
                Assert.That(details.AccessibleName, Is.EqualTo(UiLocalization.Text("SessionStatisticsForm.Accessible.Details", "Statistics details")));
                Assert.That(details.View, Is.EqualTo(View.List));
                Assert.That(details.Columns.Count, Is.EqualTo(0));
                Assert.That(details.Items.Count, Is.GreaterThan(0));

                var titleLabel = FindLabel(form, "Statistics details");
                Assert.That(titleLabel, Is.Null);
            }
        }

        private static Label FindLabel(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                var label = child as Label;
                if (label != null && string.Equals(label.Text, text, StringComparison.Ordinal))
                {
                    return label;
                }

                var nested = FindLabel(child, text);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static T FindFirst<T>(Control root)
            where T : Control
        {
            foreach (Control child in root.Controls)
            {
                var typed = child as T;
                if (typed != null)
                {
                    return typed;
                }

                var nested = FindFirst<T>(child);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }
    }
}
