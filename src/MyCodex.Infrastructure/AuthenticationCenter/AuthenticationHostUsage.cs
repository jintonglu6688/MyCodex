using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    // The backend store is shared by Codex home, including Dev and Stable clients.
    internal sealed class AuthenticationHostUsage
    {
        private readonly string _directory;
        private readonly string _dataDirectory;
        private readonly string _legacyDirectory;
        private readonly string _userFile;
        private readonly Identity _owner;
        private readonly Func<int, Identity> _readIdentity;
        private bool _registered;

        internal AuthenticationHostUsage(string dataDirectory, Identity owner = null, Func<int, Identity> readIdentity = null)
        {
            var backendDirectory = new DirectoryInfo(Path.GetFullPath(dataDirectory));
            if (backendDirectory.Parent == null) throw new ArgumentException("Backend store must have a parent directory.", nameof(dataDirectory));
            _dataDirectory = backendDirectory.FullName;
            _legacyDirectory = Path.Combine(_dataDirectory, "desktop-users");
            // A fresh native store must remain empty until the backend establishes ownership.
            _directory = Path.Combine(backendDirectory.Parent.FullName, backendDirectory.Name + ".desktop-users");
            // Established stores keep their gate and user records, including clients still running old code.
            if (Directory.Exists(_legacyDirectory) && File.Exists(Path.Combine(_dataDirectory, ".mycodex-auth-core")))
                _directory = _legacyDirectory;
            _userFile = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".json");
            _readIdentity = readIdentity ?? ReadIdentity;
            _owner = owner ?? _readIdentity(Process.GetCurrentProcess().Id);
        }

        internal async Task<FileStream> EnterAsync(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    RecoverUninitializedCoordinationDirectory();
                    Directory.CreateDirectory(_directory);
                    // A file handle has no thread affinity and is released even on a process crash.
                    return new FileStream(Path.Combine(_directory, "lifecycle.lock"), FileMode.OpenOrCreate,
                        FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    await Task.Delay(100, token).ConfigureAwait(false);
                }
            }
        }

        private void RecoverUninitializedCoordinationDirectory()
        {
            if (string.Equals(_directory, _legacyDirectory, StringComparison.OrdinalIgnoreCase) ||
                Directory.Exists(_directory) || !Directory.Exists(_legacyDirectory) ||
                File.Exists(Path.Combine(_dataDirectory, ".mycodex-auth-core"))) return;
            if ((File.GetAttributes(_dataDirectory) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(_legacyDirectory) & FileAttributes.ReparsePoint) != 0) return;
            var entries = Directory.GetFileSystemEntries(_dataDirectory);
            var files = Directory.GetFileSystemEntries(_legacyDirectory);
            var gatePath = Path.Combine(_legacyDirectory, "lifecycle.lock");
            if (entries.Length != 1 || !string.Equals(entries[0], _legacyDirectory, StringComparison.OrdinalIgnoreCase) ||
                files.Length != 1 || !string.Equals(files[0], gatePath, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(gatePath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0 ||
                new FileInfo(gatePath).Length != 0) return;
            // Move only the empty desktop lock while excluding old readers/writers.
            // Windows cannot rename its parent while the lock is open; remove that directory only if empty.
            using (var gate = new FileStream(gatePath, FileMode.Open, FileAccess.ReadWrite, FileShare.Delete))
            {
                Directory.CreateDirectory(_directory);
                File.Move(gatePath, Path.Combine(_directory, "lifecycle.lock"));
                Directory.Delete(_legacyDirectory, false);
            }
        }

        // Call these methods while holding EnterAsync's handle.
        internal void Register()
        {
            if (_registered) return;
            if (!IsComplete(_owner)) throw new IOException("Cannot identify authentication backend user.");
            var temporary = _userFile + ".tmp";
            try
            {
                File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(_owner));
                File.Move(temporary, _userFile);
                _registered = true;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        internal bool ReleaseAndIsLastUser()
        {
            if (!_registered) return false;
            File.Delete(_userFile);
            _registered = false;
            var last = true;
            foreach (var path in Directory.GetFiles(_directory, "*.json"))
            {
                try
                {
                    var expected = new JavaScriptSerializer().Deserialize<Identity>(File.ReadAllText(path));
                    if (!IsComplete(expected))
                    {
                        last = false;
                        continue;
                    }
                    var actual = _readIdentity(expected.ProcessId);
                    if (actual == null || !Matches(expected, actual)) File.Delete(path);
                    else last = false;
                }
                catch (Exception)
                {
                    // An inaccessible or damaged ownership record is not permission to stop a service.
                    last = false;
                }
            }
            return last;
        }

        private static bool IsComplete(Identity value)
        {
            return value != null && value.ProcessId > 0 && value.StartTimeUtcTicks > 0 &&
                !string.IsNullOrWhiteSpace(value.ExecutablePath) && Path.IsPathRooted(value.ExecutablePath);
        }

        private static bool Matches(Identity expected, Identity actual)
        {
            return IsComplete(actual) && expected.ProcessId == actual.ProcessId &&
                expected.StartTimeUtcTicks == actual.StartTimeUtcTicks &&
                string.Equals(Path.GetFullPath(expected.ExecutablePath), Path.GetFullPath(actual.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }

        private static Identity ReadIdentity(int processId)
        {
            Process process;
            try { process = Process.GetProcessById(processId); }
            catch (ArgumentException) { return null; }
            using (process)
            {
                if (process.HasExited) return null;
                return new Identity
                {
                    ProcessId = processId,
                    ExecutablePath = process.MainModule.FileName,
                    StartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks
                };
            }
        }

        internal sealed class Identity
        {
            public Identity() { }
            public int ProcessId { get; set; }
            public string ExecutablePath { get; set; }
            public long StartTimeUtcTicks { get; set; }
        }
    }
}
