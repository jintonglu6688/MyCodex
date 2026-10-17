using System;
using System.IO;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ProjectInstructionsServiceTests
    {
        [Test]
        public void ReadLocalStatus_WhenFileDoesNotExist_ReturnsCreatableTarget()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new ProjectInstructionsService();

                var status = service.ReadLocalStatus(root);

                Assert.That(status.Exists, Is.False);
                Assert.That(status.CanSave, Is.True);
                Assert.That(status.TargetPath, Is.EqualTo(Path.Combine(root, ProjectInstructionsService.FileName)));
                Assert.That(status.ExistingContent, Is.Empty);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void SaveLocal_CreatesUtf8ProjectInstructionsFile()
        {
            var root = CreateTempDirectory();
            try
            {
                var service = new ProjectInstructionsService();
                var targetPath = Path.Combine(root, ProjectInstructionsService.FileName);

                service.SaveLocal(targetPath, "# AGENTS.md instructions\n\n## Build");

                Assert.That(File.Exists(targetPath), Is.True);
                Assert.That(File.ReadAllText(targetPath), Does.Contain("## Build"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public void ExtractGeneratedContent_RemovesMarkdownFence()
        {
            var output = "```markdown\n# AGENTS.md instructions\n\nBody\n```";

            var content = ProjectInstructionsService.ExtractGeneratedContent(output);

            Assert.That(content, Is.EqualTo("# AGENTS.md instructions\n\nBody"));
        }

        [Test]
        public void ExtractGeneratedContent_RemovesLeadingProcessPreamble()
        {
            var output = "使用 `superpowers:using-superpowers` 技能先确认工作方式。\n继续读取仓库结构。\n\n# AGENTS.md instructions\n\n## Build";

            var content = ProjectInstructionsService.ExtractGeneratedContent(output);

            Assert.That(content, Is.EqualTo("# AGENTS.md instructions\n\n## Build"));
        }

        [Test]
        public void BuildImprovementPrompt_IncludesExistingContent()
        {
            var prompt = ProjectInstructionsService.BuildImprovementPrompt("# Existing");

            Assert.That(prompt, Does.Contain("Existing AGENTS.md"));
            Assert.That(prompt, Does.Contain("# Existing"));
            Assert.That(prompt, Does.Contain("Do not run builds"));
            Assert.That(prompt, Does.Contain("Do not preserve analysis notes"));
        }

        private static string CreateTempDirectory()
        {
            var path = Path.Combine(Path.GetTempPath(), "mycodex-project-instructions-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
