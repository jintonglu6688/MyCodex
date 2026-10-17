using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class GitWorkbenchStatusErrorFormatterTests
    {
        [Test]
        public void IsSafeDirectoryOwnershipError_ReturnsTrueForDubiousOwnershipMessage()
        {
            var message = "fatal: detected dubious ownership in repository at 'E:/BoyCloudDrive'\nTo add an exception for this directory, call:\n\ngit config --global --add safe.directory E:/BoyCloudDrive";

            Assert.That(GitWorkbenchStatusErrorFormatter.IsSafeDirectoryOwnershipError(message), Is.True);
        }

        [Test]
        public void BuildDisplayMessage_IncludesSafeDirectoryCommandForDubiousOwnership()
        {
            var message = "fatal: detected dubious ownership in repository at 'E:/BoyCloudDrive'";

            var text = GitWorkbenchStatusErrorFormatter.BuildDisplayMessage(@"E:\BoyCloudDrive", message);

            Assert.That(text, Does.Contain("safe.directory"));
            Assert.That(text, Does.Contain("E:/BoyCloudDrive"));
        }

        [Test]
        public void BuildWorktreePlaceholderText_UsesFriendlyGuidanceForDubiousOwnership()
        {
            var message = "fatal: detected dubious ownership in repository at 'E:/BoyCloudDrive'";

            var text = GitWorkbenchStatusErrorFormatter.BuildWorktreePlaceholderText(@"E:\BoyCloudDrive", message);

            Assert.That(text, Does.Contain("safe.directory").Or.Contain("Git"));
        }
    }
}
