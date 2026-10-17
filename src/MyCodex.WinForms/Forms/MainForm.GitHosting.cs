using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Dialogs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private GitCloneForm _gitCloneForm;
        private GitRemoteRepositoriesForm _gitRemoteRepositoriesForm;
        private GitHubStoreForm _gitHubStoreForm;
        private GitEnvironmentForm _gitEnvironmentForm;
        private GitRemoteCredentialsForm _gitRemoteCredentialsForm;

        private IGitHubCliService ResolveActiveGitHubCliService()
        {
            return GetActiveGitHostingProvider().GetCapability<IGitHubHostingProviderCapability>()?.GitHubCliService;
        }

        private IGitCloneService CreateActiveGitCloneService()
        {
            if (IsRemoteUnixGitRuntime())
            {
                return new RemoteMacGitCloneService(GetCurrentRemoteMacConfig());
            }

            if (IsWslGitRuntime())
            {
                return new WslGitCloneService(GetCurrentWslConfig());
            }

            return new MyCodex.Infrastructure.Git.GitCloneService();
        }

        private IGitEnvironmentService CreateActiveGitEnvironmentService()
        {
            if (IsRemoteUnixGitRuntime())
            {
                return new RemoteMacGitEnvironmentService(GetCurrentRemoteMacConfig());
            }

            if (IsWslGitRuntime())
            {
                return new WslGitEnvironmentService(GetCurrentWslConfig());
            }

            return new MyCodex.Infrastructure.Git.GitEnvironmentService();
        }

        private IGitSshKeyService CreateActiveGitSshKeyService()
        {
            if (IsRemoteUnixGitRuntime())
            {
                return new RemoteMacGitSshKeyService(GetCurrentRemoteMacConfig());
            }

            if (IsWslGitRuntime())
            {
                return new WslGitSshKeyService(GetCurrentWslConfig());
            }

            return new MyCodex.Infrastructure.Git.GitSshKeyService();
        }

        private IGitHostingProvider GetActiveGitHostingProvider()
        {
            var providerId = _appConfigSnapshot == null ? null : _appConfigSnapshot.GitHostingProviderId;
            return GetActiveGitHostingProviderService().ResolveProvider(providerId);
        }

        private IGitHostingProviderService GetActiveGitHostingProviderService()
        {
            if (IsRemoteUnixGitRuntime())
            {
                var remoteConfig = _appConfigSnapshot.GetSelectedRemoteUnixSshConfig() ?? new RemoteMacSshConfigSnapshot();
                return new GitHostingProviderService(new IGitHostingProvider[]
                {
                    new GitHubHostingProvider(GitHubCliService.CreateRemoteSsh(remoteConfig))
                });
            }

            if (IsWslGitRuntime())
            {
                return new GitHostingProviderService(new IGitHostingProvider[]
                {
                    new GitHubHostingProvider(GitHubCliService.CreateWsl(GetCurrentWslConfig()))
                });
            }

            return _gitHostingProviderService ?? new GitHostingProviderService();
        }

        private bool IsRemoteUnixGitRuntime()
        {
            return _appConfigSnapshot != null &&
                   CodexRuntimeSources.IsRemoteUnixSsh(_appConfigSnapshot.CodexRuntimeSource);
        }

        private bool IsWslGitRuntime()
        {
            return _appConfigSnapshot != null &&
                   string.Equals(_appConfigSnapshot.CodexRuntimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase);
        }

        private bool IsUnixGitRuntime()
        {
            return IsRemoteUnixGitRuntime() || IsWslGitRuntime();
        }

        private void OnGitWorkbenchMenuItemClick(object sender, EventArgs e)
        {
            ShowGitWorkbenchForCurrentContext();
        }

        private void OnGitCloneMenuItemClick(object sender, EventArgs e)
        {
            ShowGitCloneForm();
        }

        private void OnGitRemoteRepositoriesMenuItemClick(object sender, EventArgs e)
        {
            ShowGitRemoteRepositoriesForm();
        }

        private void OnGitMarketMenuItemClick(object sender, EventArgs e)
        {
            ShowGitHubStoreForm();
        }

        private void OnGitEnvironmentMenuItemClick(object sender, EventArgs e)
        {
            ShowGitEnvironmentForm();
        }

        private void OnGitRemoteCredentialsMenuItemClick(object sender, EventArgs e)
        {
            ShowGitRemoteCredentialsForm();
        }

        private void ShowGitCloneForm()
        {
            if (_isRuntimeWindowTransitionInProgress) return;
            if (_gitCloneForm != null && !_gitCloneForm.IsDisposed)
            {
                _gitCloneForm.Show();
                _gitCloneForm.Activate();
                return;
            }

            _gitCloneForm = new GitCloneForm(
                CreateActiveGitCloneService(),
                ResolveActiveGitHubCliService(),
                _screenReaderService,
                OpenClonedWorkspaceAsync,
                OpenClonedWorkspaceInGitWorkbenchAsync,
                GetDefaultCloneParentDirectory(),
                IsUnixGitRuntime(),
                IsUnixGitRuntime() ? BrowseUnixCloneTargetDirectoryAsync : (Func<string, Task<string>>)null);
            _gitCloneForm.FormClosed += (sender, e) =>
            {
                if (ReferenceEquals(_gitCloneForm, sender))
                {
                    _gitCloneForm = null;
                }
            };
            _gitCloneForm.Show();
        }

        private void ShowGitRemoteRepositoriesForm()
        {
            if (_isRuntimeWindowTransitionInProgress) return;
            if (_gitRemoteRepositoriesForm != null && !_gitRemoteRepositoriesForm.IsDisposed)
            {
                _gitRemoteRepositoriesForm.Show();
                _gitRemoteRepositoriesForm.Activate();
                return;
            }

            _gitRemoteRepositoriesForm = new GitRemoteRepositoriesForm(
                GetActiveGitHostingProviderService(),
                _screenReaderService,
                IsCurrentWorkspaceAvailable() ? _currentWorkspace.RootPath : null,
                IsCurrentWorkspaceAvailable() ? CreateGitRepositoryService(_currentWorkspace.RootPath) : null);
            _gitRemoteRepositoriesForm.FormClosed += (sender, e) =>
            {
                if (ReferenceEquals(_gitRemoteRepositoriesForm, sender))
                {
                    _gitRemoteRepositoriesForm = null;
                }
            };
            _gitRemoteRepositoriesForm.Show();
        }

        private void ShowGitHubStoreForm()
        {
            if (_isRuntimeWindowTransitionInProgress) return;
            if (_gitHubStoreForm != null && !_gitHubStoreForm.IsDisposed)
            {
                _gitHubStoreForm.Show();
                _gitHubStoreForm.Activate();
                return;
            }

            _gitHubStoreForm = new GitHubStoreForm(
                ResolveActiveGitHubCliService(),
                _screenReaderService,
                GetDefaultCloneParentDirectory(),
                IsCurrentWorkspaceAvailable() ? _currentWorkspace.RootPath : null,
                IsCurrentWorkspaceAvailable() ? CreateGitRepositoryService(_currentWorkspace.RootPath) : null);
            _gitHubStoreForm.FormClosed += (sender, e) =>
            {
                if (ReferenceEquals(_gitHubStoreForm, sender))
                {
                    _gitHubStoreForm = null;
                }
            };
            _gitHubStoreForm.Show();
        }

        private void ShowGitEnvironmentForm()
        {
            if (_isRuntimeWindowTransitionInProgress) return;
            if (_gitEnvironmentForm != null && !_gitEnvironmentForm.IsDisposed)
            {
                ActivateRuntimeWindow(_gitEnvironmentForm);
                return;
            }

            using (var form = new GitEnvironmentForm(CreateActiveGitEnvironmentService(), _screenReaderService))
            {
                _gitEnvironmentForm = form;
                try { form.ShowDialog(this); }
                finally { _gitEnvironmentForm = null; }
            }
        }

        private void ShowGitRemoteCredentialsForm()
        {
            if (_isRuntimeWindowTransitionInProgress) return;
            if (_gitRemoteCredentialsForm != null && !_gitRemoteCredentialsForm.IsDisposed)
            {
                ActivateRuntimeWindow(_gitRemoteCredentialsForm);
                return;
            }

            using (var form = new GitRemoteCredentialsForm(
                ResolveActiveGitHubCliService(), CreateActiveGitSshKeyService(), _screenReaderService))
            {
                _gitRemoteCredentialsForm = form;
                try { form.ShowDialog(this); }
                finally { _gitRemoteCredentialsForm = null; }
            }
        }

        private async Task OpenClonedWorkspaceAsync(string workspacePath)
        {
            await LoadWorkspaceAsync(workspacePath, true);
        }

        private async Task OpenClonedWorkspaceInGitWorkbenchAsync(string workspacePath)
        {
            await LoadWorkspaceAsync(workspacePath, true);
            ShowGitWorkbenchForCurrentContext();
        }

        private Task<string> BrowseUnixCloneTargetDirectoryAsync(string initialPath)
        {
            if (IsWslGitRuntime())
            {
                var choice = MessageBox.Show(
                    this,
                    UiLocalization.Text(
                        "GitCloneForm.Message.WslCloneTargetChoice",
                        "Choose where to clone.\r\n\r\nYes: WSL native directory, such as /home/user/projects.\r\nNo: Windows directory, mapped to /mnt/<drive>/... and still cloned by WSL git."),
                    UiLocalization.Text("GitCloneForm.DialogTitle.CloneTarget", "Clone target"),
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (choice == DialogResult.Cancel)
                {
                    return Task.FromResult<string>(null);
                }

                if (choice == DialogResult.No)
                {
                    return Task.FromResult(BrowseWindowsCloneTargetDirectoryForWsl(initialPath));
                }
            }

            return BrowseUnixRuntimeCloneTargetDirectoryAsync(initialPath);
        }

        private string BrowseWindowsCloneTargetDirectoryForWsl(string initialPath)
        {
            var mapper = new WindowsMountedDriveWslPathMapper();
            var title = UiLocalization.Text(
                "GitCloneForm.DialogDescription.SelectWindowsTargetParentForWsl",
                "Select the Windows directory that will contain the cloned repository. WSL git will access it through /mnt/<drive>/...");

            string windowsInitialPath;
            if (string.IsNullOrWhiteSpace(initialPath) ||
                !mapper.TryMapToClientPath(initialPath, out windowsInitialPath) ||
                !Directory.Exists(windowsInitialPath))
            {
                windowsInitialPath = null;
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (!string.IsNullOrWhiteSpace(documents) && Directory.Exists(documents))
                {
                    windowsInitialPath = documents;
                }
            }

            string selectedPath;
            if (!WindowsFolderPicker.TryPickFolder(this, title, windowsInitialPath, out selectedPath))
            {
                return null;
            }

            string wslPath;
            if (mapper.TryMapToServerPath(selectedPath, out wslPath))
            {
                return wslPath;
            }

            MessageBox.Show(
                this,
                UiLocalization.Text(
                    "GitCloneForm.Message.WindowsTargetCannotMapToWsl",
                    "The selected Windows directory cannot be mapped to WSL. Choose a drive path such as E:\\Projects."),
                UiLocalization.Text("GitCloneForm.DialogTitle.CloneTarget", "Clone target"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return null;
        }

        private Task<string> BrowseUnixRuntimeCloneTargetDirectoryAsync(string initialPath)
        {
            if (_isRuntimeWindowTransitionInProgress)
            {
                return Task.FromResult<string>(null);
            }

            var completion = new TaskCompletionSource<string>();
            var startPath = NormalizeUnixCloneBrowserStartPath(initialPath);
            var form = new RuntimeFileBrowserForm(
                BuildRuntimeFileWindowTitle(UiLocalization.Text("GitCloneForm.DialogDescription.SelectTargetParent", "Select the directory that will contain the cloned repository")),
                startPath,
                RuntimeFileBrowserSelectionMode.Directory,
                ListCurrentUnixRuntimeDirectoryAsync,
                false);
            _runtimeFileBrowserForms.Add(form);
            form.FormClosed += (sender, e) =>
            {
                _runtimeFileBrowserForms.Remove(form);
                if (!completion.Task.IsCompleted)
                {
                    completion.TrySetResult(null);
                }
            };
            form.PathSelected += (sender, e) =>
            {
                var path = GetFirstSelectedPath(e == null ? null : e.SelectedPaths);
                completion.TrySetResult(path);
            };
            form.Show(this);
            form.Activate();
            return completion.Task;
        }

        private string NormalizeUnixCloneBrowserStartPath(string initialPath)
        {
            if (string.IsNullOrWhiteSpace(initialPath))
            {
                return GetDefaultUnixWorkspacePath();
            }

            var path = initialPath.Trim();
            if (string.Equals(path, "~", StringComparison.Ordinal) ||
                path.StartsWith("~/", StringComparison.Ordinal))
            {
                return GetDefaultUnixWorkspacePath();
            }

            return WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path)
                ? path
                : GetDefaultUnixWorkspacePath();
        }

        private static string GetFirstSelectedPath(IReadOnlyList<RuntimeFileBrowserSelectedPath> selectedPaths)
        {
            if (selectedPaths == null || selectedPaths.Count == 0 || selectedPaths[0] == null)
            {
                return null;
            }

            return selectedPaths[0].Path;
        }

        private string GetDefaultCloneParentDirectory()
        {
            if (IsUnixGitRuntime())
            {
                return "~";
            }

            if (_currentWorkspace != null && !string.IsNullOrWhiteSpace(_currentWorkspace.RootPath))
            {
                try
                {
                    if (!WorkspacePathUtilities.LooksLikeUnixAbsolutePath(_currentWorkspace.RootPath) &&
                        Directory.Exists(_currentWorkspace.RootPath))
                    {
                        var parent = Directory.GetParent(_currentWorkspace.RootPath);
                        if (parent != null && Directory.Exists(parent.FullName))
                        {
                            return parent.FullName;
                        }
                    }
                }
                catch
                {
                }
            }

            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            return string.IsNullOrWhiteSpace(documents) ? Environment.CurrentDirectory : documents;
        }
    }
}
