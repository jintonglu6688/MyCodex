using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;
using System.Threading.Tasks;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class InvalidEncryptedContentFailureStateTests
    {
        [Test]
        public void Evaluate_SuppressesAsyncErrorAndStartsRepairOnMatchingTurnFailed()
        {
            var state = new InvalidEncryptedContentFailureState(8);
            var error = CreateInvalidEncryptedContentEvent("error");
            var failed = CreateInvalidEncryptedContentEvent("turn_failed");

            var errorDecision = state.Evaluate(AppServerEventRoute.Error, error);
            var failedDecision = state.Evaluate(AppServerEventRoute.TurnFailed, failed);
            var duplicateFailedDecision = state.Evaluate(AppServerEventRoute.TurnFailed, failed);

            Assert.That(errorDecision.ShouldSuppressDisplay, Is.True);
            Assert.That(errorDecision.ShouldStartRepair, Is.False);
            Assert.That(errorDecision.ShouldCompleteRepairAndRetry, Is.False);
            Assert.That(failedDecision.ShouldSuppressDisplay, Is.True);
            Assert.That(failedDecision.ShouldStartRepair, Is.True);
            Assert.That(failedDecision.ShouldCompleteRepairAndRetry, Is.True);
            Assert.That(duplicateFailedDecision.ShouldSuppressDisplay, Is.True);
            Assert.That(duplicateFailedDecision.ShouldStartRepair, Is.False);
            Assert.That(duplicateFailedDecision.ShouldCompleteRepairAndRetry, Is.False);
        }

        [Test]
        public void Evaluate_IgnoresUnrelatedErrors()
        {
            var state = new InvalidEncryptedContentFailureState(8);
            var error = new AppServerEventArgs("error", "network timeout", "thread-1", "turn-1");

            var decision = state.Evaluate(AppServerEventRoute.Error, error);

            Assert.That(decision.ShouldSuppressDisplay, Is.False);
            Assert.That(decision.ShouldStartRepair, Is.False);
            Assert.That(decision.ShouldCompleteRepairAndRetry, Is.False);
        }

        [Test]
        public async Task RepairWorkflow_RestartsAppServerAndRetriesWhenFirstRepairDoesNotRepair()
        {
            var resetCount = 0;
            var restartCount = 0;
            var repairCount = 0;

            var result = await InvalidEncryptedContentRepairWorkflow.RepairWithAppServerRestartFallbackAsync(
                () =>
                {
                    resetCount++;
                    return Task.FromResult(0);
                },
                () =>
                {
                    repairCount++;
                    return Task.FromResult(repairCount == 1
                        ? ThreadHistoryRepairResult.NotRepaired("file locked")
                        : ThreadHistoryRepairResult.CreateRepaired(107));
                },
                () =>
                {
                    restartCount++;
                    return Task.FromResult(0);
                });

            Assert.That(result.Repaired, Is.True);
            Assert.That(result.RepairCount, Is.EqualTo(107));
            Assert.That(resetCount, Is.EqualTo(1));
            Assert.That(repairCount, Is.EqualTo(2));
            Assert.That(restartCount, Is.EqualTo(1));
        }

        [Test]
        public async Task RepairWorkflow_DoesNotRestartWhenFirstRepairSucceeds()
        {
            var restartCount = 0;

            var result = await InvalidEncryptedContentRepairWorkflow.RepairWithAppServerRestartFallbackAsync(
                () => Task.FromResult(0),
                () => Task.FromResult(ThreadHistoryRepairResult.CreateRepaired(1)),
                () =>
                {
                    restartCount++;
                    return Task.FromResult(0);
                });

            Assert.That(result.Repaired, Is.True);
            Assert.That(restartCount, Is.EqualTo(0));
        }

        private static AppServerEventArgs CreateInvalidEncryptedContentEvent(string eventType)
        {
            return new AppServerEventArgs(
                eventType,
                "{\"error\":{\"code\":\"invalid_encrypted_content\",\"message\":\"The encrypted content gAAA...L6vY could not be verified.\"}}",
                "thread-1",
                "turn-1");
        }
    }
}
