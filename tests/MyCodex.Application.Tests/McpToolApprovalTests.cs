using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class McpToolApprovalTests
    {
        internal static IDictionary<string, object> Request()
        {
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                "{\"threadId\":\"thread-1\",\"serverName\":\"probe\",\"mode\":\"form\",\"message\":\"Allow probe to call echo?\",\"requestedSchema\":{\"type\":\"object\",\"properties\":{}},\"_meta\":{\"codex_approval_kind\":\"mcp_tool_call\",\"persist\":[\"session\",\"always\"]}}");
        }

        private static IDictionary<string, object> ComputerUseRequest()
        {
            // Shape captured from official node_repl/WindowsHelperTransport against an
            // owned test window. Thread/turn identities are replaced with synthetic values.
            return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                "{\"threadId\":\"thread-1\",\"turnId\":null,\"serverName\":\"node_repl\",\"mode\":\"form\",\"_meta\":{\"codex_approval_kind\":\"mcp_tool_call\",\"connector_id\":\"computer-use\",\"connector_name\":\"Computer Use\",\"persist\":[\"session\",\"always\"],\"x-codex-turn-metadata\":{\"session_id\":\"thread-1\",\"turn_id\":\"turn-1\"},\"riskLevel\":\"low\",\"tool_params\":{\"app\":\"cuatestwindow.exe\"},\"tool_params_display\":[{\"display_name\":\"App\",\"name\":\"app\",\"value\":\"cuatestwindow\"}]},\"message\":\"Allow Codex to use cuatestwindow?\",\"requestedSchema\":{\"type\":\"object\",\"properties\":{}}}");
        }

        [TestCase("en-US", "Always allow this app", "Always allow this tool")]
        [TestCase("zh-CN", "始终允许此应用", "始终允许此工具")]
        public void ComputerUsePersistentChoiceNamesTheApplication(string culture, string appLabel, string toolLabel)
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var request = ComputerUseRequest();
                Assert.That(AppServerMcpToolApprovalMapper.IsToolApproval(request), Is.True);
                var question = AppServerMcpToolApprovalMapper.BuildChoices(request).Single();
                Assert.That(question.Options.Select(option => option.Value), Is.EqualTo(new[] { "once", "session", "always" }));
                Assert.That(question.Options.Last().Label, Is.EqualTo(appLabel));
                Assert.That(question.Description, Is.EqualTo("Allow Codex to use cuatestwindow?"));
                Assert.That(AppServerMcpToolApprovalMapper.BuildChoices(Request()).Single().Options.Last().Label, Is.EqualTo(toolLabel));
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        }

        [TestCase("en-US", "Always allow this site", "Always allow this tool")]
        [TestCase("zh-CN", "始终允许此网站", "始终允许此工具")]
        public void BrowserPersistentChoiceNamesOnlyTheSiteWhenAnOriginIsPresent(string culture, string siteLabel, string toolLabel)
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
                var request = BrowserRequest();
                var meta = (IDictionary<string, object>)request["_meta"];
                meta["tool_params"] = new Dictionary<string, object> { { "origin", "https://example.test" } };
                Assert.That(AppServerMcpToolApprovalMapper.BuildChoices(request).Single().Options.Last().Label, Is.EqualTo(siteLabel));
                meta["tool_params"] = new Dictionary<string, object> { { "origin", " " } };
                Assert.That(AppServerMcpToolApprovalMapper.BuildChoices(request).Single().Options.Last().Label, Is.EqualTo(toolLabel));
                meta["origin"] = "https://example.test";
                Assert.That(AppServerMcpToolApprovalMapper.BuildChoices(request).Single().Options.Last().Label, Is.EqualTo(siteLabel));
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        }

        [TestCase(ApprovalMode.FullAccess, true)]
        [TestCase(ApprovalMode.FullAccessAutoApprove, true)]
        [TestCase(ApprovalMode.Auto, false)]
        [TestCase(ApprovalMode.ReadOnly, false)]
        [TestCase(ApprovalMode.Custom, false)]
        public async Task BrowserApprovalUsesTheSameThreadPolicyAsOtherMcpTools(ApprovalMode mode, bool automatic)
        {
            var request = BrowserRequest();
            var events = new List<AppServerEventArgs>();
            IDictionary<string, object> response = null;
            var coordinator = Coordinator(mode, events, result => response = result);
            coordinator.HandleServerRequest(new Dictionary<string, object>
            {
                { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", request }
            }, 7L);
            Assert.That(events.Single().EventType, Is.EqualTo(automatic ? "approval_resolved" : "mcp_elicitation_requested"));
            if (automatic)
            {
                Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ResolveToolUserInputRequestAsync(
                    "42", new Dictionary<string, string[]>()), "An automatic decision must not leave a pending dialog.");
            }
            else
            {
                Assert.That(response, Is.Null);
                var question = events.Single().ToolQuestions.Single();
                await coordinator.ResolveToolUserInputRequestAsync("42", new Dictionary<string, string[]>
                {
                    { question.Id, new[] { "once" } }
                });
            }
            Assert.That(response["action"], Is.EqualTo("accept"));
            Assert.That((IDictionary<string, object>)response["content"], Is.Empty);
            Assert.That(response.ContainsKey("_meta"), Is.False, "Automatic approval must not persist a site grant.");
        }

        [TestCase("browser-use", false)]
        [TestCase("computer-use", true)]
        [TestCase("another-connector", true)]
        public void FullAccessToolApprovalDoesNotDependOnConnectorOrSensitiveFlag(string connector, bool sensitive)
        {
            var request = BrowserRequest();
            var meta = (IDictionary<string, object>)request["_meta"];
            meta["connector_id"] = connector;
            meta["codex_sensitive_action"] = sensitive;
            var events = new List<AppServerEventArgs>();
            IDictionary<string, object> response = null;
            Coordinator(ApprovalMode.FullAccess, events, result => response = result).HandleServerRequest(
                new Dictionary<string, object>
                {
                    { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", request }
                }, 7L);
            Assert.That(response["action"], Is.EqualTo("accept"));
            Assert.That(events.Single().EventType, Is.EqualTo("approval_resolved"));
        }

        [TestCase("once", null)]
        [TestCase("session", "session")]
        [TestCase("always", "always")]
        public async Task ComputerUseManualScopeUsesTheOfficialResponse(string choice, string persist)
        {
            var events = new List<AppServerEventArgs>();
            IDictionary<string, object> response = null;
            var coordinator = Coordinator(ApprovalMode.Auto, events, result => response = result);
            coordinator.HandleServerRequest(new Dictionary<string, object>
            {
                { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", ComputerUseRequest() }
            }, 7L);
            var question = events.Single().ToolQuestions.Single();
            await coordinator.ResolveToolUserInputRequestAsync("42", new Dictionary<string, string[]> { { question.Id, new[] { choice } } });
            Assert.That(response["action"], Is.EqualTo("accept"));
            Assert.That((IDictionary<string, object>)response["content"], Is.Empty);
            if (persist == null) Assert.That(response.ContainsKey("_meta"), Is.False);
            else Assert.That(((IDictionary<string, object>)response["_meta"])["persist"], Is.EqualTo(persist));
        }

        [TestCase(ApprovalMode.FullAccess)]
        [TestCase(ApprovalMode.FullAccessAutoApprove)]
        public void ComputerUseFullAccessDoesNotCreatePersistentAuthorization(ApprovalMode mode)
        {
            var events = new List<AppServerEventArgs>();
            IDictionary<string, object> response = null;
            Coordinator(mode, events, result => response = result).HandleServerRequest(new Dictionary<string, object>
            {
                { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", ComputerUseRequest() }
            }, 7L);
            Assert.That(events.Single().EventType, Is.EqualTo("approval_resolved"));
            Assert.That(response["action"], Is.EqualTo("accept"));
            Assert.That(response.ContainsKey("_meta"), Is.False);
        }

        [TestCase("codex_strict_auto_review")]
        [TestCase("codex_requires_user_input")]
        [TestCase("input-schema")]
        public void ComputerUseStrictOrAdditionalInputStillRequiresTheUser(string variant)
        {
            var request = ComputerUseRequest();
            if (variant == "input-schema")
            {
                ((IDictionary<string, object>)request["requestedSchema"])["properties"] =
                    new Dictionary<string, object> { { "answer", new Dictionary<string, object> { { "type", "string" } } } };
            }
            else ((IDictionary<string, object>)request["_meta"])[variant] = true;
            var events = new List<AppServerEventArgs>();
            Coordinator(ApprovalMode.FullAccess, events, _ => Assert.Fail("Must remain manual.")).HandleServerRequest(
                new Dictionary<string, object>
                {
                    { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", request }
                }, 7L);
            Assert.That(events.Single().EventType, Is.EqualTo("mcp_elicitation_requested"));
        }

        [Test]
        public void ComputerUseCannotRequestAnUnadvertisedPersistentScope()
        {
            var request = ComputerUseRequest();
            ((IDictionary<string, object>)request["_meta"])["persist"] = new[] { "session" };
            Assert.That(AppServerMcpToolApprovalMapper.BuildChoices(request).Single().Options.Select(option => option.Value),
                Is.EqualTo(new[] { "once", "session" }));
            Assert.That(AppServerMcpToolApprovalMapper.BuildResponse(request,
                new Dictionary<string, string[]> { { AppServerMcpToolApprovalMapper.QuestionId, new[] { "always" } } })["action"], Is.EqualTo("cancel"));
        }

        [TestCase(ApprovalMode.FullAccess, true)]
        [TestCase(ApprovalMode.FullAccessAutoApprove, true)]
        [TestCase(ApprovalMode.Auto, false)]
        [TestCase(ApprovalMode.ReadOnly, false)]
        [TestCase(ApprovalMode.Custom, false)]
        public void ToolApproval_UsesCurrentThreadModeAndOriginalConnection(ApprovalMode mode, bool automatic)
        {
            var events = new List<AppServerEventArgs>();
            IDictionary<string, object> response = null;
            var coordinator = Coordinator(mode, events, result => response = result);
            coordinator.HandleServerRequest(new Dictionary<string, object>
            {
                { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", Request() }
            }, 7L);
            Assert.That(events.Single().EventType, Is.EqualTo(automatic ? "approval_resolved" : "mcp_elicitation_requested"));
            if (automatic)
            {
                Assert.That(response["action"], Is.EqualTo("accept"));
                Assert.That(response.ContainsKey("_meta"), Is.False, "Full Access must not save a grant for other modes.");
                Assert.That((IDictionary<string, object>)response["content"], Is.Empty);
            }
            else
            {
                Assert.That(response, Is.Null);
                Assert.That(events[0].ToolQuestions.Single().Options.Select(o => o.Value), Is.EqualTo(new[] { "once", "session", "always" }));
            }
        }

        [TestCase("once", null)]
        [TestCase("session", "session")]
        [TestCase("always", "always")]
        public async Task ManualApproval_ReturnsOnlyTheSelectedSupportedScope(string decision, string persist)
        {
            var events = new List<AppServerEventArgs>();
            IDictionary<string, object> response = null;
            var coordinator = Coordinator(ApprovalMode.Auto, events, result => response = result);
            coordinator.HandleServerRequest(new Dictionary<string, object>
            {
                { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", Request() }
            }, 7L);
            var question = events.Single().ToolQuestions.Single();
            await coordinator.ResolveToolUserInputRequestAsync("42", new Dictionary<string, string[]> { { question.Id, new[] { decision } } });
            Assert.That(response["action"], Is.EqualTo("accept"));
            Assert.That((IDictionary<string, object>)response["content"], Is.Empty);
            if (persist == null) Assert.That(response.ContainsKey("_meta"), Is.False);
            else Assert.That(((IDictionary<string, object>)response["_meta"])["persist"], Is.EqualTo(persist));
            Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ResolveToolUserInputRequestAsync("42", new Dictionary<string, string[]>()));
        }

        [TestCase(null, new[] { "once" })]
        [TestCase("session", new[] { "once", "session" })]
        [TestCase("always", new[] { "once", "always" })]
        [TestCase("unknown", new[] { "once" })]
        public void RememberChoices_RequireAdvertisedSupport(string persist, string[] expected)
        {
            var parameters = Request();
            ((IDictionary<string, object>)parameters["_meta"])["persist"] = persist;
            var pending = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(42, "42", parameters);
            Assert.That(pending.Questions.Single().Options.Select(o => o.Value), Is.EqualTo(expected));
            var invalid = AppServerInteractionRequestMapper.BuildToolUserInputResponse(pending,
                new Dictionary<string, string[]> { { pending.Questions[0].Id, new[] { "unsupported" } } });
            Assert.That(invalid["action"], Is.EqualTo("cancel"));
            if (persist != "always")
            {
                var forged = AppServerInteractionRequestMapper.BuildToolUserInputResponse(pending,
                    new Dictionary<string, string[]> { { pending.Questions[0].Id, new[] { "always" } } });
                Assert.That(forged["action"], Is.EqualTo("cancel"));
            }
        }

        [TestCase("unmarked")]
        [TestCase("browser_auth")]
        [TestCase("input")]
        [TestCase("url")]
        [TestCase("missing-thread")]
        [TestCase("invalid-schema")]
        [TestCase("codex_requires_user_input")]
        [TestCase("codex_strict_auto_review")]
        [TestCase("request-type")]
        [TestCase("required-schema")]
        public void FullAccess_DoesNotAutomaticallyAnswerOtherOrMalformedRequests(string variant)
        {
            AssertRequestRemainsManual(Request(), variant);
        }

        [TestCase("input")]
        [TestCase("url")]
        [TestCase("missing-thread")]
        [TestCase("codex_requires_user_input")]
        [TestCase("codex_strict_auto_review")]
        [TestCase("request-type")]
        [TestCase("required-schema")]
        public void BrowserFullAccessStillHonorsRequestsThatRequireManualInput(string variant)
        {
            AssertRequestRemainsManual(BrowserRequest(), variant);
        }

        private static void AssertRequestRemainsManual(IDictionary<string, object> parameters, string variant)
        {
            var meta = (IDictionary<string, object>)parameters["_meta"];
            if (variant == "unmarked") parameters.Remove("_meta");
            else if (variant == "browser_auth") meta["codex_approval_kind"] = "browser_auth";
            else if (variant == "url") parameters["mode"] = "url";
            else if (variant == "missing-thread") parameters.Remove("threadId");
            else if (variant == "input") ((IDictionary<string, object>)parameters["requestedSchema"])["properties"] =
                new Dictionary<string, object> { { "answer", new Dictionary<string, object> { { "type", "string" } } } };
            else if (variant == "invalid-schema") parameters["requestedSchema"] = new Dictionary<string, object> { { "type", "object" }, { "properties", "invalid" } };
            else if (variant == "request-type") meta["codex_request_type"] = "input_request";
            else if (variant == "required-schema") ((IDictionary<string, object>)parameters["requestedSchema"])["required"] = new[] { "answer" };
            else meta[variant] = true;
            var events = new List<AppServerEventArgs>();
            var coordinator = Coordinator(ApprovalMode.FullAccess, events, _ => Assert.Fail("Must remain manual."));
            coordinator.HandleServerRequest(new Dictionary<string, object>
            {
                { "id", 42 }, { "method", "mcpServer/elicitation/request" }, { "params", parameters }
            }, 7L);
            Assert.That(events.Single().EventType, Does.StartWith("mcp_elicitation_"));
        }

        private static AppServerDynamicRequestCoordinator Coordinator(ApprovalMode mode,
            List<AppServerEventArgs> events, Action<IDictionary<string, object>> reply)
        {
            return new AppServerDynamicRequestCoordinator(new object(), new AppServerPendingRequestRegistry(),
                () => null, _ => mode, _ => null, () => 9L,
                (id, result, epoch) =>
                {
                    Assert.That(id, Is.EqualTo(42));
                    Assert.That(epoch, Is.EqualTo(7L));
                    reply((IDictionary<string, object>)result);
                    return Task.CompletedTask;
                }, (id, code, message, epoch) => Task.CompletedTask, events.Add, path => path);
        }

        private static IDictionary<string, object> BrowserRequest()
        {
            var request = Request();
            var meta = (IDictionary<string, object>)request["_meta"];
            meta["connector_id"] = "browser-use";
            meta["codex_sensitive_action"] = true;
            return request;
        }
    }
}
