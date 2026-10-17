using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed partial class AppServerFileBrowserForm
    {
        private void StartMetadataLoad()
        {
            var generation = ++_metadataLoadGeneration;
            if (string.IsNullOrWhiteSpace(_currentPath) || _isSearchMode)
            {
                _metadataLoadTask = Task.CompletedTask;
                return;
            }

            var visiblePaths = _itemListView.Items
                .Cast<ListViewItem>()
                .Select(item => item.Tag as BrowserEntry)
                .Where(entry => entry != null)
                .Select(entry => entry.FullPath)
                .ToList();
            _metadataLoader.Prune(visiblePaths);
            _metadataLoadTask = _metadataLoader.LoadAsync(
                visiblePaths,
                batch => UpdateModifiedTimes(generation, batch));
        }

        private void UpdateModifiedTimes(int generation, IReadOnlyDictionary<string, string> values)
        {
            if (generation != _metadataLoadGeneration || IsDisposed || Disposing)
            {
                return;
            }

            foreach (ListViewItem item in _itemListView.Items)
            {
                var entry = item.Tag as BrowserEntry;
                string value;
                if (entry != null && values.TryGetValue(entry.FullPath, out value))
                {
                    item.SubItems[1].Text = value;
                }
            }
        }

        private void SchedulePendingFileRefresh()
        {
            if (!_pendingFileRefresh || _isLoading || _isSearchMode || _isModalDialogActive || IsDisposed || Disposing)
            {
                return;
            }

            _fileChangeTimer.Stop();
            _fileChangeTimer.Start();
        }

        private async Task FlushPendingFileChangesAsync()
        {
            _fileChangeTimer.Stop();
            if (!_pendingFileRefresh || _isLoading || _isSearchMode || _isModalDialogActive || IsDisposed || Disposing)
            {
                return;
            }

            var selectedNames = GetSelectedEntries().Select(entry => entry.Name).ToList();
            var focusedName = GetFocusedName();
            var focusedColumn = _activeColumnIndex;
            var refreshAllMetadata = _pendingFullMetadataRefresh;
            var changedPaths = _pendingChangedPaths.ToList();
            _pendingFileRefresh = false;
            _pendingFullMetadataRefresh = false;
            _pendingChangedPaths.Clear();
            if (refreshAllMetadata || changedPaths.Any(path => AreEquivalentBrowserPaths(path, _currentPath)))
            {
                _metadataLoader.Clear();
            }
            else
            {
                var visiblePaths = _itemListView.Items
                    .Cast<ListViewItem>()
                    .Select(item => item.Tag as BrowserEntry)
                    .Where(entry => entry != null)
                    .Select(entry => entry.FullPath)
                    .ToList();
                var invalidatedPaths = changedPaths
                    .SelectMany(path => visiblePaths.Where(visiblePath => AreEquivalentBrowserPaths(path, visiblePath)))
                    .Concat(changedPaths)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                _metadataLoader.Invalidate(invalidatedPaths);
            }

            await LoadEntriesAsync(_currentPath, focusedName, false, true, focusedColumn);
            if (IsDisposed || Disposing)
            {
                return;
            }

            RestoreSelection(selectedNames, focusedName, focusedColumn);
            var message = UiLocalization.Text("AppServerFileBrowserForm.Status.FilesUpdated", "File list updated.");
            _statusLabel.Text = message;
        }

        private void RestoreSelection(IReadOnlyList<string> selectedNames, string focusedName, int focusedColumn)
        {
            _itemListView.SelectedItems.Clear();
            ListViewItem focusedItem = null;
            foreach (ListViewItem item in _itemListView.Items)
            {
                var entry = item.Tag as BrowserEntry;
                item.Selected = entry != null && selectedNames != null && selectedNames.Any(
                    name => string.Equals(name, entry.Name, StringComparison.OrdinalIgnoreCase));
                if (entry != null && string.Equals(
                    focusedName,
                    entry.Name,
                    StringComparison.OrdinalIgnoreCase))
                {
                    focusedItem = item;
                }
            }

            _activeColumnIndex = Math.Max(0, Math.Min(focusedColumn, _itemListView.Columns.Count - 1));
            if (focusedItem != null)
            {
                focusedItem.Focused = true;
                focusedItem.EnsureVisible();
                _lastFocusedEntry = focusedItem.Tag as BrowserEntry;
            }

            UpdateSelectionText();
        }
    }
}
