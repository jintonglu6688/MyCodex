using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class MainFormWorkspaceRetentionTests
    {
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, null, "/")]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, null, "/")]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, "/srv/projects", "/srv/projects")]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, "/Users/demo/project", "/Users/demo/project")]
        [TestCase(CodexRuntimeSources.Wsl, null, "/home")]
        public void DefaultUnixWorkspacePath_UsesCurrentWorkspaceOrPortableBrowseRoot(
            string runtimeSource, string workspacePath, string expectedPath)
        {
            var form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainForm).GetField("_appConfigSnapshot", flags).SetValue(form, new AppConfigSnapshot
            {
                CodexRuntimeSource = runtimeSource,
                RemoteMacSsh = new RemoteMacSshConfigSnapshot { UserName = "root" }
            });
            typeof(MainForm).GetField("_currentWorkspace", flags).SetValue(form,
                workspacePath == null ? null : new WorkspaceInfo { RootPath = workspacePath });

            var path = typeof(MainForm).GetMethod("GetDefaultUnixWorkspacePath", flags).Invoke(form, null);

            Assert.That(path, Is.EqualTo(expectedPath));
        }

        [Test]
        public void UpsertRecentWorkspace_RetainsAllWorkspacesAndMovesExistingWorkspaceToFront()
        {
            var form = (MainForm)FormatterServices.GetUninitializedObject(typeof(MainForm));
            var workspaces = new List<WorkspaceInfo>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(MainForm).GetField("_recentWorkspaces", flags).SetValue(form, workspaces);
            var upsert = typeof(MainForm).GetMethod("UpsertRecentWorkspace", flags);

            for (var i = 0; i < 100; i++)
            {
                upsert.Invoke(form, new object[] { new WorkspaceInfo { Id = "workspace-" + i } });
            }

            Assert.That(workspaces.Select(workspace => workspace.Id),
                Is.EqualTo(Enumerable.Range(0, 100).Reverse().Select(i => "workspace-" + i)));

            var reopened = new WorkspaceInfo { Id = "WORKSPACE-0", Name = "Reopened" };
            upsert.Invoke(form, new object[] { reopened });

            Assert.That(workspaces, Has.Count.EqualTo(100));
            Assert.That(workspaces[0], Is.SameAs(reopened));
            Assert.That(workspaces.Skip(1).Select(workspace => workspace.Id),
                Is.EqualTo(Enumerable.Range(1, 99).Reverse().Select(i => "workspace-" + i)));
        }
    }
}
