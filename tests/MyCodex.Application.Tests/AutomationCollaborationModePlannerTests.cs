using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationCollaborationModePlannerTests
    {
        [Test]
        public void Build_ForProjectAutomation_ReturnsNull()
        {
            var planner = new AutomationCollaborationModePlanner();
            var definition = new AutomationDefinitionSnapshot
            {
                Kind = AutomationKind.Project,
                ModelId = "gpt-5.5",
                ReasoningEffort = "high",
                Context = new AutomationContextSnapshot
                {
                    WorkspacePath = @"E:\Project"
                }
            };

            var collaborationMode = planner.Build(definition);

            Assert.That(collaborationMode, Is.Null);
        }

        [Test]
        public void Build_ForOfficeAutomation_ReturnsDefaultModeWithOfficeInstructions()
        {
            var planner = new AutomationCollaborationModePlanner();
            var definition = new AutomationDefinitionSnapshot
            {
                Kind = AutomationKind.Office,
                ModelId = "gpt-5.5",
                ReasoningEffort = "medium",
                Context = new AutomationContextSnapshot
                {
                    OfficeRootPath = @"E:\Documents",
                    WorkspacePath = @"E:\Documents\Reports"
                }
            };

            var collaborationMode = planner.Build(definition);

            Assert.That(collaborationMode, Is.Not.Null);
            Assert.That(collaborationMode.Mode, Is.EqualTo("default"));
            Assert.That(collaborationMode.ModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(collaborationMode.ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(collaborationMode.DeveloperInstructions, Does.Contain(@"E:\Documents"));
            Assert.That(collaborationMode.DeveloperInstructions, Does.Contain(@"E:\Documents\Reports"));
            Assert.That(collaborationMode.DeveloperInstructions, Does.Contain("MyCodex Office Mode"));
            Assert.That(collaborationMode.DeveloperInstructions, Does.Not.Contain("official MyCodex Office capability is disabled"));
        }
    }
}
