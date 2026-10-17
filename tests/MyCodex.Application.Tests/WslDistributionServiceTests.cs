using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class WslDistributionServiceTests
    {
        [Test]
        public void Parse_PreservesNamesRemovesNullsAndDeduplicatesWithoutCommands()
        {
            Assert.That(WslDistributionService.Parse("\0 Debian \0\r\nUbuntu-24.04\r\ndebian\n  \n"),
                Is.EqualTo(new[] { "Debian", "Ubuntu-24.04" }));
            Assert.That(WslDistributionService.Parse(null), Is.Empty);
        }
    }
}
