using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private AppServerFileWindowManager CreateAppServerFileWindowManager()
        {
            return new AppServerFileWindowManager(
                this,
                _appServerClient,
                BuildCurrentAppServerFileWindowRuntimeScope,
                AddAppServerFileBrowserAttachmentsAsync,
                null,
                text => SpeakAuxiliaryTextWithScreenReader(text),
                textFileOpenPreferenceProvider: BuildTextFileOpenPreference,
                scopedCopiedPaths: RememberAppServerFileBrowserCopiedPaths,
                pastePathsProvider: GetAppServerFileBrowserClipboardPaths);
        }

        private AppServerFileWindowRuntimeScope BuildCurrentAppServerFileWindowRuntimeScope()
        {
            return AppServerFileWindowRuntimeScopeComposer.FromSnapshot(
                _appConfigSnapshot,
                GetCurrentRuntimeProfileIdForDiagnostics(),
                _currentInteractionMode);
        }

        private IReadOnlyList<RuntimeFileBrowserSelectedPath> GetAppServerFileBrowserClipboardPaths(
            AppServerFileWindowRuntimeScope scope)
        {
            IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths;
            return TryGetAppServerFileBrowserClipboardPaths(scope, out copiedPaths)
                ? copiedPaths
                : Array.Empty<RuntimeFileBrowserSelectedPath>();
        }

        private bool TryGetAppServerFileBrowserClipboardPaths(
            AppServerFileWindowRuntimeScope scope,
            out IReadOnlyList<RuntimeFileBrowserSelectedPath> copiedPaths)
        {
            copiedPaths = Array.Empty<RuntimeFileBrowserSelectedPath>();
            if (_appServerFileBrowserClipboardState == null)
            {
                return false;
            }

            try
            {
                var clipboardData = Clipboard.GetDataObject();
                if (!_appServerFileBrowserClipboardState.Matches(scope, clipboardData))
                {
                    return false;
                }
            }
            catch
            {
                return false;
            }

            copiedPaths = _appServerFileBrowserClipboardState.Paths;
            return copiedPaths != null && copiedPaths.Count > 0;
        }

        private void RememberAppServerFileBrowserCopiedPaths(
            AppServerFileWindowRuntimeScope scope,
            IReadOnlyList<RuntimeFileBrowserSelectedPath> selectedPaths,
            string clipboardMarker)
        {
            if (string.IsNullOrWhiteSpace(clipboardMarker))
            {
                _appServerFileBrowserClipboardState = null;
                return;
            }

            var paths = (selectedPaths ?? Array.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RuntimeFileBrowserSelectedPath(item.Path.Trim(), item.IsDirectory))
                .ToList();
            if (paths.Count == 0)
            {
                _appServerFileBrowserClipboardState = null;
                return;
            }

            _appServerFileBrowserClipboardState = new AppServerFileBrowserClipboardState(
                scope,
                paths,
                clipboardMarker);
        }

        private void ForwardAppServerFileWindowEvent(AppServerEventArgs e)
        {
            if (e != null &&
                string.Equals(e.EventType, "file_system_changed", System.StringComparison.OrdinalIgnoreCase) &&
                e.FileSystemChanged != null)
            {
                _ = _appServerFileWindowManager.HandleFileSystemChangedAsync(e.FileSystemChanged);
            }
        }

        private void ClearAppServerFileWindowWatches()
        {
            _ = _appServerFileWindowManager.ClearWatchesAsync();
        }

        private Task RestoreAppServerFileWindowWatchesAsync()
        {
            return _appServerFileWindowManager.RestoreWatchesAsync();
        }
    }
}
