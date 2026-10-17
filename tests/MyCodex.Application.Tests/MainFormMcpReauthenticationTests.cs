using System;
using System.IO;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormMcpReauthenticationTests
    {
        [Test]
        public void AppServerEventRouting_RoutesAllMcpStartupStatusesThroughStateHandler()
        {
            var reauthenticationRoute = AppServerEventRouting.Resolve(new AppServerEventArgs(
                "mcp_startup_status_updated",
                "Authentication expired.",
                reason: "reauthenticationRequired",
                mcpServerName: "github"));
            var ordinaryRoute = AppServerEventRouting.Resolve(new AppServerEventArgs(
                "mcp_startup_status_updated",
                "MCP server is ready.",
                mcpServerName: "github"));

            Assert.Multiple(() =>
            {
                Assert.That(reauthenticationRoute, Is.EqualTo(AppServerEventRoute.McpStartupStatusUpdated));
                Assert.That(ordinaryRoute, Is.EqualTo(AppServerEventRoute.McpStartupStatusUpdated));
            });
        }

        [Test]
        public void McpReauthenticationState_PreservesPendingServerAndDeduplicatesSpeech()
        {
            var state = new McpReauthenticationState();

            var first = state.Record(CreateReauthenticationEvent("github"));
            var pending = state.ConsumePending();
            var duplicate = state.Record(CreateReauthenticationEvent("GITHUB"));
            var otherServer = state.Record(CreateReauthenticationEvent("linear"));

            Assert.Multiple(() =>
            {
                Assert.That(first.ShouldSpeak, Is.True);
                Assert.That(pending.ServerName, Is.EqualTo("github"));
                Assert.That(pending.FailureReason, Is.EqualTo("reauthenticationRequired"));
                Assert.That(duplicate.ShouldSpeak, Is.False);
                Assert.That(otherServer.ShouldSpeak, Is.True);
            });
        }

        [Test]
        public void McpReauthenticationState_DoesNotReannounceAnAlreadyActiveServerAfterAnotherServer()
        {
            var state = new McpReauthenticationState();

            var firstServer = state.Record(CreateReauthenticationEvent("github"));
            var secondServer = state.Record(CreateReauthenticationEvent("linear"));
            var firstServerAgain = state.Record(CreateReauthenticationEvent("GITHUB"));

            Assert.Multiple(() =>
            {
                Assert.That(firstServer.ShouldSpeak, Is.True);
                Assert.That(secondServer.ShouldSpeak, Is.True);
                Assert.That(firstServerAgain.ShouldSpeak, Is.False);
            });
        }

        [TestCase("github")]
        [TestCase(null)]
        public void McpReauthenticationState_ReannouncesAfterServerRecovery(string serverName)
        {
            var state = new McpReauthenticationState();

            var firstFailure = state.Record(CreateReauthenticationEvent(serverName));
            var recovery = state.Record(CreateOrdinaryStartupEvent(serverName));
            var secondFailure = state.Record(CreateReauthenticationEvent(serverName));

            Assert.Multiple(() =>
            {
                Assert.That(firstFailure.ShouldSpeak, Is.True);
                Assert.That(recovery, Is.Null);
                Assert.That(secondFailure.ShouldSpeak, Is.True);
            });
        }

        [Test]
        public void MainForm_RoutesAndPassesSharedMcpReauthenticationStateToManagementForm()
        {
            var eventSource = File.ReadAllText(GetMainFormPath("MainForm.AppServerEvents.cs"));
            var settingsSource = File.ReadAllText(GetMainFormPath("MainForm.SessionSettings.cs"));
            var reauthenticationPath = GetMainFormPath("MainForm.McpReauthentication.cs");
            Assert.That(File.Exists(reauthenticationPath), Is.True, "Focused MCP reauthentication partial should exist.");
            var reauthenticationSource = File.ReadAllText(reauthenticationPath);

            Assert.Multiple(() =>
            {
                Assert.That(eventSource, Does.Contain("case AppServerEventRoute.McpStartupStatusUpdated:"));
                Assert.That(eventSource, Does.Contain("HandleMcpStartupStatusUpdated(e);"));
                Assert.That(reauthenticationSource, Does.Contain("McpReauthenticationState _mcpReauthenticationState"));
                Assert.That(reauthenticationSource, Does.Contain("SetHighPriorityStatus(instruction);"));
                Assert.That(reauthenticationSource, Does.Contain("SpeakAuxiliaryTextWithScreenReader(instruction);"));
                Assert.That(settingsSource, Does.Contain("_mcpReauthenticationState"));
            });
        }

        private static AppServerEventArgs CreateReauthenticationEvent(string serverName)
        {
            return new AppServerEventArgs(
                "mcp_startup_status_updated",
                "Authentication expired.",
                reason: "reauthenticationRequired",
                mcpServerName: serverName,
                itemStatus: "failed");
        }

        private static AppServerEventArgs CreateOrdinaryStartupEvent(string serverName)
        {
            return new AppServerEventArgs(
                "mcp_startup_status_updated",
                "MCP server is ready.",
                mcpServerName: serverName,
                itemStatus: "ready");
        }

        private static string GetMainFormPath(string fileName)
        {
            return Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms",
                "Forms",
                fileName));
        }
    }
}
