using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerConfigRequirementsMapperTests
    {
        [Test]
        public void BuildRequirements_ParsesConfiguredRequirements()
        {
            var result = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "requirements",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "allowedApprovalPolicies", new object[] { "on-request", new Dictionary<string, object> { { "granular", new Dictionary<string, object>() } } } },
                        { "allowedSandboxModes", new object[] { "read-only", "workspace-write" } },
                        { "allowedWebSearchModes", new object[] { "disabled", "live" } },
                        {
                            "featureRequirements",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "browser_use", true },
                                { "js_repl", "false" }
                            }
                        },
                        { "enforceResidency", "us" }
                    }
                }
            };

            var requirements = AppServerConfigRequirementsMapper.BuildRequirements(result);

            Assert.That(requirements.HasRequirements, Is.True);
            Assert.That(requirements.AllowedApprovalPolicies, Is.EqualTo(new[] { "on-request", "granular" }));
            Assert.That(requirements.AllowedSandboxModes, Is.EqualTo(new[] { "read-only", "workspace-write" }));
            Assert.That(requirements.AllowedWebSearchModes, Is.EqualTo(new[] { "disabled", "live" }));
            Assert.That(requirements.FeatureRequirements["browser_use"], Is.True);
            Assert.That(requirements.FeatureRequirements["js_repl"], Is.False);
            Assert.That(requirements.EnforceResidency, Is.EqualTo("us"));
        }

        [Test]
        public void BuildRequirements_ToleratesMissingRequirements()
        {
            var requirements = AppServerConfigRequirementsMapper.BuildRequirements(new Dictionary<string, object>(StringComparer.Ordinal));

            Assert.That(requirements.HasRequirements, Is.False);
            Assert.That(requirements.AllowedApprovalPolicies, Is.Empty);
            Assert.That(requirements.AllowedSandboxModes, Is.Empty);
            Assert.That(requirements.AllowedWebSearchModes, Is.Empty);
            Assert.That(requirements.FeatureRequirements, Is.Empty);
            Assert.That(requirements.NewThreadModelDefaults, Is.Null);
        }

        [Test]
        public void BuildRequirements_MapsManagedNewThreadModelDefaults()
        {
            var requirements = AppServerConfigRequirementsMapper.BuildRequirements(
                BuildRequirementsWithModels(
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "newThread",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "model", "gpt-5.6" },
                                { "modelReasoningEffort", "high" },
                                { "serviceTier", "fast" }
                            }
                        }
                    }));

            Assert.That(requirements.NewThreadModelDefaults, Is.Not.Null);
            Assert.That(requirements.NewThreadModelDefaults.ModelId, Is.EqualTo("gpt-5.6"));
            Assert.That(requirements.NewThreadModelDefaults.ModelReasoningEffort, Is.EqualTo("high"));
            Assert.That(requirements.NewThreadModelDefaults.ServiceTier, Is.EqualTo("fast"));
        }

        [Test]
        public void BuildRequirements_LeavesNewThreadDefaultsNullWhenModelsAreAbsent()
        {
            var requirements = AppServerConfigRequirementsMapper.BuildRequirements(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "requirements", new Dictionary<string, object>(StringComparer.Ordinal) }
                });

            Assert.That(requirements.NewThreadModelDefaults, Is.Null);
        }

        [Test]
        public void BuildRequirements_LeavesNewThreadDefaultsNullWhenNewThreadIsAbsent()
        {
            var requirements = AppServerConfigRequirementsMapper.BuildRequirements(
                BuildRequirementsWithModels(new Dictionary<string, object>(StringComparer.Ordinal)));

            Assert.That(requirements.NewThreadModelDefaults, Is.Null);
        }

        [Test]
        public void BuildRequirements_MapsPartiallyPopulatedNewThreadDefaults()
        {
            var requirements = AppServerConfigRequirementsMapper.BuildRequirements(
                BuildRequirementsWithModels(
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "newThread",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "model", "gpt-5.6" }
                            }
                        }
                    }));

            Assert.That(requirements.NewThreadModelDefaults, Is.Not.Null);
            Assert.That(requirements.NewThreadModelDefaults.ModelId, Is.EqualTo("gpt-5.6"));
            Assert.That(requirements.NewThreadModelDefaults.ModelReasoningEffort, Is.Null);
            Assert.That(requirements.NewThreadModelDefaults.ServiceTier, Is.Null);
        }

        private static IDictionary<string, object> BuildRequirementsWithModels(IDictionary<string, object> models)
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "requirements",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "models", models }
                    }
                }
            };
        }
    }
}
