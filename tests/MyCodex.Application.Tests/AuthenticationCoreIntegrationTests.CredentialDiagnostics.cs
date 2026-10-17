using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
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
        [Test]
        [NonParallelizable]
        [Explicit("Requires MYCODEX_AUTH_CORE_TEST_PATH and MYCODEX_CODEX_TEST_PATH. Uses isolated legacy metadata and no model requests.")]
        public async Task NativeWindowsCredentialFailureLogsLegacyReferenceAndActualConfigScope()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            var codex = Environment.GetEnvironmentVariable("MYCODEX_CODEX_TEST_PATH");
            Assert.That(File.Exists(executable) && File.Exists(codex), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-credential-diag-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            var legacyRoot = Path.Combine(root, "legacy-app");
            Directory.CreateDirectory(home);
            Directory.CreateDirectory(Path.Combine(legacyRoot, "config"));
            var provider = "mycodex_resp_" + Guid.NewGuid().ToString("N").Substring(0, 12);
            var variable = "MYCODEX_" + provider.ToUpperInvariant() + "_API_KEY";
            var config = "model = \"synthetic-model\"\nmodel_provider = \"" + provider + "\"\n[model_providers." + provider + "]\n" +
                "name = \"Legacy\"\nbase_url = \"https://legacy.example.invalid/v1\"\nwire_api = \"responses\"\nenv_key = \"" + variable + "\"\n";
            File.WriteAllText(Path.Combine(home, "config.toml"), config, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(home, "auth.json"), "{}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(legacyRoot, "config", "auth-profiles.json"),
                "{\"Profiles\":[{\"Id\":\"private-id\",\"Kind\":\"openai_compatible\",\"ModelProviderId\":\"" + provider +
                "\",\"EncryptedApiKey\":\"synthetic-protected-secret\"}]}");
            var oldHome = Environment.GetEnvironmentVariable("CODEX_HOME");
            var host = NewHost(executable, home, Path.Combine(root, "private"));
            AppServerClient runtime = null;
            await Call(host, "status");
            try
            {
                Environment.SetEnvironmentVariable("CODEX_HOME", home);
                var paths = new AppConfigService(Path.Combine(root, "runner"));
                var store = new JsonAppConfigStore(paths);
                store.Save(new AppConfigSnapshot { CodexRuntimeSource = CodexRuntimeSources.Custom, CodexCustomExecutablePath = codex });
                runtime = new AppServerClient(new CodexRuntimeManager(paths), store);
                await runtime.ConnectAsync();
                var client = new AuthenticationCenterClient("Windows", host.RequestAsync, true);
                client.CredentialDiagnostics = trigger => AuthenticationCenterClient.CollectWindowsCredentialDiagnosticsAsync(
                    trigger, token => runtime.ReadConfigAsync(null, true, token), legacyRoot, home);
                using (var output = new StringWriter())
                using (var listener = new TextWriterTraceListener(output))
                {
                    Trace.Listeners.Add(listener);
                    try
                    {
                        Assert.That((await client.ReadAsync(CancellationToken.None)).SyncError, Is.EqualTo("credential_reference_unavailable"));
                        var preset = (await client.ReadPresetsAsync(CancellationToken.None)).Single(p => p.Name == "DeepSeek");
                        var saved = await client.SaveAsync(new AuthenticationProviderEdit
                        {
                            Name = preset.Name, Kind = preset.Kind, PresetId = preset.Id, BaseUrl = preset.BaseUrl,
                            Model = preset.Model, Models = preset.Models.ToList(), Advanced = preset.Advanced,
                            ApiKey = "synthetic-new-secret"
                        }, CancellationToken.None);
                        Assert.That(saved.RecoveryRequired, Is.True);
                        Assert.That(saved.GlobalApplied, Is.False);
                        listener.Flush();
                        var log = output.ToString();
                        Assert.That(log, Does.Contain("Scope=user_layer").And.Contain("ConfigMatchesBackendHome=True"));
                        Assert.That(log, Does.Contain("Name=" + variable).And.Contain("LegacyEnvironmentMatches=1").And.Contain("LegacyProviderMatches=1"));
                        Assert.That(log, Does.Contain("Trigger=provider/list").And.Contain("Trigger=provider/save"));
                        Assert.That(log, Does.Not.Contain("synthetic-new-secret").And.Not.Contain("synthetic-protected-secret").And.Not.Contain("private-id"));
                        Assert.That(File.ReadAllText(Path.Combine(home, "config.toml")), Is.EqualTo(config));
                        File.WriteAllText(Path.Combine(root, "sanitized-diagnostics.log"), log, new UTF8Encoding(false));
                    }
                    finally { Trace.Listeners.Remove(listener); }
                }
            }
            finally
            {
                runtime?.Dispose();
                Environment.SetEnvironmentVariable("CODEX_HOME", oldHome);
                await Call(host, "backend/shutdown");
            }
        }
    }
}
