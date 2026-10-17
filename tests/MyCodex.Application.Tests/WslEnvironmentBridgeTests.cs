using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class WslEnvironmentBridgeTests
    {
        [Test]
        public void BuildWslEnvValue_AppendsValidVariables()
        {
            var value = WslEnvironmentBridge.BuildWslEnvValue(
                null,
                new[] { "MYCODEX_PROFILE_API_KEY", "HTTPS_PROXY", "CODEX_HOME" });

            Assert.That(value, Is.EqualTo("MYCODEX_PROFILE_API_KEY:HTTPS_PROXY:CODEX_HOME"));
        }

        [Test]
        public void BuildWslEnvValue_PreservesExistingEntriesAndSkipsDuplicates()
        {
            var value = WslEnvironmentBridge.BuildWslEnvValue(
                "PATH/p:HTTPS_PROXY",
                new[] { "HTTPS_PROXY", "MYCODEX_PROFILE_API_KEY" });

            Assert.That(value, Is.EqualTo("PATH/p:HTTPS_PROXY:MYCODEX_PROFILE_API_KEY"));
        }

        [Test]
        public void BuildWslEnvValue_RejectsInvalidNames()
        {
            var value = WslEnvironmentBridge.BuildWslEnvValue(
                null,
                new[] { "1BAD", "BAD-NAME", "WSLENV", "_GOOD" });

            Assert.That(value, Is.EqualTo("_GOOD"));
        }
    }
}
