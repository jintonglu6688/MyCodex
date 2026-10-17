using System.Threading;
using System.Threading.Tasks;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AppServerFilePreviewTargetExitTests
    {
        [Test]
        public async Task CancelledSlowSaveCannotLeaveEditorPreparedForExit()
        {
            var saved = new TaskCompletionSource<object>();
            using (var form = AppServerFilePreviewForm.CreateLiveForTests("test.txt", () => Task.FromResult("old"), bytes => saved.Task,
                confirmCloseChanges: () => AppServerFilePreviewCloseChoice.Save))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("draft to save");
                var preparation = form.PrepareForTargetExitAsync();
                Assert.That(form.ContentReadOnlyForTests, Is.True);
                form.CancelTargetExit();
                saved.SetResult(null);
                Assert.That(await preparation, Is.False);
                Assert.That(form.CanCommitTargetExit, Is.False);
                Assert.That(form.ContentTextForTests, Is.EqualTo("draft to save"));
                Assert.That(form.ContentReadOnlyForTests, Is.False);
            }
        }

        [Test]
        public async Task DiscardConsentDoesNotEraseDirtyDraftWhenAnotherTargetCancels()
        {
            using (var form = AppServerFilePreviewForm.CreateLiveForTests("test.txt", () => Task.FromResult("old"), bytes => Task.CompletedTask,
                confirmCloseChanges: () => AppServerFilePreviewCloseChoice.Discard))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("unsaved draft");
                Assert.That(await form.PrepareForTargetExitAsync(), Is.True);
                Assert.That(form.HasUnsavedTargetExitContent, Is.True);
                Assert.That(form.ContentReadOnlyForTests, Is.True);
                form.CancelTargetExit();
                Assert.That(form.ContentTextForTests, Is.EqualTo("unsaved draft"));
                Assert.That(form.HasUnsavedTargetExitContent, Is.True);
                Assert.That(form.ContentReadOnlyForTests, Is.False);
            }
        }

        [Test]
        public async Task ExternallyChangedFileCannotPrepareSavingAndDoesNotClose()
        {
            var saves = 0;
            using (var form = AppServerFilePreviewForm.CreateLiveForTests("test.txt", () => Task.FromResult("old"), bytes => { saves++; return Task.CompletedTask; },
                confirmCloseChanges: () => AppServerFilePreviewCloseChoice.Save))
            {
                await form.LoadForTestsAsync();
                form.SetContentTextForTests("unsaved draft");
                await form.HandleFileSystemChangedAsync(null);
                Assert.That(await form.PrepareForTargetExitAsync(), Is.False);
                Assert.That(form.IsDisposed, Is.False);
                Assert.That(form.ContentTextForTests, Is.EqualTo("unsaved draft"));
                Assert.That(form.HasUnsavedTargetExitContent, Is.True);
                Assert.That(saves, Is.Zero);
            }
        }
    }
}
