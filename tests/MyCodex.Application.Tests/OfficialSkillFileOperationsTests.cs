using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Skills;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class OfficialSkillFileOperationsTests
    {
        [Test]
        public async Task SaveNewUserSkill_UsesApprovedObservedRootThroughAppServerFileSystem()
        {
            var files = new FakeFileSystemClient();
            var operations = new OfficialSkillFileOperations(files, "E:/repo");
            operations.RefreshObservations(new[]
            {
                new AppServerSkillInfo { Scope = "user", Path = "C:/target/.codex/skills/existing/SKILL.md" }
            });

            var location = operations.GetSaveLocations()[0];
            var path = await operations.SaveAsync(new SkillFileModel
            {
                Name = "new-skill",
                Description = "Use for tests.",
                Body = "# Workflow",
                Location = location
            }, true);

            Assert.That(path, Is.EqualTo("C:/target/.codex/skills/new-skill"));
            Assert.That(files.CreatedDirectories, Is.EqualTo(new[] { "C:/target/.codex/skills/new-skill" }));
            Assert.That(files.Writes["C:/target/.codex/skills/new-skill/SKILL.md"], Does.Contain("name: \"new-skill\""));
        }

        [Test]
        public void EditOutsideCurrentObservation_FailsClosed()
        {
            var operations = new OfficialSkillFileOperations(new FakeFileSystemClient(), "E:/repo");
            operations.RefreshObservations(new[]
            {
                new AppServerSkillInfo { Scope = "user", Path = "C:/target/.codex/skills/existing/SKILL.md" }
            });

            string reason;
            var allowed = operations.CanEdit(
                new AppServerSkillInfo { Scope = "user", Path = "C:/other/.codex/skills/other/SKILL.md" },
                out reason);

            Assert.That(allowed, Is.False);
            Assert.That(reason, Does.Contain("current App Server observation"));
        }

        [Test]
        public void SkillList_DoesNotSynchronizeOrRepairPrivateSkillMirrors()
        {
            var source = File.ReadAllText(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..", "..", "..", "..", "src", "MyCodex.Infrastructure", "AppServer", "AppServerClientOperations.cs"));

            Assert.That(source, Does.Not.Contain("_trySynchronizeCurrentAuthenticationSharedSkillsAsync"));
            Assert.That(source, Does.Not.Contain("_tryRepairLocalPluginMarketManifests"));
        }

        private sealed class FakeFileSystemClient : IAppServerFileSystemClient
        {
            public readonly List<string> CreatedDirectories = new List<string>();
            public readonly Dictionary<string, string> Writes = new Dictionary<string, string>(StringComparer.Ordinal);

            public Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path)
            {
                return Task.FromResult(new List<AppServerFileSystemEntry>());
            }

            public Task<AppServerFileSystemReadResult> ReadFileAsync(string path) { throw new NotSupportedException(); }
            public Task<AppServerFileSystemMetadata> GetMetadataAsync(string path) { throw new NotSupportedException(); }

            public Task WriteFileAsync(string path, byte[] data)
            {
                Writes[path] = Encoding.UTF8.GetString(data);
                return Task.CompletedTask;
            }

            public Task CreateDirectoryAsync(string path, bool? recursive = null)
            {
                CreatedDirectories.Add(path);
                return Task.CompletedTask;
            }

            public Task RemoveAsync(string path, bool? recursive = null, bool? force = null) { throw new NotSupportedException(); }
            public Task CopyAsync(string sourcePath, string destinationPath, bool recursive) { throw new NotSupportedException(); }
            public Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId) { throw new NotSupportedException(); }
            public Task UnwatchFileSystemAsync(string watchId) { throw new NotSupportedException(); }
        }
    }
}
