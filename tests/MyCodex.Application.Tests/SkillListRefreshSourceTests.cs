using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SkillListRefreshSourceTests
    {
        [Test]
        public void SkillSelectionEntrypoints_ForceReloadSkillList()
        {
            var source = File.ReadAllText(GetWinFormsPath("Forms", "MainForm.Attachments.cs"));

            Assert.That(
                CountOccurrences(source, @"ListSkillsAsync\(new\[\]\s*\{\s*_currentWorkspace\.RootPath\s*\},\s*true\)"),
                Is.EqualTo(2));
            Assert.That(source, Does.Not.Contain("ListSkillsAsync(new[] { _currentWorkspace.RootPath }, false)"));
        }

        [Test]
        public void SkillsManagementForm_ForceReloadsSkillList()
        {
            var source = File.ReadAllText(GetWinFormsPath("Forms", "SkillsManagementForm.cs"));

            Assert.That(source, Does.Contain("ListSkillsAsync(new[] { _workspacePath }, true)"));
            Assert.That(source, Does.Not.Contain("ListSkillsAsync(new[] { _workspacePath }, false)"));
        }

        private static int CountOccurrences(string source, string pattern)
        {
            return Regex.Matches(source ?? string.Empty, pattern).Count;
        }

        private static string GetWinFormsPath(params string[] parts)
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms");

            foreach (var part in parts)
            {
                path = Path.Combine(path, part);
            }

            return Path.GetFullPath(path);
        }
    }
}
