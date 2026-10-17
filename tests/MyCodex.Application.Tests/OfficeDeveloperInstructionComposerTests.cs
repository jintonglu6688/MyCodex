using MyCodex.Application.Capabilities;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeDeveloperInstructionComposerTests
    {
        [Test]
        public void Compose_GuidesCapabilityFirstOfficeToolSelection()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("MyCodex Office Mode"));
            Assert.That(instructions, Does.Contain("Use enabled Skills, plugins, MCP tools, and CLI tools"));
            Assert.That(instructions, Does.Contain("Do not prefer a specific Office plugin solely because it is installed"));
            Assert.That(instructions, Does.Contain("when it best matches the task"));
            Assert.That(instructions, Does.Not.Contain("office/create_document"));
            Assert.That(instructions, Does.Not.Contain("office/read_document"));
            Assert.That(instructions, Does.Not.Contain("office/edit_document"));
            Assert.That(instructions, Does.Contain("plugins, Skills, and MCP tools"));
            Assert.That(instructions, Does.Contain("authoritative delivery directory"));
            Assert.That(instructions, Does.Contain("current Office working directory"));
            Assert.That(instructions, Does.Not.Contain("current Office workspace"));
            Assert.That(instructions, Does.Not.Contain("local Office automation"));
            Assert.That(instructions, Does.Not.Contain("Office automation surface"));
            Assert.That(instructions, Does.Not.Contain("Use the installed normal Codex plugin mycodex-office"));
            Assert.That(instructions, Does.Not.Contain("Prefer mycodex-office"));
        }

        [Test]
        public void Compose_WhenWorkspacePathsProvided_UsesSingleOfficeWorkingDirectory()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose(
                @"C:\ConfiguredOffice",
                @"D:\CurrentOffice");

            Assert.That(instructions, Does.Contain("The current Office working directory is: D:\\CurrentOffice."));
            Assert.That(instructions, Does.Not.Contain(@"C:\ConfiguredOffice"));
            Assert.That(instructions, Does.Not.Contain("officeRootPath"));
            Assert.That(instructions, Does.Not.Contain("officeWorkspacePath"));
            Assert.That(instructions, Does.Not.Contain("category"));
        }

        [Test]
        public void Compose_DoesNotExposeDisabledOfficialOfficeCapabilityBranch()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("MyCodex Office Mode"));
            Assert.That(instructions, Does.Contain("plugins, Skills, and MCP tools"));
            Assert.That(instructions, Does.Contain("authoritative delivery directory"));
            Assert.That(instructions, Does.Contain("current Office working directory"));
            Assert.That(instructions, Does.Not.Contain("official MyCodex Office capability is disabled"));
            Assert.That(instructions, Does.Not.Contain("office/create_document"));
            Assert.That(instructions, Does.Not.Contain("office/read_document"));
            Assert.That(instructions, Does.Not.Contain("office/edit_document"));
            Assert.That(instructions, Does.Not.Contain("MyCodex internal Office document tools"));
        }

        [Test]
        public void Compose_RequiresWorkspaceDelivery()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("Any final document, spreadsheet, presentation, image, or supporting asset"));
            Assert.That(instructions, Does.Contain("saved under the current Office working directory"));
            Assert.That(instructions, Does.Contain("temporary outputs"));
            Assert.That(instructions, Does.Contain("Do not report a file as completed unless"));
        }

        [Test]
        public void Compose_PreservesSafeFilePolicy()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("default to creating an edited copy"));
            Assert.That(instructions, Does.Contain("do not authorize overwriting"));
            Assert.That(instructions, Does.Contain("Only overwrite when the user explicitly asks"));
            Assert.That(instructions, Does.Contain("do not ask for the same confirmation again"));
            Assert.That(instructions, Does.Contain("Keep the original file unchanged"));
        }

        [Test]
        public void Compose_DirectlyExecutesRoutineOfficeWorkWithoutSoftwareWorkflow()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("execute it directly"));
            Assert.That(instructions, Does.Contain("Do not first produce a software specification, implementation plan, task list"));
            Assert.That(instructions, Does.Contain("An office or business plan requested by the user is the deliverable itself"));
            Assert.That(instructions, Does.Contain("Do not invoke software requirements analysis, specification, development planning, test-driven development, code review, or project scaffolding"));
            Assert.That(instructions, Does.Contain("Ask one focused question only"));
        }

        [Test]
        public void Compose_SelectsActuallyAvailableToolsByTaskFit()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("Use only tools that are actually enabled"));
            Assert.That(instructions, Does.Contain("If the user names a tool, prefer it"));
            Assert.That(instructions, Does.Contain("dedicated Skill, plugin, or MCP tool"));
            Assert.That(instructions, Does.Contain("one primary creation or editing tool"));
            Assert.That(instructions, Does.Contain("Do not pretend that an unavailable tool was used"));
        }

        [Test]
        public void Compose_DefinesDocumentSpreadsheetAndPresentationQuality()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("real heading and paragraph structures"));
            Assert.That(instructions, Does.Contain("real list structures"));
            Assert.That(instructions, Does.Contain("real table structures"));
            Assert.That(instructions, Does.Contain("real cell data types"));
            Assert.That(instructions, Does.Contain("prefer formulas over static calculated values"));
            Assert.That(instructions, Does.Contain("one clear subject per slide"));
            Assert.That(instructions, Does.Contain("Do not output a formal slide plan"));
        }

        [Test]
        public void Compose_ConfirmsOnlyUnclearOrUnauthorizedRiskyActions()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("A clear user request is authorization for that exact action and scope"));
            Assert.That(instructions, Does.Contain("Do not automatically send email, publish content, share links"));
            Assert.That(instructions, Does.Contain("Do not upload user files merely because a tool can access the network"));
            Assert.That(instructions, Does.Contain("Ask only when"));
        }

        [Test]
        public void Compose_ReportsDeliverablesConciselyAndHonestly()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("exact final file name and path"));
            Assert.That(instructions, Does.Contain("whether an edited copy was created"));
            Assert.That(instructions, Does.Contain("key validation performed"));
            Assert.That(instructions, Does.Contain("Do not narrate internal reasoning, tool-call history"));
        }

        [Test]
        public void Compose_HandlesToolFailureWithoutEnteringDevelopmentWorkflow()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("Do not turn a tool failure into a software development or debugging project"));
            Assert.That(instructions, Does.Contain("retry, repair, or switch tools"));
            Assert.That(instructions, Does.Contain("changes the requested format, overwrites a file, uploads data, or materially reduces quality"));
        }

        [Test]
        public void Compose_AllowsPresentationPluginsWithoutHardCodingPresentationsPriority()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("PowerPoint"));
            Assert.That(instructions, Does.Contain("PPTX"));
            Assert.That(instructions, Does.Contain("presentation-specific Skill or plugin"));
            Assert.That(instructions, Does.Contain("import"));
            Assert.That(instructions, Does.Not.Contain("Presentations"));
            Assert.That(instructions, Does.Not.Contain("prefer an enabled dedicated presentation Skill"));
            Assert.That(instructions, Does.Not.Contain("docx, xlsx, or pptx"));
            Assert.That(instructions, Does.Not.Contain("ordinary docx, xlsx, or pptx work"));
        }

        [Test]
        public void Compose_MentionsOfficeCliAsOptionalCreationInspectionAndValidationTool()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose(
                @"D:\文档\MyCodex-office",
                @"D:\文档\MyCodex-office\ppt");

            Assert.That(instructions, Does.Contain("OfficeCLI"));
            Assert.That(instructions, Does.Contain("create, inspect, preview, validate, or repair"));
            Assert.That(instructions, Does.Contain("Do not require OfficeCLI when another enabled Skill, plugin, MCP tool, or CLI tool is a better fit"));
            Assert.That(instructions, Does.Not.Contain("target Office application"));
            Assert.That(instructions, Does.Not.Contain("local PPTX maintenance"));
            Assert.That(instructions, Does.Not.Contain("OfficeCLI must not be treated as a presentation generation engine"));
        }

        [Test]
        public void Compose_RequiresGeneratedOfficeFileValidityChecksBeforeCompletion()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Contain("After creating or modifying DOCX, XLSX, or PPTX files"));
            Assert.That(instructions, Does.Contain("verify that each final file exists, is non-empty, and is a valid readable Office file"));
            Assert.That(instructions, Does.Contain("Do not report an Office file as complete if it is invalid, unreadable, corrupt, or only partially written"));
            Assert.That(instructions, Does.Contain("If validation fails, repair or regenerate the file"));
        }

        [Test]
        public void Compose_DoesNotRequireMyCodexOfficeBeforeOtherTools()
        {
            var instructions = OfficeDeveloperInstructionComposer.Compose();

            Assert.That(instructions, Does.Not.Contain("If mycodex-office is unavailable or fails"));
            Assert.That(instructions, Does.Not.Contain("before considering another installed Skill"));
            Assert.That(instructions, Does.Not.Contain("mycodex-office-presentation"));
            Assert.That(instructions, Does.Not.Contain("mycodex-office as the PPTX generation path"));
        }
    }
}
