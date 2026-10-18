using System;
using System.IO;
using System.Linq;
using MyCodex.Application.Services;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class LocalThreadSessionWatchPlannerTests
    {
        [Test]
        public void BuildWatchRoots_ReturnsExistingSessionRootsAndDeduplicatesHomes()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "MyCodex.Tests", Guid.NewGuid().ToString("N"));
            try
            {
                var firstHome = Path.Combine(tempRoot, "home-a");
                var secondHome = Path.Combine(tempRoot, "home-b");
                Directory.CreateDirectory(Path.Combine(firstHome, "sessions"));

                var roots = LocalThreadSessionWatchPlanner.BuildWatchRoots(
                    new[] { firstHome, firstHome + Path.DirectorySeparatorChar, secondHome, null, " " },
                    Directory.Exists).ToArray();

                Assert.That(roots, Is.EqualTo(new[] { Path.Combine(firstHome, "sessions") }));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
    }
}
