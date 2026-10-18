using System;
using System.IO;
using System.Linq;
using System.Text;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class MainFormWorkspaceContextMenuSourceTests
    {
        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void WorkspaceContextMenu_ExposesAppServerFilesEntry()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_workspaceAppServerFilesMenuItem"));
            Assert.That(source, Does.Contain("MainForm.Workspace.AppServerFiles"));
            Assert.That(source, Does.Contain("OnWorkspaceAppServerFilesMenuItemClick"));
            Assert.That(source, Does.Contain("ShowAppServerFilesForWorkspaceAsync"));
            Assert.That(source, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceAppServerFilesMenuItem)"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void FileMenu_OpenCommandOpensCurrentOfficeWorkspaceDirectory()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("OpenOfficeWorkspaceDirectory();"));
            Assert.That(source, Does.Contain("MainForm.File.OpenOfficeDirectory"));
            Assert.That(source, Does.Contain("ShowAppServerFilesForWorkspaceAsync(selectedItem.Workspace.RootPath);"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void WslWorkspaceOpening_ExposesSeparateWindowsAndWslEntries()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var primaryOpenHandler = RepositorySource.ExtractUntilNextPrivateMember(source, "private async void OnOpenWorkspaceButtonClick");
            var wslOpenHandler = RepositorySource.ExtractUntilNextPrivateMember(source, "private async void OnOpenWslWorkspaceMenuItemClick");

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

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void OfficeSessionContextMenuHidesSubAgentsAndArchive()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_sessionSubAgentsMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionAdvancedSeparatorMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionArchiveMenuItem.Visible = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionSubAgentsMenuItem.Enabled = !isOfficeMode"));
            Assert.That(source, Does.Contain("_sessionArchiveMenuItem.Enabled = !isOfficeMode"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void WorkspaceContextMenu_GroupsImportAndSyncEntries()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem"));
            Assert.That(source, Does.Contain("MainForm.ImportSync.Menu"));
            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem.MenuItems.Add(_workspaceSyncThreadsMenuItem)"));
            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem.MenuItems.Add(_workspaceImportOtherSessionsMenuItem)"));
            Assert.That(source, Does.Contain("_workspaceImportSyncMenuItem.MenuItems.Add(_workspaceExternalAgentConfigImportMenuItem)"));
            Assert.That(source, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceImportSyncMenuItem)"));
            Assert.That(source, Does.Not.Contain("_workspaceContextMenu.MenuItems.Add(_syncThreadsMenuItem)"));
            Assert.That(source, Does.Not.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceAssociateOtherSessionsMenuItem)"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void FileMenu_GroupsImportAndSyncEntries()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_fileImportSyncMenuItem"));
            Assert.That(source, Does.Contain("_fileSyncThreadsMenuItem"));
            Assert.That(source, Does.Contain("_fileImportOtherSessionsMenuItem"));
            Assert.That(source, Does.Contain("_fileExternalAgentConfigImportMenuItem"));
            Assert.That(source, Does.Contain("_fileImportSyncMenuItem.MenuItems.Add(_fileSyncThreadsMenuItem)"));
            Assert.That(source, Does.Contain("_fileImportSyncMenuItem.MenuItems.Add(_fileImportOtherSessionsMenuItem)"));
            Assert.That(source, Does.Contain("_fileImportSyncMenuItem.MenuItems.Add(_fileExternalAgentConfigImportMenuItem)"));
            Assert.That(source, Does.Contain("_fileMenuItem.MenuItems.Add(_fileImportSyncMenuItem)"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void ImportOtherSessionsEntries_OpenAssociationDialog()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_workspaceImportOtherSessionsMenuItem"));
            Assert.That(source, Does.Contain("MainForm.ImportSync.ImportOtherSessions"));
            Assert.That(source, Does.Contain("OnWorkspaceAssociateOtherSessionsMenuItemClick"));
            Assert.That(source, Does.Contain("ShowThreadAssociationDialogAsync"));
            Assert.That(source, Does.Contain("_fileImportOtherSessionsMenuItem.Click += OnImportOtherSessionsMenuItemClick"));
            Assert.That(source, Does.Contain("_workspaceImportOtherSessionsMenuItem.Click += OnWorkspaceAssociateOtherSessionsMenuItemClick"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void SessionListFocus_DoesNotTriggerThreadSync()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Not.Contain("_sessionList.Enter += SessionList_Enter"));
            Assert.That(source, Does.Not.Contain("private async void SessionList_Enter"));
            Assert.That(source, Does.Contain("LocalThreadSessionWatchDebounceTimer_Tick"));
            Assert.That(source, Does.Contain("LocalSubAgentThreadSyncTimer_Tick"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void LocalThreadSessionWatcher_ForcesFullThreadSyncWhenSessionFileChanges()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("new FileSystemWatcher(watchRoot, \"*.jsonl\")"));
            Assert.That(source, Does.Contain("IncludeSubdirectories = true"));
            Assert.That(source, Does.Contain("LocalThreadSessionWatchDebounceTimer_Tick"));
            Assert.That(source, Does.Contain("RunLocalCurrentSubAgentThreadSyncAsync"));
            Assert.That(source, Does.Contain("ListSubAgentThreadsAsync(parent.ThreadId, CancellationToken.None)"));
            Assert.That(source, Does.Contain("ScheduleLocalThreadSessionFileSync();"));
            Assert.That(source, Does.Contain("await RunAutomaticThreadSyncAsync(false, true);"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void LocalSubAgentThreadSyncTimer_PollsLocalSessionFilesDuringRuntime()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("LocalSubAgentThreadSyncIntervalMilliseconds"));
            Assert.That(source, Does.Contain("_localSubAgentThreadSyncTimer.Tick += LocalSubAgentThreadSyncTimer_Tick"));
            Assert.That(source, Does.Contain("_localSubAgentThreadSyncTimer.Start();"));
            Assert.That(source, Does.Contain("private async void LocalSubAgentThreadSyncTimer_Tick"));
            Assert.That(source, Does.Contain("await RunLocalCurrentSubAgentThreadSyncAsync();"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void WorkspaceContextMenu_FileBrowserEntryTextDoesNotMentionAppServer()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");
            var menuText = UiLocalization.Text("MainForm.Workspace.AppServerFiles", "File Browser(&F)...");

            Assert.That(menuText, Is.EqualTo("File Browser(&F)...").Or.EqualTo("文件浏览(&F)..."));
            Assert.That(menuText, Does.Not.Contain("App Server"));
            Assert.That(source, Does.Contain("File Browser(&F)..."));
            Assert.That(source, Does.Not.Contain("App Server Files(&F)..."));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void DeletionContextMenus_UseSingleDeleteEntryWithoutPermanentDeleteEntry()
        {
            var source = RepositorySource.ReadSourceFiles("MyCodex.WinForms", "MainForm*.cs");

            Assert.That(source, Does.Contain("_workspaceContextMenu.MenuItems.Add(_workspaceRemoveMenuItem);"));
            Assert.That(source, Does.Contain("_sessionContextMenu.MenuItems.Add(_sessionRemoveMenuItem);"));
        }
    }
}
