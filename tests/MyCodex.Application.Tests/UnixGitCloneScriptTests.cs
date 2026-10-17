using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnixGitCloneScriptTests
    {
        [Test]
        public void CloneScript_UsesPositionalArgumentsAndReportsTargetMarker()
        {
            var script = UnixGitCloneScript.CloneScript;

            Assert.That(script, Does.Contain("repository=${1:-}"));
            Assert.That(script, Does.Contain("parent=${2:-}"));
            Assert.That(script, Does.Contain("folder=${3:-}"));
            Assert.That(script, Does.Contain("submodules=${4:-false}"));
            Assert.That(script, Does.Contain("git -C \"$parent\" clone --progress"));
            Assert.That(script, Does.Contain("MYCODEX_CLONE_TARGET"));
            Assert.That(script, Does.Not.Contain("eval"));
        }

        [TestCase(".")]
        [TestCase("..")]
        [TestCase("bad/name")]
        [TestCase("bad\\name")]
        [TestCase("bad\tname")]
        public void ValidateFolderName_RejectsUnsafeNames(string folderName)
        {
            Assert.Throws<ArgumentException>(() => UnixGitCloneScript.ValidateFolderName(folderName));
        }

        [Test]
        public void ExtractCloneTargetPath_RemovesTargetMarkerAndReturnsCleanOutput()
        {
            string targetPath;

            var output = UnixGitCloneScript.ExtractCloneTargetPath(
                "Cloning into 'repo'...\nMYCODEX_CLONE_TARGET\t/home/demo/repo\n",
                out targetPath);

            Assert.That(targetPath, Is.EqualTo("/home/demo/repo"));
            Assert.That(output, Is.EqualTo("Cloning into 'repo'..."));
        }

        [Test]
        public void ReportBufferedProgress_RedactsSecretsAndSkipsEmptyLines()
        {
            var items = new List<GitCloneProgress>();

            UnixGitCloneScript.ReportBufferedProgress(
                new CollectingProgress(items),
                "https://user:secret@example.com/repo.git\n\ncomplete",
                true);

            Assert.That(items, Has.Count.EqualTo(2));
            Assert.That(items[0].Text, Is.EqualTo("https://***:***@example.com/repo.git"));
            Assert.That(items[0].IsError, Is.True);
            Assert.That(items[1].Text, Is.EqualTo("complete"));
        }

        private sealed class CollectingProgress : IProgress<GitCloneProgress>
        {
            private readonly List<GitCloneProgress> _items;

            public CollectingProgress(List<GitCloneProgress> items)
            {
                _items = items;
            }

            public void Report(GitCloneProgress value)
            {
                _items.Add(value);
            }
        }
    }
}
