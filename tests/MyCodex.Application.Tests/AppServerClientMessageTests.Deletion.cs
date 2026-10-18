using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    public sealed partial class AppServerClientMessageTests
    {
        [TestCase("zh-CN", true, "仍有分叉会话引用待删除的历史，无法永久删除。可以改用归档保留历史。")]
        [TestCase("en-US", true, "Cannot permanently delete this history because a forked conversation still references it. You can archive the conversation instead.")]
        [TestCase("zh-CN", false, "Access denied")]
        public async Task DeleteThreadFailure_PreservesStateAndDisplaysReason(string culture, bool forkReference, string expectedReason)
        {
            var originalCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            try
            {
                var client = CreateClient();
                using (var stream = new MemoryStream())
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
                {
                    FormTestDriver.SetField(client, "_isConnected", true);
                    FormTestDriver.SetField(client, "_process", Process.GetCurrentProcess());
                    FormTestDriver.SetField(client, "_inputWriter", writer);
                    var loaded = FormTestDriver.Field<HashSet<string>>(client, "_loadedThreadIds");
                    loaded.Add("thread-1");
                    var plan = SessionPermanentDeletionPlanner.Build(
                        new[] { new ChatSession { ThreadId = "thread-1" } },
                        new[] { new AppServerThreadSummary { ThreadId = "thread-1", ThreadStatus = new AppServerThreadStatus { Type = "idle" } } });
                    var task = SessionPermanentDeletionPlanner.ExecuteAsync(plan, client.DeleteThreadAsync,
                        _ => Assert.Fail("A failed deletion must not remove the session."), () => true);
                    var written = await WaitForWrittenTextAsync(stream);
                    var request = AppServerJsonValue.DeserializeDictionary(written.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).First().TrimStart('\uFEFF'));
                    var error = forkReference
                        ? "cannot delete thread thread-1: forked history still references it"
                        : expectedReason;
                    var transport = FormTestDriver.Field<AppServerProtocolTransport>(client, "_protocolTransport");
                    string ignored;
                    Assert.That(transport.TryHandleResponse(new Dictionary<string, object>
                    {
                        { "id", request["id"] },
                        { "error", new Dictionary<string, object> { { "code", -32600 }, { "message", error } } }
                    }, out ignored), Is.True);

                    var result = await task;
                    Assert.That(loaded, Does.Contain("thread-1"));
                    Assert.That(result.DeletedThreadIds, Is.Empty);
                    Assert.That(result.FailedThreadIds, Is.EqualTo(new[] { "thread-1" }));
                    Assert.That(result.FailureReasons["thread-1"], Is.EqualTo(expectedReason));
                    var text = LocalSessionRemovalTextPlanner.BuildPermanentDeletionResultText(result, new string[0], new string[0]);
                    Assert.That(text, Does.StartWith(expectedReason + " [thread-1]"));
                }
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }
        }
    }
}
