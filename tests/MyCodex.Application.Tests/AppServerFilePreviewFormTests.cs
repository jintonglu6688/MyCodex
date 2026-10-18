using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AppServerFilePreviewFormTests
    {
        [Test]
        public void FromBase64Text_DecodesUtf8Text()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                Assert.That(form.ContentTextForTests, Is.EqualTo("hello"));
                Assert.That(form.ContentAccessibleNameForTests, Is.EqualTo(UiLocalization.Text("AppServerFilePreviewForm.Accessible.Content", "Content")));
            }
        }

        [Test]
        public void FromBinaryContent_ShowsUnavailableMessage()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\data.bin",
                Convert.ToBase64String(new byte[] { 0, 1, 2 })))
            {
                Assert.That(form.ContentTextForTests, Is.EqualTo(UiLocalization.Text("AppServerFilePreviewForm.Message.PreviewUnavailable", "Preview is unavailable for this file.")));
            }
        }

        [Test]
        public void Constructor_UsesReusableScintillaEditorControl()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                Assert.That(form.EditorControlTypeNameForTests, Is.EqualTo("InternalTextEditorControl"));
                Assert.That(form.InnerEditorControlTypeNameForTests, Is.EqualTo("Scintilla"));
            }
        }

        [Test]
        public void Constructor_PreservesContentAccessibleNameOnScintillaEditor()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                Assert.That(form.ContentAccessibleNameForTests, Is.EqualTo(UiLocalization.Text("AppServerFilePreviewForm.Accessible.Content", "Content")));
                Assert.That(form.InnerEditorAccessibleNameForTests, Is.EqualTo(UiLocalization.Text("AppServerFilePreviewForm.Accessible.Content", "Content")));
            }
        }

        [Test]
        public void Constructor_ContentEditorParticipatesInTabOrder()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                Assert.That(form.EditorTabStopForTests, Is.True);
                Assert.That(form.InnerEditorTabStopForTests, Is.True);
                Assert.That(form.InnerEditorTabIndexForTests, Is.EqualTo(0));
            }
        }

        [Test]
        public void Constructor_CreatesIndependentAltTabWindow()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                Assert.That(form.ShowInTaskbar, Is.True);
                Assert.That(form.StartPosition, Is.EqualTo(FormStartPosition.CenterScreen));
            }
        }

        [Test]
        public void Shown_FocusesContentEditor()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                Assert.That(form.FocusedAccessibleNameForTests, Is.EqualTo(UiLocalization.Text("AppServerFilePreviewForm.Accessible.Content", "Content")));
            }
        }

        [TestCase(@"E:\Repo\docs\AGENTS.md", "AGENTS.md")]
        [TestCase("/home/demo/project/README.md", "README.md")]
        public void Title_UsesFileNameInsteadOfFullPath(string path, string fileName)
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                path,
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                Assert.That(form.Text, Does.StartWith(fileName + " - "));
                Assert.That(form.Text, Does.Not.StartWith("Preview - "));
                Assert.That(form.Text, Does.Not.Contain(path));
            }
        }

        [Test]
        public async Task SaveText_WritesUtf8BytesThroughCallback()
        {
            byte[] savedBytes = null;
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello")),
                data =>
                {
                    savedBytes = data;
                    return Task.CompletedTask;
                }))
            {
                form.SetContentTextForTests("updated");
                await form.SaveForTestsAsync();

                Assert.That(Encoding.UTF8.GetString(savedBytes), Is.EqualTo("updated"));
                Assert.That(form.ContentReadOnlyForTests, Is.False);
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_WhenClean_ReloadsContent()
        {
            var loader = new QueueFileLoader("first", "second");
            using (var form = AppServerFilePreviewForm.CreateLiveForTests(
                @"E:\Repo\notes.md",
                loader.ReadAsync,
                data => Task.CompletedTask))
            {
                await form.LoadForTestsAsync();
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());

                Assert.That(form.ContentTextForTests, Is.EqualTo("second"));
                Assert.That(form.IsDirtyForTests, Is.False);
            }
        }

        [Test]
        public async Task HandleFileSystemChanged_WhenDirty_DoesNotOverwriteContent()
        {
            var loader = new QueueFileLoader("first", "second");
            using (var form = AppServerFilePreviewForm.CreateLiveForTests(
                @"E:\Repo\notes.md",
                loader.ReadAsync,
                data => Task.CompletedTask))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("my edit");
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());

                Assert.That(form.ContentTextForTests, Is.EqualTo("my edit"));
                Assert.That(form.HasExternalChangeForTests, Is.True);
                Assert.That(form.ReloadEnabledForTests, Is.True);
            }
        }

        [Test]
        public async Task TextEditingEnhancements_DoNotChangeSaveDirtyAndExternalChangeFlow()
        {
            var loader = new QueueFileLoader("first", "second");
            byte[] savedBytes = null;
            using (var form = AppServerFilePreviewForm.CreateLiveForTests(
                @"E:\Repo\notes.md",
                loader.ReadAsync,
                data =>
                {
                    savedBytes = data;
                    return Task.CompletedTask;
                }))
            {
                await form.LoadForTestsAsync();

                form.SetContentTextForTests("my edit");
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                await form.SaveForTestsAsync();

                Assert.That(form.ContentTextForTests, Is.EqualTo("my edit"));
                Assert.That(savedBytes, Is.Null);
                Assert.That(form.HasExternalChangeForTests, Is.True);
                Assert.That(form.ReloadEnabledForTests, Is.True);
            }
        }

        [Test]
        public async Task ReloadAfterExternalChange_ReplacesDirtyContentWhenConfirmed()
        {
            var loader = new QueueFileLoader("first", "second");
            using (var form = AppServerFilePreviewForm.CreateLiveForTests(
                @"E:\Repo\notes.md",
                loader.ReadAsync,
                data => Task.CompletedTask,
                () => true))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("my edit");
                await form.HandleFileSystemChangedAsync(new AppServerFileSystemChangedEvent());
                await form.ReloadForTestsAsync();

                Assert.That(form.ContentTextForTests, Is.EqualTo("second"));
                Assert.That(form.IsDirtyForTests, Is.False);
                Assert.That(form.HasExternalChangeForTests, Is.False);
            }
        }

        [Test]
        public async Task ConfirmCloseWithDirtyContent_WhenSaveSelected_SavesAndAllowsClose()
        {
            byte[] savedBytes = null;
            using (var form = AppServerFilePreviewForm.CreateLiveForTests(
                @"E:\Repo\notes.md",
                () => Task.FromResult("first"),
                data =>
                {
                    savedBytes = data;
                    return Task.CompletedTask;
                },
                null,
                () => AppServerFilePreviewCloseChoice.Save))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("my edit");

                var canClose = await form.ConfirmCloseForTestsAsync();

                Assert.That(canClose, Is.True);
                Assert.That(Encoding.UTF8.GetString(savedBytes), Is.EqualTo("my edit"));
                Assert.That(form.IsDirtyForTests, Is.False);
            }
        }

        [Test]
        public async Task ConfirmCloseWithDirtyContent_WhenDiscardSelected_AllowsCloseWithoutSaving()
        {
            var saveCount = 0;
            using (var form = AppServerFilePreviewForm.CreateLiveForTests(
                @"E:\Repo\notes.md",
                () => Task.FromResult("first"),
                data =>
                {
                    saveCount++;
                    return Task.CompletedTask;
                },
                null,
                () => AppServerFilePreviewCloseChoice.Discard))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("my edit");

                var canClose = await form.ConfirmCloseForTestsAsync();

                Assert.That(canClose, Is.True);
                Assert.That(saveCount, Is.EqualTo(0));
                Assert.That(form.IsDirtyForTests, Is.False);
            }
        }

        [Test]
        public async Task ConfirmCloseWithDirtyContent_WhenCancelSelected_KeepsWindowDirty()
        {
            var saveCount = 0;
            using (var form = AppServerFilePreviewForm.CreateLiveForTests(
                @"E:\Repo\notes.md",
                () => Task.FromResult("first"),
                data =>
                {
                    saveCount++;
                    return Task.CompletedTask;
                },
                null,
                () => AppServerFilePreviewCloseChoice.Cancel))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("my edit");

                var canClose = await form.ConfirmCloseForTestsAsync();

                Assert.That(canClose, Is.False);
                Assert.That(saveCount, Is.EqualTo(0));
                Assert.That(form.IsDirtyForTests, Is.True);
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void EscapeClose_IsHandledByPreviewFormWithoutCancelButton()
        {
            var source = RepositorySource.ReadSourceFile("AppServerFilePreviewForm.cs");

            Assert.That(source, Does.Contain("keyData == Keys.Escape"));
            Assert.That(source, Does.Not.Contain("CancelButton = _closeButton;"));
        }

        [TestCase(Keys.Control | Keys.F, false)]
        [TestCase(Keys.Control | Keys.H, true)]
        public void ProcessCmdKey_OpensFindReplaceDialog(Keys keyData, bool replaceMode)
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                form.SuppressFindReplaceDialogForTests = true;

                var handled = FormTestDriver.ProcessCmdKey(form, keyData);

                Assert.That(handled, Is.True);
                Assert.That(form.FindReplaceDialogOpenCountForTests, Is.EqualTo(1));
                Assert.That(form.LastFindReplaceDialogReplaceModeForTests, Is.EqualTo(replaceMode));
            }
        }

        [Test]
        public void ProcessCmdKey_CtrlGOpensGoToLineDialog()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("one" + Environment.NewLine + "two"))))
            {
                form.SuppressGoToLineDialogForTests = true;

                var handled = FormTestDriver.ProcessCmdKey(form, Keys.Control | Keys.G);

                Assert.That(handled, Is.True);
                Assert.That(form.GoToLineDialogOpenCountForTests, Is.EqualTo(1));
            }
        }

        [TestCase(Keys.F3)]
        [TestCase(Keys.Shift | Keys.F3)]
        public void ProcessCmdKey_F3WithoutFindTextOpensFindDialog(Keys keyData)
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\AGENTS.md",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            {
                form.SuppressFindReplaceDialogForTests = true;

                var handled = FormTestDriver.ProcessCmdKey(form, keyData);

                Assert.That(handled, Is.True);
                Assert.That(form.FindReplaceDialogOpenCountForTests, Is.EqualTo(1));
                Assert.That(form.LastFindReplaceDialogReplaceModeForTests, Is.False);
            }
        }

        [Test]
        public void FindNextAndShiftF3_NavigateMatchesInTextBox()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\sample.cs",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("public one" + Environment.NewLine + "public two"))))
            {
                form.FindTextForTests = "public";

                Assert.That(FormTestDriver.ProcessCmdKey(form, Keys.F3), Is.True);
                var firstStart = form.SelectionStartForTests;
                Assert.That(form.SelectedTextForTests, Is.EqualTo("public"));

                Assert.That(FormTestDriver.ProcessCmdKey(form, Keys.F3), Is.True);
                var secondStart = form.SelectionStartForTests;
                Assert.That(secondStart, Is.GreaterThan(firstStart));
                Assert.That(form.SelectedTextForTests, Is.EqualTo("public"));

                Assert.That(FormTestDriver.ProcessCmdKey(form, Keys.Shift | Keys.F3), Is.True);
                Assert.That(form.SelectionStartForTests, Is.EqualTo(firstStart));
                Assert.That(form.SelectedTextForTests, Is.EqualTo("public"));
            }
        }

        [Test]
        public void ReplaceCurrent_ReplacesSelectedMatchAndMarksDirty()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\sample.cs",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("public class Sample")),
                data => Task.CompletedTask))
            {
                form.FindTextForTests = "Sample";
                form.ReplaceTextForTests = "Demo";
                form.FindNextForTests();

                form.ReplaceCurrentForTests();

                Assert.That(form.ContentTextForTests, Is.EqualTo("public class Demo"));
                Assert.That(form.IsDirtyForTests, Is.True);
            }
        }

        [Test]
        public void ReplaceAll_ReplacesEveryMatchAndMarksDirty()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\sample.cs",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("public a" + Environment.NewLine + "public b")),
                data => Task.CompletedTask))
            {
                form.FindTextForTests = "public";
                form.ReplaceTextForTests = "internal";

                form.ReplaceAllForTests();

                Assert.That(form.ContentTextForTests, Is.EqualTo("internal a" + Environment.NewLine + "internal b"));
                Assert.That(form.IsDirtyForTests, Is.True);
            }
        }

        [Test]
        public void ReplaceAll_WhenPreviewIsNotEditable_DoesNotModifyContent()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\data.bin",
                Convert.ToBase64String(new byte[] { 0, 1, 2 })))
            {
                var originalText = form.ContentTextForTests;
                form.FindTextForTests = originalText;
                form.ReplaceTextForTests = "edited";

                form.ReplaceAllForTests();

                Assert.That(form.ContentTextForTests, Is.EqualTo(originalText));
                Assert.That(form.IsDirtyForTests, Is.False);
            }
        }

        [Test]
        public void GoToLine_MovesCaretToRequestedLine()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\sample.txt",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("one" + Environment.NewLine + "two" + Environment.NewLine + "three"))))
            {
                form.GoToLineNumberForTests = 2;

                form.GoToLineForTests();

                Assert.That(form.CurrentLineForTests, Is.EqualTo(2));
                Assert.That(form.SelectedTextForTests, Is.Empty);
            }
        }

        [Test]
        public void FindReplaceDialog_ReplaceModeKeepsTextFieldsTogetherAndCloseLastInTabOrder()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\sample.txt",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            using (var dialog = new AppServerFilePreviewFindReplaceDialog(form, true))
            {
                Assert.That(dialog.FocusOrderAccessibleNamesForTests, Is.EqualTo(new[]
                {
                    UiLocalization.Text("AppServerFilePreviewForm.Accessible.Find", "Find"),
                    UiLocalization.Text("AppServerFilePreviewForm.Accessible.Replace", "Replace with"),
                    UiLocalization.Text("AppServerFilePreviewForm.Accessible.FindPrevious", "Find previous"),
                    UiLocalization.Text("AppServerFilePreviewForm.Accessible.FindNext", "Find next"),
                    UiLocalization.Text("AppServerFilePreviewForm.Accessible.ReplaceCurrent", "Replace current"),
                    UiLocalization.Text("AppServerFilePreviewForm.Accessible.ReplaceAll", "Replace all"),
                    UiLocalization.Text("AppServerFilePreviewForm.Accessible.Close", "Close")
                }));
            }
        }

        [Test]
        public void GoToLineDialog_ExposesAccessibleLineNumberControl()
        {
            using (var form = AppServerFilePreviewForm.CreateForTests(
                @"E:\Repo\sample.txt",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("hello"))))
            using (var dialog = new AppServerFilePreviewGoToLineDialog(form))
            {
                Assert.That(dialog.LineNumberAccessibleNameForTests, Is.EqualTo(UiLocalization.Text("AppServerFilePreviewForm.Accessible.LineNumber", "Line number")));
                Assert.That(dialog.InitialFocusAccessibleNameForTests, Is.EqualTo(UiLocalization.Text("AppServerFilePreviewForm.Accessible.LineNumber", "Line number")));
            }
        }

        private sealed class QueueFileLoader
        {
            private readonly Queue<string> _items;

            public QueueFileLoader(params string[] items)
            {
                _items = new Queue<string>(items);
            }

            public Task<string> ReadAsync()
            {
                return Task.FromResult(_items.Dequeue());
            }
        }
    }
}
