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

        private static string Enc(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        }
    }
}
