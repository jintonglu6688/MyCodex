using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerThreadResumeConfigurationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ExplicitProviderIsRetainedWithoutAConfigurationRead(bool nested)
        {
            var parameters = Parameters();
            if (nested) parameters["config"] = new Dictionary<string, object> { { "model_provider", "explicit" } };
            else parameters["modelProvider"] = "explicit";
            await AppServerThreadResumeConfiguration.SendAsync((method, values, token, timeout) =>
            {
                Assert.That(method, Is.EqualTo("thread/resume"));
                Assert.That(values, Is.SameAs(parameters));
                return Task.FromResult(new Dictionary<string, object>() as IDictionary<string, object>);
            }, parameters, CancellationToken.None, 1000, Timeout);
        }

        [Test]
        public async Task MissingDirectoryUsesTheOriginalThreadDirectoryAndDefaultProvider()
        {
            var calls = new List<string>();
            var parameters = Parameters();
            parameters.Remove("cwd");
            await AppServerThreadResumeConfiguration.SendAsync((method, values, token, timeout) =>
            {
                calls.Add(method);
                var input = (IDictionary<string, object>)values;
                if (method == "thread/read")
                {
                    Assert.That(input["includeTurns"], Is.False);
                    return Result("thread", new Dictionary<string, object> { { "cwd", "/original/project" } });
                }
                if (method == "config/read")
                {
                    Assert.That(input["cwd"], Is.EqualTo("/original/project"));
                    return Result("config", new Dictionary<string, object>());
                }
                Assert.That(input["modelProvider"], Is.EqualTo("openai"));
                Assert.That(input["threadId"], Is.EqualTo("original"));
                return Result("ok", true);
            }, parameters, CancellationToken.None, 1000, Timeout);
            Assert.That(calls, Is.EqualTo(new[] { "thread/read", "config/read", "thread/resume" }));
            Assert.That(parameters.ContainsKey("modelProvider"), Is.False);
        }

        [Test]
        public void FailedConfigurationReadDoesNotResumeWithOldCredentials()
        {
            var failure = new IOException("synthetic read failure");
            var caught = Assert.ThrowsAsync<IOException>(() => AppServerThreadResumeConfiguration.SendAsync((method, values, token, timeout) =>
            {
                Assert.That(method, Is.EqualTo("config/read"));
                return Task.FromException<IDictionary<string, object>>(failure);
            }, Parameters(), CancellationToken.None, 1000, Timeout));
            Assert.That(caught, Is.SameAs(failure));
        }

        [Test]
        public void MissingConfigurationIsNotTreatedAsTheDefaultProvider()
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => AppServerThreadResumeConfiguration.SendAsync((method, values, token, timeout) =>
            {
                Assert.That(method, Is.EqualTo("config/read"));
                return Result("config", null);
            }, Parameters(), CancellationToken.None, 1000, Timeout));
        }

        [TestCase(123)]
        [TestCase("")]
        public void InvalidProviderIsNotReplacedWithAnUnrelatedDefault(object provider)
        {
            Assert.ThrowsAsync<InvalidOperationException>(() => AppServerThreadResumeConfiguration.SendAsync((method, values, token, timeout) =>
            {
                Assert.That(method, Is.EqualTo("config/read"));
                return Result("config", new Dictionary<string, object> { { "model_provider", provider } });
            }, Parameters(), CancellationToken.None, 1000, Timeout));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PreparationUsesTheOriginalDeadlineAndCancellation(bool cancel)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var calls = 0;
                var operation = AppServerThreadResumeConfiguration.SendAsync(async (method, values, token, timeout) =>
                {
                    calls++;
                    Assert.That(method, Is.EqualTo("config/read"));
                    Assert.That(token, Is.EqualTo(cancellation.Token));
                    if (cancel) cancellation.Cancel();
                    else await Task.Delay(70);
                    return new Dictionary<string, object> { { "config", new Dictionary<string, object> { { "model_provider", "current" } } } };
                }, Parameters(), cancellation.Token, 30, Timeout);
                if (cancel) Assert.CatchAsync<OperationCanceledException>(async () => await operation);
                else Assert.ThrowsAsync<TimeoutException>(async () => await operation);
                Assert.That(calls, Is.EqualTo(1), "No resume can be sent after the deadline or cancellation.");
            }
        }

        private static Dictionary<string, object> Parameters()
        {
            return new Dictionary<string, object> { { "threadId", "original" }, { "cwd", "/work" } };
        }

        private static Task<IDictionary<string, object>> Result(string key, object value)
        {
            return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { key, value } });
        }

        private static TimeoutException Timeout(string method, int milliseconds)
        {
            return new TimeoutException("synthetic timeout");
        }
    }
}
