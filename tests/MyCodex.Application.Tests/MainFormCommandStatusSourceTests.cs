using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class MainFormCommandStatusSourceTests
    {
        [Test]
        public void CommandCompletedStatus_UsesTheActivityCommandCompactionRules()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.AppServerEvents.cs");

            Assert.That(source, Does.Contain("route == AppServerEventRoute.CommandCompleted"));
            Assert.That(source, Does.Contain(
                "ActivityCommandTextFormatter.CompactCompletionText(e.Content, e.CommandText, e.WorkingDirectory)"));
            Assert.That(source, Does.Contain("SetAppServerStatus(e, false, statusText);"));
        }
    }
}
