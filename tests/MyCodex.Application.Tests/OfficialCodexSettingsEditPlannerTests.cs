using System;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficialCodexSettingsEditPlannerTests
    {
        [Test]
        public void ReadUserSettings_ReturnsOnlyValuesOwnedByUserLayer()
        {
            var snapshot = new AppServerConfigSnapshot
            {
                ServiceTier = "priority",
                WebSearchMode = "cached"
            };
            snapshot.Origins["service_tier"] = new AppServerConfigOrigin { SourceType = "mdm" };
            snapshot.Origins["web_search"] = new AppServerConfigOrigin { SourceType = "user" };

            var result = OfficialCodexSettingsEditPlanner.ReadUserSettings(snapshot);

            Assert.That(result.ServiceTier, Is.Null);
            Assert.That(result.WebSearchMode, Is.EqualTo("cached"));
        }

        [Test]
        public void BuildEdits_DoesNotClearEffectiveValueFromNonUserLayer()
        {
            var snapshot = new AppServerConfigSnapshot { ProjectDocMaxBytes = 32768L };

            var edits = OfficialCodexSettingsEditPlanner.BuildEdits(
                snapshot,
                new OfficialCodexSettingsSelection());

            Assert.That(edits, Is.Empty);
        }

        [Test]
        public void BuildEdits_ClearsUserOverrideWhenFollowDefaultIsSelected()
        {
            var snapshot = new AppServerConfigSnapshot { ProjectDocMaxBytes = 65536L };
            snapshot.Origins["project_doc_max_bytes"] = new AppServerConfigOrigin { SourceType = "user" };

            var edits = OfficialCodexSettingsEditPlanner.BuildEdits(
                snapshot,
                new OfficialCodexSettingsSelection());

            Assert.That(edits, Has.Count.EqualTo(1));
            Assert.That(edits[0].KeyPath, Is.EqualTo("project_doc_max_bytes"));
            Assert.That(edits[0].Value, Is.Null);
        }

        [Test]
        public void BuildEdits_EmitsSupportedChangedValuesInProtocolOrder()
        {
            var requested = new OfficialCodexSettingsSelection
            {
                ModelReasoningSummary = "auto",
                ModelVerbosity = "medium",
                ServiceTier = "priority",
                WebSearchMode = "indexed",
                WebSearchContextSize = "high",
                ViewImageToolEnabled = true,
                ToolOutputTokenLimit = 16384L,
                ProjectDocMaxBytes = 131072L
            };

            var edits = OfficialCodexSettingsEditPlanner.BuildEdits(
                new AppServerConfigSnapshot(),
                requested);

            Assert.That(
                edits.Select(edit => edit.KeyPath),
                Is.EqualTo(new[]
                {
                    "model_reasoning_summary",
                    "model_verbosity",
                    "service_tier",
                    "web_search",
                    "tools.web_search.context_size",
                    "tools.view_image",
                    "tool_output_token_limit",
                    "project_doc_max_bytes"
                }));
        }

        [Test]
        public void BuildEdits_RejectsUnsupportedValues()
        {
            Assert.That(
                () => OfficialCodexSettingsEditPlanner.BuildEdits(
                    new AppServerConfigSnapshot(),
                    new OfficialCodexSettingsSelection { WebSearchMode = "future" }),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => OfficialCodexSettingsEditPlanner.BuildEdits(
                    new AppServerConfigSnapshot(),
                    new OfficialCodexSettingsSelection { ModelReasoningSummary = "AUTO" }),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void BuildEdits_PreservesExistingFutureOrCustomValues()
        {
            var snapshot = new AppServerConfigSnapshot
            {
                ServiceTier = "flex",
                ToolOutputTokenLimit = 4096L
            };
            snapshot.Origins["service_tier"] = new AppServerConfigOrigin { SourceType = "user" };
            snapshot.Origins["tool_output_token_limit"] = new AppServerConfigOrigin { SourceType = "user" };

            var edits = OfficialCodexSettingsEditPlanner.BuildEdits(
                snapshot,
                new OfficialCodexSettingsSelection
                {
                    ServiceTier = "flex",
                    ToolOutputTokenLimit = 4096L
                });

            Assert.That(edits, Is.Empty);
        }
    }
}
