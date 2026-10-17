using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class TextFileOpenServiceTests
    {
        [Test]
        public void Open_UsesInternalEditor_WhenModeIsInternal()
        {
            var internalPaths = new List<string>();
            var launched = new List<TextFileExternalEditorCommand>();
            var service = new TextFileOpenService(
                command => launched.Add(command),
                request => internalPaths.Add(request.FilePath));

            var result = service.Open(
                new TextFileOpenPreference { Mode = TextFileOpenMode.Internal },
                new TextFileOpenRequest { FilePath = @"E:\Repo\a.txt", IsLocalWindowsFile = true });

            Assert.That(result.Handled, Is.True);
            Assert.That(result.OpenedInternally, Is.True);
            Assert.That(internalPaths, Is.EqualTo(new[] { @"E:\Repo\a.txt" }));
            Assert.That(launched, Is.Empty);
        }

        [Test]
        public void Open_UsesInternalEditor_ForRemotePathEvenWhenExternalModeSelected()
        {
            var internalPaths = new List<string>();
            var launched = new List<TextFileExternalEditorCommand>();
            var service = new TextFileOpenService(
                command => launched.Add(command),
                request => internalPaths.Add(request.FilePath));

            var result = service.Open(
                new TextFileOpenPreference { Mode = TextFileOpenMode.VsCode },
                new TextFileOpenRequest { FilePath = "/home/demo/a.txt", IsLocalWindowsFile = false });

            Assert.That(result.Handled, Is.True);
            Assert.That(result.OpenedInternally, Is.True);
            Assert.That(internalPaths, Is.EqualTo(new[] { "/home/demo/a.txt" }));
            Assert.That(launched, Is.Empty);
        }

        [Test]
        public void Open_LaunchesExternalEditor_ForLocalWindowsExternalMode()
        {
            var launched = new List<TextFileExternalEditorCommand>();
            var service = new TextFileOpenService(
                command => launched.Add(command),
                request => { throw new InvalidOperationException("Internal editor should not be used."); });

            var result = service.Open(
                new TextFileOpenPreference { Mode = TextFileOpenMode.VsCode },
                new TextFileOpenRequest { FilePath = @"E:\Repo\a.txt", IsLocalWindowsFile = true });

            Assert.That(result.Handled, Is.True);
            Assert.That(result.OpenedInternally, Is.False);
            Assert.That(launched.Count, Is.EqualTo(1));
            Assert.That(launched[0].ExecutablePath, Is.EqualTo("code"));
        }

        [Test]
        public void Open_LaunchesResolvedNotepadPlusPlusPath_WhenNotepadPlusPlusModeHasNoConfiguredPath()
        {
            var launched = new List<TextFileExternalEditorCommand>();
            var service = new TextFileOpenService(
                command => launched.Add(command),
                request => { throw new InvalidOperationException("Internal editor should not be used."); },
                new TextFileExternalEditorPreferenceResolver(() => @"C:\Program Files\Notepad++\notepad++.exe"));

            var result = service.Open(
                new TextFileOpenPreference { Mode = TextFileOpenMode.NotepadPlusPlus },
                new TextFileOpenRequest { FilePath = @"E:\Repo\a.txt", IsLocalWindowsFile = true });

            Assert.That(result.Handled, Is.True);
            Assert.That(result.OpenedInternally, Is.False);
            Assert.That(launched.Count, Is.EqualTo(1));
            Assert.That(launched[0].ExecutablePath, Is.EqualTo(@"C:\Program Files\Notepad++\notepad++.exe"));
            Assert.That(launched[0].Arguments, Is.EqualTo("\"E:\\Repo\\a.txt\""));
        }

        [Test]
        public void Open_KeepsConfiguredNotepadPlusPlusPath_WhenResolverFindsAnotherPath()
        {
            var launched = new List<TextFileExternalEditorCommand>();
            var service = new TextFileOpenService(
                command => launched.Add(command),
                request => { throw new InvalidOperationException("Internal editor should not be used."); },
                new TextFileExternalEditorPreferenceResolver(() => @"C:\Program Files\Notepad++\notepad++.exe"));

            var result = service.Open(
                new TextFileOpenPreference
                {
                    Mode = TextFileOpenMode.NotepadPlusPlus,
                    ExternalEditorPath = @"D:\Portable\npp.exe"
                },
                new TextFileOpenRequest { FilePath = @"E:\Repo\a.txt", IsLocalWindowsFile = true });

            Assert.That(result.Handled, Is.True);
            Assert.That(launched[0].ExecutablePath, Is.EqualTo(@"D:\Portable\npp.exe"));
        }

        [Test]
        public void Open_ReturnsFailure_WhenCustomEditorPathIsMissing()
        {
            var service = new TextFileOpenService(
                command => { throw new InvalidOperationException("Launcher should not be called."); },
                request => { throw new InvalidOperationException("Internal editor should not be used."); });

            var result = service.Open(
                new TextFileOpenPreference { Mode = TextFileOpenMode.Custom },
                new TextFileOpenRequest { FilePath = @"E:\Repo\a.txt", IsLocalWindowsFile = true });

            Assert.That(result.Handled, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo(UiLocalization.Text(
                "TextFileOpenService.Error.CustomEditorPathMissing",
                "The custom text editor executable is not configured.")));
        }
    }
}
