using System;
using System.IO;
using System.Linq;
using MyCodex.Infrastructure.Diagnostics;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SessionTraceLogTests
    {
        [Test]
        public void RetainedSessionLogCount_IsFive()
        {
            Assert.That(SessionTraceLog.RetainedSessionLogCount, Is.EqualTo(5));
        }

        [Test]
        public void BuildFileName_UsesSessionTimestampAndProcessId()
        {
            var name = SessionTraceLog.BuildFileName(
                new DateTimeOffset(2026, 8, 4, 12, 34, 56, 789, TimeSpan.FromHours(8)),
                4321);

            Assert.That(name, Is.EqualTo("app-20260804-123456-789-p4321.log"));
        }

        [Test]
        public void FindExpiredSessionLogs_PreservesCurrentAndNewestSessions()
        {
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "session-log-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                var paths = Enumerable.Range(0, 5)
                    .Select(index => Path.Combine(directory, "app-20260804-12000" + index + "-000-p1.log"))
                    .ToArray();
                for (var index = 0; index < paths.Length; index++)
                {
                    File.WriteAllText(paths[index], index.ToString());
                    File.SetLastWriteTimeUtc(paths[index], new DateTime(2026, 8, 4, 4, 0, index, DateTimeKind.Utc));
                }

                var expired = SessionTraceLog.FindExpiredSessionLogs(directory, paths[4], 3);

                Assert.That(expired, Is.EquivalentTo(new[] { paths[0], paths[1] }));
                Assert.That(expired, Does.Not.Contain(paths[4]));
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
