using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Codex
{
    internal sealed class SharedRuntimeMutationReservation : IDisposable
    {
        private readonly string _root;
        private readonly FileStream _reservation;
        private readonly Func<IEnumerable<string>> _runningExecutablePaths;

        internal SharedRuntimeMutationReservation(string root, Func<IEnumerable<string>> runningExecutablePaths = null)
        {
            _root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            RejectLinkedAncestors(_root);
            Directory.CreateDirectory(_root);
            var lockPath = Path.Combine(_root, ".mutation.lock");
            RejectLinkedAncestors(lockPath);
            try { _reservation = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex)
            {
                throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.MutationBusy",
                    "Another window is updating this shared Codex runtime. Wait for it to finish and retry."), ex);
            }
            _runningExecutablePaths = runningExecutablePaths ?? ReadRunningExecutablePaths;
        }

        internal bool PublishPackage(string packageRoot, string destination, string targetTriple, bool forceReinstall, CancellationToken cancellationToken)
        {
            EnsureChild(destination);
            EnsureSafeTree(packageRoot);
            EnsureSafeTree(destination);
            if (Directory.Exists(destination) && !forceReinstall &&
                File.Exists(CodexRuntimeUpdateService.ResolvePackageCodexExecutablePath(destination, targetTriple))) return false;

            var staging = Path.Combine(_root, ".staging-" + Guid.NewGuid().ToString("N"));
            var backup = Path.Combine(_root, ".replaced-" + Guid.NewGuid().ToString("N"));
            var movedOriginal = false;
            try
            {
                CopyPackage(packageRoot, staging, cancellationToken);
                if (!File.Exists(CodexRuntimeUpdateService.ResolvePackageCodexExecutablePath(staging, targetTriple)))
                    throw new InvalidDataException(SharedLocalization.Text("CodexRuntimeUpdate.Error.IntegrityCheckFailed",
                        "Codex runtime package integrity check failed."));
                cancellationToken.ThrowIfCancellationRequested();
                using (var executableLocks = ReserveUnusedExecutables(destination, false))
                {
                    EnsureChild(destination);
                    EnsureSafeTree(destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    if (Directory.Exists(destination))
                    {
                        executableLocks.MoveImagesAside();
                        Directory.Move(destination, backup);
                        executableLocks.SetRestoreDirectory(backup);
                        movedOriginal = true;
                    }
                    try { Directory.Move(staging, destination); }
                    catch (Exception publishError)
                    {
                        if (movedOriginal)
                        {
                            try
                            {
                                Directory.Move(backup, destination);
                                executableLocks.SetRestoreDirectory(destination);
                                movedOriginal = false;
                            }
                            catch (Exception restoreError) { throw new AggregateException(publishError, restoreError); }
                        }
                        throw;
                    }
                    executableLocks.RetireImages();
                }
                TryDeleteTemporaryDirectory(backup);
                return true;
            }
            finally { TryDeleteTemporaryDirectory(staging); }
        }

        internal bool RemoveUnusedVersion(string directory)
        {
            EnsureChild(directory);
            if (!Directory.Exists(directory)) return false;
            using (var executableLocks = ReserveUnusedExecutables(directory, true))
            {
                if (executableLocks == null) return false;
                var removed = Path.Combine(_root, ".removed-" + Guid.NewGuid().ToString("N"));
                executableLocks.MoveImagesAside();
                Directory.Move(directory, removed);
                executableLocks.SetRestoreDirectory(removed);
                executableLocks.RetireImages();
                TryDeleteTemporaryDirectory(removed);
            }
            return true;
        }

        private ExecutableLocks ReserveUnusedExecutables(string directory, bool skipInUse)
        {
            EnsureChild(directory);
            EnsureSafeTree(directory);
            if (!Directory.Exists(directory)) return new ExecutableLocks(this, directory);
            if (IsInUse(directory))
            {
                if (skipInUse) return null;
                throw RuntimeInUse();
            }
            var locks = new ExecutableLocks(this, directory);
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Where(p => string.Equals(Path.GetExtension(p), ".exe", StringComparison.OrdinalIgnoreCase)))
                {
                    // A writable handle denies new image loading. The files are moved aside while held
                    // because Windows cannot rename a directory containing open executable handles.
                    locks.Add(file, new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete));
                }
                if (IsInUse(directory))
                {
                    locks.Dispose();
                    if (skipInUse) return null;
                    throw RuntimeInUse();
                }
                return locks;
            }
            catch (IOException)
            {
                locks.Dispose();
                if (skipInUse) return null;
                throw RuntimeInUse();
            }
            catch { locks.Dispose(); throw; }
        }

        private bool IsInUse(string directory)
        {
            var prefix = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            IEnumerable<string> paths;
            try { paths = _runningExecutablePaths()?.ToArray(); }
            catch (Exception ex) { throw RuntimeUseUnknown(ex); }
            if (paths == null) throw RuntimeUseUnknown(null);
            foreach (var path in paths)
            {
                if (string.IsNullOrWhiteSpace(path)) throw RuntimeUseUnknown(null);
                string fullPath;
                try { fullPath = Path.GetFullPath(path); }
                catch (Exception ex) { throw RuntimeUseUnknown(ex); }
                if (fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private IEnumerable<string> ReadRunningExecutablePaths()
        {
            EnsureSafeTree(_root);
            var executableNames = new HashSet<string>(Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
                .Where(p => string.Equals(Path.GetExtension(p), ".exe", StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
            var paths = new List<string>();
            foreach (var process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (!executableNames.Contains(process.ProcessName) || process.HasExited) continue;
                        var path = process.MainModule.FileName;
                        if (!string.Equals(Path.GetFileNameWithoutExtension(path), process.ProcessName, StringComparison.OrdinalIgnoreCase))
                            throw RuntimeUseUnknown(null);
                        paths.Add(path);
                    }
                    catch (Exception ex)
                    {
                        try { if (process.HasExited) continue; }
                        catch { }
                        throw RuntimeUseUnknown(ex);
                    }
                }
            }
            return paths;
        }

        private void EnsureChild(string path)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.InstallPathEscaped",
                    "Codex runtime install path escapes the bundled runtime directory."));
            RejectLinkedAncestors(fullPath);
        }

        private static void RejectLinkedAncestors(string path)
        {
            for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.UnsafeRuntimeLink",
                        "Codex runtime paths cannot contain symbolic links or junctions."));
        }

        private static void EnsureSafeTree(string path)
        {
            RejectLinkedAncestors(path);
            if (!Directory.Exists(path)) return;
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                RejectLinkedAncestors(entry);
                if (Directory.Exists(entry)) EnsureSafeTree(entry);
            }
        }

        private void CopyPackage(string source, string destination, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureChild(destination);
            Directory.CreateDirectory(destination);
            foreach (var entry in Directory.EnumerateFileSystemEntries(source))
            {
                cancellationToken.ThrowIfCancellationRequested();
                RejectLinkedAncestors(entry);
                var next = Path.Combine(destination, Path.GetFileName(entry));
                if (Directory.Exists(entry)) CopyPackage(entry, next, cancellationToken);
                else File.Copy(entry, next, false);
            }
        }

        private void TryDeleteTemporaryDirectory(string path)
        {
            try
            {
                EnsureChild(path);
                EnsureSafeTree(path);
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch { /* A hidden leftover must not replace the completed install or its original error. */ }
        }

        private static InvalidOperationException RuntimeInUse()
        {
            return new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.RuntimeInUse",
                "This Codex runtime is in use. Exit its users normally before replacing this version."));
        }

        private static InvalidOperationException RuntimeUseUnknown(Exception inner)
        {
            return new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.RuntimeUseUnknown",
                "Cannot verify which processes are using the Codex runtime. This version will not be changed."), inner);
        }

        private sealed class ExecutableLocks : IDisposable
        {
            private readonly SharedRuntimeMutationReservation _owner;
            private readonly string _directory;
            private readonly List<FileStream> _files = new List<FileStream>();
            private readonly List<string> _paths = new List<string>();
            private readonly Dictionary<string, string> _moved = new Dictionary<string, string>();
            private string _restoreDirectory;
            private string _quarantine;
            private bool _retired;

            internal ExecutableLocks(SharedRuntimeMutationReservation owner, string directory)
            {
                _owner = owner;
                _directory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                _restoreDirectory = _directory;
            }

            internal void Add(string path, FileStream file) { _paths.Add(Path.GetFullPath(path)); _files.Add(file); }

            internal void MoveImagesAside()
            {
                if (_paths.Count == 0) return;
                _quarantine = Path.Combine(_owner._root, ".execution-locks-" + Guid.NewGuid().ToString("N"));
                _owner.EnsureChild(_quarantine);
                Directory.CreateDirectory(_quarantine);
                foreach (var path in _paths)
                {
                    var reserved = Path.Combine(_quarantine, Guid.NewGuid().ToString("N") + ".reserved");
                    File.Move(path, reserved);
                    _moved.Add(path.Substring(_directory.Length + 1), reserved);
                }
            }

            internal void SetRestoreDirectory(string directory) { _restoreDirectory = directory; }
            internal void RetireImages() { _retired = true; }

            public void Dispose()
            {
                var errors = new List<Exception>();
                try
                {
                    if (!_retired)
                        foreach (var file in _moved)
                        {
                            try
                            {
                                var original = Path.Combine(_restoreDirectory, file.Key);
                                _owner.EnsureChild(original);
                                File.Move(file.Value, original);
                            }
                            catch (Exception ex) { errors.Add(ex); }
                        }
                }
                finally { foreach (var file in _files) file.Dispose(); }
                if (errors.Count != 0) throw new AggregateException("Original runtime executables could not be fully restored; their reserved files were retained.", errors);
                if (_quarantine != null) _owner.TryDeleteTemporaryDirectory(_quarantine);
            }
        }

        public void Dispose() { _reservation.Dispose(); }
    }
}
