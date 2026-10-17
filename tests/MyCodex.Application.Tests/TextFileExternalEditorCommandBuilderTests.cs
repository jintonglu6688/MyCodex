using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TextFileExternalEditorCommandBuilderTests
    {
        [Test]
        public void Build_ForVsCode_UsesGotoWhenLineIsProvided()
        {
            var command = TextFileExternalEditorCommandBuilder.Build(
                new TextFileOpenPreference { Mode = TextFileOpenMode.VsCode },
                new TextFileOpenRequest { FilePath = @"E:\Repo\src app.cs", IsLocalWindowsFile = true, Line = 12, Column = 3 });

            Assert.That(command.ExecutablePath, Is.EqualTo("code"));
            Assert.That(command.Arguments, Is.EqualTo("-g \"E:\\Repo\\src app.cs:12:3\""));
            Assert.That(command.UseShellExecute, Is.False);
        }

        [Test]
        public void Build_ForVsCode_OpensFileWhenLineIsMissing()
        {
            var command = TextFileExternalEditorCommandBuilder.Build(
                new TextFileOpenPreference { Mode = TextFileOpenMode.VsCode },
                new TextFileOpenRequest { FilePath = @"E:\Repo\README.md", IsLocalWindowsFile = true });

            Assert.That(command.ExecutablePath, Is.EqualTo("code"));
            Assert.That(command.Arguments, Is.EqualTo("\"E:\\Repo\\README.md\""));
        }

        [Test]
        public void Build_ForNotepadPlusPlus_UsesLineAndColumnSwitches()
        {
            var command = TextFileExternalEditorCommandBuilder.Build(
                new TextFileOpenPreference { Mode = TextFileOpenMode.NotepadPlusPlus },
                new TextFileOpenRequest { FilePath = @"E:\Repo\README.md", IsLocalWindowsFile = true, Line = 7, Column = 2 });

            Assert.That(command.ExecutablePath, Is.EqualTo("notepad++"));
            Assert.That(command.Arguments, Is.EqualTo("-n7 -c2 \"E:\\Repo\\README.md\""));
        }

        [Test]
        public void Build_ForCustom_ReplacesSupportedTokensAndQuotesFile()
        {
            var command = TextFileExternalEditorCommandBuilder.Build(
                new TextFileOpenPreference
                {
                    Mode = TextFileOpenMode.Custom,
                    ExternalEditorPath = @"C:\Tools\editor.exe",
                    ExternalEditorArguments = "--open {file} --line {line} --column {column}"
                },
                new TextFileOpenRequest { FilePath = @"E:\Repo\file name.txt", IsLocalWindowsFile = true, Line = 4, Column = 5 });

            Assert.That(command.ExecutablePath, Is.EqualTo(@"C:\Tools\editor.exe"));
            Assert.That(command.Arguments, Is.EqualTo("--open \"E:\\Repo\\file name.txt\" --line 4 --column 5"));
        }

        [Test]
        public void Build_ForCustomWithoutTemplate_UsesQuotedFile()
        {
            var command = TextFileExternalEditorCommandBuilder.Build(
                new TextFileOpenPreference
                {
                    Mode = TextFileOpenMode.Custom,
                    ExternalEditorPath = @"C:\Tools\editor.exe"
                },
                new TextFileOpenRequest { FilePath = @"E:\Repo\file name.txt", IsLocalWindowsFile = true });

            Assert.That(command.Arguments, Is.EqualTo("\"E:\\Repo\\file name.txt\""));
        }
    }
}
