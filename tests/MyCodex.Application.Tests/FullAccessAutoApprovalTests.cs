using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class FullAccessAutoApprovalTests
    {
        [TestCase("item/commandExecution/requestApproval", "approval_requested")]
        [TestCase("item/fileChange/requestApproval", "approval_requested")]
        [TestCase("item/permissions/requestApproval", "approval_requested")]
        [TestCase("item/tool/requestUserInput", "tool_user_input_requested")]
        [TestCase("mcpServer/elicitation/request", "mcp_elicitation_requested")]
        public void Requests_OnlyExplicitFullAccessAutomaticallyApprovesPermissions(string method, string manualEvent)
        {
            foreach (var mode in new[] { ApprovalMode.ReadOnly, ApprovalMode.Auto, ApprovalMode.FullAccess,
                ApprovalMode.Custom, ApprovalMode.FullAccessAutoApprove })
            {
                var events = new List<AppServerEventArgs>();
                IDictionary<string, object> response = null;
                var coordinator = new AppServerDynamicRequestCoordinator(
                    new object(), new AppServerPendingRequestRegistry(), () => null, _ => mode,
                    _ => null, () => 9L,
                    (id, result, epoch) =>
                    {
                        Assert.That(id, Is.EqualTo(42));
                        Assert.That(epoch, Is.EqualTo(7L), "Reply must use the request's connection epoch.");
                        response = (IDictionary<string, object>)result;
                        return Task.CompletedTask;
                    },
                    (id, code, message, epoch) => Task.CompletedTask, events.Add, path => path);
                var permissions = new Dictionary<string, object>
                {
                    { "network", new Dictionary<string, object> { { "enabled", true } } }
                };
                coordinator.HandleServerRequest(new Dictionary<string, object>
                {
                    { "id", 42 }, { "method", method },
                    { "params", new Dictionary<string, object>
                        {
                            { "threadId", "thread-1" }, { "permissions", permissions },
                            { "command", "Remove-Item -LiteralPath 'E:\\fixture' -Recurse -Force" },
                            { "mode", "form" }, { "serverName", "test" }
                        }
                    }
                }, 7L);

                var autoApprove = (mode == ApprovalMode.FullAccess || mode == ApprovalMode.FullAccessAutoApprove) &&
                    manualEvent == "approval_requested";
                Assert.That(events.Count, Is.EqualTo(1), mode.ToString());
                Assert.That(events[0].EventType, Is.EqualTo(autoApprove ? "approval_resolved" : manualEvent), mode.ToString());
                if (!autoApprove)
                {
                    Assert.That(response, Is.Null);
                    continue;
                }

                Assert.That(response, Is.Not.Null);
                if (method == "item/permissions/requestApproval")
                {
                    Assert.That(response["scope"], Is.EqualTo("turn"));
                    Assert.That(response["permissions"], Is.SameAs(permissions));
                }
                else
                {
                    Assert.That(response["decision"], Is.EqualTo("accept"));
                    Assert.That(response.Count, Is.EqualTo(1), "No persistent rule or session grant.");
                }
                Assert.That(events[0].Content, Does.Contain(MyCodex.Shared.Localization.SharedLocalization.Text(
                    "AppServerClient.Approval.Source.FullAccessAutoApprove", "automatically approved by Full Access setting")));
            }
        }

        [TestCase("thread-1", "decline")]
        [TestCase("", "accept")]
        public void AutoApproval_RequiresThreadAndSupportedDecision(string threadId, string availableDecision)
        {
            var events = new List<AppServerEventArgs>();
            var coordinator = new AppServerDynamicRequestCoordinator(
                new object(), new AppServerPendingRequestRegistry(), () => null,
                _ => ApprovalMode.FullAccessAutoApprove, _ => null, () => 1,
                (id, result, epoch) => { Assert.Fail("Must not auto-approve this request."); return Task.CompletedTask; },
                (id, code, message, epoch) => Task.CompletedTask, events.Add, path => path);
            coordinator.HandleCommandApprovalRequest(1, "1", new Dictionary<string, object>
            {
                { "threadId", threadId }, { "availableDecisions", new[] { availableDecision } }
            });
            Assert.That(events[0].EventType, Is.EqualTo("approval_requested"));
        }

        [TestCase(2, ApprovalMode.FullAccess, "full-access")]
        [TestCase(4, ApprovalMode.FullAccessAutoApprove, "full-access")]
        public void StoredAndRemoteModes_UseUnifiedFullAccess(int stored, ApprovalMode mode, string wire)
        {
            var serializer = new DataContractJsonSerializer(typeof(ApprovalMode));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(stored.ToString())))
            {
                Assert.That(serializer.ReadObject(stream), Is.EqualTo(mode));
            }
            Assert.That(HostSettingsPolicy.FormatApprovalMode(mode), Is.EqualTo(wire));
            Assert.That(HostSettingsPolicy.ParseApprovalMode(wire), Is.EqualTo(ApprovalMode.FullAccess));
        }
    }
}
