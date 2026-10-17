using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerResumeProviderIntegrationTests
    {
        [Test]
        [NonParallelizable]
        [Explicit("Requires MYCODEX_CODEX_TEST_PATH. Uses isolated history, synthetic credentials and a loopback-only API fixture.")]
        public async Task OldConversationUsesNewProviderAndKeepsHistoryAfterReconnect()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_CODEX_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "resume-provider-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            var workspace = Path.Combine(root, "work");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(workspace);
            var variable = "MYCODEX_TEST_MISSING_" + Guid.NewGuid().ToString("N");
            var previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/v1";
            var legacy = "[model_providers.legacy]\nname='Legacy'\nbase_url='" + endpoint + "'\nwire_api='responses'\nenv_key='" + variable + "'\n";
            var configPath = Path.Combine(home, "config.toml");
            File.WriteAllText(configPath, "model='synthetic-model'\nmodel_provider='legacy'\n" + legacy);
            File.WriteAllText(Path.Combine(home, "auth.json"), "{}");
            var files = new AppConfigService(Path.Combine(root, "runner"));
            var store = new JsonAppConfigStore(files);
            store.Save(new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Custom, CodexCustomExecutablePath = executable });
            AppServerClient runtime = null;
            try
            {
                Environment.SetEnvironmentVariable("CODEX_HOME", home);
                runtime = new AppServerClient(new CodexRuntimeManager(files), store);
                await runtime.ConnectAsync();
                var thread = await runtime.CreateThreadAsync(workspace, ApprovalMode.ReadOnly, "synthetic-model", null);
                var originalError = await SendForErrorAsync(runtime, thread, workspace, "original-history-marker");
                Assert.That(originalError, Does.Contain(variable));
                runtime.Dispose();
                runtime = null;
                File.WriteAllText(configPath, "model='synthetic-model'\nmodel_provider='current'\n" +
                    "[model_providers.current]\nname='Current'\nbase_url='" + endpoint + "'\nwire_api='responses'\nrequires_openai_auth=true\nrequest_max_retries=0\nstream_max_retries=0\n" + legacy);
                File.WriteAllText(Path.Combine(home, "auth.json"), "{\"OPENAI_API_KEY\":\"synthetic-key\"}");
                var response = RespondLocallyAsync(listener);
                runtime = new AppServerClient(new CodexRuntimeManager(files), store);
                await runtime.ConnectAsync();
                var newError = await SendForErrorAsync(runtime, thread, workspace, "continued-history-marker");
                Assert.That(newError, Does.Not.Contain(variable).And.Contain("synthetic local fixture"));
                Assert.That(await Task.WhenAny(response, Task.Delay(10000)), Is.SameAs(response));
                await response;
                runtime.Dispose();
                runtime = null;
                var history = Directory.GetFiles(Path.Combine(home, "sessions"), "*" + thread + ".jsonl", SearchOption.AllDirectories).Single();
                Assert.That(File.ReadAllText(history), Does.Contain("original-history-marker").And.Contain("continued-history-marker"));
                TestContext.WriteLine("Original thread ID and both user messages retained; resumed request reached only the local API fixture.");
            }
            finally
            {
                runtime?.Dispose();
                listener.Stop();
                Environment.SetEnvironmentVariable("CODEX_HOME", previousHome);
            }
        }

        private static async Task<string> SendForErrorAsync(AppServerClient runtime, string thread, string workspace, string text)
        {
            var completion = new TaskCompletionSource<string>();
            EventHandler<AppServerEventArgs> handler = (sender, value) =>
            {
                if (value.ThreadId == thread && (value.EventType == "error" || value.EventType == "turn_failed"))
                    completion.TrySetResult(value.Content);
            };
            runtime.EventReceived += handler;
            try
            {
                await runtime.SendUserMessageAsync(thread, workspace,
                    new[] { new AppServerUserInputItem { Type = "text", Text = text } }, ApprovalMode.ReadOnly, "synthetic-model", null);
                Assert.That(await Task.WhenAny(completion.Task, Task.Delay(15000)), Is.SameAs(completion.Task), "Expected a deterministic local error.");
                return await completion.Task;
            }
            finally { runtime.EventReceived -= handler; }
        }

        private static async Task RespondLocallyAsync(TcpListener listener)
        {
            using (var connection = await listener.AcceptTcpClientAsync())
            using (var stream = connection.GetStream())
            {
                var header = new StringBuilder();
                var one = new byte[1];
                while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                {
                    Assert.That(header.Length, Is.LessThan(65536));
                    Assert.That(await stream.ReadAsync(one, 0, 1), Is.EqualTo(1));
                    header.Append((char)one[0]);
                }
                var match = Regex.Match(header.ToString(), "(?im)^Content-Length:\\s*(\\d+)");
                var remaining = match.Success ? int.Parse(match.Groups[1].Value) : 0;
                var buffer = new byte[4096];
                while (remaining > 0)
                {
                    var read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining));
                    Assert.That(read, Is.GreaterThan(0));
                    remaining -= read;
                }
                var body = "{\"error\":{\"message\":\"synthetic local fixture\",\"type\":\"invalid_request_error\",\"code\":\"invalid_api_key\"}}";
                var response = Encoding.ASCII.GetBytes("HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n" + body);
                await stream.WriteAsync(response, 0, response.Length);
            }
        }
    }
}
