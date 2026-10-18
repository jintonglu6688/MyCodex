using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed partial class AppServerFileBrowserForm
    {
        private Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>> _pastePathsProvider;
        private Func<bool> _acceptWindowsFileDropProvider;

        internal void SetPastePathsProvider(
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>> pastePathsProvider,
            bool acceptWindowsFileDrop)
        {
            SetPastePathsProvider(pastePathsProvider, () => acceptWindowsFileDrop);
        }

        internal void SetPastePathsProvider(
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>> pastePathsProvider,
            Func<bool> acceptWindowsFileDropProvider)
        {
            _pastePathsProvider = pastePathsProvider;
            _acceptWindowsFileDropProvider = acceptWindowsFileDropProvider;
        }

        internal Task PasteFilesForTestsAsync()
        {
            return PasteFilesAsync();
        }

        internal bool PasteMenuEnabledForTests
        {
            get
            {
                ItemContextMenu_Popup(this, EventArgs.Empty);
                return _pasteMenuItem.Enabled;
            }
        }

        internal static IReadOnlyList<RuntimeFileBrowserSelectedPath> GetWindowsFileDropPathsForTests(
            IDataObject clipboardData)
        {
            return GetWindowsFileDropPaths(clipboardData);
        }

        private bool HasPasteFiles()
        {
            var internalPaths = _pastePathsProvider == null ? null : _pastePathsProvider();
            if (internalPaths != null && internalPaths.Count > 0)
            {
                return true;
            }

            if (!CanAcceptWindowsFileDrop())
            {
                return false;
            }

            try
            {
                var data = Clipboard.GetDataObject();
                return data != null && data.GetDataPresent(DataFormats.FileDrop);
            }
            catch
            {
                return false;
            }
        }

        private IReadOnlyList<RuntimeFileBrowserSelectedPath> GetPasteFiles()
        {
            var internalPaths = _pastePathsProvider == null ? null : _pastePathsProvider();
            if (internalPaths != null && internalPaths.Count > 0)
            {
                return internalPaths;
            }

            if (!CanAcceptWindowsFileDrop())
            {
                return Array.Empty<RuntimeFileBrowserSelectedPath>();
            }

            try
            {
                return GetWindowsFileDropPaths(Clipboard.GetDataObject());
            }
            catch
            {
                return Array.Empty<RuntimeFileBrowserSelectedPath>();
            }
        }

        private bool CanAcceptWindowsFileDrop()
        {
            try
            {
                return _acceptWindowsFileDropProvider != null && _acceptWindowsFileDropProvider();
            }
            catch
            {
                return false;
            }
        }

        private static IReadOnlyList<RuntimeFileBrowserSelectedPath> GetWindowsFileDropPaths(
            IDataObject clipboardData)
        {
            try
            {
                var paths = clipboardData == null
                    ? null
                    : clipboardData.GetData(DataFormats.FileDrop) as string[];
                return (paths ?? new string[0])
                    .Where(path => LooksLikeWindowsBrowserPath(path) && (File.Exists(path) || Directory.Exists(path)))
                    .Select(path => new RuntimeFileBrowserSelectedPath(path.Trim(), Directory.Exists(path)))
                    .ToList();
            }
            catch
            {
                return Array.Empty<RuntimeFileBrowserSelectedPath>();
            }
        }

        private async Task PasteFilesAsync()
        {
            if (_isLoading || _isSearchMode || string.IsNullOrWhiteSpace(_currentPath))
            {
                return;
            }

            var sources = GetPasteFiles();
            if (sources.Count == 0)
            {
                SetOperationStatus(UiLocalization.Text(
                    "AppServerFileBrowserForm.Status.NothingToPaste",
                    "The clipboard does not contain files that can be pasted here."));
                return;
            }

            SetLoadingState(true);
            var succeeded = 0;
            var skipped = 0;
            var failed = 0;
            string firstCopiedName = null;
            try
            {
                var entries = await _client.ReadDirectoryAsync(_currentPath);
                var names = new HashSet<string>(
                    (entries ?? new List<AppServerFileSystemEntry>())
                        .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FileName))
                        .Select(entry => entry.FileName.Trim()),
                    LooksLikeWindowsBrowserPath(_currentPath)
                        ? StringComparer.OrdinalIgnoreCase
                        : StringComparer.Ordinal);

                foreach (var source in sources.Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path)))
                {
                    var sourcePath = source.Path.Trim();
                    var name = ExtractName(sourcePath);
                    var destinationPath = CombineChildPath(_currentPath, name);
                    if (string.IsNullOrWhiteSpace(name) ||
                        AreEquivalentBrowserPaths(sourcePath, destinationPath) ||
                        (source.IsDirectory && IsSameOrDescendantPath(destinationPath, sourcePath)) ||
                        names.Contains(name))
                    {
                        skipped++;
                        continue;
                    }

                    try
                    {
                        await _client.CopyAsync(sourcePath, destinationPath, source.IsDirectory);
                        names.Add(name);
                        firstCopiedName = firstCopiedName ?? name;
                        succeeded++;
                    }
                    catch
                    {
                        failed++;
                    }
                }

                if (succeeded > 0)
                {
                    await LoadEntriesAfterOperationAsync(firstCopiedName, null);
                }

                var notPasted = skipped + failed;
                if (notPasted > 0)
                {
                    SetOperationStatus(string.Format(
                        succeeded > 0
                            ? UiLocalization.Text(
                                "AppServerFileBrowserForm.Status.PastePartial.Template",
                                "Paste partially succeeded; {0} failed.")
                            : UiLocalization.Text(
                                "AppServerFileBrowserForm.Status.PasteFailed.Template",
                                "Paste failed; {0} item(s) were not pasted."),
                        notPasted));
                }
            }
            catch (Exception ex)
            {
                ShowOperationFailure(ex.Message);
            }
            finally
            {
                SetLoadingState(false);
            }
        }

        private static bool IsSameOrDescendantPath(string candidate, string parent)
        {
            if (AreEquivalentBrowserPaths(candidate, parent))
            {
                return true;
            }

            var windows = LooksLikeWindowsBrowserPath(candidate) && LooksLikeWindowsBrowserPath(parent);
            var normalizedCandidate = candidate.Trim().Replace(windows ? '/' : '\\', windows ? '\\' : '/');
            var normalizedParent = parent.Trim().Replace(windows ? '/' : '\\', windows ? '\\' : '/').TrimEnd(windows ? '\\' : '/');
            var comparison = windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return normalizedCandidate.StartsWith(
                normalizedParent + (windows ? "\\" : "/"),
                comparison);
        }
    }
}
