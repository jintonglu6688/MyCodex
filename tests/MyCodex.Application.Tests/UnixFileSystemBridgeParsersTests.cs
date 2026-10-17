using System;
using System.Text;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnixFileSystemBridgeParsersTests
    {
        [Test]
        public void ParseDirectoryListing_DecodesBase64AndSortsDirectoriesFirst()
        {
            var output =
                "file\t" + Enc("b.txt") + "\t" + Enc("/home/demo/b.txt") + "\t0\t12\t1700000000\n" +
                "dir\t" + Enc("src") + "\t" + Enc("/home/demo/src") + "\t0\t\t1700000001\n";

            var entries = UnixFileSystemBridgeParsers.ParseDirectoryListing(output);

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Name, Is.EqualTo("src"));
            Assert.That(entries[0].IsDirectory, Is.True);
            Assert.That(entries[1].Name, Is.EqualTo("b.txt"));
            Assert.That(entries[1].Size, Is.EqualTo(12));
        }

        [Test]
        public void ParseKeyValueOutput_ReadsGitInfoFields()
        {
            var values = UnixFileSystemBridgeParsers.ParseKeyValueOutput("branch=main\nsha=abc\norigin=https://example.com/repo.git\n");

            Assert.That(UnixFileSystemBridgeParsers.GetValue(values, "branch"), Is.EqualTo("main"));
            Assert.That(UnixFileSystemBridgeParsers.GetValue(values, "sha"), Is.EqualTo("abc"));
            Assert.That(UnixFileSystemBridgeParsers.GetValue(values, "origin"), Is.EqualTo("https://example.com/repo.git"));
        }

        [Test]
        public void ParseStatusEntry_NormalizesStatusAndPath()
        {
            var entry = UnixFileSystemBridgeParsers.ParseStatusEntry(" M \"src\\\\app.cs\"");

            Assert.That(entry.FilePath, Is.EqualTo("src/app.cs"));
            Assert.That(entry.ChangeKind, Is.EqualTo("modified"));
        }

        [Test]
        public void BuildDiffTextForStatus_ReturnsUntrackedPlaceholder()
        {
            var entry = UnixFileSystemBridgeParsers.ParseStatusEntry("?? notes.txt");

            entry.DiffText = UnixFileSystemBridgeParsers.BuildDiffText(entry, string.Empty, string.Empty);

            Assert.That(entry.DiffText, Does.Contain("Untracked file: notes.txt"));
        }

        [Test]
        public void PopulateDiffLineCounts_IgnoresHeaders()
        {
            var entry = UnixFileSystemBridgeParsers.ParseStatusEntry(" M src/app.cs");
            entry.DiffText = "diff --git a/src/app.cs b/src/app.cs\n--- a/src/app.cs\n+++ b/src/app.cs\n@@ -1 +1 @@\n-old\n+new\n";
            entry.AddedLineCount = 20;
            entry.DeletedLineCount = 30;

            UnixFileSystemBridgeParsers.PopulateDiffLineCounts(entry);
            UnixFileSystemBridgeParsers.PopulateDiffLineCounts(entry);

            Assert.That(entry.AddedLineCount, Is.EqualTo(1));
            Assert.That(entry.DeletedLineCount, Is.EqualTo(1));

            entry.DiffText = string.Empty;
            UnixFileSystemBridgeParsers.PopulateDiffLineCounts(entry);

            Assert.That(entry.AddedLineCount, Is.Zero);
            Assert.That(entry.DeletedLineCount, Is.Zero);
        }

        private static string Enc(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }
    }
}
