using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficePluginOnlyHostTests
    {
        [Test]
        public void Program_DoesNotRegisterOfficeInternalToolExecutorByDefault()
        {
            var source = RepositorySource.ReadSourceFile("Program.cs");

            Assert.That(source, Does.Contain("new InternalToolRegistry(new IInternalToolExecutor[0])"));
            Assert.That(source, Does.Not.Contain("new OfficeDocumentInternalToolExecutor"));
            Assert.That(source, Does.Not.Contain("new OfficeDocumentEngine"));
        }

        [Test]
        public void MainForm_DoesNotBuildInternalToolContextForOfficeSessions()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.InternalTools.cs");

            Assert.That(source, Does.Contain("session.Mode == AppInteractionMode.Office"));
            Assert.That(source, Does.Contain("return null;"));
            Assert.That(source, Does.Contain("InternalToolThreadContextBuilder.BuildForSession"));
            Assert.That(source, Does.Not.Contain("OfficialOfficeCapabilityEnabled"));
        }

        [Test]
        public void OfficePromptGuidanceUsesCapabilityFirstToolSelection()
        {
            var source = RepositorySource.ReadSourceFile("OfficeDeveloperInstructionComposer.cs");

            Assert.That(source, Does.Contain("OfficeCapabilityPackage.CreateDefault()"));
            Assert.That(source, Does.Contain("selected Skill, plugin, MCP tool, or CLI tool"));
            Assert.That(source, Does.Not.Contain("local Office automation"));
            Assert.That(source, Does.Not.Contain("Office automation surface"));
            Assert.That(source, Does.Not.Contain("legacy MyCodex internal slash-style Office tool"));
            Assert.That(source, Does.Not.Contain("office/create_document"));
            Assert.That(source, Does.Not.Contain("office/read_document"));
            Assert.That(source, Does.Not.Contain("office/edit_document"));
            Assert.That(source, Does.Not.Contain("Prefer mycodex-office"));
            Assert.That(source, Does.Not.Contain("MyCodex internal Office document tools"));
        }
    }
}
