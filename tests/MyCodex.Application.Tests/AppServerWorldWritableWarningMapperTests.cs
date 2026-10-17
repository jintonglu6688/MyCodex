using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerWorldWritableWarningMapperTests
    {
        [Test]
        public void BuildEvent_MapsWarningFields()
        {
            var result = AppServerWorldWritableWarningMapper.BuildEvent(new Dictionary<string, object>
            {
                { "samplePaths", new object[] { @"C:\temp\a", @"D:\shared\b" } },
                { "extraCount", 2 },
                { "failedScan", true }
            });

            Assert.That(result.SamplePaths, Is.EqualTo(new[] { @"C:\temp\a", @"D:\shared\b" }));
            Assert.That(result.ExtraCount, Is.EqualTo(2));
            Assert.That(result.FailedScan, Is.True);
        }

        [Test]
        public void BuildEvent_MissingSamplePathsReturnsEmptyList()
        {
            var result = AppServerWorldWritableWarningMapper.BuildEvent(new Dictionary<string, object>
            {
                { "extraCount", "5" }
            });

            Assert.That(result.SamplePaths, Is.Empty);
            Assert.That(result.ExtraCount, Is.EqualTo(5));
            Assert.That(result.FailedScan, Is.False);
        }
    }
}
