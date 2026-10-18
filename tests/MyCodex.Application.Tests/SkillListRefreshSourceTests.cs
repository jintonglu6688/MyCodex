using System.IO;
using System.Text.RegularExpressions;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [Category(SourceGuardCategory.SourceBehavior)]
    [TestFixture]
    public sealed class SkillListRefreshSourceTests
    {
        [Test]
        public void SkillSelectionEntrypoints_ForceReloadSkillList()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.Attachments.cs");

            Assert.That(
                CountOccurrences(source, @"ListSkillsAsync\(new\[\]\s*\{\s*_currentWorkspace\.RootPath\s*\},\s*true\)"),
                Is.EqualTo(2));
            Assert.That(source, Does.Not.Contain("ListSkillsAsync(new[] { _currentWorkspace.RootPath }, false)"));
        }

        [Test]
        public void SkillsManagementForm_ForceReloadsSkillList()
        {
            var source = RepositorySource.ReadSourceFile("SkillsManagementForm.cs");

            Assert.That(source, Does.Contain("ListSkillsAsync(new[] { _workspacePath }, true)"));
            Assert.That(source, Does.Not.Contain("ListSkillsAsync(new[] { _workspacePath }, false)"));
        }

        private static int CountOccurrences(string source, string pattern)
        {
            return Regex.Matches(source ?? string.Empty, pattern).Count;
        }
    }
}
