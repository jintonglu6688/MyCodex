using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationModelMetadataFillTests
    {
        [Test]
        public void FormatChangeMovesPresetEndpointAndKeepsTypedUrl()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>()));
            var provider = new AuthenticationProvider
            {
                Id = "deepseek", Name = "DeepSeek", Kind = "responses", BaseUrl = "https://api.deepseek.com", Model = "deepseek-flash", Version = "1",
                Models = new List<AuthenticationCatalogModel> { new AuthenticationCatalogModel { Model = "deepseek-flash" } }
            };
            var metadata = new AuthenticationModelMetadataService(new FakeCatalogSource(ModelMetadataResolverTests.Catalog()),
                token => Task.FromResult<IReadOnlyList<AuthenticationProviderPreset>>(new[] { DeepSeekPreset() }));
            using (var form = new AuthenticationProviderEditForm(client, provider, modelMetadata: metadata))
            {
                form.ShowInTaskbar = false;
                form.Show();
                var kind = FormTestDriver.Field<ComboBox>(form, "_kindComboBox");
                var url = FormTestDriver.Field<TextBox>(form, "_urlTextBox");
                var status = FormTestDriver.Field<TextBox>(form, "_statusLabel");

                kind.SelectedIndex = 2;
                System.Windows.Forms.Application.DoEvents();
                Assert.That(url.Text, Is.EqualTo("https://api.deepseek.com/anthropic"));
                Assert.That(status.Text, Does.Contain("https://api.deepseek.com/anthropic"));

                kind.SelectedIndex = 1;
                System.Windows.Forms.Application.DoEvents();
                Assert.That(url.Text, Is.EqualTo("https://api.deepseek.com"));

                url.Text = "https://relay.example.test/v1";
                kind.SelectedIndex = 2;
                System.Windows.Forms.Application.DoEvents();
                Assert.That(url.Text, Is.EqualTo("https://relay.example.test/v1"), "A URL the user typed is never replaced.");
            }
        }

        [Test]
        public void OpeningProviderSavedWithMismatchedPresetEndpointCorrectsIt()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>()));
            var provider = new AuthenticationProvider
            {
                Id = "deepseek", Name = "DeepSeek", Kind = "anthropic", BaseUrl = "https://api.deepseek.com", Model = "deepseek-flash", Version = "1",
                Models = new List<AuthenticationCatalogModel> { new AuthenticationCatalogModel { Model = "deepseek-flash" } }
            };
            var metadata = new AuthenticationModelMetadataService(new FakeCatalogSource(ModelMetadataResolverTests.Catalog()),
                token => Task.FromResult<IReadOnlyList<AuthenticationProviderPreset>>(new[] { DeepSeekPreset() }));
            using (var form = new AuthenticationProviderEditForm(client, provider, modelMetadata: metadata))
            {
                form.ShowInTaskbar = false;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(FormTestDriver.Field<TextBox>(form, "_urlTextBox").Text, Is.EqualTo("https://api.deepseek.com/anthropic"));
                Assert.That(FormTestDriver.Field<TextBox>(form, "_statusLabel").Text, Does.Contain("https://api.deepseek.com/anthropic"));
            }
        }

        [Test]
        public void OpeningProviderWithMatchingEndpointLeavesItUnchanged()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>()));
            var provider = new AuthenticationProvider
            {
                Id = "deepseek", Name = "DeepSeek", Kind = "responses", BaseUrl = "https://api.deepseek.com", Model = "deepseek-flash", Version = "1",
                Models = new List<AuthenticationCatalogModel> { new AuthenticationCatalogModel { Model = "deepseek-flash" } }
            };
            var metadata = new AuthenticationModelMetadataService(new FakeCatalogSource(ModelMetadataResolverTests.Catalog()),
                token => Task.FromResult<IReadOnlyList<AuthenticationProviderPreset>>(new[] { DeepSeekPreset() }));
            using (var form = new AuthenticationProviderEditForm(client, provider, modelMetadata: metadata))
            {
                form.ShowInTaskbar = false;
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(FormTestDriver.Field<TextBox>(form, "_urlTextBox").Text, Is.EqualTo("https://api.deepseek.com"));
                Assert.That(FormTestDriver.Field<TextBox>(form, "_statusLabel").Text, Does.Not.Contain("/anthropic"));
            }
        }

        private static AuthenticationProviderPreset DeepSeekPreset()
        {
            return new AuthenticationProviderPreset
            {
                Name = "DeepSeek", Kind = "responses", BaseUrl = "https://api.deepseek.com",
                ProtocolBaseUrls = new Dictionary<string, string>
                {
                    { "responses", "https://api.deepseek.com" }, { "chat_completions", "https://api.deepseek.com" },
                    { "anthropic", "https://api.deepseek.com/anthropic" }
                }
            };
        }

        [Test]
        public async Task RetrievedAndExistingModelsReceiveOnlyBlankFields()
        {
            var requests = new List<string>();
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                requests.Add(method);
                return Task.FromResult<IDictionary<string, object>>(method == "gui/preset/list"
                    ? new Dictionary<string, object> { { "presets", new object[0] } }
                    : new Dictionary<string, object> { { "models", new[] { Model("gpt-5.5"), Model("gpt-6-sol"), Model("unknown-model") } } });
            });
            var provider = new AuthenticationProvider
            {
                Id = "relay", Name = "Relay", Kind = "responses", BaseUrl = "https://codex.zxrjy.net:3500/", Model = "gpt-5.5", Version = "1",
                Models = new List<AuthenticationCatalogModel>
                {
                    new AuthenticationCatalogModel { Model = "gpt-5.5", DisplayName = "Mine", ContextWindow = 128000 },
                    new AuthenticationCatalogModel { Model = "gpt-6-sol", ReasoningLevels = new List<string> { "high" } }
                }
            };
            var source = new FakeCatalogSource(ModelMetadataResolverTests.Catalog());
            var metadata = new AuthenticationModelMetadataService(source, token => Task.FromResult<IReadOnlyList<AuthenticationProviderPreset>>(new AuthenticationProviderPreset[0]));
            using (var form = new AuthenticationProviderEditForm(client, provider, modelMetadata: metadata))
            {
                await form.FetchModelsAsync();

                var models = FormTestDriver.Field<AuthenticationModelsControl>(form, "_models").Models.ToDictionary(model => model.Model);
                Assert.That(models["gpt-5.5"].ContextWindow, Is.EqualTo(128000), "A user's value is never replaced.");
                Assert.That(models["gpt-5.5"].ReasoningLevels, Is.EqualTo(new[] { "none", "low", "medium", "high", "xhigh" }));
                Assert.That(models["gpt-6-sol"].ContextWindow, Is.EqualTo(1050000));
                Assert.That(models["gpt-6-sol"].ReasoningLevels, Is.EqualTo(new[] { "high" }));
                Assert.That(models["unknown-model"].ContextWindow, Is.Null);
                Assert.That(models["unknown-model"].ReasoningLevels, Is.Null);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_statusLabel").Text, Does.Contain("models.dev"));
                Assert.That(source.Loads, Is.EqualTo(1));
                Assert.That(requests, Is.EqualTo(new[] { "gui/model/fetch" }), "Presets come from the injected service.");
            }
        }

        [Test]
        public void ManuallyAddedModelIsCompletedWithoutChangingOtherRows()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>()));
            var provider = new AuthenticationProvider
            {
                Id = "relay", Name = "Relay", Kind = "responses", BaseUrl = "https://codex.zxrjy.net:3500/", Model = "gpt-6-sol", Version = "1",
                Models = new List<AuthenticationCatalogModel> { new AuthenticationCatalogModel { Model = "gpt-6-sol" } }
            };
            var source = new FakeCatalogSource(ModelMetadataResolverTests.Catalog());
            var metadata = new AuthenticationModelMetadataService(source, token => Task.FromResult<IReadOnlyList<AuthenticationProviderPreset>>(new AuthenticationProviderPreset[0]));
            using (var form = new AuthenticationProviderEditForm(client, provider, modelMetadata: metadata))
            {
                var control = FormTestDriver.Field<AuthenticationModelsControl>(form, "_models");
                // The fakes complete synchronously, so the whole add handler finishes here.
                control.AddModel(new AuthenticationCatalogModel { Model = "gpt-5.5", DisplayName = "GPT-5.5" });

                var models = control.Models.ToDictionary(model => model.Model);
                Assert.That(models["gpt-5.5"].ContextWindow, Is.EqualTo(1050000));
                Assert.That(models["gpt-5.5"].ReasoningLevels, Is.EqualTo(new[] { "none", "low", "medium", "high", "xhigh" }));
                Assert.That(models["gpt-6-sol"].ContextWindow, Is.Null, "Only the added model is completed.");
                Assert.That(models["gpt-6-sol"].ReasoningLevels, Is.Null);
                Assert.That(FormTestDriver.Field<TextBox>(form, "_statusLabel").Text, Does.Contain("models.dev"));
            }
        }

        [Test]
        public async Task WithoutMetadataServiceRetrievedModelsStayUnchanged()
        {
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
                Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "models", new[] { Model("gpt-5.5") } } }));
            using (var form = new AuthenticationProviderEditForm(client, new AuthenticationProvider { Id = "a", Name = "A", Kind = "responses", BaseUrl = "https://codex.zxrjy.net:3500/", Model = "gpt-5.5", Version = "1" }))
            {
                await form.FetchModelsAsync();
                var model = FormTestDriver.Field<AuthenticationModelsControl>(form, "_models").Models.Single();
                Assert.That(model.ContextWindow, Is.Null);
                Assert.That(model.ReasoningLevels, Is.Null);
            }
        }

        private static Dictionary<string, object> Model(string id)
        {
            return new Dictionary<string, object> { { "model", id }, { "displayName", id } };
        }

        private sealed class FakeCatalogSource : IModelMetadataCatalogSource
        {
            private readonly ModelMetadataCatalog _catalog;
            private ModelMetadataCatalog _cached;

            internal FakeCatalogSource(ModelMetadataCatalog catalog) { _catalog = catalog; }

            internal int Loads { get; private set; }

            public ModelMetadataCatalog Cached => _cached;

            public Task<ModelMetadataCatalog> LoadAsync(CancellationToken cancellationToken)
            {
                Loads++;
                _cached = _catalog;
                return Task.FromResult(_catalog);
            }
        }
    }
}
