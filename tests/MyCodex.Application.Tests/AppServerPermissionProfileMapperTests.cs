using System.Collections.Generic;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPermissionProfileMapperTests
    {
        [Test]
        public void BuildListResult_MapsProfilesAndNextCursor()
        {
            var result = AppServerPermissionProfileMapper.BuildListResult(new Dictionary<string, object>
            {
                {
                    "data",
                    new object[]
                    {
                        new Dictionary<string, object> { { "id", "read-only" }, { "description", "Read only access" } },
                        new Dictionary<string, object> { { "id", "workspace-write" }, { "description", null } }
                    }
                },
                { "nextCursor", "cursor-2" }
            });

            Assert.That(result.Profiles, Has.Count.EqualTo(2));
            Assert.That(result.Profiles[0].Id, Is.EqualTo("read-only"));
            Assert.That(result.Profiles[0].Description, Is.EqualTo("Read only access"));
            Assert.That(result.Profiles[1].Id, Is.EqualTo("workspace-write"));
            Assert.That(result.Profiles[1].Description, Is.Null);
            Assert.That(result.NextCursor, Is.EqualTo("cursor-2"));
        }

        [Test]
        public void BuildListResult_SkipsBlankProfileIds()
        {
            var result = AppServerPermissionProfileMapper.BuildListResult(new Dictionary<string, object>
            {
                {
                    "data",
                    new object[]
                    {
                        new Dictionary<string, object> { { "id", " " }, { "description", "Blank" } },
                        new Dictionary<string, object> { { "id", "read-only" } }
                    }
                }
            });

            Assert.That(result.Profiles, Has.Count.EqualTo(1));
            Assert.That(result.Profiles[0].Id, Is.EqualTo("read-only"));
        }
    }
}
