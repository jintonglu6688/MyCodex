using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ChatHistoryRenderingTests
    {
        [TestCase(null)]
        [TestCase("")]
        [TestCase("short message\r\nnext line")]
        public void RenderTextKeepsShortContent(string text)
        {
            Assert.That(ChatHistoryListBox.BuildRenderText(text), Is.EqualTo(text ?? string.Empty));
        }

        [TestCase(1024)]
        [TestCase(1025)]
        [TestCase(100000)]
        [TestCase(1000000)]
        public void RenderTextHasAFixedLayoutBudget(int length)
        {
            var text = new string('x', length);
            var rendered = ChatHistoryListBox.BuildRenderText(text);

            Assert.That(rendered.Length, Is.LessThanOrEqualTo(1024));
            if (length <= 1024) Assert.That(rendered, Is.SameAs(text));
            else Assert.That(rendered, Is.EqualTo(text.Substring(0, 1023) + "\u2026"));
        }

        [TestCase(1021, true)]
        [TestCase(1022, false)]
        [TestCase(1023, false)]
        public void RenderTextDoesNotSplitAnEmojiAtThePreviewBoundary(int prefixLength, bool keepsEmoji)
        {
            const string emoji = "\ud83d\ude00";
            var text = new string('x', prefixLength) + emoji + new string('y', 20);
            var rendered = ChatHistoryListBox.BuildRenderText(text);

            Assert.That(rendered.Contains(emoji), Is.EqualTo(keepsEmoji));
            Assert.That(rendered.Length, Is.LessThanOrEqualTo(1024));
            Assert.That(rendered, Does.EndWith("\u2026"));
            Assert.DoesNotThrow(() => new UTF8Encoding(false, true).GetBytes(rendered));
        }

        [TestCase(100000, false, 10F, 240)]
        [TestCase(100000, true, 18F, 1200)]
        [TestCase(1000000, false, 18F, 240)]
        [TestCase(1000000, true, 10F, 1200)]
        public void DrawingLargeMessagesPreservesNativeAccessibilityCopyAndCompleteReading(
            int length, bool multiline, float fontSize, int width)
        {
            var builder = new StringBuilder(length + 100);
            var line = multiline ? "INFO 合成日志 processFile(path, options);\r\n" : "processFile路径参数";
            while (builder.Length < length) builder.Append(line);
            var body = builder.ToString(0, length) + "\r\ncomplete-tail-marker \ud83d\ude00";
            var item = ChatHistoryItemFactory.Create("User", body);
            var display = item.DisplayText;
            var full = item.FullText;
            var accessible = item.AccessibleText;

            using (var history = new ChatHistoryListBox())
            using (var font = new Font("Microsoft YaHei UI", fontSize))
            {
                history.Font = font;
                history.ClientSize = new Size(width, history.ItemHeight * 3);
                history.Items.Add(item);
                var handle = history.Handle;
                history.SelectSingleIndex(0);
                using (var bitmap = new Bitmap(width, history.ItemHeight))
                using (var graphics = Graphics.FromImage(bitmap))
                    typeof(ChatHistoryListBox).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(history, new object[] { new DrawItemEventArgs(graphics, font,
                            new Rectangle(0, 0, width, history.ItemHeight), 0, DrawItemState.Selected) });

                Assert.Multiple(() =>
                {
                    Assert.That(item.DisplayText, Is.SameAs(display));
                    Assert.That(item.FullText, Is.SameAs(full));
                    Assert.That(item.AccessibleText, Is.SameAs(accessible));
                    Assert.That(item.MarkdownText, Is.EqualTo(body));
                    Assert.That(item.ToString(), Is.EqualTo(accessible));
                    Assert.That(history.GetItemText(item), Is.EqualTo(accessible));
                    Assert.That(ReadNativeItemText(handle), Is.EqualTo(accessible));
                    Assert.That(history.AccessibilityObject.GetType().Name, Does.Contain("ListBoxAccessibleObject"));
                    Assert.That(history.BuildClipboardTextForSelection(), Is.EqualTo(full));
                    Assert.That(ChatHistorySpeechFormatter.BuildAutoReadText(item, true), Is.EqualTo(full.Replace("\r\n", "\n")));
                    Assert.That(ChatHistorySpeechFormatter.BuildReadableLines(item), Does.Contain("complete-tail-marker \ud83d\ude00"));
                    Assert.That(history.FocusedIndex, Is.Zero);
                });
            }
        }

        private static string ReadNativeItemText(IntPtr handle)
        {
            var length = SendMessage(handle, 0x018A, IntPtr.Zero, IntPtr.Zero).ToInt32();
            var text = new StringBuilder(length + 1);
            Assert.That(SendMessage(handle, 0x0189, IntPtr.Zero, text).ToInt32(), Is.EqualTo(length));
            return text.ToString();
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr index, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr index, StringBuilder text);
    }
}
