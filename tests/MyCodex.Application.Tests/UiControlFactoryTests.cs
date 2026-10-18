using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Theming;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class UiControlFactoryTests
    {
        [TestCase(112, DialogResult.None)]
        [TestCase(118, DialogResult.OK)]
        [TestCase(104, DialogResult.Cancel)]
        public void CreateActionButton_MatchesReplacedFormHelper(int width, DialogResult dialogResult)
        {
            using (var expected = new Button
            {
                Text = "Create(&C)",
                AccessibleName = "Create",
                Width = width,
                Height = UiTheme.ButtonHeight,
                Margin = new Padding(8, 0, 0, 0),
                UseMnemonic = true,
                TabIndex = 4,
                DialogResult = dialogResult
            })
            using (var actual = UiControlFactory.CreateActionButton("Create(&C)", "Create", 4, width, dialogResult))
            {
                AssertSameButton(actual, expected);
                Assert.That(actual.Size, Is.EqualTo(new Size(width, 32)));
            }
        }

        [Test]
        public void CreateActionButton_WithoutDialogResult_LeavesDefault()
        {
            using (var button = UiControlFactory.CreateActionButton("Refresh(&R)", "Refresh", 2, 112))
            {
                Assert.That(button.DialogResult, Is.EqualTo(DialogResult.None));
                Assert.That(button.TabIndex, Is.EqualTo(2));
            }
        }

        [TestCase(DialogResult.OK)]
        [TestCase(DialogResult.None)]
        public void CreateDialogButton_MatchesReplacedFormHelper(DialogResult dialogResult)
        {
            var glyph = dialogResult == DialogResult.OK ? UiGlyph.Ok : UiGlyph.Cancel;
            using (var expected = new Button
            {
                Text = "Allow(&A)",
                AccessibleName = "Allow pairing",
                AutoSize = true,
                TabIndex = 1,
                DialogResult = dialogResult
            })
            using (var actual = UiControlFactory.CreateDialogButton("Allow(&A)", "Allow pairing", 1, glyph, dialogResult))
            {
                UiTheme.ApplyDialogButton(expected, glyph);

                AssertSameButton(actual, expected);
                Assert.That(actual.AutoSizeMode, Is.EqualTo(AutoSizeMode.GrowOnly));
                Assert.That(actual.MinimumSize, Is.EqualTo(new Size(UiTheme.ButtonWidth, UiTheme.ButtonHeight)));
                Assert.That(actual.Image, Is.Not.Null);
            }
        }

        [TestCase("Remote name")]
        [TestCase(null)]
        public void CreateMutedLabel_MatchesReplacedFormHelpers(string text)
        {
            using (var expected = new Label
            {
                Dock = DockStyle.Fill,
                ForeColor = UiTheme.MutedTextColor,
                Text = text ?? string.Empty,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false,
                UseMnemonic = true
            })
            using (var actual = UiControlFactory.CreateMutedLabel(text))
            {
                UiTheme.ApplyLabel(expected, true);

                Assert.Multiple(() =>
                {
                    Assert.That(actual.Text, Is.EqualTo(expected.Text));
                    Assert.That(actual.Dock, Is.EqualTo(expected.Dock));
                    Assert.That(actual.TextAlign, Is.EqualTo(expected.TextAlign));
                    Assert.That(actual.TabStop, Is.False);
                    Assert.That(actual.UseMnemonic, Is.EqualTo(expected.UseMnemonic));
                    Assert.That(actual.AutoSize, Is.EqualTo(expected.AutoSize));
                    Assert.That(actual.Size, Is.EqualTo(expected.Size));
                    Assert.That(actual.ForeColor, Is.EqualTo(expected.ForeColor));
                });
            }
        }

        [Test]
        public void CreateFillTextBox_MatchesReplacedFormHelpers()
        {
            using (var expected = new TextBox { AccessibleName = "Remote URL", Dock = DockStyle.Fill, TabIndex = 3 })
            using (var actual = UiControlFactory.CreateFillTextBox("Remote URL", 3))
            {
                UiTheme.ApplyTextBox(expected, false, false);

                Assert.Multiple(() =>
                {
                    Assert.That(actual.AccessibleName, Is.EqualTo(expected.AccessibleName));
                    Assert.That(actual.Dock, Is.EqualTo(expected.Dock));
                    Assert.That(actual.TabIndex, Is.EqualTo(expected.TabIndex));
                    Assert.That(actual.Size, Is.EqualTo(expected.Size));
                    Assert.That(actual.Font, Is.EqualTo(expected.Font));
                    Assert.That(actual.BorderStyle, Is.EqualTo(expected.BorderStyle));
                    Assert.That(actual.BackColor, Is.EqualTo(expected.BackColor));
                    Assert.That(actual.ForeColor, Is.EqualTo(expected.ForeColor));
                    Assert.That(actual.Multiline, Is.False);
                    Assert.That(actual.ReadOnly, Is.False);
                });
            }
        }

        [TestCase(DockStyle.Top)]
        [TestCase(DockStyle.Fill)]
        public void CreateDropDownList_MatchesReplacedFormHelpers(DockStyle dock)
        {
            using (var expected = new ComboBox { AccessibleName = "Model", Dock = dock, DropDownStyle = ComboBoxStyle.DropDownList, TabIndex = 2 })
            using (var actual = UiControlFactory.CreateDropDownList("Model", 2, dock))
            {
                UiTheme.ApplyComboBox(expected);

                Assert.Multiple(() =>
                {
                    Assert.That(actual.AccessibleName, Is.EqualTo(expected.AccessibleName));
                    Assert.That(actual.Dock, Is.EqualTo(dock));
                    Assert.That(actual.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
                    Assert.That(actual.TabIndex, Is.EqualTo(expected.TabIndex));
                    Assert.That(actual.Size, Is.EqualTo(expected.Size));
                    Assert.That(actual.BackColor, Is.EqualTo(expected.BackColor));
                    Assert.That(actual.ForeColor, Is.EqualTo(expected.ForeColor));
                });
            }
        }

        [Test]
        public void CreateDialogButtonBar_MatchesReplacedFooterPanels()
        {
            using (var parent = new Panel { BackColor = Color.LightYellow })
            using (var inherited = UiControlFactory.CreateDialogButtonBar())
            using (var themed = UiControlFactory.CreateDialogButtonBar(UiTheme.WindowBackColor))
            {
                parent.Controls.Add(inherited);

                Assert.Multiple(() =>
                {
                    foreach (var panel in new[] { inherited, themed })
                    {
                        Assert.That(panel.Dock, Is.EqualTo(DockStyle.Fill));
                        Assert.That(panel.FlowDirection, Is.EqualTo(FlowDirection.RightToLeft));
                        Assert.That(panel.WrapContents, Is.False);
                        Assert.That(panel.AutoSize, Is.True);
                        Assert.That(panel.TabStop, Is.False);
                    }

                    Assert.That(inherited.BackColor, Is.EqualTo(Color.LightYellow), "Without a color the footer inherits its parent's back color.");
                    Assert.That(themed.BackColor, Is.EqualTo(UiTheme.WindowBackColor));
                });
            }
        }

        private static void AssertSameButton(Button actual, Button expected)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.Text, Is.EqualTo(expected.Text));
                Assert.That(actual.AccessibleName, Is.EqualTo(expected.AccessibleName));
                Assert.That(actual.Size, Is.EqualTo(expected.Size));
                Assert.That(actual.MinimumSize, Is.EqualTo(expected.MinimumSize));
                Assert.That(actual.Margin, Is.EqualTo(expected.Margin));
                Assert.That(actual.Padding, Is.EqualTo(expected.Padding));
                Assert.That(actual.AutoSize, Is.EqualTo(expected.AutoSize));
                Assert.That(actual.AutoSizeMode, Is.EqualTo(expected.AutoSizeMode));
                Assert.That(actual.UseMnemonic, Is.EqualTo(expected.UseMnemonic));
                Assert.That(actual.TabIndex, Is.EqualTo(expected.TabIndex));
                Assert.That(actual.TabStop, Is.EqualTo(expected.TabStop));
                Assert.That(actual.DialogResult, Is.EqualTo(expected.DialogResult));
                Assert.That(actual.FlatStyle, Is.EqualTo(expected.FlatStyle));
                Assert.That(actual.TextImageRelation, Is.EqualTo(expected.TextImageRelation));
            });
        }
    }
}
