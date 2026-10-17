using System.Reflection;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class OfficeModeDocumentInstructionTests
    {
        [Test]
        public void OfficeInstructions_MentionDocumentEngineAndConfirmation()
        {
            var value = InvokeBuildOfficeDeveloperInstructions();

            Assert.That(value, Does.Contain("Office document"));
            Assert.That(value, Does.Contain("DOCX"));
            Assert.That(value, Does.Contain("XLSX"));
            Assert.That(value, Does.Contain("confirm"));
            Assert.That(value, Does.Contain("overwrite"));
        }

        [Test]
        public void OfficeInstructions_TellModelToUseStructuredDocumentBlocks()
        {
            var value = InvokeBuildOfficeDeveloperInstructions();

            Assert.That(value, Does.Contain("structured blocks"));
            Assert.That(value, Does.Contain("real list"));
            Assert.That(value, Does.Contain("real table"));
            Assert.That(value, Does.Contain("page margins"));
            Assert.That(value, Does.Contain("manual bullet"));
            Assert.That(value, Does.Contain("BlockId"));
            Assert.That(value, Does.Contain("Index"));
        }

        [Test]
        public void OfficeInstructions_TellModelToReadBeforeEditingExistingDocuments()
        {
            var instructions = InvokeBuildOfficeDeveloperInstructions();

            Assert.That(instructions, Does.Contain("inspect the document structure first"));
            Assert.That(instructions, Does.Contain("read or inspect the existing document first"));
            Assert.That(instructions, Does.Contain("selected Skill, plugin, MCP tool, or CLI tool"));
            Assert.That(instructions, Does.Contain("default to creating an edited copy"));
            Assert.That(instructions, Does.Contain("Only overwrite when the user explicitly asks"));
            Assert.That(instructions, Does.Not.Contain("office/read_document"));
            Assert.That(instructions, Does.Not.Contain("office/edit_document"));
        }

        [Test]
        public void OfficeInstructions_DefaultToCopyAndDoNotRepeatExplicitOverwriteAuthorization()
        {
            var instructions = InvokeBuildOfficeDeveloperInstructions();

            Assert.That(instructions, Does.Contain("default to creating an edited copy"));
            Assert.That(instructions, Does.Contain("do not ask for the same confirmation again"));
        }

        [Test]
        public void OfficeInstructions_RequireWorkspaceDelivery()
        {
            var instructions = InvokeBuildOfficeDeveloperInstructions();

            Assert.That(instructions, Does.Contain("authoritative delivery directory"));
            Assert.That(instructions, Does.Contain("current Office working directory"));
            Assert.That(instructions, Does.Contain("Any final document, spreadsheet, presentation, image, or supporting asset"));
            Assert.That(instructions, Does.Contain("Do not report a file as completed unless"));
            Assert.That(instructions, Does.Contain("valid readable Office file"));
            Assert.That(instructions, Does.Contain("invalid, unreadable, corrupt, or only partially written"));
        }

        [Test]
        public void OfficeInstructions_DoNotAdvertiseRemovedLocalOfficeTools()
        {
            var instructions = InvokeBuildOfficeDeveloperInstructions();

            Assert.That(instructions, Does.Contain("plugins, Skills, and MCP tools"));
            Assert.That(instructions, Does.Contain("CLI tools"));
            Assert.That(instructions, Does.Not.Contain("local Office automation"));
            Assert.That(instructions, Does.Not.Contain("Office automation surface"));
            Assert.That(instructions, Does.Not.Contain("legacy MyCodex internal slash-style Office tool"));
            Assert.That(instructions, Does.Not.Contain("local PPTX maintenance"));
            Assert.That(instructions, Does.Not.Contain("office/create_document"));
            Assert.That(instructions, Does.Not.Contain("office/read_document"));
            Assert.That(instructions, Does.Not.Contain("office/edit_document"));
        }

        [Test]
        public void OfficeInstructions_AllowPresentationSpecificToolsWithoutHardCodingOnePlugin()
        {
            var instructions = InvokeBuildOfficeDeveloperInstructions();

            Assert.That(instructions, Does.Contain("PowerPoint"));
            Assert.That(instructions, Does.Contain("PPTX"));
            Assert.That(instructions, Does.Contain("presentation-specific Skill or plugin"));
            Assert.That(instructions, Does.Contain("best matches the task"));
            Assert.That(instructions, Does.Not.Contain("Presentations"));
            Assert.That(instructions, Does.Not.Contain("ordinary docx, xlsx, or pptx work"));
        }

        private static string InvokeBuildOfficeDeveloperInstructions()
        {
            var method = typeof(MainForm).GetMethod(
                "BuildOfficeDeveloperInstructions",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new System.Type[0],
                null);

            return (string)method.Invoke(null, new object[0]);
        }
    }
}
