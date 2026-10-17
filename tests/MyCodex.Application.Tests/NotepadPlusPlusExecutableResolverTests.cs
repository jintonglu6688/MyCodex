using System;
using System.Collections.Generic;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class NotepadPlusPlusExecutableResolverTests
    {
        [Test]
        public void Resolve_PrefersRegistryCandidateWhenItExists()
        {
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                @"C:\Registry\Notepad++\notepad++.exe",
                @"C:\Program Files\Notepad++\notepad++.exe"
            };
            var resolver = new NotepadPlusPlusExecutableResolver(
                path => existing.Contains(path),
                folder => folder == Environment.SpecialFolder.ProgramFiles ? @"C:\Program Files" : string.Empty,
                () => new[] { @"C:\Registry\Notepad++\notepad++.exe" });

            Assert.That(resolver.Resolve(), Is.EqualTo(@"C:\Registry\Notepad++\notepad++.exe"));
        }

        [Test]
        public void Resolve_UsesProgramFilesCandidateWhenRegistryCandidatesAreMissing()
        {
            var resolver = new NotepadPlusPlusExecutableResolver(
                path => string.Equals(path, @"C:\Program Files\Notepad++\notepad++.exe", StringComparison.OrdinalIgnoreCase),
                folder => folder == Environment.SpecialFolder.ProgramFiles ? @"C:\Program Files" : string.Empty,
                () => new[] { @"C:\Missing\Notepad++\notepad++.exe" });

            Assert.That(resolver.Resolve(), Is.EqualTo(@"C:\Program Files\Notepad++\notepad++.exe"));
        }

        [Test]
        public void Resolve_ReturnsNullWhenNoCandidateExists()
        {
            var resolver = new NotepadPlusPlusExecutableResolver(
                path => false,
                folder => string.Empty,
                () => new string[0]);

            Assert.That(resolver.Resolve(), Is.Null);
        }
    }
}
