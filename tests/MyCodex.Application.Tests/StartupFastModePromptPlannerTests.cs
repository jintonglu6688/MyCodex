using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class StartupFastModePromptPlannerTests
    {
        [TestCase("fast", true)]
        [TestCase(" FAST ", true)]
        [TestCase("priority", true)]
        [TestCase(" PRIORITY ", true)]
        [TestCase("flex", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void ShouldPrompt_ReturnsTrueOnlyForFastServiceTier(string serviceTier, bool expected)
        {
            var snapshot = new AppServerConfigSnapshot
            {
                ServiceTier = serviceTier
            };

            Assert.That(StartupFastModePromptPlanner.ShouldPrompt(snapshot), Is.EqualTo(expected));
        }

        [Test]
        public void BuildDisableFastModeEdits_RemovesOnlyServiceTierOverride()
        {
            var snapshot = new AppServerConfigSnapshot { ServiceTier = "priority" };

            var edit = StartupFastModePromptPlanner.BuildDisableFastModeEdits(snapshot).Single();

            Assert.That(edit.KeyPath, Is.EqualTo("service_tier"));
            Assert.That(edit.MergeStrategy, Is.EqualTo("replace"));
            Assert.That(edit.Value, Is.Null);
        }
    }
}
