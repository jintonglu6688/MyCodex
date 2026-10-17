using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerNotificationFactoryTests
    {
        [Test]
        public void CreateTurnFailed_UsesFallbackMessageWhenErrorIsMissing()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateTurnFailed("thread-1", "turn-1", null);

                Assert.That(args.EventType, Is.EqualTo("turn_failed"));
                Assert.That(args.Content, Is.EqualTo("Current turn request failed."));
                Assert.That(args.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(args.TurnId, Is.EqualTo("turn-1"));
            });
        }

        [Test]
        public void CreateAuthChanged_IncludesPlanTypeWhenProvided()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateAuthChanged("chatgpt", "pro");

                Assert.That(args.EventType, Is.EqualTo("auth_changed"));
                Assert.That(args.Content, Is.EqualTo("Account status updated: chatgpt / pro"));
            });
        }

        [Test]
        public void CreateThreadClosed_BuildsThreadClosedEvent()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateThreadClosed("thread-1");

                Assert.That(args.EventType, Is.EqualTo("thread_closed"));
                Assert.That(args.Content, Is.EqualTo("Session closed."));
                Assert.That(args.ThreadId, Is.EqualTo("thread-1"));
            });
        }

        [Test]
        public void CreateMcpOauthLoginCompleted_UsesFailureEventWhenLoginFails()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateMcpOauthLoginCompleted("github", false, null);

                Assert.That(args.EventType, Is.EqualTo("error"));
                Assert.That(args.Content, Is.EqualTo("MCP server login failed: github"));
                Assert.That(args.RequestId, Is.EqualTo("github"));
            });
        }

        [Test]
        public void CreateTokenUsage_PreservesTokenCounts()
        {
            var args = AppServerNotificationFactory.CreateTokenUsage("thread-1", "turn-1", 1234, 64000);

            Assert.That(args.EventType, Is.EqualTo("token_usage"));
            Assert.That(args.TotalTokens, Is.EqualTo(1234));
            Assert.That(args.ModelContextWindow, Is.EqualTo(64000));
        }

        [Test]
        public void CreateTokenUsage_PreservesDetailedThreadUsage()
        {
            var usage = new AppServerThreadTokenUsageSnapshot
            {
                ModelContextWindow = 64000,
                Total = new AppServerTokenUsageBreakdown
                {
                    TotalTokens = 1234,
                    InputTokens = 800,
                    CachedInputTokens = 300,
                    OutputTokens = 100,
                    ReasoningOutputTokens = 34
                },
                Last = new AppServerTokenUsageBreakdown
                {
                    TotalTokens = 234,
                    InputTokens = 140,
                    CachedInputTokens = 60,
                    OutputTokens = 20,
                    ReasoningOutputTokens = 14
                }
            };

            var args = AppServerNotificationFactory.CreateTokenUsage("thread-1", "turn-1", usage);

            Assert.That(args.EventType, Is.EqualTo("token_usage"));
            Assert.That(args.ThreadTokenUsage, Is.SameAs(usage));
            Assert.That(args.TotalTokens, Is.EqualTo(1234));
            Assert.That(args.ModelContextWindow, Is.EqualTo(64000));
        }

        [Test]
        public void CreateConfigWarning_BuildsVisibleProtocolWarning()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateConfigWarning(new Dictionary<string, object>
                {
                    { "message", "config file contains deprecated key" },
                    { "threadId", "thread-1" }
                });

                Assert.That(args.EventType, Is.EqualTo("protocol_warning"));
                Assert.That(args.Content, Is.EqualTo("Configuration warning: config file contains deprecated key"));
                Assert.That(args.ThreadId, Is.EqualTo("thread-1"));
            });
        }

        [Test]
        public void CreateModelRerouted_BuildsVisibleProtocolNotice()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateModelRerouted(new Dictionary<string, object>
                {
                    { "fromModel", "gpt-5.4" },
                    { "toModel", "gpt-5.4-mini" },
                    { "reason", "quota fallback" }
                });

                Assert.That(args.EventType, Is.EqualTo("protocol_notice"));
                Assert.That(args.Content, Is.EqualTo("Model rerouted: gpt-5.4 -> gpt-5.4-mini. quota fallback"));
            });
        }

        [TestCase(null, "Deprecation notice: Use pagination.")]
        [TestCase("Use pagination.", "Deprecation notice: Use pagination.")]
        [TestCase("Use thread/turns/list.", "Deprecation notice: Use pagination.\nUse thread/turns/list.")]
        public void DeprecationNotice_PreservesSummaryAndDetails(string details, string expected)
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateDeprecationNotice(new Dictionary<string, object>
                {
                    { "summary", "Use pagination." }, { "details", details }
                });
                Assert.That(args.EventType, Is.EqualTo("protocol_warning"));
                Assert.That(args.Content, Is.EqualTo(expected.Replace("\n", Environment.NewLine)));
                Assert.That(args.ThreadId, Is.Null);
            });
        }

        [TestCase("message", "Old message")]
        [TestCase("details", "Details only")]
        public void DeprecationNotice_PreservesLegacyPayloads(string field, string text)
        {
            WithCulture("en-US", () => Assert.That(AppServerNotificationFactory.CreateDeprecationNotice(
                new Dictionary<string, object> { { field, text } }).Content, Is.EqualTo("Deprecation notice: " + text)));
        }

        [Test]
        public void CreateModelVerification_FailedStatusBuildsProtocolWarning()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateModelVerification(new Dictionary<string, object>
                {
                    { "model", "gpt-5.4" },
                    { "status", "failed" }
                });

                Assert.That(args.EventType, Is.EqualTo("protocol_warning"));
                Assert.That(args.Content, Is.EqualTo("Model verification: gpt-5.4: failed"));
            });
        }

        [Test]
        public void CreateUnhandledNotification_IncludesMethodAndMessage()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateUnhandledNotification(
                    "runtime/newNotice",
                    new Dictionary<string, object>
                    {
                        { "message", "new runtime payload" }
                    });

                Assert.That(args.EventType, Is.EqualTo("protocol_warning"));
                Assert.That(args.Content, Is.EqualTo("Received an unhandled App Server notification: runtime/newNotice. new runtime payload"));
                Assert.That(args.Details, Is.EqualTo("runtime/newNotice"));
            });
        }

        [Test]
        public void CreateMcpServerStartupStatusUpdated_ReadyStatusBuildsQuietMcpEvent()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateMcpServerStartupStatusUpdated(new Dictionary<string, object>
                {
                    { "serverName", "github" },
                    { "status", "ready" }
                });

                Assert.That(args.EventType, Is.EqualTo("mcp_startup_status_updated"));
                Assert.That(args.Content, Is.EqualTo("MCP server startup status updated: github, ready."));
                Assert.That(args.McpServerName, Is.EqualTo("github"));
                Assert.That(args.ItemStatus, Is.EqualTo("ready"));
            });
        }

        [Test]
        public void CreateMcpServerStartupStatusUpdated_ReauthenticationRequiredBuildsTypedEvent()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateMcpServerStartupStatusUpdated(new Dictionary<string, object>
                {
                    { "threadId", "thread-1" },
                    { "name", "github" },
                    { "status", "failed" },
                    { "failureReason", "reauthenticationRequired" }
                });

                Assert.That(args.EventType, Is.EqualTo("mcp_startup_status_updated"));
                Assert.That(args.Content, Is.EqualTo("MCP server authentication expired: github."));
                Assert.That(args.Reason, Is.EqualTo("reauthenticationRequired"));
                Assert.That(args.McpServerName, Is.EqualTo("github"));
                Assert.That(args.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(args.ItemStatus, Is.EqualTo("failed"));
            });
        }

        [Test]
        public void CreateMcpServerStartupStatusUpdated_ReauthenticationRequiredOmitsUnsafeAuthUrl()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateMcpServerStartupStatusUpdated(new Dictionary<string, object>
                {
                    { "name", "github" },
                    { "status", "failed" },
                    { "failureReason", "reauthenticationRequired" },
                    { "message", "Open https://auth.example.test/login?token=secret" }
                });

                Assert.That(args.Content, Is.EqualTo("MCP server authentication expired: github."));
                Assert.That(args.Content, Does.Not.Contain("https://"));
                Assert.That(args.Content, Does.Not.Contain("secret"));
            });
        }

        [Test]
        public void CreateMcpServerStartupStatusUpdated_FailedStatusBuildsProtocolWarning()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateMcpServerStartupStatusUpdated(new Dictionary<string, object>
                {
                    { "name", "github" },
                    {
                        "status",
                        new Dictionary<string, object>
                        {
                            { "type", "failed" },
                            { "message", "missing token" }
                        }
                    }
                });

                Assert.That(args.EventType, Is.EqualTo("protocol_warning"));
                Assert.That(args.Content, Is.EqualTo("MCP server startup status updated: github, failed. missing token"));
                Assert.That(args.Details, Is.EqualTo("mcpServer/startupStatus/updated"));
            });
        }

        [Test]
        public void CreateAutoApprovalReviewStarted_BuildsTypedReviewEvent()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateAutoApprovalReviewStarted(new Dictionary<string, object>
                {
                    { "threadId", "thread-1" },
                    { "turnId", "turn-1" },
                    { "reviewId", "review-1" },
                    { "targetItemId", "item-1" },
                    { "startedAtMs", 123L },
                    { "review", new Dictionary<string, object> { { "status", "inProgress" }, { "riskLevel", "medium" } } },
                    { "action", new Dictionary<string, object> { { "type", "command" }, { "command", "npm test" } } }
                });

                Assert.That(args.EventType, Is.EqualTo("auto_approval_review_started"));
                Assert.That(args.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(args.TurnId, Is.EqualTo("turn-1"));
                Assert.That(args.GuardianApprovalReview, Is.Not.Null);
                Assert.That(args.GuardianApprovalReview.ReviewId, Is.EqualTo("review-1"));
                Assert.That(args.Content, Does.Contain("review-1"));
            });
        }

        [Test]
        public void CreateAutoApprovalReviewCompleted_BuildsTypedReviewEvent()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateAutoApprovalReviewCompleted(new Dictionary<string, object>
                {
                    { "threadId", "thread-1" },
                    { "turnId", "turn-1" },
                    { "reviewId", "review-1" },
                    { "startedAtMs", 123L },
                    { "completedAtMs", 456L },
                    { "decisionSource", "policy" },
                    { "review", new Dictionary<string, object> { { "status", "approved" }, { "riskLevel", "low" } } },
                    { "action", new Dictionary<string, object> { { "type", "mcpToolCall" }, { "server", "github" }, { "toolName", "list_issues" } } }
                });

                Assert.That(args.EventType, Is.EqualTo("auto_approval_review_completed"));
                Assert.That(args.GuardianApprovalReview, Is.Not.Null);
                Assert.That(args.GuardianApprovalReview.IsCompleted, Is.True);
                Assert.That(args.GuardianApprovalReview.DecisionSource, Is.EqualTo("policy"));
            });
        }

        [Test]
        public void CreateTurnModerationMetadata_BuildsSanitizedDiagnosticEvent()
        {
            WithCulture("en-US", () =>
            {
                var args = AppServerNotificationFactory.CreateTurnModerationMetadata(new Dictionary<string, object>
                {
                    { "threadId", "thread-1" },
                    { "turnId", "turn-1" },
                    { "metadata", new Dictionary<string, object> { { "decision", "blocked" }, { "secret", "do-not-copy" } } }
                });

                Assert.That(args.EventType, Is.EqualTo("turn_moderation_metadata"));
                Assert.That(args.ModerationMetadata, Is.Not.Null);
                Assert.That(args.Content, Does.Contain("blocked"));
                Assert.That(args.Details, Does.Not.Contain("do-not-copy"));
            });
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            }
        }
    }
}
