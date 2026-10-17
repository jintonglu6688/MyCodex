using System.Collections.Generic;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationRunCompletionBuilderTests
    {
        [Test]
        public void Build_CopiesChangedFilesAndLeavesGeneratedArtifactsEmpty()
        {
            var state = new TurnProgressSummaryState();
            state.TurnId = "turn-1";
            state.ChangedFiles.Add("src/MainForm.cs");
            state.ChangedFiles.Add("tests/AutomationTests.cs");

            var completion = AutomationRunCompletionBuilder.Build(
                AutomationRunStatus.Completed,
                "Done",
                null,
                state);

            Assert.That(completion.Status, Is.EqualTo(AutomationRunStatus.Completed));
            Assert.That(completion.TurnId, Is.EqualTo("turn-1"));
            Assert.That(completion.Summary, Is.EqualTo("Done"));
            CollectionAssert.AreEqual(new[] { "src/MainForm.cs", "tests/AutomationTests.cs" }, completion.ChangedFiles);
            Assert.That(completion.GeneratedArtifacts, Is.Not.Null);
            Assert.That(completion.GeneratedArtifacts, Is.Empty);

            state.ChangedFiles.Add("later.cs");
            CollectionAssert.AreEqual(new[] { "src/MainForm.cs", "tests/AutomationTests.cs" }, completion.ChangedFiles);
        }

        [Test]
        public void Build_FailedCompletionPreservesError()
        {
            var completion = AutomationRunCompletionBuilder.Build(
                AutomationRunStatus.Failed,
                "Failed",
                "tool crashed",
                new TurnProgressSummaryState());

            Assert.That(completion.Status, Is.EqualTo(AutomationRunStatus.Failed));
            Assert.That(completion.Summary, Is.EqualTo("Failed"));
            Assert.That(completion.ErrorMessage, Is.EqualTo("tool crashed"));
        }
    }
}
