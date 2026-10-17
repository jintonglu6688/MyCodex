using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppConfigSnapshotTextFileOpenSettingsTests
    {
        [Test]
        public void Defaults_UseInternalTextEditor()
        {
            var snapshot = new AppConfigSnapshot();

            Assert.That(snapshot.TextFileOpenMode, Is.EqualTo(TextFileOpenMode.Internal));
            Assert.That(snapshot.TextFileExternalEditorPath, Is.Null);
            Assert.That(snapshot.TextFileExternalEditorArguments, Is.Null);
        }

        [Test]
        public void TextFileOpenMode_NormalizesAssignedValue()
        {
            var snapshot = new AppConfigSnapshot
            {
                TextFileOpenMode = "SYSTEM"
            };

            Assert.That(snapshot.TextFileOpenMode, Is.EqualTo(TextFileOpenMode.System));
        }
    }
}
