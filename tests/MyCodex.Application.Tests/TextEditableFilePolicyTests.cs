using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TextEditableFilePolicyTests
    {
        [TestCase(@"E:\Repo\src\Program.cs")]
        [TestCase(@"E:\Repo\README.md")]
        [TestCase(@"E:\Repo\appsettings.json")]
        [TestCase(@"E:\Repo\Directory.Build.props")]
        [TestCase(@"E:\Repo\.gitignore")]
        [TestCase("/home/user/project/Dockerfile")]
        [TestCase("/home/user/project/scripts/build.sh")]
        public void ShouldOpenWithTextEditor_ReturnsTrue_ForKnownTextFiles(string path)
        {
            Assert.That(TextEditableFilePolicy.ShouldOpenWithTextEditor(path), Is.True);
        }

        [TestCase(@"E:\Repo\tool.exe")]
        [TestCase(@"E:\Repo\archive.zip")]
        [TestCase(@"E:\Repo\archive.rar")]
        [TestCase(@"E:\Repo\image.png")]
        [TestCase(@"E:\Repo\audio.mp3")]
        [TestCase(@"E:\Repo\video.mp4")]
        [TestCase(@"E:\Repo\unknown.binary")]
        [TestCase(@"E:\Repo\no-extension")]
        public void ShouldOpenWithTextEditor_ReturnsFalse_ForFilesThatShouldUseSystemDefault(string path)
        {
            Assert.That(TextEditableFilePolicy.ShouldOpenWithTextEditor(path), Is.False);
        }
    }
}
