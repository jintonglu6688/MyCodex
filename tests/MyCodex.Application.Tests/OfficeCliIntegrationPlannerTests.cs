using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficeCliIntegrationPlannerTests
    {
        [Test]
        public void BuildPaths_UsesOfficialCompatibleUserLocalOfficeCliDirectory()
        {
            var paths = OfficeCliIntegrationPlanner.BuildPaths(
                @"C:\Users\demo\AppData\Local",
                @"C:\Users\demo",
                null);

            Assert.That(paths.InstallDirectory, Is.EqualTo(@"C:\Users\demo\AppData\Local\OfficeCLI"));
            Assert.That(paths.ExecutablePath, Is.EqualTo(@"C:\Users\demo\AppData\Local\OfficeCLI\officecli.exe"));
            Assert.That(paths.SkillDirectories, Does.Contain(@"C:\Users\demo\.codex\skills\officecli"));
            Assert.That(paths.SkillDirectories, Does.Contain(@"C:\Users\demo\.agents\skills\officecli"));
        }

        [Test]
        public void BuildPaths_IncludesCodexHomeSkillDirectoryWithoutDuplicatingDefaultCodexHome()
        {
            var paths = OfficeCliIntegrationPlanner.BuildPaths(
                @"C:\Users\demo\AppData\Local",
                @"C:\Users\demo",
                @"C:\Users\demo\.codex");

            Assert.That(
                paths.SkillDirectories.Count(item => string.Equals(item, @"C:\Users\demo\.codex\skills\officecli", StringComparison.OrdinalIgnoreCase)),
                Is.EqualTo(1));
            Assert.That(paths.SkillDirectories, Does.Contain(@"C:\Users\demo\.agents\skills\officecli"));
        }

        [Test]
        public void BuildPaths_IncludesCustomCodexHomeSkillDirectory()
        {
            var paths = OfficeCliIntegrationPlanner.BuildPaths(
                @"C:\Users\demo\AppData\Local",
                @"C:\Users\demo",
                @"D:\CodexHome");

            Assert.That(paths.SkillDirectories, Does.Contain(@"D:\CodexHome\skills\officecli"));
            Assert.That(paths.SkillDirectories, Does.Contain(@"C:\Users\demo\.codex\skills\officecli"));
            Assert.That(paths.SkillDirectories, Does.Contain(@"C:\Users\demo\.agents\skills\officecli"));
        }

        [Test]
        public void PrependDirectoryToPath_AddsOfficeCliDirectoryAndRemovesDuplicate()
        {
            var currentPath = string.Join(
                Path.PathSeparator.ToString(),
                new[]
                {
                    @"C:\Windows",
                    @"C:\Users\demo\AppData\Local\OfficeCLI",
                    @"C:\Tools"
                });

            var value = OfficeCliIntegrationPlanner.PrependDirectoryToPath(
                currentPath,
                @"C:\Users\demo\AppData\Local\OfficeCLI");
            var entries = value.Split(Path.PathSeparator);

            Assert.That(entries[0], Is.EqualTo(@"C:\Users\demo\AppData\Local\OfficeCLI"));
            Assert.That(entries.Count(item => string.Equals(item, @"C:\Users\demo\AppData\Local\OfficeCLI", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
            Assert.That(entries, Does.Contain(@"C:\Windows"));
            Assert.That(entries, Does.Contain(@"C:\Tools"));
        }

        [Test]
        public void PrependDirectoryToPath_WhenCurrentPathIsEmpty_ReturnsOfficeCliDirectory()
        {
            var value = OfficeCliIntegrationPlanner.PrependDirectoryToPath(
                string.Empty,
                @"C:\Users\demo\AppData\Local\OfficeCLI");

            Assert.That(value, Is.EqualTo(@"C:\Users\demo\AppData\Local\OfficeCLI"));
        }

        [Test]
        public void TryApplyRuntimeEnvironment_WhenExecutableExists_PrependsOfficeCliDirectory()
        {
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "PATH", @"C:\Windows" }
            };

            var applied = OfficeCliIntegrationPlanner.TryApplyRuntimeEnvironment(
                environment,
                @"C:\Users\demo\AppData\Local\OfficeCLI\officecli.exe",
                path => true);

            Assert.That(applied, Is.True);
            Assert.That(environment["MYCODEX_OFFICECLI_PATH"], Is.EqualTo(@"C:\Users\demo\AppData\Local\OfficeCLI\officecli.exe"));
            Assert.That(environment["PATH"].Split(Path.PathSeparator)[0], Is.EqualTo(@"C:\Users\demo\AppData\Local\OfficeCLI"));
        }

        [Test]
        public void TryApplyRuntimeEnvironment_WhenExecutableMissing_DoesNotChangeEnvironment()
        {
            var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "PATH", @"C:\Windows" }
            };

            var applied = OfficeCliIntegrationPlanner.TryApplyRuntimeEnvironment(
                environment,
                @"C:\Users\demo\AppData\Local\OfficeCLI\officecli.exe",
                path => false);

            Assert.That(applied, Is.False);
            Assert.That(environment["PATH"], Is.EqualTo(@"C:\Windows"));
            Assert.That(environment.ContainsKey("MYCODEX_OFFICECLI_PATH"), Is.False);
        }

        [Test]
        public void EvaluateStatus_WhenExecutablePathAndSkillsExist_ReportsReady()
        {
            var paths = OfficeCliIntegrationPlanner.BuildPaths(
                @"C:\Users\demo\AppData\Local",
                @"C:\Users\demo",
                null);
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            existingPaths.Add(paths.ExecutablePath);
            foreach (var skillDirectory in paths.SkillDirectories)
            {
                existingPaths.Add(Path.Combine(skillDirectory, "SKILL.md"));
            }

            var status = OfficeCliIntegrationPlanner.EvaluateStatus(
                paths,
                @"C:\Windows;C:\Users\demo\AppData\Local\OfficeCLI",
                existingPaths.Contains);

            Assert.That(status.IsExecutableInstalled, Is.True);
            Assert.That(status.IsUserPathConfigured, Is.True);
            Assert.That(status.AreSkillsInstalled, Is.True);
            Assert.That(status.IsReady, Is.True);
        }

        [Test]
        public void EvaluateStatus_WhenSkillsAreMissing_ReportsMissingSkillDirectories()
        {
            var paths = OfficeCliIntegrationPlanner.BuildPaths(
                @"C:\Users\demo\AppData\Local",
                @"C:\Users\demo",
                null);
            var existingPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                paths.ExecutablePath
            };

            var status = OfficeCliIntegrationPlanner.EvaluateStatus(
                paths,
                @"C:\Users\demo\AppData\Local\OfficeCLI",
                existingPaths.Contains);

            Assert.That(status.IsExecutableInstalled, Is.True);
            Assert.That(status.IsUserPathConfigured, Is.True);
            Assert.That(status.AreSkillsInstalled, Is.False);
            Assert.That(status.MissingSkillDirectories, Is.EquivalentTo(paths.SkillDirectories));
            Assert.That(status.IsReady, Is.False);
        }
    }
}
