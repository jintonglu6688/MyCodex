using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerTurnPayloadMapperTests
    {
        [Test]
        public void BuildTurnPlanSteps_UsesFallbackTextFieldsAndSkipsEmptySteps()
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "plan",
                    new object[]
                    {
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "title", "Inspect code" },
                            { "status", "completed" }
                        },
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "content", "Run tests" },
                            { "status", "in_progress" }
                        },
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "status", "pending" }
                        }
                    }
                }
            };

            var steps = AppServerTurnPayloadMapper.BuildTurnPlanSteps(parameters);

            Assert.That(steps, Has.Count.EqualTo(2));
            Assert.That(steps[0].Step, Is.EqualTo("Inspect code"));
            Assert.That(steps[0].Status, Is.EqualTo("completed"));
            Assert.That(steps[1].Step, Is.EqualTo("Run tests"));
            Assert.That(steps[1].Status, Is.EqualTo("in_progress"));
        }

        [Test]
        public void ExtractPlanDeltaText_ReadsNestedItemWhenTopLevelTextIsMissing()
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "item",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "content", "Nested plan delta" }
                    }
                }
            };

            var result = AppServerTurnPayloadMapper.ExtractPlanDeltaText(parameters);

            Assert.That(result, Is.EqualTo("Nested plan delta"));
        }

        [Test]
        public void ExtractDiffText_ReadsNestedDiffObjectWhenTopLevelTextIsMissing()
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "diff",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "unifiedDiff", "diff --git a/a.txt b/a.txt" }
                    }
                }
            };

            var result = AppServerTurnPayloadMapper.ExtractDiffText(parameters);

            Assert.That(result, Is.EqualTo("diff --git a/a.txt b/a.txt"));
        }

        [Test]
        public void ExtractChangedFiles_UsesExplicitFilesAndNormalizesDuplicates()
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "changedFiles",
                    new object[]
                    {
                        "a/src/App.cs",
                        "b/src/App.cs",
                        "/dev/null",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "path", "src/Util.cs" }
                        }
                    }
                }
            };

            var result = AppServerTurnPayloadMapper.ExtractChangedFiles(parameters, "diff --git a/fallback.cs b/fallback.cs");

            Assert.That(result, Is.EqualTo(new[] { "src/App.cs", "src/Util.cs" }));
        }

        [Test]
        public void ExtractChangedFiles_FallsBackToUnifiedDiffHeaders()
        {
            var diffText = string.Join(
                "\n",
                "diff --git a/src/App.cs b/src/App.cs",
                "--- a/src/App.cs",
                "+++ b/src/App.cs",
                "diff --git a/src/Old.cs b/src/New.cs",
                "--- /dev/null",
                "+++ b/src/New.cs");

            var result = AppServerTurnPayloadMapper.ExtractChangedFiles(
                new Dictionary<string, object>(StringComparer.Ordinal),
                diffText);

            Assert.That(result, Is.EqualTo(new[] { "src/App.cs", "src/New.cs" }));
        }

        [Test]
        public void NormalizeChangedFilePath_IgnoresDevNullAndStripsDiffPrefixes()
        {
            Assert.That(AppServerTurnPayloadMapper.NormalizeChangedFilePath(" a/src/App.cs "), Is.EqualTo("src/App.cs"));
            Assert.That(AppServerTurnPayloadMapper.NormalizeChangedFilePath("b/src/App.cs"), Is.EqualTo("src/App.cs"));
            Assert.That(AppServerTurnPayloadMapper.NormalizeChangedFilePath("/dev/null"), Is.Null);
            Assert.That(AppServerTurnPayloadMapper.NormalizeChangedFilePath("   "), Is.Null);
        }
    }
}
