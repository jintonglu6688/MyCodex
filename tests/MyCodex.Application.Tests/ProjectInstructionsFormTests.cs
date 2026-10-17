using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ProjectInstructionsFormTests
    {
        [Test]
        public void ContentEditor_DoesNotConsumeTabKey()
        {
            var status = new ProjectInstructionsStatus
            {
                WorkspacePath = @"C:\repo",
                TargetPath = @"C:\repo\AGENTS.md",
                CanSave = true,
                Exists = false
            };

            using (var form = new ProjectInstructionsForm(
                status,
                canSmartGenerate: true,
                basicTemplate: "# AGENTS.md instructions",
                generateAsync: (improve, text, cancellationToken) => Task.FromResult("# AGENTS.md instructions"),
                saveAsync: text => Task.CompletedTask))
            {
                var contentTextBox = (TextBox)typeof(ProjectInstructionsForm)
                    .GetField("_contentTextBox", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);

                Assert.That(contentTextBox.AcceptsTab, Is.False);
            }
        }

        [Test]
        public void ExistingContent_UsesUpdateActionAndHidesTemplateAction()
        {
            var status = new ProjectInstructionsStatus
            {
                WorkspacePath = @"C:\repo",
                TargetPath = @"C:\repo\AGENTS.md",
                CanSave = true,
                Exists = true,
                ExistingContent = "# AGENTS.md instructions"
            };

            using (var form = new ProjectInstructionsForm(
                status,
                canSmartGenerate: true,
                basicTemplate: "# AGENTS.md instructions",
                generateAsync: (improve, text, cancellationToken) => Task.FromResult("# AGENTS.md instructions"),
                saveAsync: text => Task.CompletedTask))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var generateButton = (Button)typeof(ProjectInstructionsForm)
                    .GetField("_generateButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);
                var templateButton = (Button)typeof(ProjectInstructionsForm)
                    .GetField("_templateButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);

                Assert.That(generateButton.Text, Does.Contain("Smart Update").Or.Contain("智能更新"));
                Assert.That(templateButton.Visible, Is.False);
            }
        }

        [Test]
        public void NewContent_UsesGenerateActionAndShowsTemplateAction()
        {
            var status = new ProjectInstructionsStatus
            {
                WorkspacePath = @"C:\repo",
                TargetPath = @"C:\repo\AGENTS.md",
                CanSave = true,
                Exists = false
            };

            using (var form = new ProjectInstructionsForm(
                status,
                canSmartGenerate: true,
                basicTemplate: "# AGENTS.md instructions",
                generateAsync: (improve, text, cancellationToken) => Task.FromResult("# AGENTS.md instructions"),
                saveAsync: text => Task.CompletedTask))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();

                var generateButton = (Button)typeof(ProjectInstructionsForm)
                    .GetField("_generateButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);
                var templateButton = (Button)typeof(ProjectInstructionsForm)
                    .GetField("_templateButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);

                Assert.That(generateButton.Text, Does.Contain("Smart Generate").Or.Contain("智能生成"));
                Assert.That(templateButton.Visible, Is.True);
            }
        }
    }
}
