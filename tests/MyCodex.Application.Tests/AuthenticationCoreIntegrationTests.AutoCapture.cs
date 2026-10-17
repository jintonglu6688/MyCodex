using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AuthenticationCoreIntegrationTests
    {
        [Test]
        [Explicit("Requires MYCODEX_AUTH_CORE_TEST_PATH. Reproduces missing live credentials using isolated synthetic files; no API requests.")]
        public async Task UnavailableLiveCredentialAllowsSaveThenExplicitEnableWithAutomaticBackup()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-core-deepseek-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            Directory.CreateDirectory(home);
            var missingVariable = "MYCODEX_TEST_MISSING_" + Guid.NewGuid().ToString("N");
            var config = "model = \"old-model\"\nmodel_provider = \"old_vendor\"\n" +
                "[model_providers.old_vendor]\nname = \"Old vendor\"\nbase_url = \"https://old.example.invalid/v1\"\n" +
                "wire_api = \"responses\"\nenv_key = \"" + missingVariable + "\"\n";
            var path = Path.Combine(home, "config.toml");
            File.WriteAllText(path, config, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(home, "auth.json"), "{}", new UTF8Encoding(false));
            var host = NewHost(executable, home, Path.Combine(root, "private"));
            var client = new AuthenticationCenterClient("Windows", host.RequestAsync, true);
            await Call(host, "status");
            try
            {
                var snapshot = await client.ReadAsync(CancellationToken.None);
                Assert.That(snapshot.SyncError, Is.EqualTo("credential_reference_unavailable"));
                var preset = (await client.ReadPresetsAsync(CancellationToken.None)).Single(p => p.Name == "DeepSeek");
                var edit = new AuthenticationProviderEdit
                {
                    Id = "deepseek-repro", Name = preset.Name, PresetId = preset.Id, Kind = preset.Kind,
                    BaseUrl = preset.BaseUrl, Model = preset.Model, Models = preset.Models.ToList(),
                    Advanced = new Dictionary<string, object>(preset.Advanced), ApiKey = "synthetic-deepseek-key"
                };
                var coordinator = new AuthenticationApplyCoordinator(client, new RuntimeOperationGate().TryEnterTransition, () => Task.FromResult(true));
                var saved = await coordinator.SaveAsync(edit, CancellationToken.None);
                Assert.That(saved.Provider.HasApiKey, Is.True);
                Assert.That(saved.Provider.GlobalApplied, Is.False);
                Assert.That(saved.Provider.RecoveryRequired, Is.True);
                Assert.That(File.ReadAllText(path), Is.EqualTo(config));
                Assert.That((await client.ReadAsync(CancellationToken.None)).Providers.Count, Is.EqualTo(1));
                var plan = await client.PreflightAsync(saved.Provider, CancellationToken.None);
                Assert.That(plan.RecoveryRequired, Is.True);
                var backup = Path.Combine(root, "private", "credentials-before-recovery-" + plan.Fingerprint + ".json");
                Assert.That(File.Exists(backup), Is.False, "Saving and reading must not start the global switch or its backup.");
                Assert.That(File.ReadAllText(path), Is.EqualTo(config));
                var applied = await coordinator.ApplyAsync(saved.Provider, CancellationToken.None);
                Assert.That(applied.GlobalApplied && applied.Reconnected, Is.True);
                var current = await client.ReadAsync(CancellationToken.None);
                Assert.That(current.CurrentProviderId, Is.EqualTo(saved.Provider.Id));
                Assert.That(current.SyncError, Is.Null.Or.Empty);
                Assert.That(SelectedRoute(File.ReadAllText(path)), Does.Contain("synthetic-deepseek-key").And.Not.Contain(missingVariable));
                var bytes = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(backup));
                Assert.That(bytes["config"], Is.EqualTo(config));
                Assert.That(bytes["auth"], Is.EqualTo("{}"));
            }
            finally { await Call(host, "backend/shutdown"); }
        }

        [Test]
        [Explicit("Requires the auto-capture package at MYCODEX_AUTH_CORE_TEST_PATH. Uses isolated synthetic files only; no API requests.")]
        public async Task GlobalApiCaptureReusesModelsSeparatesCredentialsAndPreservesArchiveOnInvalidLive()
        {
            var executable = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(executable), Is.True, "Set MYCODEX_AUTH_CORE_TEST_PATH to the new packaged native host.");
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-core-capture-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            Directory.CreateDirectory(home);
            var configPath = Path.Combine(home, "config.toml");
            var authPath = Path.Combine(home, "auth.json");
            var encoding = new UTF8Encoding(false);
            var original = AutoCaptureApiConfig("first-model", "synthetic-capture-first");
            File.WriteAllText(configPath, original, encoding);
            File.WriteAllText(authPath, "{}", encoding);
            var host = NewHost(executable, home, Path.Combine(root, "private"));
            var client = new AuthenticationCenterClient("Capture test", host.RequestAsync);
            var coordinator = new AuthenticationApplyCoordinator(client, new RuntimeOperationGate().TryEnterTransition, () => Task.FromResult(true));
            var started = false;
            try
            {
                await Call(host, "status");
                started = true;
                var initial = await client.ReadAsync(CancellationToken.None);
                Assert.That(initial.LiveState, Is.EqualTo("current"));
                Assert.That(initial.SyncError, Is.Null.Or.Empty);
                var first = initial.Providers.Single();
                Assert.That(initial.CurrentProviderId, Is.EqualTo(first.Id));
                Assert.That(first.Model, Is.EqualTo("first-model"));
                Assert.That(first.HasApiKey, Is.True);
                Assert.That(initial.RouterRunning || initial.RouteRequired, Is.False);
                Assert.That(File.ReadAllText(configPath), Is.EqualTo(original), "Capture must not apply or normalize live files.");
                Assert.That(File.ReadAllText(authPath), Is.EqualTo("{}"));

                var modelEdit = AutoCaptureApiConfig("external-model", "synthetic-capture-first");
                File.WriteAllText(configPath, modelEdit, encoding);
                var updated = await client.ReadAsync(CancellationToken.None);
                Assert.That(updated.Providers.Single().Id, Is.EqualTo(first.Id));
                Assert.That(updated.Providers.Single().Model, Is.EqualTo("external-model"));
                Assert.That(updated.CurrentProviderId, Is.EqualTo(first.Id));
                Assert.That((await client.ReadAsync(CancellationToken.None)).Providers.Count, Is.EqualTo(1));
                Assert.That(File.ReadAllText(configPath), Is.EqualTo(modelEdit));

                var replacement = AutoCaptureApiConfig("second-model", "synthetic-capture-second");
                File.WriteAllText(configPath, replacement, encoding);
                var changed = await client.ReadAsync(CancellationToken.None);
                Assert.That(changed.LiveState, Is.EqualTo("current"));
                Assert.That(changed.Providers.Count, Is.EqualTo(2));
                Assert.That(changed.CurrentProviderId, Is.Not.EqualTo(first.Id));
                var secondId = changed.CurrentProviderId;
                Assert.That(changed.Providers.Single(p => p.Id == secondId).Model, Is.EqualTo("second-model"));
                Assert.That(File.ReadAllText(configPath), Is.EqualTo(replacement));

                // Applying each archive verifies the actual secret stayed with its identity,
                // beyond the intentionally redacted C# list/get DTOs.
                await coordinator.ApplyAsync(changed.Providers.Single(p => p.Id == first.Id), CancellationToken.None);
                var firstLive = File.ReadAllText(configPath);
                // v4 switches the selected route and leaves unselected user tables in place.
                Assert.That(SelectedRoute(firstLive), Does.Contain("synthetic-capture-first").And.Not.Contain("synthetic-capture-second"));
                Assert.That(firstLive, Does.Not.Contain("second-model"), "An external identity must not be backfilled into the previous archive.");
                var restored = await client.ReadAsync(CancellationToken.None);
                Assert.That(restored.Providers.Count, Is.EqualTo(2));
                Assert.That(restored.CurrentProviderId, Is.EqualTo(first.Id));
                await coordinator.ApplyAsync(restored.Providers.Single(p => p.Id == secondId), CancellationToken.None);
                var secondLive = File.ReadAllText(configPath) + (File.Exists(authPath) ? File.ReadAllText(authPath) : string.Empty);
                Assert.That(secondLive, Does.Contain("synthetic-capture-second").And.Not.Contain("synthetic-capture-first"));

                const string invalid = "model = [\n";
                File.WriteAllText(configPath, invalid, encoding);
                var unavailable = await client.ReadAsync(CancellationToken.None);
                Assert.That(unavailable.LiveState, Is.EqualTo("unavailable"));
                Assert.That(unavailable.SyncError, Is.EqualTo("invalid_live_config"));
                Assert.That(unavailable.CurrentProviderId, Is.Null.Or.Empty);
                Assert.That(unavailable.CurrentProviderVersion, Is.Null.Or.Empty);
                Assert.That(unavailable.Providers.Select(p => p.Id), Is.EquivalentTo(new[] { first.Id, secondId }));
                Assert.That(File.ReadAllText(configPath), Is.EqualTo(invalid));
                await Call(host, "backend/shutdown");
                started = false;
                Assert.That(File.ReadAllText(configPath), Is.EqualTo(invalid), "Maintenance shutdown must not restore over invalid external live.");
            }
            finally
            {
                if (started) await Call(host, "backend/shutdown");
            }
        }

        private static string SelectedRoute(string config)
        {
            var provider = Regex.Match(config, "(?m)^model_provider\\s*=\\s*\"([^\"]+)\"").Groups[1].Value;
            return Regex.Match(config, "(?ms)^\\[model_providers\\." + Regex.Escape(provider) + "\\]\\s*$(.*?)(?=^\\[|\\z)").Groups[1].Value;
        }

        private static string AutoCaptureApiConfig(string model, string key)
        {
            return "model = \"" + model + "\"\nmodel_provider = \"capture_vendor\"\n" +
                "[model_providers.capture_vendor]\nname = \"Capture vendor\"\n" +
                "base_url = \"https://capture.example.invalid/v1\"\nwire_api = \"responses\"\n" +
                "experimental_bearer_token = \"" + key + "\"\n";
        }
    }
}
