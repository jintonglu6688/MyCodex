using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ApplyComboBox_UsesNativeColorsAndPreservesHighContrastBehavior(int mode)
        {
            using (var comboBox = new ComboBox())
            {
                UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
                UiTheme.ApplyComboBox(comboBox);

                Assert.That(comboBox.BackColor, Is.EqualTo(SystemColors.Window));
                Assert.That(comboBox.ForeColor, Is.EqualTo(mode == 2 ? SystemColors.ControlText : SystemColors.WindowText));
            }
        }

        [Test]
        public void ApplyButton_InDarkModeUsesLightGlyphWithoutChangingNonButtonGlyphs()
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
                        Assert.That(buttonGlyph.GetPixel(8, 8), Is.EqualTo(lightGlyph.GetPixel(8, 8)));
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
        public void Palette_BordersMeetMinimumContrast(int mode)
        {
            var palette = UiTheme.GetPalette((UiThemeMode)mode);
            AssertContrast(palette.BorderColor, palette.WindowBackColor, 3, "Window border");
            AssertContrast(palette.BorderColor, palette.CardBackColor, 3, "Card border");
        }

        [TestCase(0)]
        [TestCase(1)]
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
        public void ButtonGlyphs_MeetMinimumContrastOnNativeLightButtonFace(int mode)
        {
            UiTheme.SetCurrentModeForTesting((UiThemeMode)mode);
            foreach (UiGlyph glyph in Enum.GetValues(typeof(UiGlyph)))
            {
                using (var button = new Button())
                {
                    UiTheme.ApplyButton(button, glyph);
                    using (var bitmap = (Bitmap)button.Image)
                    {
                        var ink = GetGlyphInk(bitmap, UiTheme.GetPalette(UiThemeMode.Light).CardBackColor);
                        AssertContrast(ink, Color.FromArgb(225, 225, 225), 3, glyph.ToString());
                    }
                }
            }
        }

        [TestCase(0)]
        [TestCase(1)]
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
            var source = RepositorySource.ReadSourceFile("UiTheme.cs");
            var outsideFactories = Regex.Replace(source,
                @"        private static UiThemePalette Create(Light|Dark|HighContrast)Palette\(\).*?\r?\n        }",
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

        private static void AssertContrast(Color foreground, Color background, double minimum, string label)
        {
            var first = RelativeLuminance(foreground);
            var second = RelativeLuminance(background);
            var ratio = (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
            Assert.That(ratio, Is.GreaterThanOrEqualTo(minimum), label + ": " + foreground + " on " + background);
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

            Assert.That(palette.WindowBackColor, Is.EqualTo(Color.FromArgb(15, 23, 42)));
            Assert.That(palette.TextColor, Is.EqualTo(Color.FromArgb(229, 231, 235)));
            Assert.That(palette.SelectedTextColor, Is.EqualTo(Color.FromArgb(248, 250, 252)));
        }

        [Test]
        public void ApplyToControlTree_UpdatesNestedControlsToDarkPalette()
        {
            using (var root = new Panel())
            using (var label = new Label())
            using (var textBox = new TextBox())
            {
                root.Controls.Add(label);
                root.Controls.Add(textBox);

                UiTheme.SetCurrentModeForTesting(UiThemeMode.Dark);
                UiTheme.ApplyToControlTree(root);

                Assert.That(root.BackColor, Is.EqualTo(UiTheme.WindowBackColor));
                Assert.That(label.ForeColor, Is.EqualTo(UiTheme.TextColor));
                Assert.That(textBox.ForeColor, Is.EqualTo(UiTheme.TextColor));
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
