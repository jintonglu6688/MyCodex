using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Theming;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [NonParallelizable]
    public sealed class GuiConsistencyTests
    {
        [TestCase("zh-CN", "Microsoft YaHei UI")]
        [TestCase("ZH-cn", "Microsoft YaHei UI")]
        [TestCase("en-US", "Segoe UI")]
        [TestCase("en", "Segoe UI")]
        [TestCase("zh-TW", "Segoe UI")]
        [TestCase("fr-FR", "Segoe UI")]
        [TestCase(null, "Segoe UI")]
        public void ResolveUiFontFamily_UsesAppliedLanguage(string language, string expected)
        {
            Assert.That(UiTheme.ResolveUiFontFamily(language), Is.EqualTo(expected));
        }

        [Test]
        public void CreateUiFont_StartupInitializationOverridesEarlyFontsAndRemainsFixed()
        {
            var original = Thread.CurrentThread.CurrentUICulture;
            var familyField = typeof(UiTheme).GetField("_uiFontFamily", BindingFlags.Static | BindingFlags.NonPublic);
            var originalFamily = familyField.GetValue(null);
            try
            {
                familyField.SetValue(null, null);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                using (var early = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular))
                {
                    Assert.That(early.Name, Is.EqualTo("Segoe UI"));
                }
                UiTheme.InitializeUiFontFamily("zh-CN");
                using (var first = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular))
                {
                    Assert.That(first.Name, Is.EqualTo("Microsoft YaHei UI"));
                    UiTheme.InitializeUiFontFamily("en-US");
                    Thread.CurrentThread.CurrentUICulture = new CultureInfo("en-US");
                    using (var english = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular))
                    {
                        Thread.CurrentThread.CurrentUICulture = new CultureInfo("zh-CN");
                        using (var chinese = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular))
                        {
                            Assert.That(english.Name, Is.EqualTo(first.Name));
                            Assert.That(chinese.Name, Is.EqualTo(first.Name));
                        }
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = original;
                familyField.SetValue(null, originalFamily);
            }
        }

        [Test]
        public void FontFactories_UseCentralSizesThroughoutWinFormsSource()
        {
            var root = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (root != null && !File.Exists(Path.Combine(root.FullName, "MyCodex.sln")))
            {
                root = root.Parent;
            }
            Assert.That(root, Is.Not.Null);
            var sources = Directory.GetFiles(Path.Combine(root.FullName, "src", "MyCodex.WinForms"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part == "bin" || part == "obj"));
            foreach (var path in sources)
            {
                Assert.That(Regex.IsMatch(File.ReadAllText(path), @"Create(?:Ui|Code)Font\(\s*\d"), Is.False, path);
            }
            Assert.That(new[] { UiTheme.BodyFontSize, UiTheme.ReadingFontSize, UiTheme.SubtitleFontSize, UiTheme.TitleFontSize },
                Is.EqualTo(new[] { 9F, 10F, 10F, 11F }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ApplyTextBox_UsesReadingSizeWithoutReducingOriginalTenPointText(bool isCode)
        {
            Assert.That(UiTheme.ReadingFontSize, Is.GreaterThanOrEqualTo(10F));
            using (var textBox = new TextBox())
            {
                UiTheme.ApplyTextBox(textBox, isCode, false);
                Assert.That(textBox.Font.SizeInPoints, Is.EqualTo(UiTheme.ReadingFontSize));
            }
        }

        [TestCase("en-US")]
        [TestCase("zh-CN")]
        public void ReadingInputs_AndTheirButtonsFitInDialogRows(string language)
        {
            var originalCulture = Thread.CurrentThread.CurrentUICulture;
            var familyField = typeof(UiTheme).GetField("_uiFontFamily", BindingFlags.Static | BindingFlags.NonPublic);
            var originalFamily = familyField.GetValue(null);
            try
            {
                familyField.SetValue(null, null);
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(language);
                using (var worktree = new GitWorktreeCreateForm(null))
                using (var commit = new GitCommitMessageEditForm("Subject", "Body", false))
                using (var owner = AppServerFilePreviewForm.CreateForTests("sample.txt", "aGVsbG8="))
                using (var goToLine = new AppServerFilePreviewGoToLineDialog(owner))
                {
                    foreach (Form form in new Form[] { worktree, commit, goToLine })
                    {
                        form.ShowInTaskbar = false;
                        form.Show();
                        AssertInputRowsFit(form);
                    }
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = originalCulture;
                familyField.SetValue(null, originalFamily);
            }
        }

        internal static void AssertInputRowsFit(Control root)
        {
            foreach (Control control in root.Controls)
            {
                var textBox = control as TextBox;
                var comboBox = control as ComboBox;
                if ((textBox != null && !textBox.Multiline) || comboBox != null || control is Button || control is NumericUpDown)
                {
                    var preferredHeight = textBox != null ? textBox.PreferredHeight
                        : comboBox != null ? comboBox.PreferredHeight : control.PreferredSize.Height;
                    var description = control.FindForm().GetType().Name + ": " + control.AccessibleName;
                    Assert.That(control.Height, Is.GreaterThanOrEqualTo(preferredHeight), description);
                    var table = control.Parent as TableLayoutPanel;
                    if (table != null)
                    {
                        var row = table.GetRow(control);
                        Assert.That(table.GetRowHeights()[row],
                            Is.GreaterThanOrEqualTo(preferredHeight + control.Margin.Vertical), description);
                    }
                    for (Control child = control; child.Parent != null; child = child.Parent)
                    {
                        Assert.That(child.Top, Is.GreaterThanOrEqualTo(child.Parent.Padding.Top), description);
                        Assert.That(child.Bottom, Is.LessThanOrEqualTo(child.Parent.ClientSize.Height - child.Parent.Padding.Bottom), description);
                    }
                }
                if (!(control is TextBoxBase) && !(control is ComboBox) && !(control is UpDownBase))
                {
                    AssertInputRowsFit(control);
                }
            }
        }

        [TestCase("OK", 75)]
        [TestCase("OK", 120)]
        [TestCase("A long translated button caption", 75)]
        public void DialogButton_GrowsToFitTextAndPreservesExistingWidth(string text, int width)
        {
            using (var panel = new FlowLayoutPanel { Width = 700 })
            using (var button = new Button { Text = text, Width = width })
            {
                UiTheme.ApplyDialogButton(button, UiGlyph.Ok);
                panel.Controls.Add(button);
                panel.PerformLayout();
                Assert.That(button.AutoSize, Is.True);
                Assert.That(button.AutoSizeMode, Is.EqualTo(AutoSizeMode.GrowOnly));
                Assert.That(button.MinimumSize, Is.EqualTo(new Size(96, 32)));
                Assert.That(button.Width, text == "OK" ? Is.EqualTo(Math.Max(96, width)) : Is.GreaterThan(96));
                Assert.That(button.Width, Is.GreaterThanOrEqualTo(button.GetPreferredSize(Size.Empty).Width));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ContentDialogs_ResizeTheirContentAndKeepMinimumSize(bool branchIntegration)
        {
            using (Form form = branchIntegration
                ? (Form)new GitBranchIntegrateForm("main", "feature", null)
                : new ModelSettingsForm("Model", null, null, null, "Default", "Description"))
            {
                var field = form.GetType().GetField(branchIntegration ? "_comparisonTextBox" : "_descriptionTextBox",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var content = (TextBox)field.GetValue(form);
                Assert.That(form.FormBorderStyle, Is.EqualTo(FormBorderStyle.Sizable));
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
                Assert.That(form.MinimumSize.Height, Is.GreaterThan(0));
                form.ShowInTaskbar = false;
                form.Show();
                form.Size = form.MinimumSize;
                var before = content.Size;
                form.Size += new Size(200, 150);
                Assert.That(content.Width, Is.GreaterThan(before.Width));
                Assert.That(content.Height, Is.GreaterThan(before.Height));
            }
        }

        [TestCase("en-US")]
        [TestCase("zh-CN")]
        public void FindReplaceButtons_FitWithoutClipping(string language)
        {
            var original = Thread.CurrentThread.CurrentUICulture;
            try
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo(language);
                using (var owner = AppServerFilePreviewForm.CreateForTests("sample.txt", "aGVsbG8="))
                using (var dialog = new AppServerFilePreviewFindReplaceDialog(owner, true))
                {
                    dialog.ShowInTaskbar = false;
                    dialog.Show();
                    var root = (TableLayoutPanel)dialog.Controls[0];
                    var buttons = root.Controls.OfType<FlowLayoutPanel>().Single();
                    foreach (var button in buttons.Controls.OfType<Button>())
                    {
                        Assert.That(buttons.ClientRectangle.Contains(button.Bounds), Is.True, button.Text);
                        Assert.That(button.Width, Is.GreaterThanOrEqualTo(button.PreferredSize.Width), button.Text);
                    }
                    Assert.That(dialog.ClientRectangle.Contains(root.Bounds), Is.True);
                }
            }
            finally
            {
                Thread.CurrentThread.CurrentUICulture = original;
            }
        }

        [Test]
        public void FileNameDialog_ButtonContainerGrowsWithLongCaption()
        {
            var type = typeof(AppServerFileBrowserForm).GetNestedType("NameDialog", BindingFlags.NonPublic);
            using (var dialog = (Form)Activator.CreateInstance(type,
                "Name", "Name", "Create a new named folder", "Create folder"))
            {
                dialog.ShowInTaskbar = false;
                dialog.Show();
                var root = (TableLayoutPanel)dialog.Controls[0];
                var footer = root.Controls.OfType<FlowLayoutPanel>().Single();
                Assert.That(footer.AutoSize, Is.True);
                Assert.That(footer.Width, Is.GreaterThan(244));
                foreach (var button in footer.Controls.OfType<Button>())
                {
                    Assert.That(footer.ClientRectangle.Contains(button.Bounds), Is.True, button.Text);
                    Assert.That(button.Width, Is.GreaterThanOrEqualTo(button.PreferredSize.Width));
                }
                Assert.That(root.ClientRectangle.Contains(footer.Bounds), Is.True);
            }
        }
    }
}
