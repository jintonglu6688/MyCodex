using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class HookSourceFileEditorTests
    {
        private const string Existing =
            "{\r\n  \"description\": \"keep me\",\r\n  \"hooks\": {\r\n    \"Stop\": [\r\n      {\r\n        \"matcher\": \"x\",\r\n        \"hooks\": [\r\n" +
            "          { \"type\": \"command\", \"command\": \"a\", \"custom\": 1 },\r\n          { \"type\": \"mcp_tool\", \"server\": \"s\", \"tool\": \"t\" }\r\n" +
            "        ]\r\n      }\r\n    ],\r\n    \"PreToolUse\": [ { \"hooks\": [ { \"type\": \"command\", \"command\": \"b\" } ] } ]\r\n  }\r\n}\r\n";

        [Test]
        public void AddHandler_StartsANewFileWithTheOfficialShape()
        {
            var bytes = HookSourceFileEditor.AddHandler(null, "preToolUse", "^Bash$",
                HookSourceFileEditor.BuildCommandHandler("check.ps1", 15, " Checking "));

            Assert.That(Encoding.UTF8.GetString(bytes), Is.EqualTo(
                "{\n  \"hooks\": {\n    \"PreToolUse\": [\n      {\n        \"matcher\": \"^Bash$\",\n        \"hooks\": [\n          {\n" +
                "            \"type\": \"command\",\n            \"command\": \"check.ps1\",\n            \"timeout\": 15,\n" +
                "            \"statusMessage\": \"Checking\"\n          }\n        ]\n      }\n    ]\n  }\n}\n"));
        }

        [Test]
        public void AddHandler_KeepsOtherContentLineEndingsAndBom()
        {
            var original = Bom(Existing);

            var text = Decode(HookSourceFileEditor.AddHandler(original, "stop", null,
                HookSourceFileEditor.BuildMcpToolHandler("srv", "tool", null, null)), out var hasBom);

            Assert.That(hasBom, Is.True);
            Assert.That(text, Does.Contain("\"description\": \"keep me\"").And.Contain("\"custom\": 1").And.Contain("\"command\": \"b\""));
            Assert.That(text.Replace("\r\n", string.Empty), Does.Not.Contain("\n"));
            Assert.That(text.IndexOf("\"server\": \"srv\"", StringComparison.Ordinal), Is.GreaterThan(text.IndexOf("\"command\": \"a\"", StringComparison.Ordinal)));
        }

        [Test]
        public void RemoveHandler_RemovesOnlyTheKeyedHandlerAndEmptyContainers()
        {
            var keepGroup = Decode(HookSourceFileEditor.RemoveHandler(Bom(Existing),
                Hook(@"C:\Users\me\.codex\hooks.json:stop:0:0", "stop", "command", "a")), out _);
            var dropEvent = Decode(HookSourceFileEditor.RemoveHandler(Bom(Existing),
                Hook(@"C:\Users\me\.codex\hooks.json:pre_tool_use:0:0", "preToolUse", "command", "b")), out _);

            Assert.That(keepGroup, Does.Not.Contain("\"command\": \"a\"").And.Contain("\"server\": \"s\"").And.Contain("\"matcher\": \"x\""));
            Assert.That(dropEvent, Does.Not.Contain("PreToolUse").And.Contain("\"command\": \"a\"").And.Contain("keep me"));
        }

        [Test]
        public void RemoveHandler_RefusesWhenThePositionNoLongerHoldsTheListedDefinition()
        {
            Assert.That(HookSourceFileEditor.RemoveHandler(Bom(Existing),
                Hook(@"C:\x\hooks.json:stop:0:0", "stop", "command", "changed")), Is.Null);
            Assert.That(HookSourceFileEditor.RemoveHandler(Bom(Existing),
                Hook(@"C:\x\hooks.json:stop:3:0", "stop", "command", "a")), Is.Null);
        }

        [Test]
        public void Paths_FollowTheTargetsSeparator()
        {
            Assert.That(HookSourceFileEditor.GetProjectHooksPath(@"E:\Repo\"), Is.EqualTo(@"E:\Repo\.codex\hooks.json"));
            Assert.That(HookSourceFileEditor.GetProjectHooksPath("/home/me/repo"), Is.EqualTo("/home/me/repo/.codex/hooks.json"));
            Assert.That(HookSourceFileEditor.GetSiblingPath("/home/me/.codex/config.toml", "hooks.json"), Is.EqualTo("/home/me/.codex/hooks.json"));
        }

        [Test]
        public async Task AddHookAsync_CreatesAMissingFileButNeverReplacesAnUnreadableOne()
        {
            var files = new FakeFileClient();
            var service = new HookManagementService(new HookManagementServiceTests.FakeHookClient(), new HookManagementServiceTests.FakeConfigClient(), files);
            var handler = HookSourceFileEditor.BuildCommandHandler("c", null, null);

            await service.AddHookAsync(@"E:\Repo\.codex\hooks.json", "stop", null, handler);
            Assert.That(files.CreatedDirectories, Is.EqualTo(new[] { @"E:\Repo\.codex" }));
            Assert.That(files.Writes.Keys, Is.EqualTo(new[] { @"E:\Repo\.codex\hooks.json" }));

            files.ExistsButUnreadable = true;
            Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.AddHookAsync(@"E:\Other\hooks.json", "stop", null, handler));
            Assert.That(files.Writes.Count, Is.EqualTo(1));
        }

        private static AppServerHookInfo Hook(string key, string eventName, string handlerType, string command)
        {
            return new AppServerHookInfo
            {
                Key = key,
                EventName = eventName,
                HandlerType = handlerType,
                Command = command,
                Source = "user",
                SourcePath = @"C:\Users\me\.codex\hooks.json"
            };
        }

        private static byte[] Bom(string text)
        {
            return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
        }

        private static string Decode(byte[] bytes, out bool hasBom)
        {
            hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return Encoding.UTF8.GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        }

        private sealed class FakeFileClient : IAppServerFileSystemClient
        {
            public bool ExistsButUnreadable { get; set; }

            public List<string> CreatedDirectories { get; } = new List<string>();

            public Dictionary<string, byte[]> Writes { get; } = new Dictionary<string, byte[]>();

            public Task<AppServerFileSystemReadResult> ReadFileAsync(string path)
            {
                throw ExistsButUnreadable ? new UnauthorizedAccessException() : (Exception)new InvalidOperationException("not found");
            }

            public Task<AppServerFileSystemMetadata> GetMetadataAsync(string path)
            {
                if (!ExistsButUnreadable)
                {
                    throw new InvalidOperationException("not found");
                }

                return Task.FromResult(new AppServerFileSystemMetadata { IsFile = true });
            }

            public Task WriteFileAsync(string path, byte[] data)
            {
                Writes[path] = data;
                return Task.CompletedTask;
            }

            public Task CreateDirectoryAsync(string path, bool? recursive = null)
            {
                CreatedDirectories.Add(path);
                return Task.CompletedTask;
            }

            public Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path)
            {
                throw new NotSupportedException();
            }

            public Task RemoveAsync(string path, bool? recursive = null, bool? force = null)
            {
                throw new NotSupportedException();
            }

            public Task CopyAsync(string sourcePath, string destinationPath, bool recursive)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId)
            {
                throw new NotSupportedException();
            }

            public Task UnwatchFileSystemAsync(string watchId)
            {
                throw new NotSupportedException();
            }
        }
    }
}
