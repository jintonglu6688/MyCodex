using System.IO;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormCommandStatusSourceTests
    {
        [Test]
        public void CommandCompletedStatus_UsesTheActivityCommandCompactionRules()
        {
            var sourcePath = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms",
                "Forms",
                "MainForm.AppServerEvents.cs"));
            var source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Contain("route == AppServerEventRoute.CommandCompleted"));
            Assert.That(source, Does.Contain(
                "ActivityCommandTextFormatter.CompactCompletionText(e.Content, e.CommandText, e.WorkingDirectory)"));
            Assert.That(source, Does.Contain("SetAppServerStatus(e, false, statusText);"));
        }
    }
}
