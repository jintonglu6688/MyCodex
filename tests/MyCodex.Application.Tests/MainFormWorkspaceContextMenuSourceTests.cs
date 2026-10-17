using System;
using System.IO;
using System.Linq;
using System.Text;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormWorkspaceContextMenuSourceTests
    {
        [Test]
        public void WorkspaceContextMenu_ExposesAppServerFilesEntry()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("_workspaceAppServerFilesMenuItem"));
            Assert.That(source, Does.Contain("MainForm.Workspace.AppServerFiles"));
            Assert.That(source, Does.Contain("OnWorkspaceAppServerFilesMenuItemClick"));
            Assert.That(source, Does.Contain("ShowAppServerFilesForWorkspaceAsync"));
            Assert.That(source, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceAppServerFilesMenuItem)"));
        }

        [Test]
        public void WorkspaceContextMenu_DoesNotExposeOfficeCategoryEntries()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Not.Contain("_officeCategoryNewMenuItem"));
            Assert.That(source, Does.Not.Contain("MainForm.OfficeCategory.New"));
            Assert.That(source, Does.Not.Contain("MainForm.OfficeCategory.OpenFolder"));
            Assert.That(source, Does.Not.Contain("MainForm.OfficeCategory.Refresh"));
            Assert.That(source, Does.Not.Contain("OnOfficeCategoryNewMenuItemClick"));
        }

        [Test]
        public void FileMenu_OpenCommandOpensCurrentOfficeWorkspaceDirectory()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("OpenOfficeWorkspaceDirectory();"));
            Assert.That(source, Does.Contain("MainForm.File.OpenOfficeDirectory"));
            Assert.That(source, Does.Contain("ShowAppServerFilesForWorkspaceAsync(selectedItem.Workspace.RootPath);"));
        }

        [Test]
        public void WslWorkspaceOpening_ExposesSeparateWindowsAndWslEntries()
        {
            var source = GetMainFormSource();
            var primaryOpenHandler = ExtractMethod(source, "private async void OnOpenWorkspaceButtonClick");
            var wslOpenHandler = ExtractMethod(source, "private async void OnOpenWslWorkspaceMenuItemClick");

            Assert.That(source, Does.Contain("_openWslWorkspaceFileMenuItem"));
            Assert.That(source, Does.Contain("_workspaceOpenWslMenuItem"));
            Assert.That(source, Does.Contain("_fileMenuItem.MenuItems.Add(_openWslWorkspaceFileMenuItem)"));
            Assert.That(source, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceOpenWslMenuItem)"));
            Assert.That(source, Does.Contain("_openWslWorkspaceFileMenuItem.Click += OnOpenWslWorkspaceMenuItemClick"));
            Assert.That(source, Does.Contain("_workspaceOpenWslMenuItem.Click += OnOpenWslWorkspaceMenuItemClick"));
            Assert.That(primaryOpenHandler, Does.Contain("if (IsRemoteMacRuntimeEnabled())"));
            Assert.That(primaryOpenHandler, Does.Not.Contain("if (IsUnixRemoteRuntimeEnabled())"));
            Assert.That(primaryOpenHandler, Does.Contain("WindowsFolderPicker.TryPickFolder("));
            Assert.That(wslOpenHandler, Does.Contain("if (!IsWslRuntimeEnabled())"));
            Assert.That(wslOpenHandler, Does.Contain("await OpenUnixWorkspacePathAsync();"));
        }

        [Test]
        public void OfficeSessionContextMenuHidesSubAgentsAndArchive()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("_sessionSubAgentsMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionAdvancedSeparatorMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionArchiveMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionSubAgentsMenuItem.Enabled = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionArchiveMenuItem.Enabled = !isOfficeMode"));
        }

        [Test]
        public void WorkspaceContextMenu_GroupsImportAndSyncEntries()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem"));
            Assert.That(source, Does.Contain("MainForm.ImportSync.Menu"));
            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem.MenuItems.Add(_workspaceSyncThreadsMenuItem)"));
            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem.MenuItems.Add(_workspaceImportOtherSessionsMenuItem)"));
            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem.MenuItems.Add(_workspaceExternalAgentConfigImportMenuItem)"));
            Assert.That(source, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceImportSyncMenuItem)"));
            Assert.That(source, Does.Not.Contain("_workspaceContextMenu.MenuItems.Add(_syncThreadsMenuItem)"));
            Assert.That(source, Does.Not.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceAssociateOtherSessionsMenuItem)"));
        }

        [Test]
        public void FileMenu_GroupsImportAndSyncEntries()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("_fileImportSyncMenuItem"));
            Assert.That(source, Does.Contain("_fileSyncThreadsMenuItem"));
            Assert.That(source, Does.Contain("_fileImportOtherSessionsMenuItem"));
            Assert.That(source, Does.Contain("_fileExternalAgentConfigImportMenuItem"));
            Assert.That(source, Does.Contain("_fileImportSyncMenuItem.MenuItems.Add(_fileSyncThreadsMenuItem)"));
            Assert.That(source, Does.Contain("_fileImportSyncMenuItem.MenuItems.Add(_fileImportOtherSessionsMenuItem)"));
            Assert.That(source, Does.Contain("_fileImportSyncMenuItem.MenuItems.Add(_fileExternalAgentConfigImportMenuItem)"));
            Assert.That(source, Does.Contain("_fileMenuItem.MenuItems.Add(_fileImportSyncMenuItem)"));
        }

        [Test]
        public void ImportOtherSessionsEntries_OpenAssociationDialog()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("_workspaceImportOtherSessionsMenuItem"));
            Assert.That(source, Does.Contain("MainForm.ImportSync.ImportOtherSessions"));
            Assert.That(source, Does.Contain("OnWorkspaceAssociateOtherSessionsMenuItemClick"));
            Assert.That(source, Does.Contain("ShowThreadAssociationDialogAsync"));
            Assert.That(source, Does.Contain("_fileImportOtherSessionsMenuItem.Click += OnImportOtherSessionsMenuItemClick"));
            Assert.That(source, Does.Contain("_workspaceImportOtherSessionsMenuItem.Click += OnWorkspaceAssociateOtherSessionsMenuItemClick"));
        }

        [Test]
        public void SessionListFocus_DoesNotTriggerThreadSync()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Not.Contain("_sessionList.Enter += SessionList_Enter"));
            Assert.That(source, Does.Not.Contain("private async void SessionList_Enter"));
            Assert.That(source, Does.Contain("LocalThreadSessionWatchDebounceTimer_Tick"));
            Assert.That(source, Does.Contain("LocalSubAgentThreadSyncTimer_Tick"));
        }

        [Test]
        public void LocalThreadSessionWatcher_ForcesFullThreadSyncWhenSessionFileChanges()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("new FileSystemWatcher(watchRoot, \"*.jsonl\")"));
            Assert.That(source, Does.Contain("IncludeSubdirectories = true"));
            Assert.That(source, Does.Contain("LocalThreadSessionWatchDebounceTimer_Tick"));
            Assert.That(source, Does.Contain("RunLocalCurrentSubAgentThreadSyncAsync"));
            Assert.That(source, Does.Contain("ListSubAgentThreadsAsync(parent.ThreadId, CancellationToken.None)"));
            Assert.That(source, Does.Contain("ScheduleLocalThreadSessionFileSync();"));
            Assert.That(source, Does.Contain("await RunAutomaticThreadSyncAsync(false, true);"));
        }

        [Test]
        public void LocalSubAgentThreadSyncTimer_PollsLocalSessionFilesDuringRuntime()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("LocalSubAgentThreadSyncIntervalMilliseconds"));
            Assert.That(source, Does.Contain("_localSubAgentThreadSyncTimer.Tick += LocalSubAgentThreadSyncTimer_Tick"));
            Assert.That(source, Does.Contain("_localSubAgentThreadSyncTimer.Start();"));
            Assert.That(source, Does.Contain("private async void LocalSubAgentThreadSyncTimer_Tick"));
            Assert.That(source, Does.Contain("await RunLocalCurrentSubAgentThreadSyncAsync();"));
        }

        [Test]
        public void WorkspaceContextMenu_FileBrowserEntryTextDoesNotMentionAppServer()
        {
            var source = GetMainFormSource();
            var menuText = UiLocalization.Text("MainForm.Workspace.AppServerFiles", "File Browser(&F)...");

            Assert.That(menuText, Is.EqualTo("File Browser(&F)...").Or.EqualTo("文件浏览(&F)..."));
            Assert.That(menuText, Does.Not.Contain("App Server"));
            Assert.That(source, Does.Contain("File Browser(&F)..."));
            Assert.That(source, Does.Not.Contain("App Server Files(&F)..."));
        }

        [Test]
        public void DeletionContextMenus_UseSingleDeleteEntryWithoutPermanentDeleteEntry()
        {
            var source = GetMainFormSource();

            Assert.That(source, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceRemoveMenuItem);"));
            Assert.That(source, Does.Contain("_sessionContextMenu.MenuItems.Add(_sessionRemoveMenuItem);"));
            Assert.That(source, Does.Not.Contain("_workspaceDeletePermanentlyMenuItem"));
            Assert.That(source, Does.Not.Contain("_sessionDeletePermanentlyMenuItem"));
            Assert.That(source, Does.Not.Contain("MainForm.Workspace.DeletePermanent"));
            Assert.That(source, Does.Not.Contain("MainForm.Session.DeletePermanent"));
        }

        private static string GetMainFormSource()
        {
            var formsDirectory = Path.GetFullPath(Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.WinForms",
                "Forms"));
            var files = Directory
                .GetFiles(formsDirectory, "MainForm*.cs", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var builder = new StringBuilder();
            for (var i = 0; i < files.Length; i++)
            {
                builder.AppendLine(File.ReadAllText(files[i]));
            }

            return builder.ToString();
        }

        private static string GetMainFormPath()
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
                "MainForm.cs"));
        }

        private static string ExtractMethod(string source, string signature)
        {
            var start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature + " should exist.");
            var nextMethod = source.IndexOf("\r\n        private ", start + signature.Length, StringComparison.Ordinal);
            if (nextMethod < 0)
            {
                nextMethod = source.IndexOf("\n        private ", start + signature.Length, StringComparison.Ordinal);
            }

            Assert.That(nextMethod, Is.GreaterThan(start), signature + " should be followed by another method.");
            return source.Substring(start, nextMethod - start);
        }
    }
}
