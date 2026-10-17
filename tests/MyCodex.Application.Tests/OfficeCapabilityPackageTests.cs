using System.Linq;
using MyCodex.Application.Capabilities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeCapabilityPackageTests
    {
        [Test]
        public void CreateDefault_ReturnsEnabledOfficialOfficePackage()
        {
            var package = OfficeCapabilityPackage.CreateDefault();

            Assert.That(package.Id, Is.EqualTo("mycodex.office"));
            Assert.That(package.DisplayName, Is.EqualTo("MyCodex Office"));
            Assert.That(package.Kind, Is.EqualTo(CapabilityPackageKind.Official));
            Assert.That(package.EnabledByDefault, Is.True);
            Assert.That(package.SupportedModes, Does.Contain(AppInteractionMode.Office));
        }

        [Test]
        public void CreateDefault_ExposesExpectedSubFeatures()
        {
            var package = OfficeCapabilityPackage.CreateDefault();
            var names = package.SubFeatures.Select(item => item.Id).ToArray();

            Assert.That(names, Is.EquivalentTo(new[]
            {
                "word",
                "spreadsheet",
                "presentation",
                "preview",
                "validation",
                "image-assist"
            }));
            Assert.That(package.SubFeatures.Single(item => item.Id == "word").State, Is.EqualTo(CapabilitySubFeatureState.Enabled));
            Assert.That(package.SubFeatures.Single(item => item.Id == "spreadsheet").State, Is.EqualTo(CapabilitySubFeatureState.Enabled));
            Assert.That(package.SubFeatures.Single(item => item.Id == "presentation").State, Is.EqualTo(CapabilitySubFeatureState.Conditional));
            Assert.That(package.SubFeatures.Single(item => item.Id == "preview").State, Is.EqualTo(CapabilitySubFeatureState.Disabled));
            Assert.That(package.SubFeatures.Single(item => item.Id == "validation").State, Is.EqualTo(CapabilitySubFeatureState.Enabled));
            Assert.That(package.SubFeatures.Single(item => item.Id == "image-assist").State, Is.EqualTo(CapabilitySubFeatureState.Conditional));
        }

        [Test]
        public void CreateDefault_DeclaresSafeDefaultPermissions()
        {
            var package = OfficeCapabilityPackage.CreateDefault();

            Assert.That(package.Permissions, Does.Contain(CapabilityPermission.UseMcpTools));
            Assert.That(package.Permissions, Does.Contain(CapabilityPermission.ReadOfficeRoot));
            Assert.That(package.Permissions, Does.Contain(CapabilityPermission.WriteOfficeRoot));
            Assert.That(package.Permissions, Does.Contain(CapabilityPermission.CreateEditedCopy));
            Assert.That(package.Permissions, Does.Not.Contain(CapabilityPermission.RunShell));
            Assert.That(package.Permissions, Does.Not.Contain(CapabilityPermission.UseLocalOfficeAutomation));
            Assert.That(package.Permissions, Does.Not.Contain(CapabilityPermission.UploadDocuments));
        }

        [Test]
        public void CreateDefault_DoesNotAdvertiseMyCodexPresentationGenerationSkill()
        {
            var package = OfficeCapabilityPackage.CreateDefault();

            Assert.That(package.SkillNames, Does.Contain("mycodex-office-document"));
            Assert.That(package.SkillNames, Does.Contain("mycodex-office-spreadsheet"));
            Assert.That(package.SkillNames, Does.Contain("mycodex-office-quality-check"));
            Assert.That(package.SkillNames, Does.Not.Contain("mycodex-office-presentation"));
        }

        [Test]
        public void CreateDefault_ContributesOfficeToolGuidanceFragment()
        {
            var package = OfficeCapabilityPackage.CreateDefault();
            var fragments = string.Join(" ", package.DeveloperInstructionFragments.ToArray());

            Assert.That(package.DeveloperInstructionFragments, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(fragments, Does.Contain("enabled Skills, plugins, MCP tools, and CLI tools"));
            Assert.That(fragments, Does.Contain("best matches the task"));
            Assert.That(fragments, Does.Contain("Do not prefer a specific Office plugin solely because it is installed"));
            Assert.That(fragments, Does.Not.Contain("office/create_document"));
            Assert.That(fragments, Does.Not.Contain("office/read_document"));
            Assert.That(fragments, Does.Not.Contain("office/edit_document"));
            Assert.That(fragments, Does.Contain("third-party"));
            Assert.That(fragments, Does.Contain("presentation"));
            Assert.That(fragments, Does.Contain("presentation-specific Skill or plugin"));
            Assert.That(fragments, Does.Not.Contain("Presentations"));
            Assert.That(fragments, Does.Not.Contain("capability is enabled"));
        }

        [Test]
        public void CreateDefault_DescribesOptionalOfficeCliWithoutBanningGeneration()
        {
            var package = OfficeCapabilityPackage.CreateDefault();
            var fragments = string.Join(" ", package.DeveloperInstructionFragments);

            Assert.That(fragments, Does.Contain("OfficeCLI"));
            Assert.That(fragments, Does.Contain("create, inspect, preview, validate, or repair"));
            Assert.That(fragments, Does.Not.Contain("use OfficeCLI to generate presentations"));
            Assert.That(fragments, Does.Not.Contain("OfficeCLI must not be treated as a presentation generation engine"));
        }

        [Test]
        public void CreateDefault_DoesNotAdvertiseRemovedLocalOfficeTools()
        {
            var package = OfficeCapabilityPackage.CreateDefault();
            var fragments = string.Join(" ", package.DeveloperInstructionFragments);

            Assert.That(fragments, Does.Not.Contain("local PPTX maintenance"));
            Assert.That(fragments, Does.Not.Contain("local Office automation"));
            Assert.That(fragments, Does.Not.Contain("Office automation surface"));
            Assert.That(fragments, Does.Not.Contain("target Office application"));
        }
    }
}
