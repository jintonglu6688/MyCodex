using System;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationResultClassifierTests
    {
        [Test]
        public void Classify_FailedRunNeedsAttention()
        {
            var classifier = new AutomationResultClassifier();

            var run = classifier.ApplyCompletion(
                BuildDefinition(false),
                BuildRun(),
                new AutomationRunCompletion
                {
                    Status = AutomationRunStatus.Failed,
                    CompletedAt = new DateTime(2026, 6, 5, 8, 10, 0),
                    Summary = "The request failed.",
                    ErrorMessage = "network failed"
                });

            Assert.That(run.Status, Is.EqualTo(AutomationRunStatus.Failed));
            Assert.That(run.NeedsAttention, Is.True);
            Assert.That(run.Unread, Is.True);
            Assert.That(run.Archived, Is.False);
            Assert.That(run.ErrorMessage, Is.EqualTo("network failed"));
        }

        [Test]
        public void Classify_CompletedRunDefaultsToNeedsAttention()
        {
            var classifier = new AutomationResultClassifier();

            var run = classifier.ApplyCompletion(
                BuildDefinition(false),
                BuildRun(),
                new AutomationRunCompletion
                {
                    Status = AutomationRunStatus.Completed,
                    CompletedAt = new DateTime(2026, 6, 5, 8, 10, 0),
                    Summary = "Found three news items."
                });

            Assert.That(run.Status, Is.EqualTo(AutomationRunStatus.Completed));
            Assert.That(run.HasFindings, Is.True);
            Assert.That(run.NeedsAttention, Is.True);
            Assert.That(run.Unread, Is.True);
            Assert.That(run.Archived, Is.False);
        }

        [Test]
        public void Classify_CompletedNoFindingsAutoArchive()
        {
            var classifier = new AutomationResultClassifier();

            var run = classifier.ApplyCompletion(
                BuildDefinition(true),
                BuildRun(),
                new AutomationRunCompletion
                {
                    Status = AutomationRunStatus.Completed,
                    CompletedAt = new DateTime(2026, 6, 5, 8, 10, 0),
                    Summary = "No important content found."
                });

            Assert.That(run.Status, Is.EqualTo(AutomationRunStatus.Completed));
            Assert.That(run.HasFindings, Is.False);
            Assert.That(run.NeedsAttention, Is.False);
            Assert.That(run.Unread, Is.False);
            Assert.That(run.Archived, Is.True);
        }

        [Test]
        public void Classify_WaitingForApprovalNeedsAttention()
        {
            var classifier = new AutomationResultClassifier();

            var run = classifier.ApplyCompletion(
                BuildDefinition(true),
                BuildRun(),
                new AutomationRunCompletion
                {
                    Status = AutomationRunStatus.WaitingForApproval,
                    CompletedAt = new DateTime(2026, 6, 5, 8, 10, 0),
                    Summary = "Approval required."
                });

            Assert.That(run.NeedsAttention, Is.True);
            Assert.That(run.Unread, Is.True);
            Assert.That(run.Archived, Is.False);
        }

        private static AutomationDefinitionSnapshot BuildDefinition(bool autoArchiveNoFindings)
        {
            return new AutomationDefinitionSnapshot
            {
                Id = "automation-1",
                AllowNoFindingsAutoArchive = autoArchiveNoFindings
            };
        }

        private static AutomationRunSnapshot BuildRun()
        {
            return new AutomationRunSnapshot
            {
                Id = "run-1",
                AutomationId = "automation-1",
                Status = AutomationRunStatus.Running,
                StartedAt = new DateTime(2026, 6, 5, 8, 0, 0)
            };
        }
    }
}
