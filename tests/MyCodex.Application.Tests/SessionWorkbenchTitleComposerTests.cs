using MyCodex.Domain.Entities;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionWorkbenchTitleComposerTests
    {
        [Test]
        public void Build_CombinesTrimmedWorkspaceNameAndFormattedSessionTitle()
        {
            var workspace = new WorkspaceInfo { Name = "  Product API  " };
            var session = new ChatSession { Title = "Implement detached workbench" };

            Assert.That(
                SessionWorkbenchTitleComposer.Build(workspace, session),
                Is.EqualTo("Product API - " + SessionDisplayFormatter.BuildDisplayTitle(session)));
        }

        [Test]
        public void Build_UsesLocalizedFallbacksForMissingWorkspaceAndSession()
        {
            var title = SessionWorkbenchTitleComposer.Build(new WorkspaceInfo(), null);

            Assert.That(title, Does.Contain(" - "));
            Assert.That(title, Does.EndWith(SessionDisplayFormatter.BuildDisplayTitle(null)));
        }

        [Test]
        public void Build_WorkingTitleIncludesKnownContextRemaining()
        {
            var workspace = new WorkspaceInfo { Name = "Product API" };
            var session = new ChatSession
            {
                Title = "Implement detached workbench",
                ThreadTokenUsageModelContextWindow = 100000,
                ThreadTokenUsageLastTotalTokens = 20000
            };

            Assert.That(
                SessionWorkbenchTitleComposer.Build(workspace, session, true),
                Is.EqualTo(SessionDisplayFormatter.BuildWorkingStatusText(session)
                    + " - Product API - "
                    + SessionDisplayFormatter.BuildDisplayTitle(session)));
        }
    }
}
