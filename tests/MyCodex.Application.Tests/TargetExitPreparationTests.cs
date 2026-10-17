using System.Threading.Tasks;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TargetExitPreparationTests
    {
        [Test]
        public async Task FailedSaveResumesEntriesWithoutStoppingWork()
        {
            var frozen = false;
            var commits = 0;
            var state = new TargetExitPreparation(id => Task.FromResult(false), value => frozen = value, () => { commits++; return true; });
            Assert.That(await state.PrepareAsync("a"), Is.False);
            Assert.That(frozen, Is.False);
            Assert.That(state.Commit("a"), Is.False);
            Assert.That(commits, Is.Zero);
        }

        [Test]
        public async Task CancellingPendingSaveCannotPrepareOrCommitLater()
        {
            var pending = new TaskCompletionSource<bool>();
            var frozen = false;
            var commits = 0;
            var state = new TargetExitPreparation(id => pending.Task, value => frozen = value, () => { commits++; return true; });
            var task = state.PrepareAsync("a");
            state.Cancel("a");
            pending.SetResult(true);
            Assert.That(await task, Is.False);
            Assert.That(state.IsReady, Is.False);
            Assert.That(state.Commit("a"), Is.False);
            Assert.That(frozen, Is.False);
            Assert.That(commits, Is.Zero);
        }

        [Test]
        public async Task CommitRequiresSamePreparedOperationAndNeverRunsDuringPreparation()
        {
            var commits = 0;
            var state = new TargetExitPreparation(id => Task.FromResult(true), value => { }, () => { commits++; return true; });
            Assert.That(state.Commit("a"), Is.False);
            Assert.That(await state.PrepareAsync("a"), Is.True);
            Assert.That(await state.PrepareAsync("b"), Is.False);
            Assert.That(state.Commit("b"), Is.False);
            Assert.That(commits, Is.Zero);
            Assert.That(state.Commit("a"), Is.True);
            Assert.That(commits, Is.EqualTo(1));
        }
    }
}
