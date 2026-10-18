using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AppServerFileWindowRegistry<T> where T : class
    {
        private readonly Dictionary<AppServerFileWindowKey, T> _items =
            new Dictionary<AppServerFileWindowKey, T>();

        public bool TryAdd(AppServerFileWindowKey key, T item)
        {
            if (key == null || item == null || _items.ContainsKey(key))
            {
                return false;
            }

            _items[key] = item;
            return true;
        }

        public T TryGet(AppServerFileWindowKey key)
        {
            T item;
            return key != null && _items.TryGetValue(key, out item) ? item : null;
        }

        public void Remove(AppServerFileWindowKey key)
        {
            if (key != null)
            {
                _items.Remove(key);
            }
        }

        public void UpdateKey(AppServerFileWindowKey oldKey, AppServerFileWindowKey newKey, T item)
        {
            TryUpdateKey(oldKey, newKey, item);
        }

        public bool TryUpdateKey(AppServerFileWindowKey oldKey, AppServerFileWindowKey newKey, T item)
        {
            if (oldKey == null || newKey == null || item == null)
            {
                return false;
            }

            if (_items.ContainsKey(newKey) && !newKey.Equals(oldKey))
            {
                return false;
            }

            Remove(oldKey);
            _items[newKey] = item;
            return true;
        }

        public IReadOnlyList<KeyValuePair<AppServerFileWindowKey, T>> Snapshot()
        {
            return new List<KeyValuePair<AppServerFileWindowKey, T>>(_items);
        }
    }

    internal sealed class AppServerFileWindowManager
    {
        private readonly IAppServerFileSystemClient _client;
        private readonly Func<AppServerFileWindowRuntimeScope> _scopeProvider;
        private readonly Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> _attachPathsAsync;
        private readonly Action<IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> _copiedPaths;
        private readonly Action<AppServerFileWindowRuntimeScope, IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> _scopedCopiedPaths;
        private readonly Func<AppServerFileWindowRuntimeScope, IReadOnlyList<RuntimeFileBrowserSelectedPath>> _pastePathsProvider;
        private readonly Action<string> _announce;
        private readonly Func<TextFileOpenPreference> _textFileOpenPreferenceProvider;
        private readonly AppServerFileWatchCoordinator _watchCoordinator;
        private readonly AppServerFileWindowRegistry<AppServerFileBrowserForm> _browsers =
            new AppServerFileWindowRegistry<AppServerFileBrowserForm>();
        private readonly AppServerFileWindowRegistry<AppServerFilePreviewForm> _previews =
            new AppServerFileWindowRegistry<AppServerFilePreviewForm>();
        private readonly Dictionary<AppServerFileWindowKey, Form> _previewReturnFocusTargets =
            new Dictionary<AppServerFileWindowKey, Form>();

        public AppServerFileWindowManager(
            Form owner,
            IAppServerFileSystemClient client,
            Func<AppServerFileWindowRuntimeScope> scopeProvider,
            Func<IReadOnlyList<RuntimeFileBrowserSelectedPath>, Task> attachPathsAsync,
            Action<IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> copiedPaths,
            Action<string> announce,
            Func<TextFileOpenPreference> textFileOpenPreferenceProvider = null,
            Action<AppServerFileWindowRuntimeScope, IReadOnlyList<RuntimeFileBrowserSelectedPath>, string> scopedCopiedPaths = null,
            Func<AppServerFileWindowRuntimeScope, IReadOnlyList<RuntimeFileBrowserSelectedPath>> pastePathsProvider = null)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            _client = client ?? throw new ArgumentNullException(nameof(client));
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
            _attachPathsAsync = attachPathsAsync;
            _copiedPaths = copiedPaths;
            _scopedCopiedPaths = scopedCopiedPaths;
            _pastePathsProvider = pastePathsProvider;
            _announce = announce;
            _textFileOpenPreferenceProvider = textFileOpenPreferenceProvider ?? (() => new TextFileOpenPreference { Mode = TextFileOpenMode.Internal });
            _watchCoordinator = new AppServerFileWatchCoordinator(client);
        }

        public void OpenBrowser(string initialPath)
        {
            OpenBrowser(initialPath, null);
        }

        public void OpenBrowser(string initialPath, AppServerFileWindowRuntimeScope fixedScope)
        {
            var scope = fixedScope ?? _scopeProvider() ?? new AppServerFileWindowRuntimeScope();
            var normalizedInitialPath = string.IsNullOrWhiteSpace(initialPath) ? string.Empty : initialPath.Trim();
            var key = AppServerFileWindowKey.CreateBrowser(
                scope.RuntimeSource,
                scope.RuntimeProfileId,
                scope.RemoteProfileId,
                scope.InteractionMode,
                normalizedInitialPath);
            var existing = _browsers.TryGet(key);
            if (Activate(existing))
            {
                return;
            }

            var form = new AppServerFileBrowserForm(
                _client,
                normalizedInitialPath,
                _attachPathsAsync,
                (paths, marker) =>
                {
                    _copiedPaths?.Invoke(paths, marker);
                    _scopedCopiedPaths?.Invoke(scope, paths, marker);
                },
                _announce);
            form.SetPastePathsProvider(
                () => !RuntimeScopesMatch(scope, _scopeProvider()) || _pastePathsProvider == null
                    ? Array.Empty<RuntimeFileBrowserSelectedPath>()
                    : _pastePathsProvider(scope),
                () => RuntimeScopesMatch(scope, _scopeProvider()) && IsWindowsRuntimeScope(scope));
            form.SetPreviewRequestHandlerForTests(path =>
            {
                OpenFileFromBrowser(path, form);
                return Task.CompletedTask;
            });
            var currentKey = key;
            _browsers.TryAdd(currentKey, form);
            form.CurrentPathChanged += async (sender, e) =>
            {
                var newKey = AppServerFileWindowKey.CreateBrowser(
                    scope.RuntimeSource,
                    scope.RuntimeProfileId,
                    scope.RemoteProfileId,
                    scope.InteractionMode,
                    e.CurrentPath);
                if (currentKey.Equals(newKey))
                {
                    return;
                }

                if (!_browsers.TryUpdateKey(currentKey, newKey, form))
                {
                    var existing = _browsers.TryGet(newKey);
                    if (Activate(existing))
                    {
                        form.Close();
                        return;
                    }
                }

                currentKey = newKey;
                await _watchCoordinator.ReplaceWatchAsync(form).ConfigureAwait(true);
            };
            form.FormClosed += (sender, e) =>
            {
                _browsers.Remove(currentKey);
                _ = _watchCoordinator.UnwatchTargetAsync(form);
            };
            form.Shown += async (sender, e) => await _watchCoordinator.WatchAsync(form).ConfigureAwait(true);
            form.Show();
            form.Activate();
        }

        private static bool IsWindowsRuntimeScope(AppServerFileWindowRuntimeScope scope)
        {
            var source = scope == null ? null : scope.RuntimeSource;
            return (scope != null && scope.InteractionMode == AppInteractionMode.Office) ||
                   (!string.Equals(source, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) &&
                    !CodexRuntimeSources.IsRemoteUnixSsh(source));
        }

        internal static bool RuntimeScopesMatch(
            AppServerFileWindowRuntimeScope expected,
            AppServerFileWindowRuntimeScope current)
        {
            return expected != null && current != null &&
                   AppServerFileWindowKey.CreateBrowser(
                       expected.RuntimeSource,
                       expected.RuntimeProfileId,
                       expected.RemoteProfileId,
                       expected.InteractionMode,
                       string.Empty)
                   .MatchesScope(current);
        }

        public void OpenPreview(string path)
        {
            OpenPreview(path, null);
        }

        private void OpenFileFromBrowser(string path, Form returnFocusTarget)
        {
            var scope = _scopeProvider() ?? new AppServerFileWindowRuntimeScope();
            string localWindowsPath;
            var hasLocalWindowsPath = AppServerFileWindowLocalPathPolicy.TryGetLocalWindowsFilePath(
                scope,
                path,
                out localWindowsPath);

            if (!TextEditableFilePolicy.ShouldOpenWithTextEditor(path))
            {
                if (!hasLocalWindowsPath)
                {
                    MessageBox.Show(
                        returnFocusTarget,
                        UiLocalization.Text(
                            "AppServerFileWindowManager.Error.SystemOpenRequiresLocalPath",
                            "This file type can only be opened with the system default app when the path is accessible from Windows."),
                        AppConstants.AppName,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                OpenWithSystemDefault(localWindowsPath, returnFocusTarget);
                return;
            }

            var preference = _textFileOpenPreferenceProvider();
            var mode = TextFileOpenMode.Normalize(preference == null ? null : preference.Mode);
            var requestPath = TextFileOpenMode.IsExternal(mode) && hasLocalWindowsPath
                ? localWindowsPath
                : path;
            var service = new TextFileOpenService(
                null,
                request => OpenPreview(request.FilePath, returnFocusTarget));
            var result = service.Open(
                preference,
                new TextFileOpenRequest
                {
                    FilePath = requestPath,
                    IsLocalWindowsFile = hasLocalWindowsPath
                });

            if (!result.Handled && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                MessageBox.Show(returnFocusTarget, result.ErrorMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void OpenWithSystemDefault(string path, Form returnFocusTarget)
        {
            var service = new TextFileOpenService(
                null,
                request => { });
            var result = service.Open(
                new TextFileOpenPreference { Mode = TextFileOpenMode.System },
                new TextFileOpenRequest
                {
                    FilePath = path,
                    IsLocalWindowsFile = true
                });

            if (!result.Handled && !string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                MessageBox.Show(returnFocusTarget, result.ErrorMessage, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenPreview(string path, Form returnFocusTarget)
        {
            var scope = _scopeProvider() ?? new AppServerFileWindowRuntimeScope();
            var normalizedPath = string.IsNullOrWhiteSpace(path) ? string.Empty : path.Trim();
            var key = AppServerFileWindowKey.CreatePreview(
                scope.RuntimeSource,
                scope.RuntimeProfileId,
                scope.RemoteProfileId,
                scope.InteractionMode,
                normalizedPath);
            RememberPreviewReturnFocusTarget(key, returnFocusTarget);
            var existing = _previews.TryGet(key);
            if (Activate(existing))
            {
                return;
            }

            var form = new AppServerFilePreviewForm(
                normalizedPath,
                async () =>
                {
                    var result = await _client.ReadFileAsync(normalizedPath).ConfigureAwait(true);
                    return DecodeText(result == null ? null : result.DataBase64);
                },
                data => _client.WriteFileAsync(normalizedPath, data));
            _previews.TryAdd(key, form);
            form.FormClosed += (sender, e) =>
            {
                var target = GetPreviewReturnFocusTarget(key);
                _previews.Remove(key);
                _previewReturnFocusTargets.Remove(key);
                _ = _watchCoordinator.UnwatchTargetAsync(form);
                ReactivatePreviewReturnFocusTarget(target);
            };
            form.Shown += async (sender, e) =>
            {
                await form.LoadAsync().ConfigureAwait(true);
                if (form.IsDisposed || form.Disposing)
                {
                    return;
                }

                await _watchCoordinator.WatchAsync(form).ConfigureAwait(true);
            };
            form.Show();
            form.Activate();
        }

        public Task HandleFileSystemChangedAsync(AppServerFileSystemChangedEvent change)
        {
            return _watchCoordinator.HandleFileSystemChangedAsync(change);
        }

        public Task ClearWatchesAsync()
        {
            return _watchCoordinator.ClearAsync();
        }

        internal IReadOnlyList<Form> GetOpenFormsSnapshot()
        {
            return _browsers.Snapshot()
                .Select(pair => pair.Value)
                .Cast<Form>()
                .Concat(_previews.Snapshot().Select(pair => pair.Value).Cast<Form>())
                .Where(form => form != null && !form.IsDisposed && !form.Disposing)
                .Distinct()
                .ToList();
        }

        internal Form GetBusyFormForRuntimeTransition()
        {
            var browser = _browsers.Snapshot()
                .Select(pair => pair.Value)
                .FirstOrDefault(form => form != null && !form.IsDisposed && !form.Disposing && form.IsBusyForRuntimeTransition);
            if (browser != null)
            {
                return browser;
            }

            return _previews.Snapshot()
                .Select(pair => pair.Value)
                .FirstOrDefault(form => form != null && !form.IsDisposed && !form.Disposing && form.IsBusyForRuntimeTransition);
        }

        internal bool TryCloseAllForRuntimeTransition()
        {
            var forms = GetOpenFormsSnapshot();
            foreach (var form in forms)
            {
                form.Close();
            }

            return GetOpenFormsSnapshot().Count == 0;
        }

        public async Task RestoreWatchesAsync()
        {
            var scope = _scopeProvider() ?? new AppServerFileWindowRuntimeScope();
            var targets = _browsers.Snapshot()
                .Where(pair => pair.Key.MatchesScope(scope))
                .Select(pair => pair.Value as IAppServerFileWatchTarget)
                .Concat(_previews.Snapshot()
                    .Where(pair => pair.Key.MatchesScope(scope))
                    .Select(pair => pair.Value as IAppServerFileWatchTarget))
                .Where(target => target != null)
                .ToList();
            foreach (var target in targets)
            {
                var form = target as Form;
                if (form != null && (form.IsDisposed || form.Disposing))
                {
                    continue;
                }

                await _watchCoordinator.ReplaceWatchAsync(target).ConfigureAwait(true);
            }
        }

        private static bool Activate(Form form)
        {
            if (form == null || form.IsDisposed || form.Disposing)
            {
                return false;
            }

            if (form.WindowState == FormWindowState.Minimized)
            {
                form.WindowState = FormWindowState.Normal;
            }

            form.Show();
            form.Activate();
            return true;
        }

        private void RememberPreviewReturnFocusTarget(AppServerFileWindowKey key, Form returnFocusTarget)
        {
            if (key == null || returnFocusTarget == null || returnFocusTarget.IsDisposed || returnFocusTarget.Disposing)
            {
                return;
            }

            _previewReturnFocusTargets[key] = returnFocusTarget;
        }

        private Form GetPreviewReturnFocusTarget(AppServerFileWindowKey key)
        {
            Form target;
            return key != null && _previewReturnFocusTargets.TryGetValue(key, out target) ? target : null;
        }

        private static void ReactivatePreviewReturnFocusTarget(Form returnFocusTarget)
        {
            if (returnFocusTarget == null || returnFocusTarget.IsDisposed || returnFocusTarget.Disposing)
            {
                return;
            }

            if (returnFocusTarget.IsHandleCreated)
            {
                try
                {
                    returnFocusTarget.BeginInvoke(new Action(() => Activate(returnFocusTarget)));
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                }
            }

            Activate(returnFocusTarget);
        }

        private static string DecodeText(string dataBase64)
        {
            if (string.IsNullOrWhiteSpace(dataBase64))
            {
                return string.Empty;
            }

            var bytes = Convert.FromBase64String(dataBase64);
            return Encoding.UTF8.GetString(bytes);
        }
    }

    internal sealed class AppServerFileWindowRuntimeScope
    {
        public string RuntimeSource { get; set; }

        public string RuntimeProfileId { get; set; }

        public string RemoteProfileId { get; set; }

        public AppInteractionMode InteractionMode { get; set; }
    }

    internal sealed class AppServerFileBrowserClipboardState
    {
        private readonly AppServerFileWindowKey _scopeKey;

        public AppServerFileBrowserClipboardState(
            AppServerFileWindowRuntimeScope scope,
            IReadOnlyList<RuntimeFileBrowserSelectedPath> paths,
            string marker)
        {
            scope = scope ?? new AppServerFileWindowRuntimeScope();
            _scopeKey = AppServerFileWindowKey.CreateBrowser(
                scope.RuntimeSource,
                scope.RuntimeProfileId,
                scope.RemoteProfileId,
                scope.InteractionMode,
                string.Empty);
            Marker = marker ?? string.Empty;
            Paths = (paths ?? Array.Empty<RuntimeFileBrowserSelectedPath>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
                .Select(item => new RuntimeFileBrowserSelectedPath(item.Path.Trim(), item.IsDirectory))
                .ToList();
        }

        public string Marker { get; private set; }

        public IReadOnlyList<RuntimeFileBrowserSelectedPath> Paths { get; private set; }

        public bool Matches(AppServerFileWindowRuntimeScope scope, IDataObject clipboardData)
        {
            if (!_scopeKey.MatchesScope(scope) || clipboardData == null ||
                !clipboardData.GetDataPresent(AppServerFileBrowserForm.ClipboardFormat))
            {
                return false;
            }

            return string.Equals(
                Convert.ToString(clipboardData.GetData(AppServerFileBrowserForm.ClipboardFormat)),
                Marker,
                StringComparison.Ordinal);
        }
    }
}
