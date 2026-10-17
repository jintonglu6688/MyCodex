using System;
using System.IO;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class PluginSkillPathResolverTests
    {
        [Test]
        public void TryResolveCurrentPath_OldHashMissingFindsCurrentSkillDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "MyCodex.PluginSkillPathResolverTests", Guid.NewGuid().ToString("N"));
            try
            {
                var oldSkill = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "oldhash", "skills", "writing-plans");
                var newSkill = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "newhash", "skills", "writing-plans");
                Directory.CreateDirectory(newSkill);
                File.WriteAllText(Path.Combine(newSkill, "SKILL.md"), "---\nname: writing-plans\n---\n");

                string resolved;
                var result = PluginSkillPathResolver.TryResolveCurrentPath(oldSkill, out resolved);

                Assert.That(result, Is.True);
                Assert.That(resolved, Is.EqualTo(newSkill));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void TryResolveCurrentPath_ExistingPathReturnsSamePath()
        {
            var root = Path.Combine(Path.GetTempPath(), "MyCodex.PluginSkillPathResolverTests", Guid.NewGuid().ToString("N"));
            try
            {
                var skill = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "hash", "skills", "systematic-debugging");
                Directory.CreateDirectory(skill);
                File.WriteAllText(Path.Combine(skill, "SKILL.md"), "---\nname: systematic-debugging\n---\n");

                string resolved;
                var result = PluginSkillPathResolver.TryResolveCurrentPath(skill, out resolved);

                Assert.That(result, Is.True);
                Assert.That(resolved, Is.EqualTo(skill));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void TryResolveCurrentPath_ExistingSkillDirectoryWithoutSkillFileFindsCurrentSkillDirectory()
        {
            var root = Path.Combine(Path.GetTempPath(), "MyCodex.PluginSkillPathResolverTests", Guid.NewGuid().ToString("N"));
            try
            {
                var staleSkill = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "oldhash", "skills", "writing-plans");
                var currentSkill = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "newhash", "skills", "writing-plans");
                Directory.CreateDirectory(staleSkill);
                Directory.CreateDirectory(currentSkill);
                File.WriteAllText(Path.Combine(currentSkill, "SKILL.md"), "---\nname: writing-plans\n---\n");

                string resolved;
                var result = PluginSkillPathResolver.TryResolveCurrentPath(staleSkill, out resolved);

                Assert.That(result, Is.True);
                Assert.That(resolved, Is.EqualTo(currentSkill));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        [Test]
        public void TryResolveCurrentPath_UnknownShapeReturnsFalse()
        {
            string resolved;
            var result = PluginSkillPathResolver.TryResolveCurrentPath(@"E:\Repo\skills\demo", out resolved);

            Assert.That(result, Is.False);
            Assert.That(resolved, Is.Null);
        }

        [Test]
        public void TryResolveCurrentPath_OldHashMissingFindsCurrentPluginRoot()
        {
            var root = Path.Combine(Path.GetTempPath(), "MyCodex.PluginSkillPathResolverTests", Guid.NewGuid().ToString("N"));
            try
            {
                var oldPlugin = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "oldhash");
                var newPlugin = Path.Combine(root, ".codex", "plugins", "cache", "openai-curated", "superpowers", "newhash");
                Directory.CreateDirectory(Path.Combine(newPlugin, ".codex-plugin"));
                File.WriteAllText(Path.Combine(newPlugin, ".codex-plugin", "plugin.json"), "{}");

                string resolved;
                var result = PluginSkillPathResolver.TryResolveCurrentPath(oldPlugin, out resolved);

                Assert.That(result, Is.True);
                Assert.That(resolved, Is.EqualTo(newPlugin));
            }
            finally
            {
                DeleteDirectory(root);
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }
}
