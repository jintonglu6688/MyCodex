using System;
using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerFuzzyFileSearchMapperTests
    {
        [Test]
        public void BuildSessionUpdated_MapsResults()
        {
            var result = AppServerFuzzyFileSearchMapper.BuildSessionUpdated(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "sessionId", "session-1" },
                    { "query", "agent" },
                    {
                        "files",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "file_name", "AGENTS.md" },
                                { "match_type", "file_name" },
                                { "path", @"E:\Repo\AGENTS.md" },
                                { "root", @"E:\Repo" },
                                { "score", 91 },
                                { "indices", new object[] { 0, 1, 2 } }
                            }
                        }
                    }
                });

            Assert.That(result.SessionId, Is.EqualTo("session-1"));
            Assert.That(result.Query, Is.EqualTo("agent"));
            Assert.That(result.IsCompleted, Is.False);
            Assert.That(result.Files, Has.Count.EqualTo(1));
            Assert.That(result.Files[0].FileName, Is.EqualTo("AGENTS.md"));
            Assert.That(result.Files[0].MatchType, Is.EqualTo("file_name"));
            Assert.That(result.Files[0].Path, Is.EqualTo(@"E:\Repo\AGENTS.md"));
            Assert.That(result.Files[0].Root, Is.EqualTo(@"E:\Repo"));
            Assert.That(result.Files[0].Score, Is.EqualTo(91U));
            Assert.That(result.Files[0].Indices, Is.EqualTo(new uint[] { 0, 1, 2 }));
        }

        [Test]
        public void BuildSessionCompleted_MapsSessionId()
        {
            var result = AppServerFuzzyFileSearchMapper.BuildSessionCompleted(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "sessionId", "session-1" }
                });

            Assert.That(result.SessionId, Is.EqualTo("session-1"));
            Assert.That(result.IsCompleted, Is.True);
            Assert.That(result.Files, Is.Empty);
        }

        [Test]
        public void BuildSearchResponse_MapsImmediateFiles()
        {
            var result = AppServerFuzzyFileSearchMapper.BuildSearchResponse(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "files",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "file_name", "Program.cs" },
                                { "match_type", "file" },
                                { "path", @"E:\Repo\src\Program.cs" },
                                { "root", @"E:\Repo" },
                                { "score", 80 },
                                { "indices", new object[] { 8, 9 } }
                            }
                        }
                    }
                });

            Assert.That(result.IsCompleted, Is.True);
            Assert.That(result.Files, Has.Count.EqualTo(1));
            Assert.That(result.Files[0].FileName, Is.EqualTo("Program.cs"));
            Assert.That(result.Files[0].Path, Is.EqualTo(@"E:\Repo\src\Program.cs"));
            Assert.That(result.Files[0].Indices, Is.EqualTo(new uint[] { 8, 9 }));
        }

        [Test]
        public void BuildSessionUpdated_PartialPayload_ReturnsSafeDefaults()
        {
            var result = AppServerFuzzyFileSearchMapper.BuildSessionUpdated(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "files", new object[] { "bad", new Dictionary<string, object>(StringComparer.Ordinal) } }
                });

            Assert.That(result.SessionId, Is.Null);
            Assert.That(result.Query, Is.Null);
            Assert.That(result.Files, Has.Count.EqualTo(1));
            Assert.That(result.Files[0].FileName, Is.Null);
            Assert.That(result.Files[0].Score, Is.EqualTo(0U));
        }
    }
}
