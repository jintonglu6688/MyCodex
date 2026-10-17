using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionDefaultsServiceTests
    {
        [Test]
        public void Resolve_UsesObservedAppServerDefaultForNewConversation()
        {
            var resolved = new SessionDefaultsService().Resolve(
                new AppConfigSnapshot(),
                Models(DefaultModel("server-default"), Model("other")),
                null);

            Assert.That(resolved.ModelId, Is.EqualTo("server-default"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(resolved.UsesFirstObservedModelFallback, Is.False);
        }

        [Test]
        public void Resolve_DoesNotInventDefaultFromFirstModel()
        {
            var resolved = new SessionDefaultsService().Resolve(
                new AppConfigSnapshot(),
                Models(Model("first-observed"), Model("second-observed")),
                null);

            Assert.That(resolved.ModelId, Is.Null);
            Assert.That(resolved.UsesFirstObservedModelFallback, Is.False);
        }

        [Test]
        public void Resolve_LeavesReasoningUnsetWhenNeitherUserNorAppServerProvidesIt()
        {
            var resolved = new SessionDefaultsService().Resolve(
                new AppConfigSnapshot(),
                Models(new AppServerModelOption { ModelId = "server-model", IsDefault = true }),
                null);

            Assert.That(resolved.ModelId, Is.EqualTo("server-model"));
            Assert.That(resolved.ReasoningEffort, Is.Null);
        }

        [Test]
        public void Resolve_IgnoresLegacyLocalModelPreferencesButKeepsPermission()
        {
            var config = new AppConfigSnapshot
            {
                RuntimeProfiles = new List<RuntimeProfileConfigSnapshot>
                {
                    new RuntimeProfileConfigSnapshot
                    {
                        RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                        DefaultModelId = "server-default",
                        DefaultReasoningEffort = "high",
                        DefaultApprovalMode = ApprovalMode.ReadOnly
                    }
                }
            };

            var resolved = new SessionDefaultsService().Resolve(
                config,
                Models(DefaultModel("server-default")),
                null);

            Assert.That(resolved.ModelId, Is.EqualTo("server-default"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(resolved.ApprovalMode, Is.EqualTo(ApprovalMode.ReadOnly));
        }

        [Test]
        public void Resolve_UsesOfficialRequirementsReasoningWhenModelHasNoDefault()
        {
            var requirements = new AppServerConfigRequirementsSnapshot
            {
                NewThreadModelDefaults = new AppServerNewThreadModelDefaults
                {
                    ModelId = "server-model",
                    ModelReasoningEffort = "ultra"
                }
            };

            var resolved = new SessionDefaultsService().Resolve(
                new AppConfigSnapshot(),
                Models(new AppServerModelOption { ModelId = "server-model" }),
                requirements);

            Assert.That(resolved.ModelId, Is.EqualTo("server-model"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo("ultra"));
        }

        [Test]
        public void Resolve_FallbackNeverImportsGlobalPermissionAuthority()
        {
            var config = new AppConfigSnapshot
            {
                DefaultApprovalMode = ApprovalMode.FullAccess,
                RuntimeProfiles = new List<RuntimeProfileConfigSnapshot>
                {
                    new RuntimeProfileConfigSnapshot
                    {
                        RuntimeProfileId = RuntimeProfileService.WindowsRuntimeProfileId,
                        DefaultApprovalMode = ApprovalMode.ReadOnly
                    }
                }
            };

            var resolved = new SessionDefaultsService().Resolve(config, Models(Model("first-observed")), null);

            Assert.That(resolved.UsesFirstObservedModelFallback, Is.False);
            Assert.That(resolved.ApprovalMode, Is.EqualTo(ApprovalMode.ReadOnly));
        }

        [Test]
        public void SetDefaults_KeepsWindowsWslAndRemotePreferencesIsolated()
        {
            var service = new SessionDefaultsService();
            var config = new AppConfigSnapshot();
            service.SetModelDefaults(config, "windows-model", "high");

            config.CodexRuntimeSource = CodexRuntimeSources.Wsl;
            config.WslAppServer = new WslAppServerConfigSnapshot
            {
                DistributionName = "Ubuntu-24.04"
            };
            service.SetModelDefaults(config, "wsl-model", "medium");

            config.CodexRuntimeSource = CodexRuntimeSources.RemoteUnixSsh;
            config.SelectedRemoteUnixSshProfileId = "linux-dev";
            config.RemoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>
            {
                new RemoteUnixSshProfileConfigSnapshot { Id = "linux-dev" }
            };
            service.SetModelDefaults(config, "remote-model", "low");

            Assert.That(config.RuntimeProfiles, Has.Count.EqualTo(3));
            Assert.That(config.RuntimeProfiles[0].DefaultModelId, Is.EqualTo("windows-model"));
            Assert.That(config.RuntimeProfiles[1].RuntimeProfileId, Is.EqualTo("wsl|ubuntu-24.04"));
            Assert.That(config.RuntimeProfiles[1].DefaultModelId, Is.EqualTo("wsl-model"));
            Assert.That(config.RuntimeProfiles[2].RuntimeProfileId, Is.EqualTo("remote-unix|linux-dev"));
            Assert.That(config.RuntimeProfiles[2].DefaultModelId, Is.EqualTo("remote-model"));
        }

        [Test]
        public void IsExplicitConversationModelAvailable_BlocksOnlyUnavailableOverrides()
        {
            var observedModels = Models(Model("available"));

            Assert.That(SessionDefaultsService.IsExplicitConversationModelAvailable(null, observedModels), Is.True);
            Assert.That(SessionDefaultsService.IsExplicitConversationModelAvailable("available", observedModels), Is.True);
            Assert.That(SessionDefaultsService.IsExplicitConversationModelAvailable("removed", observedModels), Is.False);
        }

        [TestCase("windows")]
        [TestCase("wsl|ubuntu")]
        [TestCase("remote-unix|mac")]
        [TestCase("remote-unix|linux")]
        public void Resolve_UsesOfficialModelAndEffortEvenOutsideCatalog(string scope)
        {
            var config = new AppConfigSnapshot();
            config.RuntimeProfiles.Add(new RuntimeProfileConfigSnapshot
            {
                RuntimeProfileId = scope, DefaultModelId = "recommended", DefaultReasoningEffort = "low"
            });
            var official = new AppServerConfigSnapshot { ModelId = "custom-model", ModelReasoningEffort = "high" };
            var resolved = new SessionDefaultsService().Resolve(config, Models(DefaultModel("recommended")), null, official);
            Assert.That(resolved.ModelId, Is.EqualTo("custom-model"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(config.RuntimeProfiles[0].DefaultModelId, Is.EqualTo("recommended"), "Reading must not migrate or write preferences.");
        }

        [Test]
        public void ResolveSession_FollowsChangedOfficialDefaultsUnlessExplicitlyOverridden()
        {
            var service = new SessionDefaultsService();
            var models = Models(DefaultModel("recommended"), Model("override"));
            var official = new AppServerConfigSnapshot { ModelId = "configured", ModelReasoningEffort = "high" };
            var defaults = service.Resolve(new AppConfigSnapshot(), models, null, official);
            Assert.That(SessionDefaultsService.ResolveSession(defaults, new ChatSession(), models).ModelId, Is.EqualTo("configured"));
            official.ModelId = "switched";
            official.ModelReasoningEffort = "low";
            defaults = service.Resolve(new AppConfigSnapshot(), models, null, official);
            var following = SessionDefaultsService.ResolveSession(defaults, new ChatSession(), models);
            Assert.That(following.ModelId, Is.EqualTo("switched"));
            Assert.That(following.ReasoningEffort, Is.EqualTo("low"));
            var overridden = SessionDefaultsService.ResolveSession(defaults, new ChatSession { ModelId = "override" }, models);
            Assert.That(overridden.ModelId, Is.EqualTo("override"));
            Assert.That(overridden.ReasoningEffort, Is.EqualTo("medium"), "Do not inherit another model's effort.");
            overridden = SessionDefaultsService.ResolveSession(defaults,
                new ChatSession { ModelId = "override", ReasoningEffort = "xhigh" }, models);
            Assert.That(overridden.ReasoningEffort, Is.EqualTo("xhigh"));
        }

        [Test]
        public void Resolve_ManagedDefaultsTakePrecedenceWithoutRequiringCatalogEntry()
        {
            var resolved = new SessionDefaultsService().Resolve(new AppConfigSnapshot(), Models(DefaultModel("recommended")),
                new AppServerConfigRequirementsSnapshot
                {
                    NewThreadModelDefaults = new AppServerNewThreadModelDefaults { ModelId = "managed", ModelReasoningEffort = "high" }
                }, new AppServerConfigSnapshot { ModelId = "configured", ModelReasoningEffort = "low" });
            Assert.That(resolved.ModelId, Is.EqualTo("managed"));
            Assert.That(resolved.ReasoningEffort, Is.EqualTo("high"));
        }

        [Test]
        public void Resolve_AfterProviderSwitch_DropsUnsupportedInheritedEffort()
        {
            var service = new SessionDefaultsService();
            var official = new AppServerConfigSnapshot { ModelId = "gpt-6-sol", ModelReasoningEffort = "high" };
            var original = DefaultModel("gpt-6-sol");
            original.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "high" });
            Assert.That(service.Resolve(new AppConfigSnapshot(), Models(original), null, official).ReasoningEffort,
                Is.EqualTo("high"));

            var replacement = new AppServerModelOption { ModelId = "qwen-3.8-max", DefaultReasoningEffort = "low" };
            replacement.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "low" });
            official.ModelId = replacement.ModelId;
            var defaults = service.Resolve(new AppConfigSnapshot(), Models(replacement), null, official);

            Assert.That(defaults.ReasoningEffort, Is.EqualTo("low"));
            Assert.That(SessionDefaultsService.ResolveSession(defaults, new ChatSession(), Models(replacement)).ReasoningEffort,
                Is.EqualTo("low"));
            Assert.That(official.ModelReasoningEffort, Is.EqualTo("high"), "Resolving effective settings must not rewrite shared preferences.");
            official.ModelId = original.ModelId;
            Assert.That(service.Resolve(new AppConfigSnapshot(), Models(original), null, official).ReasoningEffort,
                Is.EqualTo("high"), "Switching back must retain a supported preference.");
        }

        [TestCase("high", "low", "low")]
        [TestCase("low", "none", "low")]
        [TestCase("high", "high", "none")]
        [TestCase("high", null, "none")]
        [TestCase(null, "low", "low")]
        [TestCase(null, null, null)]
        [TestCase("LOW", "none", "low")]
        public void Resolve_UsesOnlyReportedEffortsWhenCatalogDeclaresThem(string configured, string recommended, string expected)
        {
            var model = new AppServerModelOption { ModelId = "provider-model", DefaultReasoningEffort = recommended };
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "none" });
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "low" });
            var resolved = new SessionDefaultsService().Resolve(new AppConfigSnapshot(), Models(model), null,
                new AppServerConfigSnapshot { ModelId = model.ModelId, ModelReasoningEffort = configured });
            Assert.That(resolved.ReasoningEffort, Is.EqualTo(expected));
        }

        private static List<AppServerModelOption> Models(params AppServerModelOption[] models)
        {
            return new List<AppServerModelOption>(models);
        }

        private static AppServerModelOption DefaultModel(string modelId)
        {
            var result = Model(modelId);
            result.IsDefault = true;
            return result;
        }

        private static AppServerModelOption Model(string modelId)
        {
            return new AppServerModelOption
            {
                ModelId = modelId,
                DisplayName = modelId,
                DefaultReasoningEffort = "medium"
            };
        }
    }
}
