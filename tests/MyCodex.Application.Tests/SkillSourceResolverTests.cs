using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Skills;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class SkillSourceResolverTests
    {
        [Test]
        public void Resolve_UsesPluginSourcePath_WhenSkillIsUnderPlugin()
        {
            var skill = new AppServerSkillInfo
            {
                Path = @"C:\Users\Alice\.codex\plugins\cache\openai-curated\superpowers\dc902811\skills\brainstorming",
                Scope = "plugin"
            };
            var plugins = new[]
            {
                new AppServerPluginSummary
                {
                    Name = "superpowers",
                    DisplayName = "Superpowers",
                    MarketplaceName = "OpenAI Curated",
                    SourcePath = @"C:\Users\Alice\.codex\plugins\cache\openai-curated\superpowers\dc902811"
                }
            };

            var source = SkillSourceResolver.Resolve(skill, @"E:\Repo", plugins);

            Assert.That(source.Kind, Is.EqualTo(SkillSourceKind.Plugin));
            Assert.That(source.DisplayText, Does.Contain("Superpowers"));
            Assert.That(source.DisplayText, Does.Contain("OpenAI Curated"));
            Assert.That(source.PluginDisplayName, Is.EqualTo("Superpowers"));
            Assert.That(source.MarketplaceName, Is.EqualTo("OpenAI Curated"));
        }

        [Test]
        public void Resolve_InfersPluginFromCachePath_WhenPluginSummaryIsUnavailable()
        {
            var skill = new AppServerSkillInfo
            {
                Path = "/Users/alice/.codex/plugins/cache/openai-primary-runtime/documents/26.430.10722/skills/documents",
                Scope = "plugin"
            };

            var source = SkillSourceResolver.Resolve(skill, "/Users/alice/work/repo", null);

            Assert.That(source.Kind, Is.EqualTo(SkillSourceKind.Plugin));
            Assert.That(source.DisplayText, Does.Contain("documents"));
            Assert.That(source.DisplayText, Does.Contain("openai-primary-runtime"));
            Assert.That(source.PluginDisplayName, Is.EqualTo("documents"));
            Assert.That(source.MarketplaceName, Is.EqualTo("openai-primary-runtime"));
        }

        [Test]
        public void Resolve_DetectsBuiltInSystemSkillFromCodexSystemPath()
        {
            var skill = new AppServerSkillInfo
            {
                Path = @"C:\Users\Alice\.codex\skills\.system\imagegen",
                Scope = "system"
            };

            var source = SkillSourceResolver.Resolve(skill, @"E:\Repo", null);

            Assert.That(source.Kind, Is.EqualTo(SkillSourceKind.System));
            Assert.That(source.DisplayText, Is.Not.Empty);
        }

        [Test]
        public void Resolve_DetectsWorkspaceSkillFromWorkspacePath()
        {
            var skill = new AppServerSkillInfo
            {
                Path = @"E:\Repo\.codex\skills\repo-helper",
                Scope = "repo"
            };

            var source = SkillSourceResolver.Resolve(skill, @"E:\Repo", new List<AppServerPluginSummary>());

            Assert.That(source.Kind, Is.EqualTo(SkillSourceKind.Workspace));
            Assert.That(source.DisplayText, Is.Not.Empty);
        }
    }
}
