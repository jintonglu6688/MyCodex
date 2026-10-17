using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class AppServerConfigWriteGuardTests
    {
        [Test]
        public void Evaluate_UsesFreshOwningOriginVersionForOnlyTheRequestedField()
        {
            var snapshot = new AppServerConfigSnapshot();
            snapshot.Origins["mcp_servers.demo"] = new AppServerConfigOrigin { Version = "v17" };

            var result = AppServerConfigWriteGuard.Evaluate(
                snapshot,
                new[] { AppServerConfigWriteEdit.CreateReplace("mcp_servers.demo.enabled", false) });

            Assert.That(result.CanWrite, Is.True);
            Assert.That(result.ExpectedVersion, Is.EqualTo("v17"));
        }

        [Test]
        public void Evaluate_FailsClosedWhenObservedOriginHasNoVersion()
        {
            var snapshot = new AppServerConfigSnapshot();
            snapshot.Origins["mcp_servers.demo"] = new AppServerConfigOrigin();

            var result = AppServerConfigWriteGuard.Evaluate(
                snapshot,
                new[] { AppServerConfigWriteEdit.CreateReplace("mcp_servers.demo.enabled", false) });

            Assert.That(result.CanWrite, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("missing-version"));
        }

        [Test]
        public void EvaluateUserConfig_UsesActiveUserLayerVersionForNewOverride()
        {
            var snapshot = new AppServerConfigSnapshot();
            snapshot.Layers.Add(new AppServerConfigLayer
            {
                SourceType = "user",
                Version = "user-v4"
            });
            snapshot.Origins["web_search"] = new AppServerConfigOrigin
            {
                SourceType = "project",
                Version = "project-v9"
            };

            var result = AppServerConfigWriteGuard.EvaluateUserConfig(
                snapshot,
                new[] { AppServerConfigWriteEdit.CreateReplace("web_search", "live") });

            Assert.That(result.CanWrite, Is.True);
            Assert.That(result.ExpectedVersion, Is.EqualTo("user-v4"));
        }

        [Test]
        public void EvaluateUserConfig_FailsClosedWithoutObservedUserLayer()
        {
            var snapshot = new AppServerConfigSnapshot();
            snapshot.Origins["web_search"] = new AppServerConfigOrigin
            {
                SourceType = "project",
                Version = "project-v9"
            };

            var result = AppServerConfigWriteGuard.EvaluateUserConfig(
                snapshot,
                new[] { AppServerConfigWriteEdit.CreateReplace("web_search", "live") });

            Assert.That(result.CanWrite, Is.False);
            Assert.That(result.ReasonCode, Is.EqualTo("missing-user-layer"));
        }
    }
}
