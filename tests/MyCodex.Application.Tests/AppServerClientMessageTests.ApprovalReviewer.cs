using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AppServerClientMessageTests
    {
        [TestCase(@"E:\Repo", ApprovalMode.FullAccessAutoApprove, "user", "danger-full-access")]
        [TestCase("/home/wsl/repo", ApprovalMode.FullAccessAutoApprove, "user", "danger-full-access")]
        [TestCase("/Users/mac/repo", ApprovalMode.FullAccessAutoApprove, "user", "danger-full-access")]
        [TestCase("/home/linux/repo", ApprovalMode.FullAccessAutoApprove, "user", "danger-full-access")]
        [TestCase(@"E:\Repo", ApprovalMode.FullAccess, "user", "danger-full-access")]
        [TestCase("/home/wsl/repo", ApprovalMode.FullAccess, "user", "danger-full-access")]
        [TestCase("/Users/mac/repo", ApprovalMode.FullAccess, "user", "danger-full-access")]
        [TestCase("/home/linux/repo", ApprovalMode.FullAccess, "user", "danger-full-access")]
        [TestCase(@"E:\Repo", ApprovalMode.Auto, "user", "workspace-write")]
        [TestCase(@"E:\Repo", ApprovalMode.ReadOnly, "user", "read-only")]
        [TestCase(@"E:\Repo", ApprovalMode.Custom, "user", "read-only")]
        [TestCase(@"E:\Repo", null, null, null)]
        public async Task CreateThreadAsync_SelectsApprovalReviewer(
            string workspace, ApprovalMode? mode, string reviewer, string sandbox)
        {
            var client = CreateClient();
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                FormTestDriver.SetField(client, "_isConnected", true);
                FormTestDriver.SetField(client, "_process", Process.GetCurrentProcess());
                FormTestDriver.SetField(client, "_inputWriter", writer);

                var operation = client.CreateThreadAsync(workspace, mode, null, null);
                var request = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, 1));
                Assert.That(request["method"], Is.EqualTo("thread/start"));
                var parameters = AppServerJsonValue.GetDictionary(request, "params");
                Assert.That(parameters["cwd"], Is.EqualTo(workspace));
                AssertApprovalReviewerParameters(parameters, reviewer, sandbox, "sandbox");
                CompleteRequest(client, Convert.ToString(request["id"], CultureInfo.InvariantCulture),
                    new Dictionary<string, object>
                    {
                        { "thread", new Dictionary<string, object> { { "id", "thread-1" } } }
                    });
                Assert.That(await operation, Is.EqualTo("thread-1"));
            }
        }

        [TestCase(ApprovalMode.FullAccessAutoApprove, "user", "danger-full-access", "dangerFullAccess")]
        [TestCase(ApprovalMode.FullAccess, "user", "danger-full-access", "dangerFullAccess")]
        [TestCase(ApprovalMode.Auto, "user", "workspace-write", "workspaceWrite")]
        [TestCase(ApprovalMode.ReadOnly, "user", "read-only", "readOnly")]
        [TestCase(ApprovalMode.Custom, "user", "read-only", "readOnly")]
        [TestCase(null, null, null, null)]
        public async Task SendUserMessageAsync_AppliesReviewerToResumedAndAlreadyLoadedThreads(
            ApprovalMode? mode, string reviewer, string sandbox, string sandboxType)
        {
            foreach (var alreadyLoaded in new[] { false, true })
            foreach (var pagination in new[] { false, true })
            {
                var client = CreateClient();
                typeof(AppServerClient).GetProperty("ProtocolCapabilities").SetValue(client,
                    new AppServerProtocolCapabilities { SupportsSkills = true, SupportsThreadHistoryPagination = pagination });
                using (var stream = new MemoryStream())
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                {
                    FormTestDriver.SetField(client, "_isConnected", true);
                    FormTestDriver.SetField(client, "_process", Process.GetCurrentProcess());
                    FormTestDriver.SetField(client, "_inputWriter", writer);
                    if (alreadyLoaded) FormTestDriver.Field<HashSet<string>>(client, "_loadedThreadIds").Add("thread-1");
                    FormTestDriver.Field<Dictionary<string, ApprovalMode>>(client, "_threadApprovalModes")["thread-1"] =
                        mode == ApprovalMode.FullAccess ? ApprovalMode.Auto : ApprovalMode.FullAccess;

                    var operation = client.SendUserMessageAsync("thread-1", TestContext.CurrentContext.WorkDirectory,
                        new[] { new AppServerUserInputItem { Type = "text", Text = "hello" } }, mode, null, null);
                    var lineCount = 0;
                    if (!alreadyLoaded)
                    {
                        await CompleteResumeProviderReadAsync(client, stream, ++lineCount);
                        var resume = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, ++lineCount));
                        Assert.That(resume["method"], Is.EqualTo("thread/resume"));
                        Assert.That(AppServerJsonValue.GetDictionary(resume, "params")["modelProvider"], Is.EqualTo("current-provider"));
                        Assert.That(AppServerJsonValue.GetDictionary(resume, "params").ContainsKey("excludeTurns"), Is.EqualTo(pagination));
                        AssertApprovalReviewerParameters(AppServerJsonValue.GetDictionary(resume, "params"), reviewer, sandbox, "sandbox");
                        CompleteRequest(client, Convert.ToString(resume["id"], CultureInfo.InvariantCulture),
                            new Dictionary<string, object>
                            {
                                { "thread", new Dictionary<string, object> { { "id", "thread-1" } } }
                            });
                    }

                    var skills = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, ++lineCount));
                    Assert.That(skills["method"], Is.EqualTo("skills/list"));
                    CompleteRequest(client, Convert.ToString(skills["id"], CultureInfo.InvariantCulture),
                        new Dictionary<string, object> { { "snapshots", new object[0] } });

                    var turn = ReadLastWrittenMessage(await WaitForWrittenLineCountAsync(stream, ++lineCount));
                    Assert.That(turn["method"], Is.EqualTo("turn/start"));
                    AssertApprovalReviewerParameters(AppServerJsonValue.GetDictionary(turn, "params"), reviewer, sandboxType, "sandboxPolicy");
                    CompleteRequest(client, Convert.ToString(turn["id"], CultureInfo.InvariantCulture),
                        new Dictionary<string, object>
                        {
                            { "turn", new Dictionary<string, object> { { "id", "turn-1" } } }
                        });
                    await operation;
                }
            }
        }

        private static void AssertApprovalReviewerParameters(
            IDictionary<string, object> parameters, string reviewer, string sandbox, string sandboxKey)
        {
            if (reviewer == null)
            {
                Assert.That(parameters.ContainsKey("approvalPolicy"), Is.False);
                Assert.That(parameters.ContainsKey("approvalsReviewer"), Is.False);
                Assert.That(parameters.ContainsKey(sandboxKey), Is.False);
                return;
            }

            Assert.That(parameters["approvalPolicy"], Is.EqualTo("on-request"));
            Assert.That(parameters["approvalsReviewer"], Is.EqualTo(reviewer));
            Assert.That(sandboxKey == "sandboxPolicy"
                ? AppServerJsonValue.GetDictionary(parameters, sandboxKey)["type"]
                : parameters[sandboxKey], Is.EqualTo(sandbox));
        }
    }
}
