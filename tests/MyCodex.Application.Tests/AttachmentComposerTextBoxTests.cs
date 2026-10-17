using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AttachmentComposerTextBoxTests
    {
        [Test]
        public void SetComposeText_UsesStandardTextBoxAndDisplaysEditableAttachmentMarker()
        {
            var attachment = CreateAttachment("notes.txt", "[File: notes.txt]");
            using (var host = CreateHost(out var composer))
            {
                composer.SetComposeText(
                    "before [File: notes.txt] after",
                    new[] { attachment },
                    0,
                    0);

                Assert.That(composer, Is.InstanceOf<TextBox>());
                Assert.That(composer, Is.Not.InstanceOf<RichTextBox>());
                Assert.That(composer.WordWrap, Is.False);
                Assert.That(composer.ScrollBars, Is.EqualTo(ScrollBars.Both));
                Assert.That(
                    composer.Text,
                    Is.EqualTo("before " + Environment.NewLine + "[File: notes.txt]" + Environment.NewLine + " after"));
                Assert.That(composer.ComposeText, Is.EqualTo("before [File: notes.txt] after"));
                Assert.That(composer.TextLength, Is.EqualTo(composer.Text.Length));

                var markerOffset = composer.Text.IndexOf(attachment.MarkerText, StringComparison.Ordinal);
                composer.Select(markerOffset + 1, 4);
                composer.SelectedText = "Attachment";

                Assert.That(
                    composer.Text,
                    Is.EqualTo("before " + Environment.NewLine + "[Attachment: notes.txt]" + Environment.NewLine + " after"));
            }
        }

        [Test]
        public void Focus_AlwaysEnablesCurrentIme()
        {
            using (var host = CreateHost(out var composer))
            using (var next = new Button { Text = "Next" })
            {
                Assert.That(composer.ImeMode, Is.EqualTo(ImeMode.On));

                host.Controls.Add(next);
                host.Show();
                Assert.That(composer.Focus(), Is.True);
                Assert.That(composer.ImeMode, Is.EqualTo(ImeMode.On));

                Assert.That(next.Focus(), Is.True);
                Assert.That(composer.Focus(), Is.True);
                Assert.That(composer.ImeMode, Is.EqualTo(ImeMode.On));
            }
        }

        [Test]
        public void InsertAttachments_ReplacesSelectionWithPlainTextMarkers()
        {
            var first = CreateAttachment("one.txt", "[File: one.txt]");
            var second = CreateAttachment("two.txt", "[File: two.txt]");
            using (var host = CreateHost(out var composer))
            {
                composer.SetComposeText("before selected after", Array.Empty<PendingAttachmentItem>(), 7, 8);

                composer.InsertAttachments(new[] { first, second });

                Assert.That(
                    composer.Text,
                    Is.EqualTo(
                        "before " + Environment.NewLine +
                        "[File: one.txt]" + Environment.NewLine +
                        "[File: two.txt]" + Environment.NewLine +
                        " after"));
                Assert.That(composer.ComposeText, Is.EqualTo("before [File: one.txt] [File: two.txt] after"));
                Assert.That(composer.SelectionStart, Is.EqualTo(composer.Text.IndexOf(" after", StringComparison.Ordinal)));
            }
        }

        [Test]
        public void ConsecutiveAttachmentInsertionsRemainSeparated()
        {
            var first = CreateAttachment("one.txt", "[File: one.txt]");
            var second = CreateAttachment("two.txt", "[File: two.txt]");
            using (var host = CreateHost(out var composer))
            {
                composer.InsertAttachments(new[] { first });
                composer.InsertAttachments(new[] { second });

                Assert.That(
                    composer.Text,
                    Is.EqualTo("[File: one.txt]" + Environment.NewLine + "[File: two.txt]" + Environment.NewLine));
                Assert.That(composer.ComposeText, Is.EqualTo("[File: one.txt] [File: two.txt]"));
            }
        }

        [Test]
        public void SetComposeText_MapsLfSelectionToNativeTextBoxLineEndings()
        {
            using (var host = CreateHost(out var composer))
            {
                composer.SetComposeText("first\nsecond", Array.Empty<PendingAttachmentItem>(), 6, 6);

                Assert.That(composer.SelectedText, Is.EqualTo("second"));
                Assert.That(composer.SelectionStart, Is.EqualTo(("first" + Environment.NewLine).Length));
            }
        }

        [Test]
        public void RemoveAttachment_RemovesTrackedMarkerAndPreservesSurroundingText()
        {
            var attachment = CreateAttachment("notes.txt", "[File: notes.txt]");
            using (var host = CreateHost(out var composer))
            {
                composer.SetComposeText(
                    "before [File: notes.txt] after",
                    new[] { attachment },
                    0,
                    0);

                composer.RemoveAttachment(attachment);

                Assert.That(composer.Text, Is.EqualTo("before after"));
            }
        }

        [Test]
        public void EditingBeforeAttachmentKeepsItsTrackedRemovalPositionCurrent()
        {
            var attachment = CreateAttachment("notes.txt", "[File: notes.txt]");
            using (var host = CreateHost(out var composer))
            {
                composer.SetComposeText(attachment.MarkerText, new[] { attachment }, 0, 0);
                composer.Select(0, 0);
                composer.SelectedText = "prefix ";

                composer.RemoveAttachment(attachment);

                Assert.That(composer.Text, Is.EqualTo("prefix "));
            }
        }

        [Test]
        public void RemoveAllAttachments_DoesNotRemoveIdenticalOrdinaryText()
        {
            var attachment = CreateAttachment("notes.txt", "[File: notes.txt]");
            const string ordinaryText = "literal [File: notes.txt]";
            using (var host = CreateHost(out var composer))
            {
                composer.SetComposeText(ordinaryText, Array.Empty<PendingAttachmentItem>(), ordinaryText.Length, 0);
                composer.InsertAttachments(new[] { attachment });

                composer.RemoveAllAttachments();

                Assert.That(composer.Text, Is.EqualTo(ordinaryText));
            }
        }

        [Test]
        public void ReadOnlyBlocksAttachmentEditingPaths()
        {
            var attachment = CreateAttachment("notes.txt", "[File: notes.txt]");
            using (var host = CreateHost(out var composer))
            {
                composer.SetComposeText(attachment.MarkerText, new[] { attachment }, 0, 0);
                composer.ReadOnly = true;

                composer.InsertAttachments(new[] { CreateAttachment("other.txt", "[File: other.txt]") });
                composer.RemoveAttachment(attachment);
                composer.RemoveAllAttachments();
                composer.InsertNewLine();

                Assert.That(composer.Text, Is.EqualTo(attachment.MarkerText + Environment.NewLine));
                Assert.That(composer.ComposeText, Is.EqualTo(attachment.MarkerText));
            }
        }

        [Test]
        public void TabAndShiftTabLeaveNestedComposerWithoutEditingText()
        {
            using (var host = new Form())
            using (var before = new Button { Text = "Before", TabIndex = 0 })
            using (var outer = new Panel { TabIndex = 1, TabStop = false })
            using (var inner = new TableLayoutPanel { Dock = DockStyle.Fill, TabStop = false })
            using (var composer = new AttachmentComposerTextBox
            {
                Text = "message",
                Dock = DockStyle.Fill,
                TabIndex = 0
            })
            using (var after = new Button { Text = "After", TabIndex = 2 })
            {
                inner.Controls.Add(composer);
                outer.Controls.Add(inner);
                host.Controls.Add(before);
                host.Controls.Add(outer);
                host.Controls.Add(after);
                host.Show();

                Assert.That(composer.Focus(), Is.True);
                Assert.That(composer.ProcessCommandKeyForTests(Keys.Tab), Is.True);
                Assert.That(after.Focused, Is.True);
                Assert.That(composer.Text, Is.EqualTo("message"));

                Assert.That(composer.Focus(), Is.True);
                Assert.That(composer.ProcessCommandKeyForTests(Keys.Shift | Keys.Tab), Is.True);
                Assert.That(before.Focused, Is.True);
                Assert.That(composer.Text, Is.EqualTo("message"));
            }
        }

        [Test]
        public void LongLineRemainsOneUnwrappedLogicalLine()
        {
            var longLine = new string('x', 32768);
            using (var host = CreateHost(out var composer))
            {
                host.ClientSize = new Size(180, 120);
                composer.SetComposeText(longLine, Array.Empty<PendingAttachmentItem>(), longLine.Length, 0);

                Assert.That(composer.Lines, Has.Length.EqualTo(1));
                Assert.That(composer.Text, Is.EqualTo(longLine));
                Assert.That(composer.AccessibleCaretOffset, Is.EqualTo(longLine.Length));
            }
        }

        private static Form CreateHost(out AttachmentComposerTextBox composer)
        {
            var host = new Form();
            composer = new AttachmentComposerTextBox
            {
                AccessibleName = "Input",
                Dock = DockStyle.Fill
            };
            host.Controls.Add(composer);
            host.CreateControl();
            composer.CreateControl();
            return host;
        }

        private static PendingAttachmentItem CreateAttachment(string displayName, string markerText)
        {
            return new PendingAttachmentItem(
                @"E:\" + displayName,
                displayName,
                false,
                AttachmentKind.File,
                markerText,
                null,
                null);
        }
    }
}
