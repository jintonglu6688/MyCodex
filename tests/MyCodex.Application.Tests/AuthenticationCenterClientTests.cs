using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Infrastructure.Processes;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationCenterClientTests
    {
        [TestCase(true)]
        [TestCase(false)]
        public async Task RuntimePreparation_UsesAdvertisedCapabilityBeforeReturning(bool supported)
        {
            var calls = new List<string>();
            var preparation = new TaskCompletionSource<IDictionary<string, object>>();
            using (var cts = new CancellationTokenSource())
            {
                var operation = AuthenticationCenterClient.EnsureBackendPreparedAsync((method, args, token) =>
                {
                    Assert.That(token, Is.EqualTo(cts.Token));
                    calls.Add(method);
                    if (method == "status") return Task.FromResult<IDictionary<string, object>>(
                        new Dictionary<string, object> { { "capabilities", supported ? new[] { "runtimeConfigPreparation" } : new string[0] } });
                    Assert.That(method, Is.EqualTo("runtime/prepare"));
                    return preparation.Task;
                }, cts.Token);
                Assert.That(operation.IsCompleted, Is.EqualTo(!supported));
                preparation.SetResult(new Dictionary<string, object> { { "changed", true } });
                await operation;
                Assert.That(calls, Is.EqualTo(supported ? new[] { "status", "runtime/prepare" } : new[] { "status" }));
            }
        }

        [Test]
        public void RuntimePreparation_DoesNotHideCleanupFailure()
        {
            Assert.ThrowsAsync<AuthenticationCenterException>(() => AuthenticationCenterClient.EnsureBackendPreparedAsync(
                (method, args, token) => method == "status"
                    ? Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "capabilities", new[] { "runtimeConfigPreparation" } } })
                    : throw new AuthenticationCenterException("config_conflict"), CancellationToken.None));
        }

        [Test]
        [Explicit("Requires a prepared MYCODEX_AUTH_CORE_TEST_PATH package; only isolated fake provider data is used.")]
        public async Task BuiltHostRejectsDifferentResidentIdentityBeforeSavingAndLeavesItRunning()
        {
            var source = Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_TEST_PATH");
            Assert.That(File.Exists(source), Is.True);
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "auth-identity-" + Guid.NewGuid().ToString("N"));
            var home = Path.Combine(root, "codex");
            var store = Path.Combine(root, "private");
            Directory.CreateDirectory(home);
            var executable = Path.Combine(root, "mycodex-auth-host.exe");
            var manifestPath = Path.Combine(root, AuthenticationHostArtifact.ManifestFileName);
            File.Copy(source, executable);
            File.Copy(Path.Combine(Path.GetDirectoryName(source), AuthenticationHostArtifact.ManifestFileName), manifestPath);
            var original = new LocalAuthenticationHost(CodexRuntimeSources.Bundled, executable, home, store);
            var empty = new Dictionary<string, object>();
            await original.RequestAsync("status", empty, CancellationToken.None);
            try
            {
                var serializer = new JavaScriptSerializer();
                var manifest = serializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(manifestPath));
                manifest["sourceRevision"] = new string('0', 40);
                File.WriteAllText(manifestPath, serializer.Serialize(manifest));
                var changed = new LocalAuthenticationHost(CodexRuntimeSources.Bundled, executable, home, store);
                var error = Assert.ThrowsAsync<AuthenticationCenterException>(async () =>
                    await changed.RequestAsync("gui/provider/save", new Dictionary<string, object>
                    {
                        { "id", "must-not-save" }, { "name", "Test" }, { "kind", "responses" },
                        { "baseUrl", "https://example.test/v1" }, { "model", "test" }, { "apiKey", "synthetic" }
                    }, CancellationToken.None));
                Assert.That(error.Code, Is.EqualTo("backend_version_mismatch"));
                var shutdownError = Assert.ThrowsAsync<AuthenticationCenterException>(() => changed.ShutdownAsync(CancellationToken.None));
                Assert.That(shutdownError.Code, Is.EqualTo("backend_version_mismatch"));
                var status = await original.RequestAsync("status", empty, CancellationToken.None);
                var rows = await original.RequestAsync("gui/provider/list", empty, CancellationToken.None);
                Assert.That((System.Collections.IEnumerable)rows["providers"], Is.Empty);
                Assert.That(File.Exists(Path.Combine(home, "config.toml")), Is.False);
            }
            finally
            {
                await original.RequestAsync("backend/shutdown", empty, CancellationToken.None);
            }
        }

        [TestCase(2)]
        [TestCase(4)]
        public void OtherProtocolVersionsAreRejectedBeforeReadingProviders(int protocol)
        {
            var methods = new List<string>();
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                methods.Add(method);
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "protocolVersion", protocol }, { "route", new Dictionary<string, object>() }
                });
            });
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => client.ReadAsync(CancellationToken.None)).Code,
                Is.EqualTo("protocol_mismatch"));
            Assert.That(methods, Is.EqualTo(new[] { "status" }));
        }

        [Test]
        public async Task SaveSendsOnlyProviderFieldsWithoutCommonConfigSharing()
        {
            IDictionary<string, object> sent = null;
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                sent = values;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "id", "a" }, { "version", "v2" }, { "globalApplied", false }
                });
            });
            await client.SaveAsync(new AuthenticationProviderEdit { Id = "a", Name = "A", Kind = "responses" }, CancellationToken.None);
            Assert.That(sent.ContainsKey("commonConfigEnabled"), Is.False,
                "Protocol 3 keeps global settings in config.toml instead of a per-provider sharing switch.");
        }

        [Test]
        public async Task ChatGptSaveUsesAccountBindingWithoutApiFieldsAndAllowsUnspecifiedModel()
        {
            IDictionary<string, object> sent = null;
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                sent = values;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "id", "a" }, { "version", "v1" }, { "globalApplied", false } });
            });
            await client.SaveAsync(new AuthenticationProviderEdit
            {
                Id = "a", Name = "ChatGPT", Kind = "chatgpt", AccountId = "archived-account", BaseUrl = "",
                ApiKey = "irrelevant-synthetic-key", ClearApiKey = true
            }, CancellationToken.None);
            Assert.That(sent.ContainsKey("baseUrl"), Is.False, "ChatGPT must use the backend's official endpoint default.");
            Assert.That(sent.ContainsKey("apiKey") || sent.ContainsKey("clearApiKey"), Is.False);
            Assert.That(sent["model"], Is.EqualTo(string.Empty));
            Assert.That(sent["accountId"], Is.EqualTo("archived-account"));
        }

        [Test]
        public async Task PresetsAndModelEditsRoundTripUnknownCapabilitiesWithoutApplying()
        {
            var methods = new List<string>();
            IDictionary<string, object> saved = null;
            var model = new Dictionary<string, object>
            {
                { "model", "test" }, { "displayName", "test" }, { "contextWindow", null },
                { "reasoningLevels", null }, { "futureCapability", "preserved" }, { "baseInstructions", "hidden" }
            };
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                methods.Add(method);
                if (method == "gui/preset/list")
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "presets", new[] { new Dictionary<string, object> { { "id", "custom" }, { "name", "Custom" }, { "kind", "responses" }, { "available", true }, { "allowsProtocolSelection", true },
                            { "protocolBaseUrls", new Dictionary<string, object> { { "responses", "https://a.test" }, { "anthropic", "https://a.test/anthropic" }, { "future", 1 } } } } } }
                    });
                if (method == "gui/model/fetch")
                {
                    Assert.That(values.ContainsKey("apiKey"), Is.False);
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "models", new[] { model } } });
                }
                saved = values;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "id", "a" }, { "version", "v2" }, { "globalApplied", false }, { "models", values["models"] },
                    { "accountId", values["accountId"] }, { "advanced", values["advanced"] }
                });
            });
            var presets = await client.ReadPresetsAsync(CancellationToken.None);
            Assert.That(presets[0].Available && presets[0].AllowsProtocolSelection, Is.True);
            Assert.That(presets[0].ProtocolBaseUrls, Is.EqualTo(new Dictionary<string, string>
                { { "responses", "https://a.test" }, { "anthropic", "https://a.test/anthropic" } }));
            var models = await client.FetchModelsAsync(new AuthenticationModelFetchRequest { Kind = "chatgpt", AccountId = "account-a" }, CancellationToken.None);
            Assert.That(models[0].ContextWindow, Is.Null);
            models[0].DisplayName = "Custom name";
            var provider = await client.SaveAsync(new AuthenticationProviderEdit
            {
                Id = "a", AccountId = "account-a", PresetId = "chatgpt", Kind = "chatgpt",
                Models = new List<AuthenticationCatalogModel>(models), Advanced = new Dictionary<string, object> { { "futureOption", true } }
            }, CancellationToken.None);
            Assert.That(provider.AccountId, Is.EqualTo("account-a"));
            Assert.That(provider.Models[0].Metadata["futureCapability"], Is.EqualTo("preserved"));
            Assert.That(provider.Models[0].DisplayName, Is.EqualTo("Custom name"));
            Assert.That(provider.Models[0].BaseInstructions, Is.EqualTo("hidden"));
            Assert.That(provider.Advanced["futureOption"], Is.True);
            Assert.That(saved.ContainsKey("apiKey"), Is.False);
            Assert.That(methods, Is.EqualTo(new[] { "gui/preset/list", "gui/model/fetch", "gui/provider/save" }));
        }

        [Test]
        public async Task EditsAndCopiesSendVersionsWithoutReadingStoredKeys()
        {
            IDictionary<string, object> sent = null;
            string method = null;
            var client = new AuthenticationCenterClient("Test", (name, values, token) =>
            {
                method = name;
                sent = values;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "id", "a" }, { "version", "v2" }, { "globalApplied", false }, { "hasApiKey", true }
                });
            });
            var edit = new AuthenticationProviderEdit { Id = "a", ExpectedVersion = "v1" };
            var provider = await client.SaveAsync(edit, CancellationToken.None);
            Assert.That(sent.ContainsKey("apiKey"), Is.False);
            Assert.That(sent["expectedVersion"], Is.EqualTo("v1"));
            edit.ClearApiKey = true;
            edit.ApiKey = "discarded-synthetic-key";
            await client.SaveAsync(edit, CancellationToken.None);
            Assert.That(sent["clearApiKey"], Is.True);
            Assert.That(sent.ContainsKey("apiKey"), Is.False);
            await client.CopyAsync(provider, "Copy", CancellationToken.None);
            Assert.That(method, Is.EqualTo("gui/provider/copy"));
            Assert.That(sent["expectedVersion"], Is.EqualTo("v2"));
            Assert.That(sent["newId"], Is.Not.EqualTo("a"));
            Assert.That(sent.ContainsKey("apiKey"), Is.False);
        }

        [Test]
        public async Task DeleteReportsWhetherTheLastChatGptAccountWasSignedOut()
        {
            IDictionary<string, object> sent = null;
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/provider/delete"));
                sent = values;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "deleted", true }, { "accountRemoved", true }
                });
            });
            var removed = await client.DeleteAsync(new AuthenticationProvider
            {
                Id = "saved-chatgpt", Version = "v1", Kind = "chatgpt"
            }, CancellationToken.None);
            Assert.That(removed, Is.True);
            Assert.That(sent.Keys, Is.EquivalentTo(new[] { "providerId", "expectedVersion" }));
            Assert.That(sent["expectedVersion"], Is.EqualTo("v1"));
            var incompatible = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "deleted", true } }));
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => incompatible.DeleteAsync(new AuthenticationProvider
            {
                Id = "saved-chatgpt", Version = "v1"
            }, CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo("protocol_mismatch"));
        }

        [Test]
        public void IncompatibleProtocolStopsBeforeReadingProviders()
        {
            var calls = 0;
            var client = new AuthenticationCenterClient("Test", (name, values, token) =>
            {
                calls++;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "protocolVersion", 99 } });
            });
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => client.ReadAsync(CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo("protocol_mismatch"));
            Assert.That(calls, Is.EqualTo(1));
        }

        [TestCase(CodexRuntimeSources.Wsl)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh)]
        public void NonLocalTargetDoesNotFallBackToWindows(string source)
        {
            var client = AuthenticationCenterClient.CreateLocal(source);
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => client.ReadAsync(CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo("unsupported_platform"));
        }

    }
}
