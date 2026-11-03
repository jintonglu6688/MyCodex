using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Theming;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class UiThemeTests
    {
        private UiThemeMode _originalMode;

        [SetUp]
        public void SaveThemeMode()
        {
            _originalMode = UiTheme.CurrentMode;
        }

        [TearDown]
        public void RestoreThemeMode()
        {
            UiTheme.SetCurrentModeForTesting(_originalMode);
        }

        [TestCase("auto", false, false, 0)]
        [TestCase("auto", false, true, 1)]
        [TestCase("light", false, true, 0)]
        [TestCase("dark", false, false, 1)]
        [TestCase("dark", true, true, 2)]
        [TestCase("light", true, false, 2)]
        [TestCase("dark", true, false, 2)]
        [TestCase("warm", false, false, 3)]
        [TestCase("warm", false, true, 3)]
        [TestCase("warm", true, false, 2)]
        public void ResolveMode_ReturnsExpectedMode(string preference, bool isHighContrast, bool systemUsesDarkTheme, int expected)
        {
            var mode = UiTheme.ResolveMode(preference, isHighContrast, systemUsesDarkTheme);

            Assert.That((int)mode, Is.EqualTo(expected));
        }

        [TestCase("light")]
        [TestCase("dark")]
        public void ResolveMode_HighContrastOverridesExplicitPreferenceAndUpdatesAllOpenForms(string preference)
        {
            using (var first = new Form())
            using (var second = new Form())
            using (var textBox = new TextBox())
            {
                first.ShowInTaskbar = false;
                second.ShowInTaskbar = false;
                second.Controls.Add(textBox);
                first.Show();
                second.Show();
                foreach (var highContrast in new[] { false, true, false })
                {
                    var mode = UiTheme.ResolveMode(preference, highContrast, false);
                    UiTheme.SetCurrentMode(mode);
                    UiTheme.ApplyToOpenForms();

                    Assert.That(UiTheme.CurrentMode, Is.EqualTo(highContrast
                        ? UiThemeMode.HighContrast
                        : preference == "dark" ? UiThemeMode.Dark : UiThemeMode.Light));
                    var palette = UiTheme.GetPalette(mode);
                    Assert.That(first.BackColor, Is.EqualTo(palette.WindowBackColor));
                    Assert.That(second.BackColor, Is.EqualTo(palette.WindowBackColor));
                    Assert.That(textBox.ForeColor, Is.EqualTo(palette.TextColor));
                    if (highContrast)
                    {
                        Assert.That(first.BackColor, Is.EqualTo(SystemColors.Control));
                        Assert.That(textBox.BackColor, Is.EqualTo(SystemColors.Window));
                    }
                }
            }
        }

        [Test]
        public void ApplyTextBox_RepeatedDirectAndTreeApplicationPreservesFontAndSingleNormalizer()
        {
            using (var root = new Panel())
            using (var textBox = new TextReadCountingTextBox { Multiline = true })
            using (var customFont = new Font("Consolas", 14F, FontStyle.Bold))
            {
                root.Controls.Add(textBox);
                UiTheme.ApplyTextBox(textBox, false, false);
                textBox.Font = customFont;
                Assert.That(textBox.CountTextReadsOnTextChanged(), Is.EqualTo(1));

                for (var i = 0; i < 3; i++)
                {
                    UiTheme.SetCurrentModeForTesting(i % 2 == 0 ? UiThemeMode.Dark : UiThemeMode.Light);
                    UiTheme.ApplyTextBox(textBox, false, false);
                    UiTheme.ApplyToControlTree(root);
                }

                Assert.That(textBox.CountTextReadsOnTextChanged(), Is.EqualTo(1));
                Assert.That(textBox.Font, Is.SameAs(customFont));
                Assert.That(textBox.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(textBox.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                textBox.Text = "one\ntwo\rthree";
                Assert.That(textBox.Text, Is.EqualTo("one\r\ntwo\r\nthree"));
            }
        }

        [Test]
        public void ApplyTextBox_HandleCreationKeepsReadableThemeColorsInBothModes()
        {
            using (var textBox = new AttachmentComposerTextBox())
            {
                foreach (var mode in new[] { UiThemeMode.Dark, UiThemeMode.Light })
                {
                    UiTheme.SetCurrentModeForTesting(mode);
                    UiTheme.ApplyTextBox(textBox, false, false);
                    var handle = textBox.Handle;
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(handle, Is.Not.EqualTo(IntPtr.Zero));
                    Assert.That(textBox.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                    Assert.That(textBox.ForeColor, Is.EqualTo(UiTheme.TextColor));
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ApplyComboBox_UsesThemeOwnerDrawingAndPreservesHighContrast(int mode)
        {
            using (var comboBox = new ComboBox())
            {
                UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
                UiTheme.ApplyComboBox(comboBox);

                if (mode != 2)
                {
                    Assert.That(comboBox.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                    Assert.That(comboBox.ForeColor, Is.EqualTo(UiTheme.TextColor));
                    Assert.That(comboBox.DrawMode, Is.EqualTo(DrawMode.OwnerDrawFixed));
                }
                else
                {
                    Assert.That(comboBox.BackColor, Is.EqualTo(SystemColors.Window));
                    Assert.That(comboBox.ForeColor, Is.EqualTo(SystemColors.ControlText));
                    Assert.That(comboBox.DrawMode, Is.EqualTo(DrawMode.Normal));
                }
            }
        }

        [Test]
        public void ApplyComboBox_RestoresNativeStyleInHighContrast()
        {
            using (var comboBox = new ComboBox { DrawMode = DrawMode.Normal })
            {
                UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
                UiTheme.ApplyComboBox(comboBox);
                Assert.That(comboBox.DrawMode, Is.EqualTo(DrawMode.OwnerDrawFixed));

                UiTheme.SetCurrentModeForTesting(UiThemeMode.HighContrast);
                UiTheme.ApplyComboBox(comboBox);
                Assert.That(comboBox.DrawMode, Is.EqualTo(DrawMode.Normal));
                Assert.That(comboBox.BackColor, Is.EqualTo(SystemColors.Window));
                Assert.That(comboBox.ForeColor, Is.EqualTo(SystemColors.ControlText));
            }
        }

        [Test]
        public void ApplyButton_InDarkModeUsesDarkSurfaceAndDarkGlyph()
        {
            using (var button = new Button())
            {
                UiTheme.SetCurrentModeForTesting(UiThemeMode.Light);
                using (var lightGlyph = UiTheme.CreateGlyph(UiGlyph.Stop, UiTheme.ButtonIconSize))
                {
                    UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
                    UiTheme.ApplyButton(button, UiGlyph.Stop);
                    using (var buttonGlyph = (Bitmap)button.Image)
                    using (var darkGlyph = UiTheme.CreateGlyph(UiGlyph.Stop, UiTheme.ButtonIconSize))
                    {
                        Assert.That(button.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                        Assert.That(button.ForeColor, Is.EqualTo(UiTheme.TextColor));
                        Assert.That(button.FlatStyle, Is.EqualTo(FlatStyle.Flat));
                        Assert.That(button.UseVisualStyleBackColor, Is.False);
                        Assert.That(buttonGlyph.GetPixel(8, 8), Is.EqualTo(darkGlyph.GetPixel(8, 8)));
                        if (!UiTheme.IsHighContrast)
                        {
                            Assert.That(darkGlyph.GetPixel(8, 8), Is.Not.EqualTo(lightGlyph.GetPixel(8, 8)));
                        }
                    }
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void Palette_TextAndSelectionMeetMinimumContrast(int mode)
        {
            var palette = UiTheme.GetPalette((UiThemeMode)mode);
            foreach (var background in new[] { palette.WindowBackColor, palette.PanelBackColor, palette.CardBackColor })
            {
                AssertContrast(palette.TextColor, background, 4.5, "Text");
                AssertContrast(palette.MutedTextColor, background, 4.5, "Muted text");
            }

            AssertContrast(palette.SelectedTextColor, palette.SelectedBackColor, 4.5, "Selection");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void Palette_BordersMeetMinimumContrast(int mode)
        {
            var palette = UiTheme.GetPalette((UiThemeMode)mode);
            AssertContrast(palette.BorderColor, palette.WindowBackColor, 3, "Window border");
            AssertContrast(palette.BorderColor, palette.CardBackColor, 3, "Card border");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void StatusIndicators_MeetMinimumContrastOnActualStatusStrip(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var statusStrip = new StatusStrip())
            {
                UiTheme.ApplyStatusStrip(statusStrip);
                foreach (UiTheme.StatusType status in Enum.GetValues(typeof(UiTheme.StatusType)))
                {
                    using (var bitmap = UiTheme.CreateStatusIndicator(status))
                    {
                        AssertContrast(bitmap.GetPixel(8, 8), statusStrip.BackColor, 3, status.ToString());
                    }
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void ButtonTextAndGlyphs_MeetMinimumContrastOnButtonSurface(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            foreach (UiGlyph glyph in Enum.GetValues(typeof(UiGlyph)))
            {
                using (var button = new Button())
                {
                    UiTheme.ApplyButton(button, glyph);
                    var buttonFace = button.UseVisualStyleBackColor
                        ? Color.FromArgb(225, 225, 225)
                        : button.BackColor;
                    AssertContrast(button.ForeColor, buttonFace, 4.5, glyph + " text");
                    if (!button.UseVisualStyleBackColor)
                    {
                        AssertContrast(button.ForeColor, button.FlatAppearance.MouseOverBackColor, 4.5, glyph + " hover text");
                        AssertContrast(button.ForeColor, button.FlatAppearance.MouseDownBackColor, 4.5, glyph + " pressed text");
                        AssertContrast(button.FlatAppearance.BorderColor, buttonFace, 3, glyph + " border");
                    }
                    using (var bitmap = (Bitmap)button.Image)
                    {
                        var ink = GetGlyphInk(bitmap, buttonFace);
                        AssertContrast(ink, buttonFace, 3, glyph + " icon");
                    }
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void ChatCategories_TextStripeAndRenderedIconMeetMinimumContrast(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            AssertChatContrast(null);
            foreach (ChatHistoryCategory category in Enum.GetValues(typeof(ChatHistoryCategory)))
            {
                foreach (var sender in new[] { "codex", "user" })
                {
                    AssertChatContrast(new ChatHistoryItem { Category = category, Sender = sender });
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void SplitterGrip_MeetsMinimumContrastBeforeAndAfterThemeChange(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var splitter = new SplitContainer { BackColor = UiTheme.WindowBackColor })
            {
                foreach (var nextMode in new[] { mode, 1 - mode, mode })
                {
                    UiTheme.SetCurrentModeForTesting((UiThemeMode)nextMode);
                    UiTheme.ApplyToControlTree(splitter);
                    Assert.That(splitter.BackColor, Is.EqualTo(UiTheme.WindowBackColor));
                    foreach (var orientation in new[] { Orientation.Vertical, Orientation.Horizontal })
                    {
                        using (var bitmap = new Bitmap(64, 64))
                        using (var graphics = Graphics.FromImage(bitmap))
                        {
                            graphics.Clear(splitter.BackColor);
                            UiTheme.DrawSplitterGrip(graphics, new Rectangle(0, 0, 64, 64), orientation);
                            var ink = GetGlyphInk(bitmap, splitter.BackColor);
                            Assert.That(ink.ToArgb(), Is.EqualTo(UiTheme.BorderColor.ToArgb()));
                            AssertContrast(ink, splitter.BackColor, 3, "Splitter grip");
                        }
                    }
                }
            }
        }

        [Test]
        public void HighContrastPalette_UsesOnlySystemColors()
        {
            var palette = UiTheme.GetPalette(UiThemeMode.HighContrast);
            foreach (var property in typeof(UiThemePalette).GetProperties())
            {
                Assert.That(((Color)property.GetValue(palette)).IsSystemColor, Is.True, property.Name);
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void ThemeSource_CustomColorsStayInFactoriesAndMainSplittersUseWindowBackground()
        {
            var source = RepositorySource.ReadSourceFile("UiTheme.cs") +
                Environment.NewLine +
                RepositorySource.ReadSourceFile("UiTheme.Palettes.cs");
            var outsideFactories = Regex.Replace(source,
                @"        private static UiThemePalette Create(Light|WarmLight|Dark|HighContrast)Palette\(\).*?\r?\n        }",
                string.Empty, RegexOptions.Singleline);
            Assert.That(outsideFactories, Does.Not.Contain("Color.FromArgb"));

            var layout = RepositorySource.ReadSourceFile("MainForm.Layout.cs");
            foreach (var name in new[] { "_mainSplitContainer", "_contentSplitContainer" })
            {
                Assert.That(layout, Does.Match(name + @" = new SplitContainer\s*{[^}]*BackColor = UiTheme.WindowBackColor,"));
            }
        }

        private static void AssertChatContrast(ChatHistoryItem item)
        {
            var background = UiTheme.GetChatCategoryBackColor(item);
            var label = item == null ? "Empty item" : item.Category + "/" + item.Sender;
            AssertContrast(UiTheme.TextColor, background, 4.5, label + " text");
            AssertContrast(UiTheme.GetChatCategoryColor(item), background, 3, label + " stripe");
            using (var bitmap = UiTheme.CreateChatCategoryGlyph(item, 16))
            {
                AssertContrast(GetGlyphInk(bitmap, UiTheme.CardBackColor), background, 3, label + " icon");
            }
        }

        private static Color GetGlyphInk(Bitmap bitmap, Color contrastColor)
        {
            var counts = new Dictionary<int, int>();
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var color = bitmap.GetPixel(x, y);
                    if (color.A != 255 || color.ToArgb() == contrastColor.ToArgb())
                    {
                        continue;
                    }

                    counts.TryGetValue(color.ToArgb(), out var count);
                    counts[color.ToArgb()] = count + 1;
                }
            }

            Assert.That(counts, Is.Not.Empty, "Rendered glyph must contain opaque ink");
            return Color.FromArgb(counts.OrderByDescending(pair => pair.Value).First().Key);
        }

        private static void InvokeThemeDrawing(string methodName, object sender, object eventArgs)
        {
            var method = typeof(UiTheme).GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(null, new[] { sender, eventArgs });
        }

        private static int CountPixelsNotEqual(Bitmap bitmap, Color expected)
        {
            var count = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    if (bitmap.GetPixel(x, y).ToArgb() != expected.ToArgb())
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static void AssertContrast(Color foreground, Color background, double minimum, string label)
        {
            Assert.That(ContrastRatio(foreground, background), Is.GreaterThanOrEqualTo(minimum), label + ": " + foreground + " on " + background);
        }

        private static double ContrastRatio(Color foreground, Color background)
        {
            var first = RelativeLuminance(foreground);
            var second = RelativeLuminance(background);
            return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
        }

        private static object InvokeTheme(string methodName, params object[] arguments)
        {
            var method = typeof(UiTheme).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            return method.Invoke(null, arguments);
        }

        private static double RelativeLuminance(Color color)
        {
            return 0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);
        }

        private static double LinearChannel(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        private sealed class TextReadCountingTextBox : TextBox
        {
            private int _textReads;

            public override string Text
            {
                get
                {
                    _textReads++;
                    return base.Text;
                }
                set { base.Text = value; }
            }

            public int CountTextReadsOnTextChanged()
            {
                _textReads = 0;
                OnTextChanged(EventArgs.Empty);
                return _textReads;
            }
        }

        [Test]
        public void GetPalette_ReturnsDarkPaletteWithReadableContrast()
        {
            var palette = UiTheme.GetPalette(UiThemeMode.Dark);

            Assert.That(palette.WindowBackColor, Is.EqualTo(Color.FromArgb(7, 10, 15)));
            Assert.That(palette.TextColor, Is.EqualTo(Color.FromArgb(244, 247, 250)));
            Assert.That(palette.SelectedTextColor, Is.EqualTo(Color.FromArgb(255, 255, 255)));
        }

        [Test]
        public void ApplyToControlTree_UpdatesNestedControlsToDarkPalette()
        {
            using (var root = new Panel())
            using (var label = new Label())
            using (var textBox = new TextBox())
            using (var button = new Button())
            {
                root.Controls.Add(label);
                root.Controls.Add(textBox);
                root.Controls.Add(button);

                UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
                UiTheme.ApplyToControlTree(root);

                Assert.That(root.BackColor, Is.EqualTo(UiTheme.WindowBackColor));
                Assert.That(label.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(textBox.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(button.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(button.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(button.FlatStyle, Is.EqualTo(FlatStyle.Flat));
                Assert.That(button.UseVisualStyleBackColor, Is.False);
            }
        }

        [Test]
        public void ApplyToControlTree_ThemesTabsTreesAndListHeaders()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var root = new Panel())
            using (var tabs = new ThemedTabControl())
            using (var page = new TabPage("Settings"))
            using (var tree = new TreeView())
            using (var list = new ListView { View = View.Details, Size = new Size(300, 200) })
            {
                tabs.TabPages.Add(page);
                page.Controls.Add(tree);
                root.Controls.Add(tabs);
                root.Controls.Add(list);
                list.Columns.Add("Name", 120);
                list.Columns.Add("Status", 150);
                var expandedNode = tree.Nodes.Add("Plugins");
                expandedNode.Nodes.Add("Installed");
                expandedNode.Expand();

                UiTheme.ApplyToControlTree(root);

                Assert.That(page.BackColor, Is.EqualTo(UiTheme.WindowBackColor));
                Assert.That(page.UseVisualStyleBackColor, Is.False);
                Assert.That(tree.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(tree.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(tree.LineColor, Is.EqualTo(UiTheme.BorderColor));
                Assert.That(expandedNode.IsExpanded, Is.True);
                Assert.That(list.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(list.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(list.OwnerDraw, Is.True);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ListViewListRows_UseThemeSelectionBackground(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var list = new ListView { View = View.List, Size = new Size(300, 200) })
            using (var bitmap = new Bitmap(300, 30))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var item = list.Items.Add(new ListViewItem("Selected list row"));
                item.Selected = true;
                UiTheme.ApplyListView(list);
                Assert.That(list.OwnerDraw, Is.True);

                InvokeThemeDrawing(
                    "DrawListViewItem",
                    list,
                    new DrawListViewItemEventArgs(
                        graphics,
                        item,
                        new Rectangle(0, 0, 260, 24),
                        0,
                        ListViewItemStates.Selected));
                Assert.That(bitmap.GetPixel(250, 5), Is.EqualTo(UiTheme.SelectedBackColor));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ApplyToolStrip_UsesThemeRendererAndRecursesIntoDropDowns(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var toolStrip = new ToolStrip { RenderMode = ToolStripRenderMode.System })
            using (var item = new ToolStripDropDownButton("Actions"))
            using (var disabledItem = new ToolStripMenuItem("Unavailable") { Enabled = false })
            using (var bitmap = new Bitmap(220, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                toolStrip.Size = new Size(220, 32);
                toolStrip.Items.Add(item);
                item.DropDownItems.Add(disabledItem);

                UiTheme.ApplyToolStrip(toolStrip);

                Assert.That(toolStrip.RenderMode, Is.EqualTo(ToolStripRenderMode.Custom));
                Assert.That(toolStrip.Renderer, Is.Not.Null);
                Assert.That(item.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(disabledItem.ForeColor, Is.EqualTo(UiTheme.MutedTextColor));

                toolStrip.Renderer.DrawToolStripBackground(
                    new ToolStripRenderEventArgs(graphics, toolStrip));
                Assert.That(bitmap.GetPixel(10, 10), Is.EqualTo(UiTheme.WindowBackColor));
            }
        }

        [Test]
        public void ApplyToolStrip_RestoresSystemRendererInHighContrast()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.HighContrast);
            using (var toolStrip = new ToolStrip())
            {
                UiTheme.ApplyToolStrip(toolStrip);

                Assert.That(toolStrip.RenderMode, Is.EqualTo(ToolStripRenderMode.System));
            }
        }

        [Test]
        public void ApplyToControlTree_OwnerDrawsClassicMenusInThemeModes()
        {
            using (var form = new Form())
            using (var menu = new MainMenu())
            {
                var parent = new MenuItem("File");
                var child = new MenuItem("Open");
                parent.MenuItems.Add(child);
                menu.MenuItems.Add(parent);
                form.Menu = menu;

                UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
                UiTheme.ApplyToControlTree(form);
                Assert.That(parent.OwnerDraw, Is.True);
                Assert.That(child.OwnerDraw, Is.True);

                UiTheme.SetCurrentModeForTesting(UiThemeMode.Light);
                UiTheme.ApplyToControlTree(form);
                Assert.That(parent.OwnerDraw, Is.True);
                Assert.That(child.OwnerDraw, Is.True);

                UiTheme.SetCurrentModeForTesting(UiThemeMode.HighContrast);
                UiTheme.ApplyToControlTree(form);
                Assert.That(parent.OwnerDraw, Is.False);
                Assert.That(child.OwnerDraw, Is.False);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void SelectedListAndTreeRows_UseThemeSelectionBackground(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var list = new ListView { View = View.Details })
            using (var tree = new TreeView())
            using (var bitmap = new Bitmap(400, 30))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                list.Columns.Add("Name", 240);
                var item = list.Items.Add(new ListViewItem("Selected row"));
                item.Selected = true;
                UiTheme.ApplyListView(list);
                InvokeThemeDrawing(
                    "DrawListViewItem",
                    list,
                    new DrawListViewItemEventArgs(
                        graphics,
                        item,
                        new Rectangle(0, 0, 240, 24),
                        0,
                        ListViewItemStates.Selected));
                Assert.That(bitmap.GetPixel(230, 5), Is.EqualTo(UiTheme.SelectedBackColor));

                graphics.Clear(UiTheme.CardBackColor);
                var node = tree.Nodes.Add("Selected node");
                tree.SelectedNode = node;
                UiTheme.ApplyTreeView(tree);
                InvokeThemeDrawing(
                    "DrawTreeNode",
                    tree,
                    new DrawTreeNodeEventArgs(
                        graphics,
                        node,
                        new Rectangle(0, 0, 240, 24),
                        TreeNodeStates.Selected));
                Assert.That(bitmap.GetPixel(230, 5), Is.EqualTo(UiTheme.SelectedBackColor));
            }
        }

        [Test]
        public void TreeNodes_DrawAtTheirNativeLevelIndent()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var tree = new TreeView { Size = new Size(300, 200), ShowRootLines = true })
            {
                var root = tree.Nodes.Add("Root");
                var child = root.Nodes.Add("Child");
                var grandchild = child.Nodes.Add("Grandchild");
                root.ExpandAll();
                Assert.That(tree.Handle, Is.Not.EqualTo(IntPtr.Zero));
                UiTheme.ApplyTreeView(tree);

                var row = new Rectangle(0, 0, 300, 20);
                var lefts = new[] { root, child, grandchild }
                    .Select(node => (int)InvokeTheme("GetTreeNodeLabelLeft", tree, node, row))
                    .ToArray();

                Assert.That(lefts[1] - lefts[0], Is.EqualTo(tree.Indent));
                Assert.That(lefts[2] - lefts[1], Is.EqualTo(tree.Indent));
                Assert.That(lefts[0], Is.GreaterThan(0));
            }
        }

        [Test]
        public void ApplyTreeView_KeepsExpansionWithoutRaisingTreeEvents()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var tree = new TreeView { Size = new Size(300, 200) })
            {
                var root = tree.Nodes.Add("Root");
                var child = root.Nodes.Add("Child");
                child.Nodes.Add("Grandchild");
                root.ExpandAll();
                tree.SelectedNode = child;
                Assert.That(tree.Handle, Is.Not.EqualTo(IntPtr.Zero));
                var events = 0;
                tree.AfterSelect += (sender, args) => events++;
                tree.AfterCollapse += (sender, args) => events++;
                tree.AfterExpand += (sender, args) => events++;

                UiTheme.ApplyTreeView(tree);

                Assert.That(events, Is.EqualTo(0));
                Assert.That(root.IsExpanded && child.IsExpanded, Is.True);
                Assert.That(tree.SelectedNode, Is.SameAs(child));
            }
        }

        [Test]
        public void ListViewFirstColumn_IsClippedToItsColumnWidth()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var list = new ListView { View = View.Details, Size = new Size(460, 200) })
            {
                list.Columns.Add("Name", 120);
                list.Columns.Add("Status", 150);
                var item = list.Items.Add(new ListViewItem(new[] { "A first column value that is much wider than its column", "Second" }));
                Assert.That(list.Handle, Is.Not.EqualTo(IntPtr.Zero));

                var first = (Rectangle)InvokeTheme("GetListViewCellBounds", list, item.SubItems[0], 0);
                var second = (Rectangle)InvokeTheme("GetListViewCellBounds", list, item.SubItems[1], 1);

                Assert.That(item.SubItems[0].Bounds.Width, Is.GreaterThan(120), "precondition: the native first cell spans the row");
                Assert.That(first.Width, Is.EqualTo(120));
                Assert.That(first.Right, Is.LessThanOrEqualTo(second.Left));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void AutoSizeToggles_AreWideEnoughForThemedGlyphAndText(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var panel = new FlowLayoutPanel { Size = new Size(800, 200), FlowDirection = FlowDirection.TopDown, WrapContents = false })
            using (var checkBox = new ThemedCheckBox { Text = "Fetch remote updates first(&F)", AutoSize = true })
            using (var radioButton = new ThemedRadioButton { Text = "自动朗读新回复(&A)", AutoSize = true })
            {
                panel.Controls.Add(checkBox);
                panel.Controls.Add(radioButton);
                Assert.That(panel.Handle, Is.Not.EqualTo(IntPtr.Zero));
                UiTheme.ApplyToControlTree(panel);
                panel.PerformLayout();

                foreach (ButtonBase control in new ButtonBase[] { checkBox, radioButton })
                {
                    var text = TextRenderer.MeasureText(control.Text, control.Font, Size.Empty, TextFormatFlags.SingleLine);
                    var chrome = UiTheme.Scale(4) + UiTheme.Scale(16) + UiTheme.Scale(6) + UiTheme.Scale(4);
                    Assert.That(control.Width, Is.GreaterThanOrEqualTo(chrome + text.Width), control.Text);
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(3)]
        public void DisabledText_IsDimmerThanEnabledText(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);

            var enabled = ContrastRatio(UiTheme.TextColor, UiTheme.CardBackColor);
            var disabled = ContrastRatio(UiTheme.DisabledTextColor, UiTheme.CardBackColor);
            var muted = ContrastRatio(UiTheme.MutedTextColor, UiTheme.CardBackColor);

            Assert.That(disabled, Is.LessThan(muted));
            Assert.That(disabled, Is.LessThan(enabled / 1.5));
            Assert.That(disabled, Is.GreaterThanOrEqualTo(2.5), "disabled text must stay legible");
        }

        [Test]
        public void ComboBoxChrome_CoversTheNativeFlatFrameWhenDisabled()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var comboBox = new ThemedComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Enabled = false })
            {
                comboBox.Items.Add("Monday");
                comboBox.SelectedIndex = 0;
                UiTheme.ApplyComboBox(comboBox);
                Assert.That(comboBox.Handle, Is.Not.EqualTo(IntPtr.Zero));
                Rectangle item;
                Rectangle button;
                Assert.That(NativeWindowTheme.TryGetComboBoxParts(comboBox, out item, out button), Is.True);

                using (var bitmap = new Bitmap(comboBox.Width, comboBox.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    UiTheme.PaintComboBoxChrome(comboBox, graphics);

                    var middle = comboBox.ClientSize.Height / 2;
                    Assert.That(bitmap.GetPixel(1, middle).ToArgb(), Is.EqualTo(UiTheme.CardBackColor.ToArgb()), "inner frame");
                    Assert.That(bitmap.GetPixel(item.Left + 2, 1).ToArgb(), Is.EqualTo(UiTheme.CardBackColor.ToArgb()), "top frame");
                    if (button.Left - item.Right > 0)
                    {
                        Assert.That(bitmap.GetPixel(button.Left - 1, middle).ToArgb(), Is.EqualTo(UiTheme.CardBackColor.ToArgb()), "gap before the button");
                    }
                }
            }
        }

        [Test]
        public void ThemedTabControl_KeepsNativeTabSizes()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var themed = new ThemedTabControl { Size = new Size(500, 200) })
            using (var native = new TabControl { Size = new Size(500, 200), Font = themed.Font })
            {
                foreach (var text in new[] { "General", "A much longer tab title" })
                {
                    themed.TabPages.Add(new TabPage(text));
                    native.TabPages.Add(new TabPage(text));
                }

                Assert.That(themed.Handle, Is.Not.EqualTo(IntPtr.Zero));
                Assert.That(native.Handle, Is.Not.EqualTo(IntPtr.Zero));
                UiTheme.ApplyToControlTree(themed);

                var textHeight = TextRenderer.MeasureText("Ag", themed.Font).Height;
                Assert.That(themed.SizeMode, Is.EqualTo(TabSizeMode.Normal));
                Assert.That(themed.GetTabRect(0).Height, Is.GreaterThanOrEqualTo(textHeight + 4));
                Assert.That(themed.GetTabRect(0).Height, Is.LessThanOrEqualTo(native.GetTabRect(0).Height + UiTheme.Scale(3)));
                Assert.That(themed.GetTabRect(0).Width, Is.LessThan(themed.GetTabRect(1).Width));
            }
        }

        [Test]
        public void ThemedProgressBar_AnimatesOnlyWhileVisibleInMarqueeStyle()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
            using (var progressBar = new ThemedProgressBar { Style = ProgressBarStyle.Continuous })
            {
                form.Controls.Add(progressBar);
                form.Show();
                Assert.That(progressBar.IsAnimatingForTests, Is.False);

                progressBar.Style = ProgressBarStyle.Marquee;
                Assert.That(progressBar.IsAnimatingForTests, Is.True);

                progressBar.Visible = false;
                Assert.That(progressBar.IsAnimatingForTests, Is.False);
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void SelectedComboAndMenuItems_UseThemeSelectionBackground(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var comboBox = new ComboBox())
            using (var menuItem = new MenuItem("Selected menu item"))
            using (var bitmap = new Bitmap(400, 30))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                comboBox.Items.Add("Selected combo item");
                comboBox.SelectedIndex = 0;
                UiTheme.ApplyComboBox(comboBox);
                InvokeThemeDrawing(
                    "DrawComboBoxItem",
                    comboBox,
                    new DrawItemEventArgs(
                        graphics,
                        comboBox.Font,
                        new Rectangle(0, 0, 240, 24),
                        0,
                        DrawItemState.Selected));
                Assert.That(bitmap.GetPixel(230, 5), Is.EqualTo(UiTheme.SelectedBackColor));

                graphics.Clear(UiTheme.CardBackColor);
                InvokeThemeDrawing(
                    "DrawMenuItem",
                    menuItem,
                    new DrawItemEventArgs(
                        graphics,
                        SystemInformation.MenuFont,
                        new Rectangle(0, 0, 400, 26),
                        0,
                        DrawItemState.Selected));
                Assert.That(bitmap.GetPixel(300, 15), Is.EqualTo(UiTheme.SelectedBackColor));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void SelectedListBoxItems_UseThemeSelectionBackground(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var listBox = new ListBox())
            using (var bitmap = new Bitmap(240, 30))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                listBox.Items.Add("Selected list item");
                listBox.SelectedIndex = 0;
                UiTheme.ApplyListBox(listBox);
                InvokeThemeDrawing(
                    "DrawListBoxItem",
                    listBox,
                    new DrawItemEventArgs(
                        graphics,
                        listBox.Font,
                        new Rectangle(0, 0, 240, 24),
                        0,
                        DrawItemState.Selected));
                Assert.That(bitmap.GetPixel(230, 5), Is.EqualTo(UiTheme.SelectedBackColor));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void CheckBoxAndRadioButton_UseThemePaintingAndStateColors(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var parent = new Panel { BackColor = UiTheme.PanelBackColor })
            using (var checkBox = new ThemedCheckBox { Text = "Enabled", Checked = true, Size = new Size(180, 28) })
            using (var radioButton = new ThemedRadioButton { Text = "Choice", Checked = true, Size = new Size(180, 28) })
            using (var bitmap = new Bitmap(180, 28))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                parent.Controls.Add(checkBox);
                parent.Controls.Add(radioButton);
                UiTheme.ApplyToControlTree(parent);
                Assert.That(checkBox.BackColor, Is.EqualTo(parent.BackColor));
                Assert.That(checkBox.UseVisualStyleBackColor, Is.False);
                Assert.That(radioButton.BackColor, Is.EqualTo(parent.BackColor));
                Assert.That(radioButton.UseVisualStyleBackColor, Is.False);

                graphics.Clear(parent.BackColor);
                InvokeThemeDrawing(
                    "PaintToggleControl",
                    checkBox,
                    new PaintEventArgs(graphics, new Rectangle(0, 0, 180, 28)));
                Assert.That(CountPixelsNotEqual(bitmap, parent.BackColor), Is.GreaterThan(0));

                graphics.Clear(parent.BackColor);
                InvokeThemeDrawing(
                    "PaintToggleControl",
                    radioButton,
                    new PaintEventArgs(graphics, new Rectangle(0, 0, 180, 28)));
                Assert.That(CountPixelsNotEqual(bitmap, parent.BackColor), Is.GreaterThan(0));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ThemedProgressBar_UsesThemeTrackAndFillColors(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var progressBar = new ThemedProgressBar
                   {
                       Minimum = 0,
                       Maximum = 100,
                       Value = 50,
                       Style = ProgressBarStyle.Continuous,
                       Size = new Size(200, 24)
                   })
            using (var bitmap = new Bitmap(200, 24))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                var paint = typeof(ThemedProgressBar).GetMethod(
                    "PaintBar",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(paint, Is.Not.Null);
                paint.Invoke(progressBar, new object[] { graphics });

                Assert.That(bitmap.GetPixel(10, 10), Is.EqualTo(UiTheme.AccentColor));
                Assert.That(bitmap.GetPixel(190, 10), Is.EqualTo(UiTheme.SubtleBackColor));
            }
        }

        [Test]
        public void ApplyToControlTree_ThemesUpDownAndCalendarControls()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var root = new Panel())
            using (var numeric = new NumericUpDown())
            using (var picker = new ThemedDateTimePicker())
            {
                root.Controls.Add(numeric);
                root.Controls.Add(picker);

                UiTheme.ApplyToControlTree(root);

                Assert.That(numeric.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(numeric.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(picker.CalendarMonthBackground, Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(picker.CalendarForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(picker.CalendarTitleBackColor, Is.EqualTo(UiTheme.PanelBackColor));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void ThemedDateTimePicker_PaintsItsClosedFieldInThemeColors(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            using (var picker = new ThemedDateTimePicker { Size = new Size(220, 32) })
            using (var bitmap = new Bitmap(220, 32))
            using (var graphics = Graphics.FromImage(bitmap))
            {
                picker.CreateControl();
                var paint = typeof(ThemedDateTimePicker).GetMethod(
                    "PaintChrome",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(paint, Is.Not.Null);
                paint.Invoke(picker, new object[] { graphics });

                var bottomInterior = Math.Max(1, picker.Height - 4);
                Assert.That(bitmap.GetPixel(8, bottomInterior), Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(bitmap.GetPixel(Math.Max(1, picker.Width - 3), bottomInterior), Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(bitmap.GetPixel(0, 0), Is.EqualTo(UiTheme.BorderColor));
            }
        }

        [Test]
        public void ApplyForm_WhenShown_ThemesControlsCreatedAfterApplyForm()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var form = new Form { ShowInTaskbar = false })
            using (var button = new Button { Text = "Action" })
            {
                UiTheme.ApplyForm(form);
                form.Controls.Add(button);
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                Assert.That(button.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(button.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(button.FlatStyle, Is.EqualTo(FlatStyle.Flat));
            }
        }

        [Test]
        public void ApplyForm_WhenShown_ThemesControlsAddedAfterShown()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
            using (var form = new Form { ShowInTaskbar = false })
            using (var host = new Panel { Dock = DockStyle.Fill })
            using (var button = new Button { Text = "Late action" })
            {
                form.Controls.Add(host);
                UiTheme.ApplyForm(form);
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                host.Controls.Add(button);
                System.Windows.Forms.Application.DoEvents();

                Assert.That(button.BackColor, Is.EqualTo(UiTheme.CardBackColor));
                Assert.That(button.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(button.FlatStyle, Is.EqualTo(FlatStyle.Flat));
            }
        }

        [Test]
        public void CommandTerminalOutputControl_UsesDedicatedTerminalPaletteInLightMode()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Light);

            using (var control = new CommandTerminalOutputControl())
            {
                Assert.That(control.BackColor, Is.EqualTo(Color.FromArgb(11, 18, 32)));
                Assert.That(control.ForeColor, Is.EqualTo(Color.FromArgb(226, 232, 240)));
            }
        }

        [Test]
        public void ApplyToControlTree_ReappliesTerminalPaletteAfterThemeChange()
        {
            using (var parent = new Panel())
            using (var control = new CommandTerminalOutputControl())
            {
                parent.Controls.Add(control);

                UiTheme.SetCurrentModeForTesting(UiThemeMode.Light);
                UiTheme.ApplyToControlTree(parent);

                Assert.That(control.BackColor, Is.EqualTo(Color.FromArgb(11, 18, 32)));
                Assert.That(control.ForeColor, Is.EqualTo(Color.FromArgb(226, 232, 240)));
            }
        }

        [Test]
        public void StructuredMessageViewerForm_UsesThemeBackColor()
        {
            UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);

            using (var form = new StructuredMessageViewerForm(new ChatHistoryItem()))
            {
                Assert.That(form.BackColor, Is.EqualTo(UiTheme.WindowBackColor));
            }
        }
    }
}
