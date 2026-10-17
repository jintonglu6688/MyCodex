using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.Fakes;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class ArchivedThreadRestoreFormTests
    {
        [TestCase(@"E:\Repo", true)]
        [TestCase(null, false)]
        public void ArchivedThreadWithStaleLocalRecordCanBeRestoredOnlyWithWorkspace(string workspacePath, bool canRestore)
        {
            var client = new Client();
            using (var form = new ArchivedThreadRestoreForm(client, client, new[] { "root" }, @"E:\Repo", _ => true))
            {
                typeof(ArchivedThreadRestoreForm).GetMethod("BindThreads", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(form, new object[] { new List<AppServerThreadSummary>
                    {
                        new AppServerThreadSummary { ThreadId = "root", WorkingDirectory = workspacePath, Title = "Root" }
                    } });
                var list = (ListView)typeof(ArchivedThreadRestoreForm).GetField("_threadListView", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
                Assert.That(list.Items[0].Checked, Is.EqualTo(canRestore));
                Assert.That(form.GetSelectedThreads().Count, Is.EqualTo(canRestore ? 1 : 0));
                if (canRestore) Assert.That(form.GetSelectedThreads()[0].ThreadId, Is.EqualTo("root"));
                Assert.That(list.AccessibleName, Is.Not.Empty);
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
            }
        }

        private sealed class Client : UnsupportedAppServerThreadClient, IAppServerConnectionClient
        {
            public event EventHandler<AppServerEventArgs> EventReceived { add { } remove { } }
            public AppServerProtocolCapabilities ProtocolCapabilities => new AppServerProtocolCapabilities();
            public Task ConnectAsync() => Task.CompletedTask;
            public Task RestartAsync() => Task.CompletedTask;
        }
    }
}
