using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AuthenticationCoreIntegrationTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [NonParallelizable]
        [Explicit("Requires MYCODEX_CODEX_TEST_PATH and MYCODEX_AUTH_CORE_TEST_PATH; uses synthetic history and a loopback model only.")]
        public async Task LegacyProviderRecoversThroughNativeRuntimeAndKeepsOriginalThreadAfterRestart(bool hasPersistedSettings, bool headerAlreadyMigrated)
        {
            var codex = Environment.GetEnvironmentVariable("MYCODEX_CODEX_TEST_PATH");
            var auth = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(codex) && File.Exists(auth), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "provider-recovery-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            var work = Path.Combine(root, "work");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(work);
            var id = Guid.NewGuid().ToString();
            var folder = Path.Combine(home, "sessions", "2026", "11", "10");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "rollout-2026-11-10T01-00-00-" + id + ".jsonl");
            var json = new JavaScriptSerializer();
            var seed = new List<string>
            {
                json.Serialize(new { timestamp = "2026-11-10T01:00:00Z", type = "session_meta", payload = new
                {
                    id, session_id = id, timestamp = "2026-11-10T01:00:00Z", cwd = work, originator = "codex", cli_version = "0.0.0", source = "cli", model_provider = headerAlreadyMigrated ? "custom" : "mycodex_resp_0123456789ab"
                } }),
                json.Serialize(new { timestamp = "2026-11-10T01:00:00Z", type = "response_item", payload = new
                {
                    type = "message", role = "user", content = new[] { new { type = "input_text", text = "original-history-marker" } }
                } })
            };
            if (hasPersistedSettings)
            {
                // Match the settings event emitted by native Codex, including its distinct provider field.
                seed.Add(json.Serialize(new { timestamp = "2026-11-10T01:00:00Z", type = "event_msg", payload = new
                {
                    type = "thread_settings_applied", thread_id = id, thread_settings = new
                    {
                        model = "synthetic-model", model_provider_id = "mycodex_resp_0123456789ab",
                        approval_policy = "on-request", approvals_reviewer = "user", cwd = work,
                        runtime_workspace_roots = new[] { work }, disabled_plugin_ids = new string[0],
                        permission_profile = new { type = "managed", network = "restricted", file_system = new
                        {
                            type = "restricted", entries = new[] { new { path = new { type = "special", value = new { kind = "root" } }, access = "read" } }
                        } },
                        collaboration_mode = new { mode = "default", settings = new
                        {
                            model = "synthetic-model", reasoning_effort = (string)null, developer_instructions = (string)null
                        } }
                    }
                } }));
            }
            File.WriteAllLines(file, seed, new UTF8Encoding(false));
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var endpoint = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/v1";
            File.WriteAllText(Path.Combine(home, "config.toml"), "model='synthetic-model'\nmodel_provider='custom'\nweb_search='disabled'\n[analytics]\nenabled=false\n[model_providers.custom]\nname='Synthetic'\nbase_url='" + endpoint + "'\nwire_api='responses'\nrequires_openai_auth=false\nrequest_max_retries=0\nstream_max_retries=0\n");
            var files = new AppConfigService(Path.Combine(root, "runner"));
            var store = new JsonAppConfigStore(files);
            store.Save(new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Custom, CodexCustomExecutablePath = codex });
            var previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            var host = NewHost(auth, home, Path.Combine(root, "private"));
            var client = new AuthenticationCenterClient("Test", host.RequestAsync);
            AppServerClient runtime = null;
            try
            {
                Environment.SetEnvironmentVariable("CODEX_HOME", home);
                await Call(host, "status");
                runtime = new AppServerClient(new CodexRuntimeManager(files), store);
                var repairs = 0;
                var coordinator = new SessionProviderMigrationCoordinator(() => client,
                    (cwd, token) => runtime.ReadConfigAsync(cwd, false, token),
                    repair => { repairs++; return runtime.RunSessionProviderMaintenanceAsync(repair); });
                runtime.ThreadProviderRecovery = coordinator.TryRepairAsync;
                runtime.SessionProviderRecoveryBeforeConnect = coordinator.RecoverBeforeConnectAsync;
                await runtime.ConnectAsync();
                for (var turn = 0; turn < 2; turn++)
                {
                    if (turn != 0) await runtime.RestartAsync();
                    var completed = new TaskCompletionSource<bool>();
                    string error = null;
                    EventHandler<AppServerEventArgs> observe = (sender, value) =>
                    {
                        if (value.ThreadId != id) return;
                        if (value.EventType == "error" || value.EventType == "turn_failed") error = value.Content;
                        if (value.EventType == "turn_completed") completed.TrySetResult(true);
                    };
                    runtime.EventReceived += observe;
                    var response = ServeProviderSuccessAsync(listener);
                    try
                    {
                        await runtime.SendUserMessageAsync(id, work,
                            new[] { new AppServerUserInputItem { Type = "text", Text = "continued-history-marker-" + turn } },
                            ApprovalMode.ReadOnly, "synthetic-model", null);
                        Assert.That(await Task.WhenAny(completed.Task, Task.Delay(30000)), Is.SameAs(completed.Task), error);
                        Assert.That(error, Is.Null);
                        await response;
                    }
                    finally { runtime.EventReceived -= observe; }
                }
                var expectedRepairs = headerAlreadyMigrated ? 0 : 1;
                Assert.That(repairs, Is.EqualTo(expectedRepairs));
                runtime.Dispose();
                runtime = null;
                var history = File.ReadAllText(file);
                var userMessages = File.ReadLines(file).Select(line => json.Deserialize<Dictionary<string, object>>(line))
                    .Where(row => row["type"] as string == "response_item")
                    .Select(row => (Dictionary<string, object>)row["payload"])
                    .Where(row => row.TryGetValue("role", out var role) && role as string == "user")
                    .Select(row => json.Serialize(row)).ToArray();
                foreach (var marker in new[] { "original-history-marker", "continued-history-marker-0", "continued-history-marker-1" })
                    Assert.That(userMessages.Count(message => message.Contains(marker)), Is.EqualTo(1), marker);
                Assert.That(history, Does.Contain("original-history-marker").And.Contain("continued-history-marker-0").And.Contain("continued-history-marker-1").And.Contain("synthetic answer"));
                var head = (Dictionary<string, object>)json.DeserializeObject(File.ReadLines(file).First());
                Assert.That(((Dictionary<string, object>)head["payload"])["id"], Is.EqualTo(id));
                Assert.That(((Dictionary<string, object>)head["payload"])["model_provider"], Is.EqualTo("custom"));
                Assert.That(Directory.GetFiles(Path.Combine(home, "sessions"), "rollout-*.jsonl", SearchOption.AllDirectories), Has.Length.EqualTo(1));
                var backups = Path.Combine(root, "private", "backups");
                var journals = Directory.Exists(backups) ? Directory.GetFiles(backups, "journal.json", SearchOption.AllDirectories) : new string[0];
                Assert.That(journals, Has.Length.EqualTo(expectedRepairs));
                TestContext.WriteLine("Two successful native turns, {0} migration(s), original thread and history retained across restart.", expectedRepairs);
            }
            finally
            {
                runtime?.Dispose();
                listener.Stop();
                await host.ShutdownAsync(CancellationToken.None);
                Environment.SetEnvironmentVariable("CODEX_HOME", previousHome);
            }
        }

        private static async Task ServeProviderSuccessAsync(TcpListener listener)
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
                Assert.That(match.Success, Is.True);
                var remaining = int.Parse(match.Groups[1].Value);
                var buffer = new byte[4096];
                while (remaining > 0)
                {
                    var read = await stream.ReadAsync(buffer, 0, Math.Min(buffer.Length, remaining));
                    Assert.That(read, Is.GreaterThan(0));
                    remaining -= read;
                }
                var item = "{\"type\":\"message\",\"id\":\"msg_test\",\"role\":\"assistant\",\"status\":\"completed\",\"content\":[{\"type\":\"output_text\",\"text\":\"synthetic answer\"}]}";
                var body = "event: response.output_item.done\ndata: {\"type\":\"response.output_item.done\",\"output_index\":0,\"item\":" + item + "}\n\n" +
                    "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"id\":\"resp_test\",\"status\":\"completed\",\"output\":[" + item + "],\"usage\":{\"input_tokens\":1,\"output_tokens\":1,\"total_tokens\":2}}}\n\n";
                var bytes = Encoding.UTF8.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nContent-Length: " + Encoding.UTF8.GetByteCount(body) + "\r\nConnection: close\r\n\r\n" + body);
                await stream.WriteAsync(bytes, 0, bytes.Length);
            }
        }
    }
}
