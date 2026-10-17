using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AppServerClientMessageTests
    {
        [TestCase(@"E:\work\project")]
        [TestCase("/home/test/wsl-project")]
        [TestCase("/Users/test/mac-project")]
        [TestCase("/srv/unix-project")]
        public async Task ColdResumeUsesCurrentWorkspaceProviderWithoutRewritingHistory(string workspace)
        {
            using (var client = CreateClient())
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                SetPrivateField(client, "_inputWriter", writer);
                var parameters = new Dictionary<string, object>
                {
                    { "threadId", "original-thread" }, { "cwd", workspace },
                    { "excludeTurns", true }, { "approvalPolicy", "never" },
                    { "config", new Dictionary<string, object> { { "model_reasoning_effort", "high" } } }
                };
                var operation = InvokeComputerUseRequest(client, "thread/resume", parameters, CancellationToken.None);
                var read = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 1));
                Assert.That(read["method"], Is.EqualTo("config/read"));
                Assert.That(AppServerJsonValue.GetDictionary(read, "params")["cwd"], Is.EqualTo(workspace));
                Assert.That(RuntimeOperationGate.Shared.TryEnterTransition(), Is.Null);
                CompleteRequest(client, Convert.ToString(read["id"], CultureInfo.InvariantCulture),
                    new Dictionary<string, object> { { "config", new Dictionary<string, object> { { "model_provider", "current-provider" } } } });
                var resume = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 2));
                Assert.That(resume["method"], Is.EqualTo("thread/resume"));
                var sent = AppServerJsonValue.GetDictionary(resume, "params");
                Assert.That(sent["modelProvider"], Is.EqualTo("current-provider"));
                Assert.That(sent["threadId"], Is.EqualTo("original-thread"));
                Assert.That(sent["excludeTurns"], Is.True);
                Assert.That(sent["approvalPolicy"], Is.EqualTo("never"));
                Assert.That(AppServerJsonValue.GetDictionary(sent, "config")["model_reasoning_effort"], Is.EqualTo("high"));
                Assert.That(parameters.ContainsKey("modelProvider"), Is.False);
                CompleteRequest(client, Convert.ToString(resume["id"], CultureInfo.InvariantCulture),
                    new Dictionary<string, object> { { "thread", new Dictionary<string, object> { { "id", "original-thread" } } } });
                await operation;
                SetPrivateField(client, "_inputWriter", null);
            }
        }

        private static async Task CompleteResumeProviderReadAsync(AppServerClient client, MemoryStream stream, int lineCount)
        {
            var read = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, lineCount));
            Assert.That(read["method"], Is.EqualTo("config/read"));
            CompleteRequest(client, Convert.ToString(read["id"], CultureInfo.InvariantCulture),
                new Dictionary<string, object> { { "config", new Dictionary<string, object> { { "model_provider", "current-provider" } } } });
        }

        [Test]
        public async Task ResumeDoesNotSendOldConfigurationToANewConnectionEpoch()
        {
            using (var client = CreateClient())
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                SetPrivateField(client, "_inputWriter", writer);
                var operation = InvokeComputerUseRequest(client, "thread/resume", new Dictionary<string, object>
                {
                    { "threadId", "original" }, { "cwd", "/work" }
                }, CancellationToken.None);
                var read = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 1));
                SetPrivateField(client, "_connectionEpoch", GetPrivateField<long>(client, "_connectionEpoch") + 1);
                CompleteRequest(client, Convert.ToString(read["id"], CultureInfo.InvariantCulture),
                    new Dictionary<string, object> { { "config", new Dictionary<string, object> { { "model_provider", "old-runtime" } } } });
                var failure = Assert.ThrowsAsync<InvalidOperationException>(async () => await operation);
                Assert.That(failure.InnerException, Is.TypeOf<AppServerTransportWriteException>());
                Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Not.Contain("thread/resume"));
                SetPrivateField(client, "_inputWriter", null);
            }
        }
    }
}
