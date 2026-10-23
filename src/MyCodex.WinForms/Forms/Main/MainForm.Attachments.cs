using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Markets;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Dialogs;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;
using MyCodex.WinForms.Skills;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private bool _suppressNextAttachmentButtonClick;

        private void OnAttachmentButtonClick(object sender, EventArgs e)
        {
            if (_suppressNextAttachmentButtonClick)
            {
                _suppressNextAttachmentButtonClick = false;
                return;
            }

            ShowAttachmentMenu();
        }

        private void AttachmentButton_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter && e.KeyCode != Keys.Space)
            {
                return;
            }

            _suppressNextAttachmentButtonClick = true;
            ShowAttachmentMenu();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        private async Task<bool> TryPasteAttachmentFromClipboardAsync()
        {
            if (await TryPasteAppServerFileBrowserAttachmentsFromClipboardAsync())
            {
                return true;
            }

            return TryPasteAttachmentFromClipboard();
        }

        private void PendingAttachmentListBox_MouseDown(object sender, MouseEventArgs e)
        {
            SelectListItemOnRightClick(_pendingAttachmentListBox, e);
        }

        private void PendingAttachmentListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelectedPendingAttachments();
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Apps && _pendingAttachmentListBox.SelectedIndex >= 0)
            {
                ShowPopupMenuForListBoxSelection(_pendingAttachmentContextMenu, _pendingAttachmentListBox);
                e.SuppressKeyPress = true;
                e.Handled = true;
            }
        }

        private void ShowAttachmentMenu()
        {
            ShowPopupMenuBelowControl(_attachmentMenu, _attachmentButton, true);
        }

        private void OpenAttachmentMenuFromKeyboard()
        {
            if (_attachmentButton == null || !_attachmentButton.Enabled)
            {
                return;
            }

            _attachmentButton.Focus();
            ShowAttachmentMenu();
        }

        private void PendingAttachmentContextMenu_Popup(object sender, EventArgs e)
        {
            var hasSelection = _pendingAttachmentListBox.SelectedItem is PendingAttachmentItem;
            var hasItems = _pendingAttachments.Count > 0;
            _removeSelectedAttachmentMenuItem.Enabled = hasSelection;
            _clearAttachmentsMenuItem.Enabled = hasItems;
        }

        private void OnSelectImageAttachmentMenuItemClick(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = UiLocalization.Text("MainForm.FileDialog.Image.Filter", "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All Files|*.*");
                dialog.Multiselect = true;
                dialog.Title = UiLocalization.Text("MainForm.FileDialog.Image.Title", "Select image");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var addedImageCount = AddImageAttachments(dialog.FileNames, false);
                AnnounceAddedAttachments(addedImageCount, 0);
            }
        }

        private void OnSelectFileAttachmentMenuItemClick(object sender, EventArgs e)
        {
            if (IsUnixRemoteRuntimeEnabled())
            {
                ShowUnixPathAttachmentBrowser(false);
                return;
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = UiLocalization.Text("MainForm.FileDialog.All.Filter", "All Files|*.*");
                dialog.Multiselect = true;
                dialog.Title = UiLocalization.Text("MainForm.FileDialog.File.Title", "Select file");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var addedFileCount = AddFileAttachments(dialog.FileNames);
                AnnounceAddedAttachments(0, addedFileCount);
            }
        }

        private void OnSelectFolderAttachmentMenuItemClick(object sender, EventArgs e)
        {
            if (IsUnixRemoteRuntimeEnabled())
            {
                ShowUnixPathAttachmentBrowser(true);
                return;
            }

            string selectedPath;
            if (!WindowsFolderPicker.TryPickFolder(
                this,
                UiLocalization.Text("MainForm.DialogDescription.SelectFolderToAttach", "Select folder to attach"),
                GetDefaultAttachmentSelectionPath(),
                out selectedPath))
            {
                return;
            }

            var addedFolderCount = AddFolderAttachments(new[] { selectedPath });
            AnnounceAddedAttachments(0, 0, 0, addedFolderCount);
        }

        private async void OnSelectSkillAttachmentMenuItemClick(object sender, EventArgs e)
        {
            if (_currentWorkspace == null || !IsCurrentWorkspaceAvailable())
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeLoadSkill", "Please open a workspace first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.LoadSkill", "Load skill")))
            {
                return;
            }

            try
            {
                SetStatus(UiLocalization.Text("MainForm.Status.LoadingSkills", "Loading skills..."));
                var snapshots = await _appServerClient.ListSkillsAsync(new[] { _currentWorkspace.RootPath }, true);
                var skills = snapshots == null
                    ? new List<AppServerSkillInfo>()
                    : snapshots
                        .SelectMany(snapshot => snapshot != null && snapshot.Skills != null
                            ? snapshot.Skills
                            : Enumerable.Empty<AppServerSkillInfo>())
                        .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.Path))
                        .OrderByDescending(skill => skill.Enabled)
                        .ThenBy(skill => GetSkillInsertionDisplayName(skill), StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                if (skills.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("MainForm.Message.NoAvailableSkillsInWorkspace", "No available skills in current workspace."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatus(UiLocalization.Text("MainForm.Status.NoAvailableSkillsInWorkspace", "No available skills in current workspace"));
                    return;
                }

                var pluginSources = await SkillSourcePluginLoader.TryLoadAsync(_appServerClient);
                using (var form = new InsertSkillForm(skills, _currentWorkspace.RootPath, pluginSources))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.SelectedSkill == null)
                    {
                        return;
                    }

                    var inserted = AddSkillAttachment(form.SelectedSkill);
                    if (inserted == null)
                    {
                        MessageBox.Show(
                            this,
                            UiLocalization.Text("MainForm.Message.SkillAlreadyInserted", "This skill has already been inserted into the current input."),
                            AppConstants.AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.SkillAlreadyInserted", "This skill has already been inserted into the current input"));
                        return;
                    }

                    InsertAttachmentMarkersAtCursor(new[] { inserted });
                    AnnounceAddedAttachments(0, 0, 1);
                }
            }
            catch (Exception ex)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.LoadSkillsFailed", "Failed to load skills"));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private async void OnSelectPluginAttachmentMenuItemClick(object sender, EventArgs e)
        {
            var selectedPlugin = await SelectPluginAttachmentCandidateAsync();
            if (selectedPlugin == null)
            {
                return;
            }

            var inserted = AddPluginAttachment(selectedPlugin);
            if (inserted == null)
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.PluginAlreadyInserted", "This plugin has already been inserted into the current input."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.PluginAlreadyInserted", "This plugin has already been inserted into the current input"));
                return;
            }

            InsertAttachmentMarkersAtCursor(new[] { inserted });
            AnnounceAddedAttachments(0, 0, 0, 0, 1);
        }

        private void OnRemoveSelectedAttachmentMenuItemClick(object sender, EventArgs e)
        {
            RemoveSelectedPendingAttachments();
        }

        private void OnClearAttachmentsMenuItemClick(object sender, EventArgs e)
        {
            RemoveAllAttachmentMarkersFromInput();
            ClearPendingAttachments(true);
        }

        private void RemoveAllAttachmentMarkersFromInput()
        {
            if (_inputBox == null || _pendingAttachments.Count == 0)
            {
                return;
            }

            _inputBox.RemoveAllAttachments();
        }

        private bool TryPasteAttachmentFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    using (var image = Clipboard.GetImage())
                    {
                        if (image == null)
                        {
                            return false;
                        }

                        var imageBytes = ConvertImageToPngBytes(image);
                        var tempPath = SaveClipboardImageToTempFile(imageBytes);
                        var addedAttachments = AddAttachments(new[]
                        {
                            new AttachmentCandidate(tempPath, true, AttachmentKind.Image, imageBytes)
                        });
                        InsertAttachmentMarkersAtCursor(addedAttachments);
                        var addedImageCount = CountAttachmentsByKind(addedAttachments, AttachmentKind.Image);
                        AnnounceAddedAttachments(addedImageCount, 0);
                        return addedImageCount > 0;
                    }
                }

                if (Clipboard.ContainsFileDropList())
                {
                    var candidates = new List<AttachmentCandidate>();
                    foreach (string path in Clipboard.GetFileDropList())
                    {
                        if (string.IsNullOrWhiteSpace(path))
                        {
                            continue;
                        }

                        var isDirectory = Directory.Exists(path);
                        var isFile = File.Exists(path);
                        if (!isDirectory && !isFile)
                        {
                            continue;
                        }

                        candidates.Add(new AttachmentCandidate(
                            path,
                            false,
                            !isDirectory && IsSupportedImageFile(path) ? AttachmentKind.Image : AttachmentKind.File,
                            null,
                            isDirectory,
                            false));
                    }

                    var addedAttachments = AddAttachments(candidates);
                    var addedImageCount = CountAttachmentsByKind(addedAttachments, AttachmentKind.Image);
                    var addedFileCount = CountFileAttachments(addedAttachments);
                    var addedFolderCount = CountFolderAttachments(addedAttachments);
                    if (addedImageCount > 0 || addedFileCount > 0 || addedFolderCount > 0)
                    {
                        InsertAttachmentMarkersAtCursor(addedAttachments);
                        AnnounceAddedAttachments(addedImageCount, addedFileCount, 0, addedFolderCount);
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.PasteAttachmentFailed", "Failed to paste attachment"));
                AddChatHistoryItem(GetErrorSenderText(), ex.Message);
            }

            return false;
        }

        private async Task<bool> TryPasteAppServerFileBrowserAttachmentsFromClipboardAsync()
        {
            IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths;
            if (!TryGetAppServerFileBrowserClipboardPaths(out copiedPaths))
            {
                return false;
            }

            await AddAppServerFileBrowserAttachmentsAsync(copiedPaths);
            return true;
        }

        private bool TryGetAppServerFileBrowserClipboardPaths(out IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths)
        {
            if (!IsUnixRemoteRuntimeEnabled())
            {
                copiedPaths = Array.Empty<RuntimeFileBrowserSelectedPath>();
                return false;
            }

            return TryGetAppServerFileBrowserClipboardPaths(
                BuildCurrentAppServerFileWindowRuntimeScope(),
                out copiedPaths);
        }

        private void ShowUnixPathAttachmentBrowser(bool isFolder)
        {
            ShowRuntimeFileBrowser(
                BuildRuntimeFileWindowTitle(
                    UiLocalization.Text(
                        isFolder ? "RuntimeFileBrowserForm.Title.AttachFolder" : "RuntimeFileBrowserForm.Title.AttachFile",
                        isFolder ? "Attach Runtime Folder" : "Attach Runtime File")),
                GetDefaultRemoteAttachmentPath(),
                isFolder ? RuntimeFileBrowserSelectionMode.Directory : RuntimeFileBrowserSelectionMode.File,
                async selectedPaths => await AddRemotePathAttachmentsAsync(selectedPaths, isFolder),
                true);
        }

        private async Task AddRemotePathAttachmentsAsync(IEnumerable<RuntimeFileBrowserSelectedPath> selectedPaths, bool isFolder)
        {
            var paths = (selectedPaths ?? Enumerable.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => item.Path.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (paths.Count == 0)
            {
                return;
            }

            foreach (var remotePath in paths)
            {
                try
                {
                    SetStatus(UiLocalization.Text("MainForm.Status.CheckingRemoteAttachment", "Checking runtime attachment..."));
                    var exists = await UnixRuntimePathExistsAsync(remotePath, isFolder);
                    if (!exists)
                    {
                        MessageBox.Show(
                            this,
                            string.Format(
                                UiLocalization.Text(
                                    isFolder
                                        ? "MainForm.Message.RemoteAttachmentFolderNotFound.Template"
                                        : "MainForm.Message.RemoteAttachmentFileNotFound.Template",
                                    isFolder
                                        ? "The runtime folder was not found: {0}"
                                        : "The runtime file was not found: {0}"),
                                remotePath),
                            AppConstants.AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        SetStatus(UiLocalization.Text("MainForm.Status.RemoteAttachmentNotFound", "Runtime attachment was not found"));
                        return;
                    }
                }
                catch (Exception ex)
                {
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CheckRemoteAttachmentFailed", "Failed to check runtime attachment"));
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            var addedAttachments = AddAttachments(paths.Select(path => new AttachmentCandidate(path, false, AttachmentKind.File, null, isFolder, true)));
            InsertAttachmentMarkersAtCursor(addedAttachments);
            AnnounceAddedAttachments(0, CountFileAttachments(addedAttachments), 0, CountFolderAttachments(addedAttachments));
        }

        private async Task AddAppServerFileBrowserAttachmentsAsync(IReadOnlyList<RuntimeFileBrowserSelectedPath> selectedPaths)
        {
            var paths = (selectedPaths ?? Array.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RuntimeFileBrowserSelectedPath(item.Path.Trim(), item.IsDirectory))
                .GroupBy(item => item.Path, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            if (paths.Count == 0)
            {
                return;
            }

            if (IsUnixRemoteRuntimeEnabled())
            {
                foreach (var item in paths)
                {
                    try
                    {
                        SetStatus(UiLocalization.Text("MainForm.Status.CheckingRemoteAttachment", "Checking runtime attachment..."));
                        if (await UnixRuntimePathExistsAsync(item.Path, item.IsDirectory))
                        {
                            continue;
                        }

                        MessageBox.Show(
                            this,
                            string.Format(
                                UiLocalization.Text(
                                    item.IsDirectory
                                        ? "MainForm.Message.RemoteAttachmentFolderNotFound.Template"
                                        : "MainForm.Message.RemoteAttachmentFileNotFound.Template",
                                    item.IsDirectory
                                        ? "The runtime folder was not found: {0}"
                                        : "The runtime file was not found: {0}"),
                                item.Path),
                            AppConstants.AppName,
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        SetStatus(UiLocalization.Text("MainForm.Status.RemoteAttachmentNotFound", "Runtime attachment was not found"));
                        return;
                    }
                    catch (Exception ex)
                    {
                        SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CheckRemoteAttachmentFailed", "Failed to check runtime attachment"));
                        MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }

                var remoteAttachments = AddAttachments(paths.Select(item => new AttachmentCandidate(item.Path, false, AttachmentKind.File, null, item.IsDirectory, true)));
                InsertAttachmentMarkersAtCursor(remoteAttachments);
                AnnounceAddedAttachments(0, CountFileAttachments(remoteAttachments), 0, CountFolderAttachments(remoteAttachments));
                return;
            }

            var addedAttachments = AddAttachments(paths.Select(item => new AttachmentCandidate(
                item.Path,
                false,
                !item.IsDirectory && IsSupportedImageFile(item.Path) ? AttachmentKind.Image : AttachmentKind.File,
                null,
                item.IsDirectory,
                false)));
            InsertAttachmentMarkersAtCursor(addedAttachments);
            AnnounceAddedAttachments(
                CountAttachmentsByKind(addedAttachments, AttachmentKind.Image),
                CountFileAttachments(addedAttachments),
                0,
                CountFolderAttachments(addedAttachments));
        }

        private PendingAttachmentItem AddSkillAttachment(AppServerSkillInfo skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.Path))
            {
                return null;
            }

            if (ContainsPendingAttachment(skill.Path))
            {
                return null;
            }

            var displayName = GetSkillInsertionDisplayName(skill);
            var markerText = BuildAttachmentMarkerText(AttachmentKind.Skill, displayName, skill.Path);
            var attachment = new PendingAttachmentItem(
                skill.Path,
                displayName,
                false,
                AttachmentKind.Skill,
                markerText,
                null,
                skill.Name,
                IsSkillDirectoryAttachmentPath(skill.Path),
                IsUnixRemoteRuntimeEnabled());
            _pendingAttachments.Add(attachment);
            RefreshPendingAttachmentState();
            return attachment;
        }

        private PendingAttachmentItem AddPluginAttachment(AppServerPluginSummary plugin)
        {
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.SourcePath))
            {
                return null;
            }

            if (ContainsPendingAttachment(plugin.SourcePath))
            {
                return null;
            }

            var displayName = GetPluginInsertionDisplayName(plugin);
            var markerText = BuildAttachmentMarkerText(AttachmentKind.Plugin, displayName, plugin.SourcePath);
            var attachment = new PendingAttachmentItem(
                plugin.SourcePath,
                displayName,
                false,
                AttachmentKind.Plugin,
                markerText,
                null,
                string.IsNullOrWhiteSpace(plugin.Name) ? displayName : plugin.Name,
                true,
                IsUnixRemoteRuntimeEnabled());
            _pendingAttachments.Add(attachment);
            RefreshPendingAttachmentState();
            return attachment;
        }

        private List<PendingAttachmentItem> AddAttachments(IEnumerable<AttachmentCandidate> candidates)
        {
            var addedAttachments = new List<PendingAttachmentItem>();
            if (candidates == null)
            {
                return addedAttachments;
            }

            foreach (var candidate in candidates)
            {
                var attachment = AddAttachment(candidate);
                if (attachment != null)
                {
                    addedAttachments.Add(attachment);
                }
            }

            if (addedAttachments.Count > 0)
            {
                RefreshPendingAttachmentState();
            }

            return addedAttachments;
        }

        private PendingAttachmentItem AddAttachment(AttachmentCandidate candidate)
        {
            if (candidate == null || (!candidate.IsRemotePath && !AttachmentPathExists(candidate.Kind, candidate.Path)))
            {
                return null;
            }

            if (candidate.Kind == AttachmentKind.Image && !IsSupportedImageFile(candidate.Path))
            {
                return null;
            }

            if (ContainsPendingAttachment(candidate.Path))
            {
                return null;
            }

            var displayName = GetAttachmentDisplayName(candidate.Path);
            var markerText = BuildAttachmentMarkerText(candidate.Kind, displayName, candidate.Path, candidate.IsDirectory);
            var attachment = new PendingAttachmentItem(
                candidate.Path,
                displayName,
                candidate.IsTemporaryFile,
                candidate.Kind,
                markerText,
                candidate.InMemoryContent,
                null,
                candidate.IsDirectory,
                candidate.IsRemotePath);
            _pendingAttachments.Add(attachment);
            return attachment;
        }

        private int AddImageAttachments(IEnumerable<string> paths, bool temporaryFiles)
        {
            var addedAttachments = AddAttachments((paths ?? Array.Empty<string>())
                .Select(path => new AttachmentCandidate(path, temporaryFiles, AttachmentKind.Image, null)));
            InsertAttachmentMarkersAtCursor(addedAttachments);
            return CountAttachmentsByKind(addedAttachments, AttachmentKind.Image);
        }

        private int AddFileAttachments(IEnumerable<string> paths)
        {
            var addedAttachments = AddAttachments((paths ?? Array.Empty<string>())
                .Select(path => new AttachmentCandidate(path, false, AttachmentKind.File, null)));
            InsertAttachmentMarkersAtCursor(addedAttachments);
            return CountFileAttachments(addedAttachments);
        }

        private int AddFolderAttachments(IEnumerable<string> paths)
        {
            var addedAttachments = AddAttachments((paths ?? Array.Empty<string>())
                .Select(path => new AttachmentCandidate(path, false, AttachmentKind.File, null, true, false)));
            InsertAttachmentMarkersAtCursor(addedAttachments);
            return CountFolderAttachments(addedAttachments);
        }

        private string GetDefaultAttachmentSelectionPath()
        {
            if (_currentWorkspace != null && !string.IsNullOrWhiteSpace(_currentWorkspace.RootPath) && Directory.Exists(_currentWorkspace.RootPath))
            {
                return _currentWorkspace.RootPath;
            }

            return Environment.CurrentDirectory;
        }

        private string GetDefaultRemoteAttachmentPath()
        {
            if (_currentWorkspace != null && IsUnixRuntimePath(_currentWorkspace.RootPath))
            {
                return _currentWorkspace.RootPath;
            }

            return GetDefaultUnixWorkspacePath();
        }

        private Task<bool> UnixRuntimePathExistsAsync(string path, bool isDirectory)
        {
            if (IsRemoteMacRuntimeEnabled())
            {
                return isDirectory
                    ? _remoteMacFileSystemBridge.DirectoryExistsAsync(GetCurrentRemoteMacConfig(), path)
                    : _remoteMacFileSystemBridge.FileExistsAsync(GetCurrentRemoteMacConfig(), path);
            }

            return isDirectory
                ? _wslFileSystemBridge.DirectoryExistsAsync(GetCurrentWslConfig(), path)
                : _wslFileSystemBridge.FileExistsAsync(GetCurrentWslConfig(), path);
        }

        private void SynchronizePendingAttachmentsWithInputText(bool deleteTemporaryFiles)
        {
            if (_pendingAttachments.Count == 0)
            {
                return;
            }

            var syncResult = ComposeAttachmentState.SynchronizeWithInputText(_pendingAttachments, _inputBox.ComposeText);
            if (!syncResult.Changed)
            {
                return;
            }

            if (deleteTemporaryFiles)
            {
                foreach (var attachment in syncResult.RemovedAttachments)
                {
                    DeleteTemporaryAttachmentFile(attachment);
                }
            }

            RefreshPendingAttachmentState();

            var removedImageCount = ComposeAttachmentState.CountAttachmentsByKind(syncResult.RemovedAttachments, AttachmentKind.Image);
            var removedFileCount = ComposeAttachmentState.CountFileAttachments(syncResult.RemovedAttachments);
            var removedFolderCount = ComposeAttachmentState.CountFolderAttachments(syncResult.RemovedAttachments);
            var removedSkillCount = ComposeAttachmentState.CountAttachmentsByKind(syncResult.RemovedAttachments, AttachmentKind.Skill);
            var removedPluginCount = ComposeAttachmentState.CountAttachmentsByKind(syncResult.RemovedAttachments, AttachmentKind.Plugin);
            if (removedImageCount > 0 || removedFileCount > 0 || removedFolderCount > 0 || removedSkillCount > 0 || removedPluginCount > 0)
            {
                SetStatus(ComposeAttachmentState.BuildAttachmentChangeMessage(UiLocalization.Text("MainForm.Attachment.Action.Removed", "Removed"), removedImageCount, removedFileCount, removedSkillCount, removedFolderCount, removedPluginCount));
            }
        }

        private string SaveClipboardImageToTempFile(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.Exception.ClipboardImageEmpty", "Clipboard image content is empty and cannot be saved."));
            }

            var directory = GetAttachmentTempDirectory();
            Directory.CreateDirectory(directory);
            var fileName = string.Format("paste-{0:yyyyMMdd-HHmmssfff}.png", DateTime.Now);
            var fullPath = Path.Combine(directory, fileName);
            File.WriteAllBytes(fullPath, imageBytes);
            return fullPath;
        }

        private string GetAttachmentTempDirectory()
        {
            return _appConfigService.GetAttachmentTempDirectory();
        }

        private void RemoveSelectedPendingAttachments()
        {
            var selectedItem = _pendingAttachmentListBox.SelectedItem as PendingAttachmentItem;
            if (selectedItem == null)
            {
                return;
            }

            RemoveAttachmentMarkerFromInput(selectedItem);
            DeleteTemporaryAttachmentFile(selectedItem);
            _pendingAttachments.Remove(selectedItem);
            RefreshPendingAttachmentState();
            AnnounceRemovedAttachments(
                selectedItem.Kind == AttachmentKind.Image ? 1 : 0,
                selectedItem.Kind == AttachmentKind.File && !IsFolderAttachment(selectedItem) ? 1 : 0,
                selectedItem.Kind == AttachmentKind.Skill ? 1 : 0,
                IsFolderAttachment(selectedItem) ? 1 : 0,
                selectedItem.Kind == AttachmentKind.Plugin ? 1 : 0);
        }

        private void RemoveAttachmentMarkerFromInput(PendingAttachmentItem attachment)
        {
            if (attachment == null || string.IsNullOrWhiteSpace(attachment.MarkerText) || _inputBox == null)
            {
                return;
            }

            _inputBox.RemoveAttachment(attachment);
            _inputBox.Focus();
        }

        private void AnnounceAddedAttachments(int imageCount, int fileCount, int skillCount = 0, int folderCount = 0, int pluginCount = 0)
        {
            AnnounceAttachmentChange(UiLocalization.Text("MainForm.Attachment.Action.Added", "Added"), imageCount, fileCount, skillCount, folderCount, pluginCount);
        }

        private void AnnounceRemovedAttachments(int imageCount, int fileCount, int skillCount = 0, int folderCount = 0, int pluginCount = 0)
        {
            AnnounceAttachmentChange(UiLocalization.Text("MainForm.Attachment.Action.Removed", "Removed"), imageCount, fileCount, skillCount, folderCount, pluginCount);
        }

        private void AnnounceAttachmentChange(string actionPrefix, int imageCount, int fileCount, int skillCount, int folderCount, int pluginCount)
        {
            if (imageCount <= 0 && fileCount <= 0 && skillCount <= 0 && folderCount <= 0 && pluginCount <= 0)
            {
                return;
            }

            var message = ComposeAttachmentState.BuildAttachmentChangeMessage(actionPrefix, imageCount, fileCount, skillCount, folderCount, pluginCount);
            SetStatus(message);
            SpeakAuxiliaryTextWithScreenReader(message);
        }

        private void ClearPendingAttachments(bool deleteTemporaryFiles)
        {
            if (deleteTemporaryFiles)
            {
                foreach (var item in _pendingAttachments)
                {
                    DeleteTemporaryAttachmentFile(item);
                }
            }

            _pendingAttachments.Clear();
            RefreshPendingAttachmentState();
        }

        private void ReplacePendingAttachments(IEnumerable<PendingAttachmentItem> items, bool deleteTemporaryFiles)
        {
            if (deleteTemporaryFiles)
            {
                foreach (var item in _pendingAttachments)
                {
                    DeleteTemporaryAttachmentFile(item);
                }
            }

            _pendingAttachments.Clear();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item != null)
                    {
                        _pendingAttachments.Add(item.Clone());
                    }
                }
            }

            RefreshPendingAttachmentState();
        }

        private void SaveCurrentComposeState()
        {
            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.Id))
            {
                SaveCurrentModeUnboundComposeState();
                return;
            }

            _composeDraftStore.Save(GetComposeDraftScopeKey(_currentSession), _currentSession.Id, _inputBox.ComposeText, _pendingAttachments);
        }

        private ComposeDraftState CaptureCurrentComposeDraftState()
        {
            return ComposeAttachmentState.CaptureDraftState(_inputBox.ComposeText, _pendingAttachments);
        }

        private void RestoreComposeDraftState(ComposeDraftState state)
        {
            if (state == null)
            {
                return;
            }

            ReplacePendingAttachments(state.Attachments, false);
            SetInputBoxText(state.DraftText ?? string.Empty, (state.DraftText ?? string.Empty).Length, 0);
            SynchronizePendingAttachmentsWithInputText(false);
        }

        private void ApplyComposeState(ChatSession session)
        {
            var draftState = session == null
                ? GetCurrentModeUnboundComposeState()
                : _composeDraftStore.Get(GetComposeDraftScopeKey(session), session.Id);
            ReplacePendingAttachments(draftState != null ? draftState.Attachments : null, false);

            if (session == null || string.IsNullOrWhiteSpace(session.Id) || draftState == null)
            {
                var draftText = draftState != null ? draftState.DraftText ?? string.Empty : string.Empty;
                SetInputBoxText(draftText, draftText.Length, 0);
            }
            else
            {
                SetInputBoxText(draftState.DraftText ?? string.Empty, (draftState.DraftText ?? string.Empty).Length, 0);
            }

            SynchronizePendingAttachmentsWithInputText(false);
        }

        private void RemoveComposeState(ChatSession session, bool deleteTemporaryFiles)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.Id))
            {
                return;
            }

            if (_currentSession != null &&
                string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase))
            {
                SetInputBoxText(string.Empty, 0, 0);
                ReplacePendingAttachments(null, deleteTemporaryFiles);
            }

            _composeDraftStore.Remove(GetComposeDraftScopeKey(session), session.Id, deleteTemporaryFiles, DeleteTemporaryAttachmentFile);
        }

        private void ClearAllComposeStates(bool deleteTemporaryFiles)
        {
            _composeDraftStore.ClearAll(deleteTemporaryFiles, DeleteTemporaryAttachmentFile);
            ReplacePendingAttachments(null, deleteTemporaryFiles);
            SetInputBoxText(string.Empty, 0, 0);
        }

        private async Task<List<PendingAttachmentItem>> EnsurePendingAttachmentsReadyForSendAsync(
            IReadOnlyList<PendingAttachmentItem> attachments)
        {
            var targetAttachments = attachments ?? new PendingAttachmentItem[0];
            var unavailableAttachments = PendingAttachmentSendValidator.EnsureReady(targetAttachments);
            if (!IsRemoteMacRuntimeEnabled() || targetAttachments.Count == 0)
            {
                return unavailableAttachments;
            }

            foreach (var item in targetAttachments)
            {
                if (item == null ||
                    !item.IsRemotePath ||
                    string.IsNullOrWhiteSpace(item.LocalPath))
                {
                    continue;
                }

                var exists = item.IsDirectory
                    ? await _remoteMacFileSystemBridge.DirectoryExistsAsync(GetCurrentRemoteMacConfig(), item.LocalPath)
                    : await _remoteMacFileSystemBridge.FileExistsAsync(GetCurrentRemoteMacConfig(), item.LocalPath);
                if (!exists)
                {
                    unavailableAttachments.Add(item);
                }
            }

            return unavailableAttachments;
        }

        private static void DeleteTemporaryAttachmentFile(PendingAttachmentItem item)
        {
            if (item == null || !item.IsTemporaryFile || string.IsNullOrWhiteSpace(item.LocalPath))
            {
                return;
            }

            try
            {
                if (File.Exists(item.LocalPath))
                {
                    File.Delete(item.LocalPath);
                }
            }
            catch
            {
            }
        }

        private string GetComposeDraftScopeKey(ChatSession session)
        {
            var fallbackOfficeRuntimeScopeId = session != null && session.Mode == AppInteractionMode.Office
                ? GetActiveOfficeRuntimeTarget().RuntimeScopeId
                : null;
            return SessionRuntimeIdentityResolver.ResolveDraftScopeId(
                session,
                GetConfiguredRuntimeProfileId(),
                fallbackOfficeRuntimeScopeId);
        }

        private void RefreshPendingAttachmentState()
        {
            RefreshPendingAttachmentState(IsCurrentWorkspaceAvailable());
        }

        private void RefreshPendingAttachmentState(bool workspaceAvailable)
        {
            _pendingAttachmentListBox.BeginUpdate();
            try
            {
                _pendingAttachmentListBox.Items.Clear();
                foreach (var item in _pendingAttachments)
                {
                    _pendingAttachmentListBox.Items.Add(item);
                }
            }
            finally
            {
                _pendingAttachmentListBox.EndUpdate();
            }

            var hasAttachments = _pendingAttachments.Count > 0;
            _attachmentPanel.Visible = hasAttachments;
            UpdateComposePanelHeight();
            _pendingAttachmentListBox.Enabled = hasAttachments && _currentSession != null && workspaceAvailable;
            _removeSelectedAttachmentMenuItem.Enabled = hasAttachments && _pendingAttachmentListBox.SelectedItem is PendingAttachmentItem;
            _clearAttachmentsMenuItem.Enabled = hasAttachments;
        }

        private bool CanPasteSubAgentWorkbenchAttachments()
        {
            try
            {
                IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths;
                if (TryGetAppServerFileBrowserClipboardPaths(out copiedPaths))
                {
                    return true;
                }

                return Clipboard.ContainsImage() || Clipboard.ContainsFileDropList();
            }
            catch
            {
                return false;
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> PasteSubAgentWorkbenchAttachmentsAsync()
        {
            try
            {
                var runtimeAttachments = await CreateSubAgentWorkbenchRuntimeClipboardAttachmentsAsync();
                if (runtimeAttachments.Count > 0)
                {
                    return runtimeAttachments;
                }

                return CreateSubAgentWorkbenchClipboardAttachments();
            }
            catch (Exception ex)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.PasteAttachmentFailed", "Failed to paste attachment"));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new PendingAttachmentItem[0];
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> CreateSubAgentWorkbenchRuntimeClipboardAttachmentsAsync()
        {
            IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths;
            if (!TryGetAppServerFileBrowserClipboardPaths(out copiedPaths))
            {
                return new PendingAttachmentItem[0];
            }

            var paths = (copiedPaths ?? Array.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RuntimeFileBrowserSelectedPath(item.Path.Trim(), item.IsDirectory))
                .GroupBy(item => item.Path, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToList();
            if (paths.Count == 0)
            {
                return new PendingAttachmentItem[0];
            }

            foreach (var item in paths)
            {
                try
                {
                    SetStatus(UiLocalization.Text("MainForm.Status.CheckingRemoteAttachment", "Checking runtime attachment..."));
                    if (await UnixRuntimePathExistsAsync(item.Path, item.IsDirectory))
                    {
                        continue;
                    }

                    MessageBox.Show(
                        this,
                        string.Format(
                            UiLocalization.Text(
                                item.IsDirectory
                                    ? "MainForm.Message.RemoteAttachmentFolderNotFound.Template"
                                    : "MainForm.Message.RemoteAttachmentFileNotFound.Template",
                                item.IsDirectory
                                    ? "The runtime folder was not found: {0}"
                                    : "The runtime file was not found: {0}"),
                            item.Path),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatus(UiLocalization.Text("MainForm.Status.RemoteAttachmentNotFound", "Runtime attachment was not found"));
                    return new PendingAttachmentItem[0];
                }
                catch (Exception ex)
                {
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CheckRemoteAttachmentFailed", "Failed to check runtime attachment"));
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return new PendingAttachmentItem[0];
                }
            }

            return CreateSubAgentWorkbenchAttachments(paths.Select(item => new AttachmentCandidate(
                item.Path,
                false,
                AttachmentKind.File,
                null,
                item.IsDirectory,
                true)));
        }

        private IReadOnlyList<PendingAttachmentItem> CreateSubAgentWorkbenchClipboardAttachments()
        {
            if (Clipboard.ContainsImage())
            {
                using (var image = Clipboard.GetImage())
                {
                    if (image == null)
                    {
                        return new PendingAttachmentItem[0];
                    }

                    var imageBytes = ConvertImageToPngBytes(image);
                    var tempPath = SaveClipboardImageToTempFile(imageBytes);
                    return CreateSubAgentWorkbenchAttachments(new[]
                    {
                        new AttachmentCandidate(tempPath, true, AttachmentKind.Image, imageBytes)
                    });
                }
            }

            if (!Clipboard.ContainsFileDropList())
            {
                return new PendingAttachmentItem[0];
            }

            var candidates = new List<AttachmentCandidate>();
            foreach (string path in Clipboard.GetFileDropList())
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var isDirectory = Directory.Exists(path);
                var isFile = File.Exists(path);
                if (!isDirectory && !isFile)
                {
                    continue;
                }

                candidates.Add(new AttachmentCandidate(
                    path,
                    false,
                    !isDirectory && IsSupportedImageFile(path) ? AttachmentKind.Image : AttachmentKind.File,
                    null,
                    isDirectory,
                    false));
            }

            return CreateSubAgentWorkbenchAttachments(candidates);
        }

        private Task<IReadOnlyList<PendingAttachmentItem>> SelectSubAgentWorkbenchImageAttachmentsAsync()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = UiLocalization.Text("MainForm.FileDialog.Image.Filter", "Image Files|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp|All Files|*.*");
                dialog.Multiselect = true;
                dialog.Title = UiLocalization.Text("MainForm.FileDialog.Image.Title", "Select image");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return Task.FromResult((IReadOnlyList<PendingAttachmentItem>)new PendingAttachmentItem[0]);
                }

                return Task.FromResult((IReadOnlyList<PendingAttachmentItem>)CreateSubAgentWorkbenchAttachments(
                    dialog.FileNames.Select(path => new AttachmentCandidate(path, false, AttachmentKind.Image, null))));
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSubAgentWorkbenchFileAttachmentsAsync()
        {
            if (IsUnixRemoteRuntimeEnabled())
            {
                return await SelectSubAgentWorkbenchUnixAttachmentsAsync(false);
            }

            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = UiLocalization.Text("MainForm.FileDialog.All.Filter", "All Files|*.*");
                dialog.Multiselect = true;
                dialog.Title = UiLocalization.Text("MainForm.FileDialog.File.Title", "Select file");
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return new PendingAttachmentItem[0];
                }

                return CreateSubAgentWorkbenchAttachments(
                    dialog.FileNames.Select(path => new AttachmentCandidate(path, false, AttachmentKind.File, null)));
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSubAgentWorkbenchFolderAttachmentsAsync()
        {
            if (IsUnixRemoteRuntimeEnabled())
            {
                return await SelectSubAgentWorkbenchUnixAttachmentsAsync(true);
            }

            string selectedPath;
            if (!WindowsFolderPicker.TryPickFolder(
                this,
                UiLocalization.Text("MainForm.DialogDescription.SelectFolderToAttach", "Select folder to attach"),
                GetDefaultAttachmentSelectionPath(),
                out selectedPath))
            {
                return new PendingAttachmentItem[0];
            }

            return CreateSubAgentWorkbenchAttachments(new[]
            {
                new AttachmentCandidate(selectedPath, false, AttachmentKind.File, null, true, false)
            });
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSubAgentWorkbenchSkillAttachmentsAsync()
        {
            if (_currentWorkspace == null || !IsCurrentWorkspaceAvailable())
            {
                MessageBox.Show(
                    this,
                    UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeLoadSkill", "Please open a workspace first."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return new PendingAttachmentItem[0];
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.LoadSkill", "Load skill")))
            {
                return new PendingAttachmentItem[0];
            }

            try
            {
                SetStatus(UiLocalization.Text("MainForm.Status.LoadingSkills", "Loading skills..."));
                var snapshots = await _appServerClient.ListSkillsAsync(new[] { _currentWorkspace.RootPath }, true);
                var skills = snapshots == null
                    ? new List<AppServerSkillInfo>()
                    : snapshots
                        .SelectMany(snapshot => snapshot != null && snapshot.Skills != null
                            ? snapshot.Skills
                            : Enumerable.Empty<AppServerSkillInfo>())
                        .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.Path))
                        .OrderByDescending(skill => skill.Enabled)
                        .ThenBy(skill => GetSkillInsertionDisplayName(skill), StringComparer.CurrentCultureIgnoreCase)
                        .ToList();

                if (skills.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("MainForm.Message.NoAvailableSkillsInWorkspace", "No available skills in current workspace."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatus(UiLocalization.Text("MainForm.Status.NoAvailableSkillsInWorkspace", "No available skills in current workspace"));
                    return new PendingAttachmentItem[0];
                }

                var pluginSources = await SkillSourcePluginLoader.TryLoadAsync(_appServerClient);
                using (var form = new InsertSkillForm(skills, _currentWorkspace.RootPath, pluginSources))
                {
                    if (form.ShowDialog(this) != DialogResult.OK || form.SelectedSkill == null)
                    {
                        return new PendingAttachmentItem[0];
                    }

                    return CreateSubAgentWorkbenchSkillAttachment(form.SelectedSkill);
                }
            }
            catch (Exception ex)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.LoadSkillsFailed", "Failed to load skills"));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return new PendingAttachmentItem[0];
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSubAgentWorkbenchPluginAttachmentsAsync()
        {
            var plugin = await SelectPluginAttachmentCandidateAsync();
            if (plugin == null || string.IsNullOrWhiteSpace(plugin.SourcePath))
            {
                return new PendingAttachmentItem[0];
            }

            var displayName = GetPluginInsertionDisplayName(plugin);
            return new[]
            {
                new PendingAttachmentItem(
                    plugin.SourcePath,
                    displayName,
                    false,
                    AttachmentKind.Plugin,
                    ComposeAttachmentState.BuildAttachmentMarkerText(null, AttachmentKind.Plugin, displayName, plugin.SourcePath),
                    null,
                    string.IsNullOrWhiteSpace(plugin.Name) ? displayName : plugin.Name,
                    true,
                    IsUnixRemoteRuntimeEnabled())
            };
        }

        private async Task<AppServerPluginSummary> SelectPluginAttachmentCandidateAsync()
        {
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.LoadPlugin", "Load plugin")))
            {
                return null;
            }

            try
            {
                SetStatus(UiLocalization.Text("MainForm.Status.LoadingPlugins", "Loading plugins..."));
                var installed = await AppServerInstalledPluginObservationService.LoadAsync(
                    _appServerClient,
                    _appServerClient.ProtocolCapabilities,
                    System.Threading.CancellationToken.None);
                var insertablePlugins = PluginAttachmentSelectionBuilder.BuildInsertablePlugins(installed.Plugins);
                if (insertablePlugins.Count == 0)
                {
                    MessageBox.Show(
                        this,
                        UiLocalization.Text("MainForm.Message.NoAvailablePlugins", "No installed plugins are available to insert."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatus(UiLocalization.Text("MainForm.Status.NoAvailablePlugins", "No installed plugins are available to insert"));
                    return null;
                }

                using (var form = new InsertPluginForm(insertablePlugins))
                {
                    return form.ShowDialog(this) == DialogResult.OK ? form.SelectedPlugin : null;
                }
            }
            catch (Exception ex)
            {
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.LoadPluginsFailed", "Failed to load plugins"));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> SelectSubAgentWorkbenchUnixAttachmentsAsync(bool isFolder)
        {
            var selectedPaths = new List<RuntimeFileBrowserSelectedPath>();
            using (var form = new RuntimeFileBrowserForm(
                BuildRuntimeFileWindowTitle(
                    UiLocalization.Text(
                        isFolder ? "RuntimeFileBrowserForm.Title.AttachFolder" : "RuntimeFileBrowserForm.Title.AttachFile",
                        isFolder ? "Attach Runtime Folder" : "Attach Runtime File")),
                GetDefaultRemoteAttachmentPath(),
                isFolder ? RuntimeFileBrowserSelectionMode.Directory : RuntimeFileBrowserSelectionMode.File,
                ListCurrentUnixRuntimeDirectoryAsync,
                true))
            {
                form.PathSelected += (sender, e) =>
                {
                    selectedPaths = (e.SelectedPaths ?? Array.Empty<RuntimeFileBrowserSelectedPath>()).ToList();
                };
                form.ShowDialog(this);
            }

            var paths = selectedPaths
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => item.Path.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (paths.Count == 0)
            {
                return new PendingAttachmentItem[0];
            }

            foreach (var remotePath in paths)
            {
                try
                {
                    SetStatus(UiLocalization.Text("MainForm.Status.CheckingRemoteAttachment", "Checking runtime attachment..."));
                    var exists = await UnixRuntimePathExistsAsync(remotePath, isFolder);
                    if (exists)
                    {
                        continue;
                    }

                    MessageBox.Show(
                        this,
                        string.Format(
                            UiLocalization.Text(
                                isFolder
                                    ? "MainForm.Message.RemoteAttachmentFolderNotFound.Template"
                                    : "MainForm.Message.RemoteAttachmentFileNotFound.Template",
                                isFolder
                                    ? "The runtime folder was not found: {0}"
                                    : "The runtime file was not found: {0}"),
                            remotePath),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    SetStatus(UiLocalization.Text("MainForm.Status.RemoteAttachmentNotFound", "Runtime attachment was not found"));
                    return new PendingAttachmentItem[0];
                }
                catch (Exception ex)
                {
                    SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CheckRemoteAttachmentFailed", "Failed to check runtime attachment"));
                    MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return new PendingAttachmentItem[0];
                }
            }

            return CreateSubAgentWorkbenchAttachments(paths.Select(path => new AttachmentCandidate(path, false, AttachmentKind.File, null, isFolder, true)));
        }

        private IReadOnlyList<PendingAttachmentItem> CreateSubAgentWorkbenchSkillAttachment(AppServerSkillInfo skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.Path))
            {
                return new PendingAttachmentItem[0];
            }

            var displayName = GetSkillInsertionDisplayName(skill);
            return new[]
            {
                new PendingAttachmentItem(
                    skill.Path,
                    displayName,
                    false,
                    AttachmentKind.Skill,
                    ComposeAttachmentState.BuildAttachmentMarkerText(null, AttachmentKind.Skill, displayName, skill.Path),
                    null,
                    skill.Name,
                    IsSkillDirectoryAttachmentPath(skill.Path),
                    IsUnixRemoteRuntimeEnabled())
            };
        }

        private static bool IsSkillDirectoryAttachmentPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var normalized = path.Trim().TrimEnd('/', '\\');
            var separatorIndex = normalized.LastIndexOfAny(new[] { '/', '\\' });
            var name = separatorIndex >= 0 ? normalized.Substring(separatorIndex + 1) : normalized;
            return !string.Equals(name, SkillFileEditor.SkillFileName, StringComparison.OrdinalIgnoreCase);
        }

        private static IReadOnlyList<PendingAttachmentItem> CreateSubAgentWorkbenchAttachments(IEnumerable<AttachmentCandidate> candidates)
        {
            var attachments = new List<PendingAttachmentItem>();
            if (candidates == null)
            {
                return attachments;
            }

            foreach (var candidate in candidates)
            {
                if (candidate == null ||
                    string.IsNullOrWhiteSpace(candidate.Path) ||
                    (!candidate.IsRemotePath && !AttachmentPathExists(candidate.Kind, candidate.Path)))
                {
                    continue;
                }

                if (candidate.Kind == AttachmentKind.Image && !IsSupportedImageFile(candidate.Path))
                {
                    continue;
                }

                if (ComposeAttachmentState.ContainsPendingAttachment(attachments, candidate.Path))
                {
                    continue;
                }

                var displayName = GetAttachmentDisplayName(candidate.Path);
                var markerText = ComposeAttachmentState.BuildAttachmentMarkerText(
                    attachments,
                    candidate.Kind,
                    displayName,
                    candidate.Path,
                    candidate.IsDirectory);
                attachments.Add(new PendingAttachmentItem(
                    candidate.Path,
                    displayName,
                    candidate.IsTemporaryFile,
                    candidate.Kind,
                    markerText,
                    candidate.InMemoryContent,
                    null,
                    candidate.IsDirectory,
                    candidate.IsRemotePath));
            }

            return attachments;
        }

        private async Task<IReadOnlyList<PendingAttachmentItem>> ValidateSubAgentWorkbenchAttachmentsAsync(IReadOnlyList<PendingAttachmentItem> attachments)
        {
            var unavailableAttachments = PendingAttachmentSendValidator.EnsureReady(attachments);
            if (!IsRemoteMacRuntimeEnabled() || attachments == null || attachments.Count == 0)
            {
                return unavailableAttachments;
            }

            foreach (var item in attachments)
            {
                if (item == null || !item.IsRemotePath || string.IsNullOrWhiteSpace(item.LocalPath))
                {
                    continue;
                }

                var exists = await UnixRuntimePathExistsAsync(item.LocalPath, item.IsDirectory);
                if (!exists)
                {
                    unavailableAttachments.Add(item);
                }
            }

            return unavailableAttachments;
        }

        private List<AppServerUserInputItem> BuildOutgoingInputItems(string text, IReadOnlyList<PendingAttachmentItem> attachments)
        {
            return OutgoingInputItemBuilder.Build(text, attachments, GetImageAttachmentBytes);
        }

        private static List<string> CollectTurnSkillNames(IReadOnlyList<PendingAttachmentItem> attachments)
        {
            var skillNames = new List<string>();
            if (attachments == null || attachments.Count == 0)
            {
                return skillNames;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < attachments.Count; i++)
            {
                var attachment = attachments[i];
                if (attachment == null || attachment.Kind != AttachmentKind.Skill)
                {
                    continue;
                }

                var skillName = GetTurnSkillDisplayName(attachment);
                if (string.IsNullOrWhiteSpace(skillName) || !seen.Add(skillName))
                {
                    continue;
                }

                skillNames.Add(skillName);
            }

            return skillNames;
        }

        private static string GetTurnSkillDisplayName(PendingAttachmentItem attachment)
        {
            if (attachment == null)
            {
                return string.Empty;
            }

            var skillName = !string.IsNullOrWhiteSpace(attachment.DisplayName)
                ? attachment.DisplayName.Trim()
                : string.Empty;
            if (skillName.Length > 0)
            {
                return skillName;
            }

            skillName = !string.IsNullOrWhiteSpace(attachment.InputName)
                ? attachment.InputName.Trim()
                : string.Empty;
            if (skillName.Length > 0)
            {
                return skillName;
            }

            if (string.IsNullOrWhiteSpace(attachment.LocalPath))
            {
                return string.Empty;
            }

            return Path.GetFileName(attachment.LocalPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private byte[] GetImageAttachmentBytes(PendingAttachmentItem attachment)
        {
            if (attachment == null)
            {
                return null;
            }

            if (attachment.InMemoryContent != null && attachment.InMemoryContent.Length > 0)
            {
                return attachment.InMemoryContent;
            }

            if (string.IsNullOrWhiteSpace(attachment.LocalPath) || !File.Exists(attachment.LocalPath))
            {
                return null;
            }

            try
            {
                return File.ReadAllBytes(attachment.LocalPath);
            }
            catch
            {
                return null;
            }
        }

        private static string BuildOutgoingMessagePreview(string text, IReadOnlyList<PendingAttachmentItem> attachments)
        {
            return OutgoingInputItemBuilder.BuildPreviewText(text, attachments);
        }

        private string BuildAttachmentMarkerText(AttachmentKind kind, string displayName, string path, bool isDirectory = false)
        {
            return ComposeAttachmentState.BuildAttachmentMarkerText(_pendingAttachments, kind, displayName, path, isDirectory);
        }

        private bool ContainsPendingAttachment(string path)
        {
            return ComposeAttachmentState.ContainsPendingAttachment(_pendingAttachments, path);
        }

        private void InsertAttachmentMarkersAtCursor(IReadOnlyCollection<PendingAttachmentItem> attachments)
        {
            var normalized = (attachments ?? Array.Empty<PendingAttachmentItem>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.MarkerText))
                .ToArray();
            if (normalized.Length == 0)
            {
                return;
            }

            _inputBox.InsertAttachments(normalized);
            SortPendingAttachmentsByInputText(_inputBox.ComposeText);
            RefreshPendingAttachmentState();
            _inputBox.Focus();
        }

        private void SetInputBoxText(string text, int selectionStart, int selectionLength)
        {
            _isUpdatingInputBoxText = true;
            try
            {
                _inputBox.SetComposeText(text ?? string.Empty, _pendingAttachments, selectionStart, selectionLength);
            }
            finally
            {
                _isUpdatingInputBoxText = false;
            }
        }

        private bool SortPendingAttachmentsByInputText(string inputText)
        {
            return ComposeAttachmentState.SortByInputText(_pendingAttachments, inputText);
        }

        private static bool AttachmentPathExists(AttachmentKind kind, string path)
        {
            return PendingAttachmentSendValidator.PathExists(kind, path);
        }

        private static string GetAttachmentDisplayName(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return UiLocalization.Text("MainForm.Attachment.Value.Unnamed", "Unnamed");
            }

            var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(trimmedPath))
            {
                return UiLocalization.Text("MainForm.Attachment.Value.Unnamed", "Unnamed");
            }

            var displayName = Path.GetFileName(trimmedPath);
            return string.IsNullOrWhiteSpace(displayName) ? trimmedPath : displayName;
        }

        private static int CountAttachmentsByKind(IEnumerable<PendingAttachmentItem> attachments, AttachmentKind kind)
        {
            return ComposeAttachmentState.CountAttachmentsByKind(attachments, kind);
        }

        private static int CountFileAttachments(IEnumerable<PendingAttachmentItem> attachments)
        {
            return ComposeAttachmentState.CountFileAttachments(attachments);
        }

        private static int CountFolderAttachments(IEnumerable<PendingAttachmentItem> attachments)
        {
            return ComposeAttachmentState.CountFolderAttachments(attachments);
        }

        private static bool IsFolderAttachment(PendingAttachmentItem item)
        {
            return ComposeAttachmentState.IsFolderAttachment(item);
        }

        private static string GetSkillInsertionDisplayName(AppServerSkillInfo skill)
        {
            return ComposeAttachmentState.GetSkillInsertionDisplayName(skill);
        }

        private static string GetPluginInsertionDisplayName(AppServerPluginSummary plugin)
        {
            return ComposeAttachmentState.GetPluginInsertionDisplayName(plugin);
        }

        private static byte[] ConvertImageToPngBytes(Image image)
        {
            if (image == null)
            {
                return null;
            }

            using (var stream = new MemoryStream())
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                return stream.ToArray();
            }
        }

        private static bool IsSupportedImageFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".gif", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".bmp", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".webp", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class AttachmentCandidate
        {
            public AttachmentCandidate(string path, bool isTemporaryFile, AttachmentKind kind, byte[] inMemoryContent)
                : this(path, isTemporaryFile, kind, inMemoryContent, false, false)
            {
            }

            public AttachmentCandidate(
                string path,
                bool isTemporaryFile,
                AttachmentKind kind,
                byte[] inMemoryContent,
                bool isDirectory,
                bool isRemotePath)
            {
                Path = path;
                IsTemporaryFile = isTemporaryFile;
                Kind = kind;
                InMemoryContent = inMemoryContent != null ? (byte[])inMemoryContent.Clone() : null;
                IsDirectory = isDirectory;
                IsRemotePath = isRemotePath;
            }

            public string Path { get; private set; }

            public bool IsTemporaryFile { get; private set; }

            public AttachmentKind Kind { get; private set; }

            public byte[] InMemoryContent { get; private set; }

            public bool IsDirectory { get; private set; }

            public bool IsRemotePath { get; private set; }
        }

    }
}
