using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class McpToolTestFormTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpToolTestForm_RequiresExplicitRunAndDoesNotCallOnSelection()
        {
            WithCulture("en-US", () =>
            {
                var client = new FakeMcpClient();
                using (var form = new McpToolTestForm(client, "thread-1", "github"))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(client.ToolCallCount, Is.EqualTo(0));
                    Assert.That(FormTestDriver.FindControl<Button>(form, "Run Tool").AccessibleName, Is.EqualTo("Run Tool"));
                    Assert.That(FormTestDriver.FindControl<TextBox>(form, "Arguments JSON").Multiline, Is.True);
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpToolTestForm_InvalidJsonShowsStatusAndDoesNotCallAppServer()
        {
            WithCulture("en-US", () =>
            {
                var client = new FakeMcpClient();
                using (var form = new McpToolTestForm(client, "thread-1", "github"))
                {
                    form.SetToolNameForTests("list_issues");
                    form.SetArgumentsJsonForTests("{ invalid");

                    form.RunToolForTests().GetAwaiter().GetResult();

                    Assert.That(client.ToolCallCount, Is.EqualTo(0));
                    Assert.That(form.GetStatusTextForTests(), Does.Contain("JSON"));
                }
            });
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        private sealed class FakeMcpClient : IAppServerMcpClient
        {
            public int ToolCallCount { get; private set; }

            public Task<AppServerMcpToolCallResult> CallMcpToolAsync(AppServerMcpToolCallRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                ToolCallCount++;
                return Task.FromResult(new AppServerMcpToolCallResult { DisplayText = "done" });
            }

            public Task<AppServerMcpResourceReadResult> ReadMcpResourceAsync(string server, string uri, string threadId = null, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
            public Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync() { throw new NotSupportedException(); }
            public Task ReloadMcpServerConfigAsync() { throw new NotSupportedException(); }
            public Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(string serverName, int? timeoutSecs = null) { throw new NotSupportedException(); }
        }
    }
}
