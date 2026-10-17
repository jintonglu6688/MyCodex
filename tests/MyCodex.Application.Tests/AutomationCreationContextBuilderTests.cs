using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationCreationContextBuilderTests
    {
        [Test]
        public void ForProjectWorkspace_BuildsProjectCodingContext()
        {
            var workspace = new WorkspaceInfo
            {
                Id = "project-workspace",
                Name = "Project",
                RootPath = @"E:\Project",
                Kind = WorkspaceKind.Coding
            };

            var context = AutomationCreationContextBuilder.ForProjectWorkspace(
                workspace,
                "wsl|ubuntu",
                "gpt-5.5",
                "high",
                ApprovalMode.Auto);

            Assert.That(context.Kind, Is.EqualTo(AutomationKind.Project));
            Assert.That(context.InteractionMode, Is.EqualTo(AppInteractionMode.Coding));
            Assert.That(context.WorkspaceId, Is.EqualTo("project-workspace"));
            Assert.That(context.WorkspacePath, Is.EqualTo(@"E:\Project"));
            Assert.That(context.RuntimeProfileId, Is.EqualTo("wsl|ubuntu"));
            Assert.That(context.ModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(context.ReasoningEffort, Is.EqualTo("high"));
            Assert.That(context.ApprovalMode, Is.EqualTo(ApprovalMode.Auto));
        }

        [Test]
        public void ForOfficeWorkspace_BuildsOfficeContextUsingActualRuntimeAndOfficeScope()
        {
            var workspace = new WorkspaceInfo
            {
                Id = "office-reports",
                Name = "Reports",
                RootPath = @"E:\Documents\Reports",
                Kind = WorkspaceKind.Office
            };

            var context = AutomationCreationContextBuilder.ForOfficeWorkspace(
                workspace,
                "/home/alice/office",
                "windows",
                "wsl|ubuntu",
                "gpt-5.5",
                "medium",
                ApprovalMode.ReadOnly);

            Assert.That(context.Kind, Is.EqualTo(AutomationKind.Office));
            Assert.That(context.InteractionMode, Is.EqualTo(AppInteractionMode.Office));
            Assert.That(context.WorkspaceId, Is.EqualTo("office-reports"));
            Assert.That(context.WorkspacePath, Is.EqualTo(@"E:\Documents\Reports"));
            Assert.That(context.OfficeRootPath, Is.EqualTo("/home/alice/office"));
            Assert.That(context.RuntimeProfileId, Is.EqualTo("windows"));
            Assert.That(context.OfficeRuntimeScopeId, Is.EqualTo("wsl|ubuntu"));
            Assert.That(context.ModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(context.ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(context.ApprovalMode, Is.EqualTo(ApprovalMode.ReadOnly));
        }

        [Test]
        public void ForThread_PreservesThreadContext()
        {
            var context = AutomationCreationContextBuilder.ForThread(
                "thread-1",
                AppInteractionMode.Coding,
                "windows",
                "workspace-1",
                @"E:\Project",
                "gpt-5.5",
                "low",
                ApprovalMode.FullAccess);

            Assert.That(context.Kind, Is.EqualTo(AutomationKind.Thread));
            Assert.That(context.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(context.InteractionMode, Is.EqualTo(AppInteractionMode.Coding));
            Assert.That(context.RuntimeProfileId, Is.EqualTo("windows"));
            Assert.That(context.WorkspaceId, Is.EqualTo("workspace-1"));
            Assert.That(context.WorkspacePath, Is.EqualTo(@"E:\Project"));
            Assert.That(context.ModelId, Is.EqualTo("gpt-5.5"));
            Assert.That(context.ReasoningEffort, Is.EqualTo("low"));
            Assert.That(context.ApprovalMode, Is.EqualTo(ApprovalMode.FullAccess));
        }
    }
}
