using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerFileSystemMapperTests
    {
        [Test]
        public void BuildDirectoryEntries_MapsKnownEntries()
        {
            var entries = AppServerFileSystemMapper.BuildDirectoryEntries(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "entries",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "fileName", "AGENTS.md" },
                                { "isDirectory", false },
                                { "isFile", true }
                            },
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "fileName", "src" },
                                { "isDirectory", true },
                                { "isFile", false }
                            }
                        }
                    }
                });

            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].FileName, Is.EqualTo("AGENTS.md"));
            Assert.That(entries[0].IsDirectory, Is.False);
            Assert.That(entries[0].IsFile, Is.True);
            Assert.That(entries[1].FileName, Is.EqualTo("src"));
            Assert.That(entries[1].IsDirectory, Is.True);
            Assert.That(entries[1].IsFile, Is.False);
        }

        [Test]
        public void BuildReadResult_MapsBase64Data()
        {
            var result = AppServerFileSystemMapper.BuildReadResult(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "dataBase64", "YWdlbnQ=" }
                });

            Assert.That(result.DataBase64, Is.EqualTo("YWdlbnQ="));
        }

        [Test]
        public void BuildMetadata_MapsTimestampsAndKinds()
        {
            var result = AppServerFileSystemMapper.BuildMetadata(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "isDirectory", false },
                    { "isFile", true },
                    { "isSymlink", true },
                    { "createdAtMs", 100L },
                    { "modifiedAtMs", 200L }
                });

            Assert.That(result.IsDirectory, Is.False);
            Assert.That(result.IsFile, Is.True);
            Assert.That(result.IsSymlink, Is.True);
            Assert.That(result.CreatedAtMs, Is.EqualTo(100L));
            Assert.That(result.ModifiedAtMs, Is.EqualTo(200L));
        }

        [Test]
        public void BuildChangedEvent_MapsWatchIdAndChangedPaths()
        {
            var result = AppServerFileSystemMapper.BuildChangedEvent(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "watchId", "watch-1" },
                    { "changedPaths", new object[] { @"E:\Repo\AGENTS.md", @"E:\Repo\src" } }
                });

            Assert.That(result.WatchId, Is.EqualTo("watch-1"));
            Assert.That(result.ChangedPaths, Is.EqualTo(new[] { @"E:\Repo\AGENTS.md", @"E:\Repo\src" }));
        }

        [Test]
        public void BuildDirectoryEntries_PartialPayload_ReturnsSafeDefaults()
        {
            var entries = AppServerFileSystemMapper.BuildDirectoryEntries(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "entries", new object[] { "bad", new Dictionary<string, object>(StringComparer.Ordinal) } }
                });

            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(entries[0].FileName, Is.Null);
            Assert.That(entries[0].IsDirectory, Is.False);
            Assert.That(entries[0].IsFile, Is.False);
        }
    }
}
