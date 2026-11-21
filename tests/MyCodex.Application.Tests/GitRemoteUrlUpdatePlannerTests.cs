using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitRemoteUrlUpdatePlannerTests
    {
        private static readonly GitRemoteEntry RedactedRemote = new GitRemoteEntry
        {
            Name = "origin",
            FetchUrl = "https://***@example.com/repo.git",
            PushUrl = "https://***@example.com/push.git"
        };

        [Test]
        public void UntouchedRedactedUrls_AreNotWrittenBack()
        {
            var plan = GitRemoteUrlUpdatePlanner.Plan(RedactedRemote, RedactedRemote.FetchUrl, RedactedRemote.PushUrl);

            Assert.That(plan.HasChanges, Is.False);
        }

        [Test]
        public void ChangingOnlyThePushUrl_LeavesTheFetchUrlAlone()
        {
            var plan = GitRemoteUrlUpdatePlanner.Plan(RedactedRemote, RedactedRemote.FetchUrl, "https://example.com/new-push.git");

            Assert.That(plan.FetchUrl, Is.Null);
            Assert.That(plan.UpdatePushUrl, Is.True);
            Assert.That(plan.PushUrl, Is.EqualTo("https://example.com/new-push.git"));
        }

        [Test]
        public void ClearingTheSeparatePushUrl_RemovesIt()
        {
            var plan = GitRemoteUrlUpdatePlanner.Plan(RedactedRemote, RedactedRemote.FetchUrl, "  ");

            Assert.That(plan.UpdatePushUrl, Is.True);
            Assert.That(plan.PushUrl, Is.Null);
        }

        [Test]
        public void RemoteWithoutSeparatePushUrl_ChangesOnlyTheFetchUrl()
        {
            var remote = new GitRemoteEntry { Name = "origin", FetchUrl = "https://example.com/a.git", PushUrl = "https://example.com/a.git" };

            var plan = GitRemoteUrlUpdatePlanner.Plan(remote, "https://example.com/b.git", null);

            Assert.That(plan.FetchUrl, Is.EqualTo("https://example.com/b.git"));
            Assert.That(plan.UpdatePushUrl, Is.False);
        }

        [Test]
        public void RedactionMarker_IsDetected()
        {
            Assert.That(GitRemoteUrlUpdatePlanner.ContainsRedactedCredential("https://***@example.com/x.git"), Is.True);
            Assert.That(GitRemoteUrlUpdatePlanner.ContainsRedactedCredential("https://example.com/x.git"), Is.False);
            Assert.That(GitRemoteUrlUpdatePlanner.ContainsRedactedCredential(null), Is.False);
        }
    }
}
