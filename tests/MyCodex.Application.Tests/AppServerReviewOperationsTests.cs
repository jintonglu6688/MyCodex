using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerReviewOperationsTests
    {
        [Test, Combinatorial]
        public async Task FourTargets_BothSessionModesUseOfficialInlineReview(
            [Values(0, 1, 2, 3)] int kind, [Values(false, true)] bool detached)
        {
            var targets = new[]
            {
                AppServerReviewTarget.CreateUncommittedChanges(),
                AppServerReviewTarget.CreateBaseBranch(" main "),
                AppServerReviewTarget.CreateCommit(" abcdef1 ", " Fix bug "),
                AppServerReviewTarget.CreateCustom(" Check cancellation ")
            };
            var types = new[] { "uncommittedChanges", "baseBranch", "commit", "custom" };
            var calls = new List<string>();
            IDictionary<string, object> review = null;
            var operations = new AppServerReviewOperations((method, input, cancellationToken, timeout) =>
            {
                calls.Add(method);
                if (method == "review/start") review = (IDictionary<string, object>)input;
                if (method == "thread/resume") Assert.That(((IDictionary<string, object>)input)["excludeTurns"], Is.True);
                if (method == "thread/start") Assert.That(((IDictionary<string, object>)input).ContainsKey("excludeTurns"), Is.False);
                var response = Response(method, "/repo");
                if (detached && (method == "thread/start" || method == "review/start"))
                {
                    ((IDictionary<string, object>)response["thread"])["id"] = "new-review";
                    response["reviewThreadId"] = "new-review";
                }
                return Task.FromResult(response);
            }, excludeTurns: true);
            var mapped = AppServerReviewOperations.BuildTarget(targets[kind]);
            Assert.That(mapped["type"], Is.EqualTo(types[kind]));
            if (kind == 1) Assert.That(mapped["branch"], Is.EqualTo("main"));
            if (kind == 2)
            {
                Assert.That(mapped["sha"], Is.EqualTo("abcdef1"));
                Assert.That(mapped["title"], Is.EqualTo("Fix bug"));
            }
            if (kind == 3) Assert.That(mapped["instructions"], Is.EqualTo("Check cancellation"));
            await operations.StartAsync("source", "/repo", ApprovalMode.ReadOnly,
                "session-model", "high", null, targets[kind], detached, CancellationToken.None);
            Assert.That(calls, Is.EqualTo(detached
                ? new[] { "thread/start", "review/start" }
                : new[] { "thread/read", "thread/unsubscribe", "thread/resume", "review/start" }));
            Assert.That(review["threadId"], Is.EqualTo(detached ? "new-review" : "source"));
            Assert.That(review["delivery"], Is.EqualTo("inline"));
            Assert.That((IDictionary<string, object>)review["target"], Is.EquivalentTo(mapped));
            Assert.That(review.Keys, Is.EquivalentTo(new[] { "threadId", "target", "delivery" }));
        }

        [Test, Combinatorial]
        public void EmptyTargetInput_DoesNotPrepareOrStartReview([Values(1, 2, 3)] int kind, [Values(false, true)] bool detached)
        {
            var target = new AppServerReviewTarget { Kind = (AppServerReviewTargetKind)kind };
            var calls = 0;
            var operations = new AppServerReviewOperations((method, input, cancellationToken, timeout) =>
            {
                calls++;
                return Task.FromResult(Response(method, "/repo"));
            });
            Assert.ThrowsAsync<InvalidOperationException>(() => operations.StartAsync("source", "/repo", ApprovalMode.ReadOnly,
                "session-model", "high", null, target, detached, CancellationToken.None));
            Assert.That(calls, Is.Zero);
        }

        [TestCase(@"E:\Repo", null, "session-model", false)]
        [TestCase(@"E:\Repo", null, "session-model", true)]
        [TestCase("/home/wsl/repo", "review-model", "review-model", false)]
        [TestCase("/Users/mac/repo", "review-model", "review-model", false)]
        [TestCase("/home/linux/repo", "review-model", "review-model", false)]
        [TestCase("/home/wsl/repo", "review-model", "review-model", true)]
        [TestCase("/Users/mac/repo", "review-model", "review-model", true)]
        [TestCase("/home/linux/repo", "review-model", "review-model", true)]
        public async Task Review_PreparesVerifiedSettingsAndSendsOnlyReviewFields(string path, string reviewModel, string expectedModel, bool newSession)
        {
            var calls = new List<string>();
            IDictionary<string, object> resume = null;
            IDictionary<string, object> review = null;
            var operations = new AppServerReviewOperations((method, input, cancellationToken, timeout) =>
            {
                calls.Add(method);
                if (method == "thread/resume" || method == "thread/start") resume = (IDictionary<string, object>)input;
                if (method == "review/start") review = (IDictionary<string, object>)input;
                var response = Response(method, path);
                if (newSession)
                {
                    ((IDictionary<string, object>)response["thread"])["id"] = "new-review";
                    response["reviewThreadId"] = "new-review";
                }
                return Task.FromResult(response);
            });
            var result = await operations.StartAsync("source", path, ApprovalMode.ReadOnly, "session-model", "high",
                reviewModel, AppServerReviewTarget.CreateUncommittedChanges(), newSession, CancellationToken.None);

            Assert.That(calls, Is.EqualTo(newSession ? new[] { "thread/start", "review/start" } : new[] { "thread/read", "thread/unsubscribe", "thread/resume", "review/start" }));
            Assert.That(resume["cwd"], Is.EqualTo(path));
            Assert.That(resume["model"], Is.EqualTo("session-model"));
            Assert.That(resume["approvalsReviewer"], Is.EqualTo("user"));
            var config = (IDictionary<string, object>)resume["config"];
            Assert.That(config["model_reasoning_effort"], Is.EqualTo("high"));
            Assert.That(config["review_model"], Is.EqualTo(expectedModel));
            Assert.That(review.Keys, Is.EquivalentTo(new[] { "threadId", "target", "delivery" }));
            Assert.That(result.ConfiguredModelId, Is.EqualTo(expectedModel));
            Assert.That(result.ConfiguredReasoningEffort, Is.EqualTo("high"));
            Assert.That(result.ReviewThreadId, Is.EqualTo(newSession ? "new-review" : "source"));
        }

        [TestCase("model")]
        [TestCase("reasoningEffort")]
        [TestCase("cwd")]
        [TestCase("approvalPolicy")]
        [TestCase("approvalsReviewer")]
        [TestCase("sandbox")]
        [TestCase("active")]
        public void ResumeMismatch_DoesNotStartReview(string changed)
        {
            var started = false;
            var operations = new AppServerReviewOperations((method, input, cancellationToken, timeout) =>
            {
                if (method == "review/start") started = true;
                var response = Response(method, "/repo");
                if (method == "thread/resume")
                {
                    if (changed == "active") ((IDictionary<string, object>)response["thread"])["status"] = new Dictionary<string, object> { { "type", "active" } };
                    else response[changed] = "old-value";
                }
                return Task.FromResult(response);
            });
            Assert.ThrowsAsync<InvalidOperationException>(() => operations.StartAsync("source", "/repo", ApprovalMode.ReadOnly,
                "session-model", "high", null, AppServerReviewTarget.CreateUncommittedChanges(), false, CancellationToken.None));
            Assert.That(started, Is.False);
        }

        [TestCase("model")]
        [TestCase("cwd")]
        [TestCase("approvalPolicy")]
        [TestCase("approvalsReviewer")]
        [TestCase("sandbox")]
        [TestCase("reasoningEffort")]
        [TestCase("failure")]
        [TestCase("cancel")]
        public void NewSessionFailure_PreservesCreatedThreadWithoutTouchingSource(string failure)
        {
            var calls = new List<string>();
            var operations = new AppServerReviewOperations((method, input, cancellationToken, timeout) =>
            {
                calls.Add(method);
                var parameters = (IDictionary<string, object>)input;
                if (method == "thread/start")
                {
                    Assert.That(parameters.ContainsKey("threadId"), Is.False);
                    Assert.That(parameters.ContainsKey("effort"), Is.False);
                    Assert.That(((IDictionary<string, object>)parameters["config"])["model_reasoning_effort"], Is.EqualTo("high"));
                }
                else
                {
                    Assert.That(parameters["threadId"], Is.EqualTo("new-review"));
                    if (failure == "cancel") throw new OperationCanceledException();
                    throw new InvalidOperationException("review failed");
                }
                var response = Response(method, "/repo");
                ((IDictionary<string, object>)response["thread"])["id"] = "new-review";
                if (failure != "failure" && failure != "cancel") response[failure] = "wrong";
                return Task.FromResult(response);
            });
            var error = Assert.ThrowsAsync<AppServerReviewStartException>(() => operations.StartAsync("source", "/repo", ApprovalMode.ReadOnly,
                "session-model", "high", null, AppServerReviewTarget.CreateUncommittedChanges(), true, CancellationToken.None));
            Assert.That(error.CreatedReview.CreatedThread.ThreadId, Is.EqualTo("new-review"));
            Assert.That(error.CreatedReview.TurnId, Is.Null);
            Assert.That(calls, Is.EqualTo(failure == "failure" || failure == "cancel"
                ? new[] { "thread/start", "review/start" } : new[] { "thread/start" }));
            if (failure == "cancel") Assert.That(error.InnerException, Is.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public void ActiveSource_IsNotUnsubscribed()
        {
            var calls = new List<string>();
            var operations = new AppServerReviewOperations((method, input, cancellationToken, timeout) =>
            {
                calls.Add(method);
                var result = Response(method, "/repo");
                ((IDictionary<string, object>)result["thread"])["status"] = new Dictionary<string, object> { { "type", "active" } };
                return Task.FromResult(result);
            });
            Assert.ThrowsAsync<InvalidOperationException>(() => operations.StartAsync("source", "/repo", ApprovalMode.ReadOnly,
                "session-model", "high", null, AppServerReviewTarget.CreateUncommittedChanges(), false, CancellationToken.None));
            Assert.That(calls, Is.EqualTo(new[] { "thread/read" }));
        }

        private static IDictionary<string, object> Response(string method, string path)
        {
            return new Dictionary<string, object>
            {
                { "thread", new Dictionary<string, object> { { "id", "source" }, { "status", new Dictionary<string, object> { { "type", "idle" } } } } },
                { "model", "session-model" }, { "reasoningEffort", "high" }, { "cwd", path },
                { "approvalPolicy", "on-request" }, { "sandbox", new Dictionary<string, object> { { "type", "readOnly" } } },
                { "approvalsReviewer", "user" },
                { "turn", new Dictionary<string, object> { { "id", "review-turn" } } }, { "reviewThreadId", "source" }
            };
        }
    }
}
