using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TextFileOpenModeTests
    {
        [TestCase(null, TextFileOpenMode.Internal)]
        [TestCase("", TextFileOpenMode.Internal)]
        [TestCase("  ", TextFileOpenMode.Internal)]
        [TestCase("internal", TextFileOpenMode.Internal)]
        [TestCase("SYSTEM", TextFileOpenMode.System)]
        [TestCase("vscode", TextFileOpenMode.VsCode)]
        [TestCase("notepadPlusPlus", TextFileOpenMode.NotepadPlusPlus)]
        [TestCase("custom", TextFileOpenMode.Custom)]
        [TestCase("unknown", TextFileOpenMode.Internal)]
        public void Normalize_ReturnsSupportedModeOrInternal(string value, string expected)
        {
            Assert.That(TextFileOpenMode.Normalize(value), Is.EqualTo(expected));
        }

        [Test]
        public void IsExternal_ReturnsTrueOnlyForProcessBackedModes()
        {
            Assert.That(TextFileOpenMode.IsExternal(TextFileOpenMode.Internal), Is.False);
            Assert.That(TextFileOpenMode.IsExternal(TextFileOpenMode.System), Is.True);
            Assert.That(TextFileOpenMode.IsExternal(TextFileOpenMode.VsCode), Is.True);
            Assert.That(TextFileOpenMode.IsExternal(TextFileOpenMode.NotepadPlusPlus), Is.True);
            Assert.That(TextFileOpenMode.IsExternal(TextFileOpenMode.Custom), Is.True);
        }
    }
}
