using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ChatWorkbenchControlTests
    {
        [Test]
        public void Models_CanCarryTextAttachmentsAndInputItems()
        {
            var attachment = CreateFileAttachment();
            var inputItem = new AppServerUserInputItem { Type = "text", Text = "hello" };
            var request = new ChatWorkbenchSendRequest(
                "hello [File: a.txt]",
                "hello [File: a.txt]",
                new[] { inputItem },
                new[] { attachment });
            var result = new ChatWorkbenchSendResult(true, true, "sent");

            Assert.That(request.RawText, Is.EqualTo("hello [File: a.txt]"));
            Assert.That(request.InputItems, Has.Count.EqualTo(1));
            Assert.That(request.Attachments, Has.Count.EqualTo(1));
            Assert.That(result.ClearInput, Is.True);
            Assert.That(result.StatusText, Is.EqualTo("sent"));
        }

        [Test]
        public void Control_ExposesAccessibleChatComposeAndActionControls()
        {
            using (UseCulture("en-US"))
            using (var control = new ChatWorkbenchControl(CreateCallbacks()))
            {
                Assert.That(FormTestDriver.Single<ChatHistoryListBox>(control).AccessibleName, Is.EqualTo("Messages"));
                var input = FormTestDriver.Single<AttachmentComposerTextBox>(control);
                Assert.That(input.AccessibleName, Is.EqualTo("Input"));
                Assert.That(input, Is.InstanceOf<TextBox>());
                Assert.That(input, Is.Not.InstanceOf<RichTextBox>());

                var pendingItems = FormTestDriver.Descendants<ListBox>(control)
                    .Single(listBox => string.Equals(listBox.AccessibleName, "Pending items", StringComparison.Ordinal));
                Assert.That(pendingItems.TabIndex, Is.EqualTo(3));

                var buttons = FormTestDriver.Descendants<Button>(control).ToList();
                Assert.That(buttons.Any(button => string.Equals(button.AccessibleName, "Insert", StringComparison.Ordinal)), Is.True);
                Assert.That(buttons.Any(button => string.Equals(button.AccessibleName, "Send", StringComparison.Ordinal)), Is.True);
                Assert.That(buttons.Any(button => string.Equals(button.AccessibleName, "Stop", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public void ChatHistoryVisibleItemsUpdater_DoesNotReplaceUnchangedItems()
        {
            using (var listBox = new ChatHistoryListBox())
            {
                var existingItem = CreateChatItem("thread-1", "turn-1", "Codex", "reply");
                var reloadedItem = CreateChatItem("thread-1", "turn-1", "Codex", "reply");
                listBox.Items.Add(existingItem);

                var changed = ChatHistoryVisibleItemsUpdater.Apply(listBox.Items, new[] { reloadedItem });

                Assert.That(changed, Is.False);
                Assert.That(listBox.Items.Count, Is.EqualTo(1));
                Assert.That(listBox.Items[0], Is.SameAs(existingItem));
            }
        }

        [Test]
        public void ReplaceHistoryItems_UnchangedMessagesDoNotInvalidateOrChangeSelectionAndViewport()
        {
            using (var control = new ChatWorkbenchControl(CreateCallbacks()))
            {
                var texts = Enumerable.Range(0, 12).Select(index => index == 0
                    ? new string('x', 100000) + "\r\nlast line" : "message " + index).ToArray();
                control.ReplaceHistoryItems(texts.Select(text => CreateChatItem("thread", "turn", "User", text)));
                var history = FormTestDriver.Single<ChatHistoryListBox>(control);
                history.Dock = DockStyle.None;
                history.ClientSize = new Size(400, history.ItemHeight * 3);
                var handle = history.Handle;
                history.SelectSingleIndex(2);
                history.ProcessExplorerSelectionKeyForTests(Keys.Shift | Keys.Down);
                history.TopIndex = 1;
                var selected = history.GetSelectedIndicesInDisplayOrder();
                var focused = history.FocusedIndex;
                var top = history.TopIndex;
                var invalidations = 0;
                var selectionChanges = 0;
                history.Invalidated += (sender, args) => invalidations++;
                history.SelectedIndexChanged += (sender, args) => selectionChanges++;

                for (var iteration = 0; iteration < 3; iteration++)
                    control.ReplaceHistoryItems(texts.Select(text => CreateChatItem("thread", "turn", "User", text)));

                Assert.Multiple(() =>
                {
                    Assert.That(invalidations, Is.Zero);
                    Assert.That(selectionChanges, Is.Zero);
                    Assert.That(history.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(selected));
                    Assert.That(history.FocusedIndex, Is.EqualTo(focused));
                    Assert.That(history.TopIndex, Is.EqualTo(top));
                });
            }
        }

        [Test]
        public void ReplaceHistoryItems_UpdatesTurnMetadataWhenPresentationIsUnchanged()
        {
            using (var control = new ChatWorkbenchControl(CreateCallbacks()))
            {
                var original = CreateChatItem("thread", "old-turn", "User", "same text");
                control.ReplaceHistoryItems(new[] { original });
                var replacement = CreateChatItem("thread", "new-turn", "User", "same text");
                replacement.TurnIndex = 3;
                replacement.TurnCount = 5;

                control.ReplaceHistoryItems(new[] { replacement });

                Assert.That(control.GetHistoryItems()[0], Is.SameAs(original));
                Assert.That(original.TurnId, Is.EqualTo("new-turn"));
                Assert.That(original.TurnIndex, Is.EqualTo(3));
                Assert.That(original.TurnCount, Is.EqualTo(5));
            }
        }

        [Test]
        public void ChatHistoryVisibleItemsUpdater_AppendsWithoutClearingExistingItems()
        {
            using (var listBox = new ChatHistoryListBox())
            {
                var existingItem = CreateChatItem("thread-1", "turn-1", "User", "question");
                var appendedItem = CreateChatItem("thread-1", "turn-2", "Codex", "reply");
                listBox.Items.Add(existingItem);

                var changed = ChatHistoryVisibleItemsUpdater.Apply(listBox.Items, new[]
                {
                    CreateChatItem("thread-1", "turn-1", "User", "question"),
                    appendedItem
                });

                Assert.That(changed, Is.True);
                Assert.That(listBox.Items.Count, Is.EqualTo(2));
                Assert.That(listBox.Items[0], Is.SameAs(existingItem));
                Assert.That(listBox.Items[1], Is.SameAs(appendedItem));
            }
        }

        [TestCase(10F)]
        [TestCase(18F)]
        public void ChatHistoryListBox_FontChangesResizeTwoLineItemsWithoutChangingNativeText(float fontSize)
        {
            using (var listBox = CreateHistoryListBoxWithItems(3))
            using (var font = new Font("Microsoft YaHei UI", fontSize))
            {
                var item = listBox.Items[1];
                var text = listBox.GetItemText(item);
                var textFlags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
                var initialTextHeight = TextRenderer.MeasureText("Ag\nAg", listBox.Font, new Size(int.MaxValue, int.MaxValue), textFlags).Height;
                Assert.That(listBox.ItemHeight, Is.GreaterThanOrEqualTo(initialTextHeight + 8));
                listBox.SelectSingleIndex(1);
                listBox.Font = font;

                Assert.That(listBox.DrawMode, Is.EqualTo(DrawMode.OwnerDrawFixed));
                var twoLineHeight = TextRenderer.MeasureText("Ag\nAg", font, new Size(int.MaxValue, int.MaxValue), textFlags).Height;
                Assert.That(listBox.ItemHeight, Is.GreaterThanOrEqualTo(twoLineHeight + 8));
                Assert.That(listBox.SelectedItem, Is.SameAs(item));
                Assert.That(listBox.GetItemText(item), Is.EqualTo(text));
            }
        }

        [TestCase(10F, false)]
        [TestCase(18F, false)]
        [TestCase(10F, true)]
        [TestCase(18F, true)]
        public void ChatHistoryListBox_DrawsCenteredTextWithinTwoLines(float fontSize, bool multiline)
        {
            using (var listBox = new ChatHistoryListBox())
            using (var font = new Font(listBox.Font.FontFamily, fontSize))
            {
                listBox.Font = font;
                listBox.ForeColor = Color.Fuchsia;
                var text = multiline ? string.Join(" ", Enumerable.Repeat("HHHH", 40)) : "HHHH";
                var item = new ChatHistoryItem { DisplayText = text, AccessibleText = "Complete message" };
                listBox.Items.Add(item);
                using (var bitmap = new Bitmap(240, listBox.ItemHeight))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    typeof(ChatHistoryListBox).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(listBox, new object[] { new DrawItemEventArgs(graphics, font,
                            new Rectangle(0, 0, bitmap.Width, bitmap.Height), 0, DrawItemState.None) });
                    var top = bitmap.Height;
                    var bottom = -1;
                    for (var y = 0; y < bitmap.Height; y++)
                    {
                        for (var x = 32; x < bitmap.Width; x++)
                        {
                            if (bitmap.GetPixel(x, y).ToArgb() == Color.Fuchsia.ToArgb())
                            {
                                top = Math.Min(top, y);
                                bottom = Math.Max(bottom, y);
                            }
                        }
                    }

                    Assert.That(bottom, Is.GreaterThanOrEqualTo(top), "Text must be rendered.");
                    Assert.That((top + bottom) / 2.0, Is.EqualTo((bitmap.Height - 1) / 2.0).Within(font.Height / 4.0));
                    Assert.That(bottom - top + 1, Is.LessThanOrEqualTo(2 * font.Height));
                    Assert.That(bottom - top + 1, multiline ? Is.GreaterThan(font.Height) : Is.LessThanOrEqualTo(font.Height));
                    Assert.That(listBox.GetItemText(item), Is.EqualTo("Complete message"));
                    Assert.That(item.DisplayText, Is.EqualTo(text));
                }
            }
        }

        [TestCase(10F)]
        [TestCase(18F)]
        public void ChatHistoryListBox_ScrollsUsingCurrentTwoLineItemHeight(float fontSize)
        {
            using (var listBox = CreateHistoryListBoxWithItems(20))
            using (var font = new Font(listBox.Font.FontFamily, fontSize))
            {
                listBox.Font = font;
                listBox.IntegralHeight = false;
                listBox.ClientSize = new Size(400, listBox.ItemHeight * 3);
                listBox.CreateControl();
                listBox.SelectSingleIndex(8);

                Assert.That(listBox.TopIndex, Is.EqualTo(6));
                Assert.That(listBox.GetItemRectangle(8).Bottom, Is.LessThanOrEqualTo(listBox.ClientSize.Height));
            }
        }

        [Test]
        public void ChatHistoryListBox_UsesNativeListBoxAccessibilityForPositionAnnouncements()
        {
            using (var listBox = new ChatHistoryListBox())
            {
                listBox.Items.Add(CreateChatItem("thread-1", "turn-1", "User", "question"));
                listBox.Items.Add(CreateChatItem("thread-1", "turn-2", "Codex", "reply"));

                var root = listBox.AccessibilityObject;

                Assert.That(root.GetType().FullName, Does.Contain("ListBoxAccessibleObject"));
                Assert.That(root.Role, Is.EqualTo(AccessibleRole.List));
            }
        }

        [Test]
        public void ChatHistoryListBox_DoesNotOverrideListBoxAccessibilityInstance()
        {
            var method = typeof(ChatHistoryListBox).GetMethod(
                "CreateAccessibilityInstance",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That(method.DeclaringType, Is.Not.EqualTo(typeof(ChatHistoryListBox)));
        }

        [Test]
        public void ChatHistoryListBox_NativeItemTextPreservesAccessibleLineBreaks()
        {
            using (var listBox = new ChatHistoryListBox())
            {
                var item = new ChatHistoryItem
                {
                    DisplayText = "codex: first second third",
                    FullText = "codex: first\r\nsecond\r\nthird",
                    AccessibleText = "codex: first" + Environment.NewLine + "second" + Environment.NewLine + "third"
                };
                listBox.Items.Add(item);

                Assert.That(listBox.GetItemText(item), Is.EqualTo(item.AccessibleText));
                Assert.That(listBox.GetItemText(item), Does.Contain(Environment.NewLine));
            }
        }

        [Test]
        public void ChatHistoryListBox_UsesExtendedMultiSelectForExplorerStyleSelection()
        {
            using (var listBox = new ChatHistoryListBox())
            {
                Assert.That(listBox.SelectionMode, Is.EqualTo(SelectionMode.MultiExtended));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlArrowMovesFocusWithoutChangingSelection()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                listBox.SelectSingleIndex(1);

                var handled = listBox.ProcessExplorerSelectionKeyForTests(Keys.Control | Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 1 }));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(listBox.Items[2]));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlArrowMovesNativeFocusForScreenReaders()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                listBox.SelectSingleIndex(3);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Control | Keys.Up);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.SelectedIndices.Cast<int>().ToArray(), Is.EqualTo(new[] { 2 }));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 3 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlArrowKeyMessageMovesOneItemPerPress()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessKeyMessageForTests(Keys.Control | Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(3));
                Assert.That(listBox.SelectedIndex, Is.EqualTo(3));
                Assert.That(listBox.SelectedIndices.Cast<int>().ToArray(), Is.EqualTo(new[] { 3 }));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(listBox.Items[3]));

                handled = listBox.ProcessKeyMessageForTests(Keys.Control | Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(4));
                Assert.That(listBox.SelectedIndex, Is.EqualTo(4));
                Assert.That(listBox.SelectedIndices.Cast<int>().ToArray(), Is.EqualTo(new[] { 4 }));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(listBox.Items[4]));

                handled = listBox.ProcessKeyMessageForTests(Keys.Control | Keys.Up);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(3));
                Assert.That(listBox.SelectedIndex, Is.EqualTo(3));
                Assert.That(listBox.SelectedIndices.Cast<int>().ToArray(), Is.EqualTo(new[] { 3 }));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(listBox.Items[3]));

                handled = listBox.ProcessKeyMessageForTests(Keys.Control | Keys.Up);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.SelectedIndex, Is.EqualTo(2));
                Assert.That(listBox.SelectedIndices.Cast<int>().ToArray(), Is.EqualTo(new[] { 2 }));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(listBox.Items[2]));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlArrowUpdatesFocusedIndexBeforeSelectedIndexChanged()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                var observedFocusedIndices = new List<int>();
                listBox.SelectSingleIndex(3);
                listBox.SelectedIndexChanged += (sender, e) => observedFocusedIndices.Add(listBox.FocusedIndex);

                listBox.ProcessCommandKeyForTests(Keys.Control | Keys.Up);

                Assert.That(observedFocusedIndices, Is.Not.Empty);
                Assert.That(observedFocusedIndices.All(index => index == 2), Is.True);
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlArrowRaisesSingleSelectedIndexChangedForTarget()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                var observedFocusedIndices = new List<int>();
                listBox.SelectSingleIndex(3);
                listBox.SelectedIndexChanged += (sender, e) => observedFocusedIndices.Add(listBox.FocusedIndex);

                listBox.ProcessCommandKeyForTests(Keys.Control | Keys.Up);

                Assert.That(observedFocusedIndices, Is.EqualTo(new[] { 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_PlainArrowAtEndIsHandledWithoutReselectingItem()
        {
            using (var listBox = CreateHistoryListBoxWithItems(3))
            {
                var selectedIndexChangedCount = 0;
                listBox.SelectSingleIndex(2);
                listBox.SelectedIndexChanged += (sender, e) => selectedIndexChangedCount++;

                var handled = listBox.ProcessCommandKeyForTests(Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(selectedIndexChangedCount, Is.EqualTo(0));
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_PlainArrowMovesSelectionForScreenReaders()
        {
            using (var listBox = CreateHistoryListBoxWithItems(3))
            {
                listBox.SelectSingleIndex(1);

                var handled = listBox.ProcessExplorerSelectionKeyForTests(Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_ProcessCmdKeyHandlesPlainArrowBeforeNativeListBox()
        {
            using (var listBox = CreateHistoryListBoxWithItems(3))
            {
                listBox.SelectSingleIndex(1);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_ProcessCmdKeyHandlesPlainHomeBeforeNativeListBox()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Home);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(0));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 0 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_ProcessCmdKeyHandlesPlainEndBeforeNativeListBox()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                listBox.SelectSingleIndex(1);

                var handled = listBox.ProcessCommandKeyForTests(Keys.End);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(3));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 3 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_ProcessCmdKeySpaceTogglesFocusedSelection()
        {
            using (var listBox = CreateHistoryListBoxWithItems(3))
            {
                listBox.SelectSingleIndex(1);
                listBox.ProcessCommandKeyForTests(Keys.Control | Keys.Down);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Space);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 1, 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_SpaceTogglesFocusedItemSelection()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                listBox.SelectSingleIndex(1);
                listBox.ProcessExplorerSelectionKeyForTests(Keys.Control | Keys.Down);

                var handled = listBox.ProcessExplorerSelectionKeyForTests(Keys.Space);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 1, 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_ShiftArrowExtendsSelectionFromAnchor()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(1);

                var handled = listBox.ProcessExplorerSelectionKeyForTests(Keys.Shift | Keys.Down);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(2));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 1, 2 }));

                listBox.ProcessExplorerSelectionKeyForTests(Keys.Shift | Keys.Down);

                Assert.That(listBox.FocusedIndex, Is.EqualTo(3));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 1, 2, 3 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlShiftHomeExtendsSelectionToStart()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Control | Keys.Shift | Keys.Home);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(0));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 0, 1, 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlHomeMovesFocusToStartWithoutChangingSelection()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Control | Keys.Home);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(0));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlEndMovesFocusToEndWithoutChangingSelection()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Control | Keys.End);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(4));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_ShiftHomeExtendsSelectionToStart()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Shift | Keys.Home);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(0));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 0, 1, 2 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_ShiftEndExtendsSelectionToEnd()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Shift | Keys.End);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(4));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 2, 3, 4 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlShiftEndExtendsSelectionToEnd()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                listBox.SelectSingleIndex(2);

                var handled = listBox.ProcessCommandKeyForTests(Keys.Control | Keys.Shift | Keys.End);

                Assert.That(handled, Is.True);
                Assert.That(listBox.FocusedIndex, Is.EqualTo(4));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 2, 3, 4 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_GetSelectedItemsReturnsDisplayOrder()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                listBox.SetSelected(3, true);
                listBox.SetSelected(1, true);

                var selected = listBox.GetSelectedChatHistoryItemsInDisplayOrder();

                Assert.That(selected, Is.EqualTo(new[] { listBox.Items[1], listBox.Items[3] }));
            }
        }

        [Test]
        public void ChatHistoryListBox_BuildClipboardTextUsesSelectedItemsInDisplayOrder()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                listBox.SetSelected(3, true);
                listBox.SetSelected(1, true);

                var text = listBox.BuildClipboardTextForSelection();

                Assert.That(
                    text,
                    Is.EqualTo(
                        ((ChatHistoryItem)listBox.Items[1]).FullText +
                        Environment.NewLine +
                        Environment.NewLine +
                        ((ChatHistoryItem)listBox.Items[3]).FullText));
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlARequestsSingleSelectionSummary()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                var summaries = new List<ChatHistorySelectionSummaryEventArgs>();
                listBox.SelectSingleIndex(2);
                listBox.SelectionSummaryRequested += (sender, e) => summaries.Add(e);

                var handled = listBox.ProcessExplorerSelectionKeyForTests(Keys.Control | Keys.A);

                Assert.That(handled, Is.True);
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(listBox.Items[2]));
                Assert.That(listBox.SelectedIndices.Cast<int>().ToArray(), Is.EqualTo(new[] { 2 }));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 0, 1, 2, 3 }));
                Assert.That(summaries, Has.Count.EqualTo(1));
                Assert.That(summaries[0].SelectedCount, Is.EqualTo(4));
                Assert.That(summaries[0].TotalCount, Is.EqualTo(4));
                Assert.That(summaries[0].IsAllSelected, Is.True);
            }
        }

        [Test]
        public void ChatHistoryListBox_CtrlShiftEndRequestsRangeSelectionSummary()
        {
            using (var listBox = CreateHistoryListBoxWithItems(5))
            {
                var summaries = new List<ChatHistorySelectionSummaryEventArgs>();
                listBox.SelectSingleIndex(2);
                listBox.SelectionSummaryRequested += (sender, e) => summaries.Add(e);

                var handled = listBox.ProcessExplorerSelectionKeyForTests(Keys.Control | Keys.Shift | Keys.End);

                Assert.That(handled, Is.True);
                Assert.That(summaries, Has.Count.EqualTo(1));
                Assert.That(summaries[0].SelectedCount, Is.EqualTo(3));
                Assert.That(summaries[0].TotalCount, Is.EqualTo(5));
                Assert.That(summaries[0].StartIndex, Is.EqualTo(2));
                Assert.That(summaries[0].EndIndex, Is.EqualTo(4));
                Assert.That(summaries[0].FocusedIndex, Is.EqualTo(4));
                Assert.That(summaries[0].IsAllSelected, Is.False);
            }
        }

        [Test]
        public void ChatHistoryListBox_LeftRightRequestsAnchorNavigationBeforeNativeSelection()
        {
            using (var listBox = CreateHistoryListBoxWithItems(4))
            {
                var directions = new List<int>();
                listBox.SelectSingleIndex(1);
                listBox.AnchorNavigationRequested += (sender, e) =>
                {
                    directions.Add(e.Direction);
                    e.Handled = true;
                };

                var handled = listBox.ProcessCommandKeyForTests(Keys.Right);

                Assert.That(handled, Is.True);
                Assert.That(directions, Is.EqualTo(new[] { 1 }));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(listBox.Items[1]));
                Assert.That(listBox.GetSelectedIndicesInDisplayOrder(), Is.EqualTo(new[] { 1 }));
            }
        }

        [Test]
        public void ChatHistoryListBox_DefaultContextMenuCopiesMessages()
        {
            using (UseCulture("en-US"))
            using (var listBox = new ChatHistoryListBox())
            {
                Assert.That(listBox.ContextMenu, Is.Not.Null);
                Assert.That(listBox.ContextMenu.MenuItems[0].Text, Is.EqualTo("Copy Message(&C)"));
            }
        }

        [Test]
        public void ChatWorkbenchControl_SelectionSummaryAnnouncesStatus()
        {
            using (UseCulture("en-US"))
            {
                var announcements = new List<string>();
                var callbacks = CreateCallbacks();
                callbacks.AnnounceStatus = announcements.Add;

                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    control.ReplaceHistoryItems(new[]
                    {
                        CreateChatItem("thread-1", "turn-1", "User", "first"),
                        CreateChatItem("thread-1", "turn-2", "Codex", "second")
                    });
                    var listBox = FormTestDriver.Single<ChatHistoryListBox>(control);

                    listBox.ProcessExplorerSelectionKeyForTests(Keys.Control | Keys.A);

                    Assert.That(announcements, Has.Count.EqualTo(1));
                    Assert.That(announcements[0], Is.EqualTo("Selected all 2 messages."));
                }
            }
        }

        [Test]
        public async Task SendAsync_BuildsTextAndAttachmentInputItemsThenClearsDraft()
        {
            using (UseCulture("en-US"))
            {
                ChatWorkbenchSendRequest capturedRequest = null;
                var callbacks = CreateCallbacks();
                callbacks.SendAsync = request =>
                {
                    capturedRequest = request;
                    return Task.FromResult(new ChatWorkbenchSendResult(true, true, "sent"));
                };

                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    control.SetInputText("please inspect ");
                    control.AddPendingAttachments(new[] { CreateFileAttachment() });
                    var input = FormTestDriver.Single<AttachmentComposerTextBox>(control);
                    Assert.That(
                        input.Text,
                        Is.EqualTo("please inspect " + Environment.NewLine + "[File: a.txt]" + Environment.NewLine));
                    Assert.That(control.InputText, Is.EqualTo("please inspect [File: a.txt]"));
                    await control.SendCurrentInputAsync();

                    Assert.That(capturedRequest, Is.Not.Null);
                    Assert.That(capturedRequest.InputItems.Select(item => item.Type), Is.EqualTo(new[] { "text", "text" }));
                    Assert.That(capturedRequest.InputItems[0].Text, Is.EqualTo("please inspect "));
                    Assert.That(capturedRequest.InputItems[1].Text, Is.EqualTo(@"E:\Repo\a.txt"));
                    Assert.That(control.InputText, Is.EqualTo(string.Empty));
                    Assert.That(control.PendingAttachments, Is.Empty);
                }
            }
        }

        [Test]
        public async Task SendAsync_DoesNotSendEmptyDraft()
        {
            using (UseCulture("en-US"))
            {
                var sendCount = 0;
                var callbacks = CreateCallbacks();
                callbacks.SendAsync = request =>
                {
                    sendCount++;
                    return Task.FromResult(new ChatWorkbenchSendResult(true, true, "sent"));
                };

                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    await control.SendCurrentInputAsync();
                }

                Assert.That(sendCount, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task PasteAttachmentsFromHostAddsPendingAttachment()
        {
            using (UseCulture("en-US"))
            {
                var callbacks = CreateCallbacks();
                callbacks.CanPasteAttachments = () => true;
                callbacks.PasteAttachmentsAsync = () => Task.FromResult((IReadOnlyList<PendingAttachmentItem>)new[]
                {
                    CreateFileAttachment()
                });

                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    var addedCount = await control.PasteAttachmentsFromHostAsync();

                    Assert.That(addedCount, Is.EqualTo(1));
                    Assert.That(control.PendingAttachments, Has.Count.EqualTo(1));
                    Assert.That(control.InputText, Does.Contain("[File: a.txt]"));
                }
            }
        }

        [Test]
        public void Messages_AltEnterOpensSelectedMessageDetailWhenCallbackExists()
        {
            using (UseCulture("en-US"))
            {
                ChatHistoryItem capturedItem = null;
                var callbacks = CreateCallbacks();
                callbacks.OpenMessageDetail = item => capturedItem = item;
                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    var item = CreateChatItem("thread-1", "turn-1", "Codex", "# Reply");
                    control.ReplaceHistoryItems(new[] { item });
                    var listBox = FormTestDriver.Single<ChatHistoryListBox>(control);
                    listBox.SelectedIndex = 0;
                    var args = new KeyEventArgs(Keys.Alt | Keys.Enter);

                    InvokeHistoryListBoxKeyDown(control, args);

                    Assert.That(capturedItem, Is.SameAs(item));
                    Assert.That(args.Handled, Is.True);
                    Assert.That(args.SuppressKeyPress, Is.True);
                }
            }
        }

        [Test]
        public void Messages_EnterOpensFocusedMessageDetailWhenSelectionDiffers()
        {
            using (UseCulture("en-US"))
            {
                ChatHistoryItem capturedItem = null;
                var callbacks = CreateCallbacks();
                callbacks.OpenMessageDetail = item => capturedItem = item;
                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    var selectedItem = CreateChatItem("thread-1", "turn-1", "Codex", "# Selected");
                    var focusedItem = CreateChatItem("thread-1", "turn-2", "User", "# Focused");
                    control.ReplaceHistoryItems(new[] { selectedItem, focusedItem });
                    var listBox = FormTestDriver.Single<ChatHistoryListBox>(control);
                    listBox.SelectSingleIndex(0);
                    listBox.ProcessExplorerSelectionKeyForTests(Keys.Control | Keys.Down);
                    var args = new KeyEventArgs(Keys.Enter);

                    InvokeHistoryListBoxKeyDown(control, args);

                    Assert.That(capturedItem, Is.SameAs(focusedItem));
                    Assert.That(args.Handled, Is.True);
                    Assert.That(args.SuppressKeyPress, Is.True);
                }
            }
        }

        [Test]
        public void HistoryBrowseAccess_UsesSnapshotsAndDoesNotChangeNativeSelection()
        {
            using (var control = new ChatWorkbenchControl(CreateCallbacks()))
            {
                var firstItem = CreateChatItem("thread-1", "turn-1", "Codex", "# First");
                var secondItem = CreateChatItem("thread-1", "turn-2", "User", "# Second");
                control.ReplaceHistoryItems(new[] { firstItem, secondItem });
                var listBox = FormTestDriver.Single<ChatHistoryListBox>(control);
                listBox.SelectSingleIndex(0);

                var items = control.GetHistoryItems();
                control.EnsureHistoryItemVisible(1);

                Assert.That(items, Is.EqualTo(new[] { firstItem, secondItem }));
                Assert.That(items, Is.Not.SameAs(control.GetHistoryItems()));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(firstItem));
            }
        }

        [Test]
        public void ReplaceHistoryItems_RestoresFocusedMessageByIdentityWhenSystemHeadersAreRemoved()
        {
            using (var control = new ChatWorkbenchControl(CreateCallbacks()))
            {
                var systemHeader = ChatHistoryItemFactory.Create("System", "session header", ChatHistoryCategory.System);
                var firstItem = CreateChatItem("thread-1", "turn-1", "User", "first message");
                var focusedItem = CreateChatItem("thread-1", "turn-2", "Codex", "focused message");
                control.ReplaceHistoryItems(new[] { systemHeader, firstItem, focusedItem });
                var listBox = FormTestDriver.Single<ChatHistoryListBox>(control);
                listBox.SelectSingleIndex(2);

                var reloadedFirstItem = CreateChatItem("thread-1", "turn-1", "User", "first message");
                var reloadedFocusedItem = CreateChatItem("thread-1", "turn-2", "Codex", "focused message");
                control.ReplaceHistoryItems(new[] { reloadedFirstItem, reloadedFocusedItem });

                Assert.That(listBox.FocusedIndex, Is.EqualTo(1));
                Assert.That(listBox.PrimaryChatHistoryItem, Is.SameAs(reloadedFocusedItem));
                Assert.That(listBox.GetSelectedChatHistoryItemsInDisplayOrder(), Is.EqualTo(new[] { reloadedFocusedItem }));
            }
        }

        [Test]
        public void Messages_AltEnterOpensFocusedMessageDetailWhenSelectionDiffers()
        {
            using (UseCulture("en-US"))
            {
                ChatHistoryItem capturedItem = null;
                var callbacks = CreateCallbacks();
                callbacks.OpenMessageDetail = item => capturedItem = item;
                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    var firstItem = CreateChatItem("thread-1", "turn-1", "Codex", "# First");
                    var focusedItem = CreateChatItem("thread-1", "turn-2", "User", "# Focused");
                    control.ReplaceHistoryItems(new[] { firstItem, focusedItem });
                    var listBox = FormTestDriver.Single<ChatHistoryListBox>(control);
                    listBox.SelectSingleIndex(0);
                    listBox.ProcessExplorerSelectionKeyForTests(Keys.Control | Keys.Down);
                    var args = new KeyEventArgs(Keys.Alt | Keys.Enter);

                    InvokeHistoryListBoxKeyDown(control, args);

                    Assert.That(capturedItem, Is.SameAs(focusedItem));
                    Assert.That(args.Handled, Is.True);
                    Assert.That(args.SuppressKeyPress, Is.True);
                }
            }
        }

        [Test]
        public void InputKeyDown_UsesConfiguredSendMessageShortcutMode()
        {
            using (UseCulture("en-US"))
            {
                var sendCount = 0;
                var callbacks = CreateCallbacks();
                callbacks.GetSendMessageShortcutMode = () => SendMessageShortcutMode.CtrlEnterNewLineEnterSend;
                callbacks.SendAsync = request =>
                {
                    sendCount++;
                    return Task.FromResult(new ChatWorkbenchSendResult(true, false, "sent"));
                };

                using (var control = new ChatWorkbenchControl(callbacks))
                {
                    control.SetInputText("hello");
                    var enterArgs = new KeyEventArgs(Keys.Enter);

                    InvokeInputTextBoxKeyDown(control, enterArgs);

                    Assert.That(sendCount, Is.EqualTo(1));
                    Assert.That(enterArgs.Handled, Is.True);
                    Assert.That(enterArgs.SuppressKeyPress, Is.True);

                    var ctrlEnterArgs = new KeyEventArgs(Keys.Control | Keys.Enter);
                    InvokeInputTextBoxKeyDown(control, ctrlEnterArgs);

                    Assert.That(sendCount, Is.EqualTo(1));
                    Assert.That(control.InputText, Does.Contain("\n"));
                    Assert.That(ctrlEnterArgs.Handled, Is.True);
                    Assert.That(ctrlEnterArgs.SuppressKeyPress, Is.True);
                }
            }
        }

        private static ChatWorkbenchHostCallbacks CreateCallbacks()
        {
            return new ChatWorkbenchHostCallbacks
            {
                ReadImageBytes = attachment => attachment != null ? attachment.InMemoryContent : null,
                ValidateAttachmentsAsync = attachments => Task.FromResult((IReadOnlyList<PendingAttachmentItem>)new PendingAttachmentItem[0]),
                SendAsync = request => Task.FromResult(new ChatWorkbenchSendResult(true, true, "sent")),
                StopAsync = () => Task.CompletedTask,
                CanSend = () => true,
                CanStop = () => false
            };
        }

        private static PendingAttachmentItem CreateFileAttachment()
        {
            return new PendingAttachmentItem(
                @"E:\Repo\a.txt",
                "a.txt",
                false,
                AttachmentKind.File,
                "[File: a.txt]",
                null,
                null);
        }

        private static ChatHistoryListBox CreateHistoryListBoxWithItems(int count)
        {
            var listBox = new ChatHistoryListBox();
            for (var i = 0; i < count; i++)
            {
                listBox.Items.Add(CreateChatItem("thread-1", "turn-" + i, i % 2 == 0 ? "User" : "Codex", "message " + i));
            }

            return listBox;
        }

        private static ChatHistoryItem CreateChatItem(string threadId, string turnId, string sender, string text)
        {
            var item = ChatHistoryItemFactory.Create(sender, text, ChatHistoryCategory.Conversation);
            item.ThreadId = threadId;
            item.TurnId = turnId;
            item.TurnIndex = 1;
            return item;
        }

        private static void InvokeHistoryListBoxKeyDown(ChatWorkbenchControl control, KeyEventArgs args)
        {
            var method = typeof(ChatWorkbenchControl).GetMethod("HistoryListBox_KeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var listBox = FormTestDriver.Single<ChatHistoryListBox>(control);
            method.Invoke(control, new object[] { listBox, args });
        }

        private static void InvokeInputTextBoxKeyDown(ChatWorkbenchControl control, KeyEventArgs args)
        {
            var method = typeof(ChatWorkbenchControl).GetMethod("InputTextBox_KeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var textBox = FormTestDriver.Single<AttachmentComposerTextBox>(control);
            method.Invoke(control, new object[] { textBox, args });
        }

        private static IEnumerable<Control> Flatten(Control control)
        {
            yield return control;

            foreach (Control child in control.Controls)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;
            private readonly CultureInfo _originalDefaultCulture;
            private readonly CultureInfo _originalDefaultUiCulture;

            public CultureScope(string cultureName)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
                _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
            }
        }
    }
}
