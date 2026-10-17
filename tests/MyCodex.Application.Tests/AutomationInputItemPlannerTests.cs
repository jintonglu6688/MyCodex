using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationInputItemPlannerTests
    {
        [Test]
        public void Build_MapsSkillPluginAndPromptInOrder()
        {
            var planner = new AutomationInputItemPlanner(path => true);
            var definition = new AutomationDefinitionSnapshot
            {
                Prompt = "Summarize news.",
                InputItems = new List<AutomationInputItemSnapshot>
                {
                    new AutomationInputItemSnapshot
                    {
                        Type = AutomationInputItemKind.Skill,
                        Name = "news-digest",
                        DisplayName = "News Digest",
                        Path = @"E:\skills\news-digest"
                    },
                    new AutomationInputItemSnapshot
                    {
                        Type = AutomationInputItemKind.Plugin,
                        Name = "mycodex-office",
                        DisplayName = "MyCodex Office",
                        Path = @"E:\plugins\mycodex-office"
                    }
                }
            };

            var plan = planner.Build(definition);

            Assert.That(plan.Succeeded, Is.True);
            Assert.That(plan.ErrorMessage, Is.Null);
            Assert.That(plan.InputItems, Has.Count.EqualTo(3));
            Assert.That(plan.InputItems[0].Type, Is.EqualTo("skill"));
            Assert.That(plan.InputItems[0].Name, Is.EqualTo("news-digest"));
            Assert.That(plan.InputItems[0].Path, Is.EqualTo(@"E:\skills\news-digest"));
            Assert.That(plan.InputItems[1].Type, Is.EqualTo("mention"));
            Assert.That(plan.InputItems[1].Name, Is.EqualTo("mycodex-office"));
            Assert.That(plan.InputItems[1].Path, Is.EqualTo(@"E:\plugins\mycodex-office"));
            Assert.That(plan.InputItems[2].Type, Is.EqualTo("text"));
            Assert.That(plan.InputItems[2].Text, Is.EqualTo("Summarize news."));
        }

        [Test]
        public void Build_UsesDisplayNameWhenNameIsMissing()
        {
            var planner = new AutomationInputItemPlanner(path => true);
            var definition = new AutomationDefinitionSnapshot
            {
                Prompt = "Run it.",
                InputItems = new List<AutomationInputItemSnapshot>
                {
                    new AutomationInputItemSnapshot
                    {
                        Type = AutomationInputItemKind.Skill,
                        DisplayName = "News Digest",
                        Path = @"E:\skills\news-digest"
                    }
                }
            };

            var plan = planner.Build(definition);

            Assert.That(plan.Succeeded, Is.True);
            Assert.That(plan.InputItems[0].Name, Is.EqualTo("News Digest"));
        }

        [Test]
        public void Build_FailsWhenPathIsMissing()
        {
            var planner = new AutomationInputItemPlanner(path => true);
            var definition = new AutomationDefinitionSnapshot
            {
                Prompt = "Run it.",
                InputItems = new List<AutomationInputItemSnapshot>
                {
                    new AutomationInputItemSnapshot
                    {
                        Type = AutomationInputItemKind.Plugin,
                        Name = "mycodex-office",
                        DisplayName = "MyCodex Office",
                        Path = " "
                    }
                }
            };

            var plan = planner.Build(definition);

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.InputItems, Is.Empty);
            Assert.That(plan.ErrorMessage, Does.Contain("mycodex-office"));
        }

        [Test]
        public void Build_FailsWhenPathDoesNotExist()
        {
            var planner = new AutomationInputItemPlanner(path => false);
            var definition = new AutomationDefinitionSnapshot
            {
                Prompt = "Run it.",
                InputItems = new List<AutomationInputItemSnapshot>
                {
                    new AutomationInputItemSnapshot
                    {
                        Type = AutomationInputItemKind.Skill,
                        Name = "news-digest",
                        DisplayName = "News Digest",
                        Path = @"E:\missing\news-digest"
                    }
                }
            };

            var plan = planner.Build(definition);

            Assert.That(plan.Succeeded, Is.False);
            Assert.That(plan.InputItems.Any(), Is.False);
            Assert.That(plan.ErrorMessage, Does.Contain("news-digest"));
        }
    }
}
