using System;
using MyCodex.WinForms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class StartupCommandLineOptionsTests
    {
        [Test]
        public void Parse_UsesExplicitWorkspaceOption()
        {
            var options = StartupCommandLineOptions.Parse(new[] { "--workspace", @"E:\Projects\site" });

            Assert.That(options.HasWorkspacePath, Is.True);
            Assert.That(options.WorkspacePath, Is.EqualTo(@"E:\Projects\site"));
        }

        [Test]
        public void Parse_UsesExplicitWorkspaceEqualsOption()
        {
            var options = StartupCommandLineOptions.Parse(new[] { "--workspace=E:\\Projects\\site" });

            Assert.That(options.HasWorkspacePath, Is.True);
            Assert.That(options.WorkspacePath, Is.EqualTo(@"E:\Projects\site"));
        }

        [Test]
        public void Parse_UsesFirstPositionalPathAfterChannelArguments()
        {
            var options = StartupCommandLineOptions.Parse(new[] { "--channel", "Dev", @"E:\Projects\site" });

            Assert.That(options.HasWorkspacePath, Is.True);
            Assert.That(options.WorkspacePath, Is.EqualTo(@"E:\Projects\site"));
        }

        [Test]
        public void Parse_IgnoresEmptyAndOptionOnlyArguments()
        {
            var options = StartupCommandLineOptions.Parse(new[] { "", "--channel=Dev", "--unknown" });

            Assert.That(options.HasWorkspacePath, Is.False);
            Assert.That(options.WorkspacePath, Is.Empty);
        }

        [Test]
        public void Parse_TargetAfterPositionalWorkspaceAndChannel_IsStillRead()
        {
            var options = StartupCommandLineOptions.Parse(new[] { "--channel", "Dev", @"E:\Projects\site", "--target", "wsl-ubuntu" });

            Assert.That(options.WorkspacePath, Is.EqualTo(@"E:\Projects\site"));
            Assert.That(options.TargetId, Is.EqualTo("wsl-ubuntu"));
        }

        [Test]
        public void Parse_TargetEqualsAfterExplicitWorkspaceAndChannel_IsStillRead()
        {
            var options = StartupCommandLineOptions.Parse(new[] { "--workspace", "/home/alice/repo", "--channel=Dev", "--target=ssh-build" });

            Assert.That(options.WorkspacePath, Is.EqualTo("/home/alice/repo"));
            Assert.That(options.TargetId, Is.EqualTo("ssh-build"));
        }

        [Test]
        public void Parse_TargetBeforeWorkspace_DoesNotConsumeWorkspacePath()
        {
            var options = StartupCommandLineOptions.Parse(new[] { "--target", "windows", "--channel", "Dev", "--workspace", @"E:\Projects\site" });

            Assert.That(options.WorkspacePath, Is.EqualTo(@"E:\Projects\site"));
            Assert.That(options.TargetId, Is.EqualTo("windows"));
        }

        [TestCase("--target=")]
        [TestCase("--target=   ")]
        [TestCase("--target")]
        public void Parse_EmptyExplicitTarget_RejectsInsteadOfUsingLastOpened(string targetOption)
        {
            Assert.Throws<ArgumentException>(() => StartupCommandLineOptions.Parse(new[] { "--workspace", @"E:\Projects\site", targetOption }));
        }
    }
}
