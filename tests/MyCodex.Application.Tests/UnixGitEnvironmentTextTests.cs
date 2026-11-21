using System.Text;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UnixGitEnvironmentTextTests
    {
        [Test]
        public void ProbeOutput_IsRedactedInDetailsAndValues()
        {
            const string helper = "!f() { echo password=ghp_SyntheticSecret123; }; f";
            var details = new StringBuilder();

            UnixGitEnvironmentText.AppendDetails(details, "credential.helper", 0, helper, "fatal: https://user:pat-value@example.com/repo.git");
            var value = UnixGitEnvironmentText.GetBestOutput(helper, string.Empty);

            Assert.That(details.ToString(), Does.Not.Contain("ghp_SyntheticSecret123").And.Not.Contain("pat-value"));
            Assert.That(value, Does.Not.Contain("ghp_SyntheticSecret123"));
            Assert.That(value, Does.Contain("credential").Or.Contain("echo"));
        }
    }
}
