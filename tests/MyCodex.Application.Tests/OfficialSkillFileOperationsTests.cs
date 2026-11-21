using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.SourceGuards;
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
        public async Task EditBodyAndPrompt_PreservesFrontMatterAndOtherAgentSettings()
        {
            var files = new FakeFileSystemClient();
            files.Files["C:/target/.codex/skills/demo/SKILL.md"] =
                "---\nname: demo\ndescription: >\n  Use for demo\n  tasks.\nlicense: MIT\nmetadata:\n  short-description: Demo\n---\n\nOld body\n";
            files.Files["C:/target/.codex/skills/demo/agents/openai.yaml"] =
                "interface:\n  display_name: \"Demo\"\n  default_prompt: \"Old prompt\"\npolicy:\n  allow_implicit_invocation: false\n";
            var skill = new AppServerSkillInfo
            {
                Scope = "user",
                Path = "C:/target/.codex/skills/demo/SKILL.md",
                DisplayName = "Demo",
                DefaultPrompt = "Old prompt"
            };
            var operations = new OfficialSkillFileOperations(files, "E:/repo");
            operations.RefreshObservations(new[] { skill });

            var model = await operations.LoadForEditAsync(skill);
            Assert.That(model.Description, Is.EqualTo("Use for demo tasks."));
            model.Body = "New body";
            model.DefaultPrompt = "New prompt";
            await operations.SaveAsync(model, false);

            var markdown = files.Files["C:/target/.codex/skills/demo/SKILL.md"];
            Assert.That(markdown, Does.Contain("description: >").And.Contain("  tasks.").And.Contain("license: MIT").And.Contain("  short-description: Demo"));
            Assert.That(markdown, Does.Contain("New body").And.Not.Contain("Old body"));
            var agent = files.Files["C:/target/.codex/skills/demo/agents/openai.yaml"];
            Assert.That(agent, Does.Contain("  default_prompt: \"New prompt\"").And.Not.Contain("Old prompt"));
            Assert.That(agent, Does.Contain("  display_name: \"Demo\"").And.Contain("  allow_implicit_invocation: false"));
            Assert.That(files.CreatedDirectories, Is.Empty);
        }

        [Test]
        public async Task EditDescription_ReplacesOnlyThatFieldAndCreatesAgentFileWhenMissing()
        {
            var files = new FakeFileSystemClient();
            files.Files["C:/target/.codex/skills/demo/SKILL.md"] =
                "---\nname: demo\ndescription: |\n  Old text\nlicense: MIT\n---\nBody\n";
            var skill = new AppServerSkillInfo { Scope = "user", Path = "C:/target/.codex/skills/demo/SKILL.md" };
            var operations = new OfficialSkillFileOperations(files, "E:/repo");
            operations.RefreshObservations(new[] { skill });

            var model = await operations.LoadForEditAsync(skill);
            model.Description = "New text";
            model.DisplayName = "Demo Skill";
            await operations.SaveAsync(model, false);

            var markdown = files.Files["C:/target/.codex/skills/demo/SKILL.md"];
            Assert.That(markdown, Does.Contain("description: \"New text\"").And.Contain("license: MIT").And.Contain("name: demo"));
            Assert.That(markdown, Does.Not.Contain("Old text"));
            Assert.That(files.CreatedDirectories, Is.EqualTo(new[] { "C:/target/.codex/skills/demo/agents" }));
            Assert.That(files.Files["C:/target/.codex/skills/demo/agents/openai.yaml"], Is.EqualTo("interface:\n  display_name: \"Demo Skill\"\n"));
        }

        private sealed class FakeFileSystemClient : IAppServerFileSystemClient
        {
            public readonly List<string> CreatedDirectories = new List<string>();
            public readonly Dictionary<string, string> Writes = new Dictionary<string, string>(StringComparer.Ordinal);
            public readonly Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);

            public Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path)
            {
                var prefix = path.TrimEnd('/') + "/";
                var names = Files.Keys.Concat(CreatedDirectories)
                    .Where(item => item.StartsWith(prefix, StringComparison.Ordinal))
                    .Select(item => item.Substring(prefix.Length).Split('/')[0])
                    .Distinct()
                    .Select(name => new AppServerFileSystemEntry { FileName = name })
                    .ToList();
                return Task.FromResult(names);
            }

            public Task<AppServerFileSystemReadResult> ReadFileAsync(string path)
            {
                return Task.FromResult(new AppServerFileSystemReadResult
                {
                    DataBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Files[path]))
                });
            }
            public Task<AppServerFileSystemMetadata> GetMetadataAsync(string path) { throw new NotSupportedException(); }

            public Task WriteFileAsync(string path, byte[] data)
            {
                Writes[path] = Encoding.UTF8.GetString(data);
                Files[path] = Writes[path];
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
