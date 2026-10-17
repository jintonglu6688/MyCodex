using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TextFileOpenPreferenceComposerTests
    {
        [Test]
        public void FromSnapshot_UsesDefaults_WhenSnapshotIsNull()
        {
            var preference = TextFileOpenPreferenceComposer.FromSnapshot(null);

            Assert.That(preference.Mode, Is.EqualTo(TextFileOpenMode.Internal));
            Assert.That(preference.ExternalEditorPath, Is.Null);
            Assert.That(preference.ExternalEditorArguments, Is.Null);
        }

        [Test]
        public void FromSnapshot_MapsConfiguredExternalEditorSettings()
        {
            var snapshot = new AppConfigSnapshot
            {
                TextFileOpenMode = "VSCODE",
                TextFileExternalEditorPath = @"E:\Tools\editor.exe",
                TextFileExternalEditorArguments = "--reuse {file}"
            };

            var preference = TextFileOpenPreferenceComposer.FromSnapshot(snapshot);

            Assert.That(preference.Mode, Is.EqualTo(TextFileOpenMode.VsCode));
            Assert.That(preference.ExternalEditorPath, Is.EqualTo(@"E:\Tools\editor.exe"));
            Assert.That(preference.ExternalEditorArguments, Is.EqualTo("--reuse {file}"));
        }
    }
}
