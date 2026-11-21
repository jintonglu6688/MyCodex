using System.Collections.Generic;
using MyCodex.Application.DTOs;
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
        public void Build_UsesTheTurnsFinalReplyInsteadOfTheGenericCompletionText()
        {
            var state = new TurnProgressSummaryState();
            TurnProgressSummaryReducer.ApplyEvent(state, new AppServerEventArgs("turn_started", "Started", "thread-1", "turn-1"));
            TurnProgressSummaryReducer.ApplyEvent(state, new AppServerEventArgs("assistant", "Checking...", "thread-1", "turn-1"));
            TurnProgressSummaryReducer.ApplyEvent(state, new AppServerEventArgs("assistant", "No findings.", "thread-1", "turn-1"));

            var completion = AutomationRunCompletionBuilder.Build(
                AutomationRunStatus.Completed,
                "Current turn request completed.",
                null,
                state);

            Assert.That(completion.Summary, Is.EqualTo("No findings."));

            TurnProgressSummaryReducer.ApplyEvent(state, new AppServerEventArgs("turn_started", "Started", "thread-1", "turn-2"));
            Assert.That(AutomationRunCompletionBuilder.Build(AutomationRunStatus.Completed, "Current turn request completed.", null, state).Summary,
                Is.EqualTo("Current turn request completed."), "A new turn must not reuse the previous turn's reply.");
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
