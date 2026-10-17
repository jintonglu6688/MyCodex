using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class CodexThreadHistoryRepairServiceTests
    {
        [Test]
        public async Task TryRepairInvalidEncryptedContentAsync_RemovesAllEncryptedContentFields()
        {
            var root = CreateUniqueDirectory();
            var sessionPath = Path.Combine(root, "rollout-demo.jsonl");
            var corrupted = "{\"type\":\"response_item\",\"payload\":{\"type\":\"reasoning\",\"encrypted_content\":\"gAAAAABdemo==</thinking>\\n\\n我来定位焦点恢复逻辑。\",\"summary\":[],\"content\":[]}}";
            var normal = "{\"type\":\"response_item\",\"payload\":{\"type\":\"reasoning\",\"encrypted_content\":\"gAAAAABnormal==\",\"summary\":[],\"content\":[]}}";
            File.WriteAllText(sessionPath, corrupted + "\n" + normal + "\n", new UTF8Encoding(false));
            var service = new CodexThreadHistoryRepairService(new[] { root });

            var result = await service.TryRepairInvalidEncryptedContentAsync(new ChatSession { SessionPath = sessionPath });

            Assert.That(result.Repaired, Is.True);
            Assert.That(result.RepairCount, Is.EqualTo(2));
            var lines = File.ReadAllLines(sessionPath, Encoding.UTF8);
            var repairedPayload = ReadPayload(lines[0]);
            var normalPayload = ReadPayload(lines[1]);
            Assert.That(repairedPayload.ContainsKey("encrypted_content"), Is.False);
            Assert.That(repairedPayload["type"], Is.EqualTo("reasoning"));
            Assert.That(repairedPayload.ContainsKey("summary"), Is.True);
            Assert.That(normalPayload.ContainsKey("encrypted_content"), Is.False);
            Assert.That(Directory.GetFiles(root, "*.bak"), Has.Length.EqualTo(1));
        }

        [Test]
        public async Task TryRepairInvalidEncryptedContentAsync_LeavesUnsupportedPathUnchanged()
        {
            var root = CreateUniqueDirectory();
            var sessionPath = Path.Combine(root, "rollout-demo.jsonl");
            var original = "{\"type\":\"response_item\",\"payload\":{\"type\":\"reasoning\",\"encrypted_content\":\"gAAAAABdemo==</thinking>\\n\\n我来定位焦点恢复逻辑。\"}}";
            File.WriteAllText(sessionPath, original + "\n", new UTF8Encoding(false));
            var service = new CodexThreadHistoryRepairService(new[] { Path.Combine(root, "other") });

            var result = await service.TryRepairInvalidEncryptedContentAsync(new ChatSession { SessionPath = sessionPath });

            Assert.That(result.Repaired, Is.False);
            Assert.That(File.ReadAllText(sessionPath, Encoding.UTF8), Is.EqualTo(original + "\n"));
        }

        [Test]
        public async Task TryRepairInvalidEncryptedContentAsync_RepairsProfileSessionRoot()
        {
            var root = CreateUniqueDirectory();
            var profileSessionsRoot = Path.Combine(root, "profiles", "work", ".codex", "sessions");
            Directory.CreateDirectory(profileSessionsRoot);
            var sessionPath = Path.Combine(profileSessionsRoot, "rollout-profile.jsonl");
            File.WriteAllText(
                sessionPath,
                "{\"type\":\"response_item\",\"payload\":{\"encrypted_content\":\"encrypted\",\"content\":[]}}\n",
                new UTF8Encoding(false));
            var service = new CodexThreadHistoryRepairService(new[] { profileSessionsRoot });

            var result = await service.TryRepairInvalidEncryptedContentAsync(new ChatSession { SessionPath = sessionPath });

            Assert.That(result.Repaired, Is.True);
            Assert.That(File.ReadAllText(sessionPath, Encoding.UTF8), Does.Not.Contain("encrypted_content"));
        }

        [Test]
        public async Task TryRepairInvalidEncryptedContentAsync_PreservesOriginalWhenReplaceFails()
        {
            var root = CreateUniqueDirectory();
            var sessionPath = Path.Combine(root, "rollout-demo.jsonl");
            var original = "{\"type\":\"response_item\",\"payload\":{\"encrypted_content\":\"encrypted\",\"content\":[]}}\n";
            File.WriteAllText(sessionPath, original, new UTF8Encoding(false));
            var service = new CodexThreadHistoryRepairService(
                new[] { root },
                (source, destination, backup) => { throw new IOException("injected replace failure"); });

            var result = await service.TryRepairInvalidEncryptedContentAsync(new ChatSession { SessionPath = sessionPath });

            Assert.That(result.Repaired, Is.False);
            Assert.That(File.ReadAllText(sessionPath, Encoding.UTF8), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
        }

        [Test]
        public async Task TryRepairInvalidEncryptedContentAsync_PreservesOriginalWhenRepairedJsonLinesAreInvalid()
        {
            var root = CreateUniqueDirectory();
            var sessionPath = Path.Combine(root, "rollout-demo.jsonl");
            var original = "{\"type\":\"response_item\",\"payload\":{\"encrypted_content\":\"encrypted\"}\n";
            File.WriteAllText(sessionPath, original, new UTF8Encoding(false));
            var service = new CodexThreadHistoryRepairService(new[] { root });

            var result = await service.TryRepairInvalidEncryptedContentAsync(new ChatSession { SessionPath = sessionPath });

            Assert.That(result.Repaired, Is.False);
            Assert.That(File.ReadAllText(sessionPath, Encoding.UTF8), Is.EqualTo(original));
            Assert.That(Directory.GetFiles(root, "*.tmp"), Is.Empty);
        }

        private static Dictionary<string, object> ReadPayload(string line)
        {
            var serializer = new JavaScriptSerializer();
            var root = serializer.DeserializeObject(line) as Dictionary<string, object>;
            Assert.That(root, Is.Not.Null);
            return root["payload"] as Dictionary<string, object>;
        }

        private static string CreateUniqueDirectory()
        {
            var root = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "thread-history-repair-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }
    }
}
