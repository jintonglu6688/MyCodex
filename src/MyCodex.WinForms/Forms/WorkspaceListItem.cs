using System;
using System.Windows.Forms;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class WorkspaceListItem
    {
        private readonly bool _isAvailable;
        private readonly AppServerHostPlatform _hostPlatform;
        private readonly string _remoteProfileDisplayName;
        private readonly bool _isCurrent;
        private int _workingSessionCount;

        public WorkspaceListItem(WorkspaceInfo workspace, bool isAvailable, AppServerHostPlatform hostPlatform, string remoteProfileDisplayName, bool isCurrent, int workingSessionCount)
        {
            Workspace = workspace;
            _isAvailable = isAvailable;
            _hostPlatform = hostPlatform;
            _remoteProfileDisplayName = remoteProfileDisplayName;
            _isCurrent = isCurrent;
            _workingSessionCount = Math.Max(0, workingSessionCount);
        }

        public WorkspaceInfo Workspace { get; }

        public ListViewItem CreateListViewItem(bool showSource)
        {
            var values = showSource
                ? new[] { GetDisplayName(), GetSourceText(), GetBranchText() }
                : new[] { GetDisplayName(), GetBranchText() };
            var item = new ListViewItem(values)
            {
                Tag = this,
                ToolTipText = GetPathText()
            };
            return item;
        }

        public void UpdateWorkingSessionCount(ListViewItem item, int workingSessionCount)
        {
            var normalizedCount = Math.Max(0, workingSessionCount);
            if (item == null || _workingSessionCount == normalizedCount)
            {
                return;
            }

            _workingSessionCount = normalizedCount;
            item.Text = GetDisplayName();
        }

        public override string ToString()
        {
            return string.Format("{0}, {1}, {2}", GetDisplayName(), GetSourceText(), GetPathText());
        }

        private string GetDisplayName()
        {
            var name = Workspace != null && !string.IsNullOrWhiteSpace(Workspace.Name)
                ? Workspace.Name
                : UiLocalization.Text("MainForm.WorkspaceList.Value.UnknownName", "(Unknown)");
            var displayName = _isAvailable
                ? name
                : name + UiLocalization.Text("MainForm.WorkspaceList.InvalidPathSuffix", " (Path unavailable)");
            return CurrentListItemMarker.Append(
                SessionDisplayFormatter.PrependWorkingStatus(displayName, _workingSessionCount),
                _isCurrent);
        }

        private string GetSourceText()
        {
            if (Workspace == null || string.IsNullOrWhiteSpace(Workspace.RootPath))
            {
                return string.Empty;
            }

            return WorkspacePathUtilities.LooksLikeUnixAbsolutePath(Workspace.RootPath) &&
                   !WorkspacePathUtilities.LooksLikeWslMountedWindowsDrivePath(Workspace.RootPath)
                ? (_hostPlatform == AppServerHostPlatform.RemoteMac
                    ? (string.IsNullOrWhiteSpace(_remoteProfileDisplayName)
                        ? UiLocalization.Text("MainForm.WorkspaceList.Source.RemoteUnix", "SSH")
                        : _remoteProfileDisplayName)
                    : UiLocalization.Text("MainForm.WorkspaceList.Source.Wsl", "WSL"))
                : UiLocalization.Text("MainForm.WorkspaceList.Source.Host", "Host");
        }

        private string GetBranchText()
        {
            return Workspace != null && !string.IsNullOrWhiteSpace(Workspace.GitBranch)
                ? Workspace.GitBranch
                : UiLocalization.Text("MainForm.WorkspaceList.Value.UnknownBranch", "(Unknown)");
        }

        private string GetPathText()
        {
            return Workspace != null && !string.IsNullOrWhiteSpace(Workspace.RootPath)
                ? Workspace.RootPath
                : string.Empty;
        }
    }
}
