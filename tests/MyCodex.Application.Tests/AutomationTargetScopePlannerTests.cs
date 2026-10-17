using System;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationTargetScopePlannerTests
    {
        [TestCase(@"C:\project", false, true)]
        [TestCase("C:/project", false, true)]
        [TestCase(@"\\server\share\project", false, true)]
        [TestCase(@"\\wsldata\share\project", false, true)]
        [TestCase(@"\\wsl$\Debian\project", false, false)]
        [TestCase(@"\\wsl.localhost\Debian\project", false, false)]
        [TestCase("/srv/project", false, false)]
        [TestCase("/srv/project", true, true)]
        [TestCase(@"C:\project", true, false)]
        [TestCase(@"\\server\share\project", true, false)]
        [TestCase("relative/project", true, false)]
        [TestCase("relative/project", false, false)]
        [TestCase("", true, false)]
        [TestCase(null, false, false)]
        [TestCase("/srv/bad\nproject", true, false)]
        [TestCase("/srv/project\r\n", true, false)]
        [TestCase("\tC:\\project", false, false)]
        [TestCase("C:\\bad\0project", false, false)]
        public void CompatiblePath_RespectsHostBoundariesAndRejectsControlCharacters(string path, bool unix, bool accepted)
        {
            Assert.That(AutomationTargetScopePlanner.IsCompatiblePath(path, unix), Is.EqualTo(accepted));
        }

        [TestCase(AppInteractionMode.Coding)]
        [TestCase(AppInteractionMode.Office)]
        public void OfficeOrCodingScope_IsRequiredAndDoesNotFallBackToTheOtherModesScope(AppInteractionMode mode)
        {
            var planner = new AutomationTargetScopePlanner("wsl|debian", true);
            var definition = new AutomationDefinitionSnapshot
            {
                Context = new AutomationContextSnapshot
                {
                    InteractionMode = mode, RuntimeProfileId = mode == AppInteractionMode.Coding ? "WSL|DEBIAN" : "other",
                    OfficeRuntimeScopeId = mode == AppInteractionMode.Office ? "WSL|DEBIAN" : "other",
                    WorkspacePath = "/srv/project", OfficeRootPath = mode == AppInteractionMode.Office ? "/srv/documents" : null
                }
            };
            Assert.That(planner.Matches(definition), Is.True);
            if (mode == AppInteractionMode.Coding)
            {
                definition.Context.RuntimeProfileId = null;
                definition.Context.OfficeRuntimeScopeId = "wsl|debian";
            }
            else
            {
                definition.Context.OfficeRuntimeScopeId = null;
                definition.Context.RuntimeProfileId = "wsl|debian";
            }
            Assert.That(planner.Matches(definition), Is.False);
            Assert.Throws<InvalidOperationException>(() => planner.Validate(definition));
        }

        [Test]
        public void MissingContextOrForeignOfficeRoot_CannotBecomeAnOwnedAutomation()
        {
            var planner = new AutomationTargetScopePlanner("remote-unix|build", true);
            Assert.That(planner.Matches(null), Is.False);
            Assert.That(planner.Matches(new AutomationDefinitionSnapshot { Context = null }), Is.False);
            Assert.That(planner.Matches(new AutomationDefinitionSnapshot
            {
                Context = new AutomationContextSnapshot
                {
                    InteractionMode = AppInteractionMode.Office, OfficeRuntimeScopeId = "remote-unix|build",
                    WorkspacePath = "/home/dev/reports", OfficeRootPath = @"C:\documents"
                }
            }), Is.False);
        }
    }
}
