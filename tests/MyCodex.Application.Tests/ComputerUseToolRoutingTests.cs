using System.Collections.Generic;
using MyCodex.Application.Capabilities;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseToolRoutingTests
    {
        [Test]
        public void RoutesNativeSettingsToAuthorizedNodeReplAndBrowserPagesToCuaRepl()
        {
            var text = ComputerUseToolRoutingInstructionComposer.Compose("Existing Office instructions", true, true);
            Assert.That(text, Does.StartWith("Existing Office instructions"));
            Assert.That(text, Does.Contain("mcp__node_repl__js").And.Contain("@oai/sky"));
            Assert.That(text, Does.Contain("mcp__cua_repl__js").And.Contain("external browser pages only"));
            Assert.That(text, Does.Contain("Trusted RPC service is not configured: sky"));
            Assert.That(ComputerUseToolRoutingInstructionComposer.Compose(text, true, true), Is.EqualTo(text));
        }

        [Test]
        public void BrowserOnlyConversationDoesNotClaimNativeAccess()
        {
            var text = ComputerUseToolRoutingInstructionComposer.Compose(null, false, true);
            Assert.That(text, Does.Contain("Native desktop control is unavailable"));
            Assert.That(text, Does.Not.Contain("owns the authorized desktop connection"));
            Assert.That(ComputerUseToolRoutingInstructionComposer.Compose("keep", false, false), Is.EqualTo("keep"));
        }

        [Test]
        public void ThreadOverridePreservesOtherInstructionsAndDoesNotChangeInputsOrGlobalConfig()
        {
            var config = new Dictionary<string, object> { { "developer_instructions", "global instructions" } };
            var overrides = new Dictionary<string, object> { { "developer_instructions", "thread instructions" }, { "model", "keep" } };
            var result = ComputerUseConfigurationPlanner.ApplyToolRouting(config, overrides, true, true);
            Assert.That(result["developer_instructions"], Does.StartWith("thread instructions"));
            Assert.That(result["model"], Is.EqualTo("keep"));
            Assert.That(config["developer_instructions"], Is.EqualTo("global instructions"));
            Assert.That(overrides["developer_instructions"], Is.EqualTo("thread instructions"));
            var inherited = ComputerUseConfigurationPlanner.ApplyToolRouting(config, new Dictionary<string, object>(), true, false);
            Assert.That(inherited["developer_instructions"], Does.StartWith("global instructions"));
            var cleared = ComputerUseConfigurationPlanner.ApplyToolRouting(config,
                new Dictionary<string, object> { { "developer_instructions", null } }, true, true);
            Assert.That(cleared["developer_instructions"], Does.Not.Contain("global instructions"));
        }
    }
}
