using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal sealed class SharedRelayProcessRecord
    {
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; }
        public long StartTimeUtcTicks { get; set; }

        public EmbeddedRelayProcessIdentity ToIdentity()
        {
            return new EmbeddedRelayProcessIdentity(ProcessId, ExecutablePath, StartTimeUtcTicks);
        }

        public static SharedRelayProcessRecord Capture(Process process, string expectedExecutablePath = null)
        {
            var identity = expectedExecutablePath == null
                ? new EmbeddedRelayProcessAccess().Inspect(process)
                : new EmbeddedRelayProcessController().CaptureStartedProcess(
                    process, process.Id, expectedExecutablePath, TimeSpan.FromSeconds(3));
            if (identity == null)
            {
                throw new InvalidOperationException("Unable to verify the shared Relay process identity.");
            }

            return new SharedRelayProcessRecord
            {
                ProcessId = identity.ProcessId,
                ExecutablePath = identity.ExecutablePath,
                StartTimeUtcTicks = identity.StartTimeUtcTicks
            };
        }
    }

    internal sealed class SharedRelayLease
    {
        public string Id { get; set; }
        public SharedRelayProcessRecord User { get; set; }
    }

    internal sealed class SharedRelayState
    {
        public string InstallationExecutable { get; set; }
        public SharedRelayProcessRecord Host { get; set; }
        public List<SharedRelayLease> Leases { get; set; } = new List<SharedRelayLease>();
        public bool Ready { get; set; }
        public string Error { get; set; }
        public long SettingsRevision { get; set; }
    }

    // A file lock can be held across async continuations; a named mutex cannot.
    internal sealed class SharedRelayStateStore
    {
        private readonly string _directory;
        private readonly Func<SharedRelayProcessRecord, bool> _isAlive;
        private string _lastReadFailure;
        private readonly Func<string> _recoveryBlockReason;

        public SharedRelayStateStore(string sharedRoot, Func<SharedRelayProcessRecord, bool> isAlive = null,
            Func<string> recoveryBlockReason = null)
        {
            _directory = EmbeddedRelayDirectoryLayout.Create(Path.GetFullPath(sharedRoot)).RootDirectory;
            _isAlive = isAlive ?? IsProcessAlive;
            _recoveryBlockReason = recoveryBlockReason ?? (() => SharedRelayOwnershipRecovery.GetBlockReason(sharedRoot));
        }

        public T WithLock<T>(Func<SharedRelayState, T> action, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_directory);
            using (Acquire(cancellationToken))
            {
                var path = Path.Combine(_directory, "shared-host.json");
                var serializer = new JavaScriptSerializer();
                var state = File.Exists(path) ? ReadState(path, serializer) : new SharedRelayState();

                state.Leases.RemoveAll(lease => !_isAlive(lease.User));
                if (!string.IsNullOrWhiteSpace(state.InstallationExecutable) && state.Leases.Exists(lease =>
                    !string.Equals(lease.User.ExecutablePath, state.InstallationExecutable, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException("A live Relay user belongs to a different installation; ownership must be repaired before changing the service.");
                }
                if (state.Leases.Count == 0 && !IsAlive(state.Host))
                {
                    state.InstallationExecutable = null;
                }
                var result = action(state);
                WriteState(path, serializer, state);

                return result;
            }
        }

        private static void WriteState(string path, JavaScriptSerializer serializer, SharedRelayState state)
        {
            var temporary = path + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(serializer.Serialize(state));
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private SharedRelayState ReadState(string path, JavaScriptSerializer serializer)
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.All(value => value == 0)) return RecoverEmptyState(path, bytes, serializer);
            var json = string.Empty;
            SharedRelayState state;
            try
            {
                using (var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true))
                {
                    json = reader.ReadToEnd();
                }
                // Extra leading encoding markers carry no ownership data. Never discard NULs or missing fields.
                var normalized = json.TrimStart('\ufeff');
                state = serializer.Deserialize<SharedRelayState>(normalized);
                if (state == null || state.Leases == null ||
                    (state.Host != null && !IsComplete(state.Host)) ||
                    state.Leases.Exists(lease => lease == null || string.IsNullOrWhiteSpace(lease.Id) || !IsComplete(lease.User)))
                {
                    throw RecordFailure(path, bytes, json, "InvalidOwnership");
                }
                if (normalized.Length != json.Length)
                {
                    Trace.TraceInformation("[RemoteRelay] OwnershipEncodingRecovered; Path={0}; RemovedBomCount={1}; LeaseCount={2}",
                        path, json.Length - normalized.Length, state.Leases.Count);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException ||
                ex is FormatException || ex is OverflowException)
            {
                // Parser exception messages can contain the original file, including secret-bearing text.
                throw RecordFailure(path, bytes, json, ex.GetType().Name);
            }
            _lastReadFailure = null;
            return state;
        }

        private SharedRelayState RecoverEmptyState(string path, byte[] bytes, JavaScriptSerializer serializer)
        {
            string reason;
            try { reason = _recoveryBlockReason(); }
            catch (Exception ex) { reason = "InspectionFailed:" + ex.GetType().Name; }
            if (reason != null)
            {
                RecordFailure(path, bytes, new string('\0', bytes.Length), reason);
                throw new InvalidDataException(SharedLocalization.Text("RemoteRelay.Error.RecoveryBusy",
                    "Phone connection needs to repair its state. Exit all MyCodex windows and open only one target to retry. If this continues, restart Windows and retry."));
            }

            var backup = path + ".corrupt-" + Guid.NewGuid().ToString("N");
            File.Copy(path, backup);
            var state = new SharedRelayState();
            WriteState(path, serializer, state);
            _lastReadFailure = null;
            Trace.TraceWarning("[RemoteRelay] OwnershipRecovered; Path={0}; Bytes={1}; Backup={2}; Reason=ZeroFilled; CredentialsUnchanged=true",
                path, bytes.Length, backup);
            return state;
        }

        private InvalidDataException RecordFailure(string path, byte[] bytes, string json, string reason)
        {
            string fingerprint;
            using (var hash = SHA256.Create())
            {
                fingerprint = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
            }
            var signature = reason + ":" + fingerprint;
            if (!string.Equals(signature, _lastReadFailure, StringComparison.Ordinal))
            {
                _lastReadFailure = signature;
                Trace.TraceWarning("[RemoteRelay] OwnershipReadFailed; Path={0}; Bytes={1}; Characters={2}; " +
                    "NulCount={3}; BomCount={4}; FirstCodePoint={5}; Sha256={6}; Reason={7}; OwnershipRetained=true",
                    path, bytes.Length, json.Length, json.Count(value => value == '\0'), json.Count(value => value == '\ufeff'),
                    json.Length == 0 ? "none" : "U+" + ((int)json[0]).ToString("X4"), fingerprint, reason);
            }
            return new InvalidDataException(SharedLocalization.Text("RemoteRelay.Error.OwnershipUnreadable",
                "Phone connection state could not be read. Details are in the application log; please send it for support. The existing service was left unchanged."));
        }

        public bool IsAlive(SharedRelayProcessRecord identity)
        {
            return identity != null && _isAlive(identity);
        }

        internal static bool IsProcessAlive(SharedRelayProcessRecord identity)
        {
            if (!IsComplete(identity))
            {
                throw new InvalidDataException("The shared Relay process identity is incomplete; no process was stopped.");
            }

            Process process;
            try
            {
                process = Process.GetProcessById(identity.ProcessId);
            }
            catch (ArgumentException)
            {
                return false;
            }

            using (process)
            {
                try
                {
                    var actual = new EmbeddedRelayProcessAccess().Inspect(process);
                    if (actual == null)
                    {
                        if (process.HasExited) return false;
                        throw new InvalidOperationException("The live Relay process identity could not be inspected; ownership was retained.");
                    }

                    return actual.ProcessId == identity.ProcessId &&
                        actual.StartTimeUtcTicks == identity.StartTimeUtcTicks &&
                        string.Equals(actual.ExecutablePath, identity.ExecutablePath, StringComparison.OrdinalIgnoreCase);
                }
                catch (InvalidOperationException)
                {
                    if (process.HasExited) return false;
                    throw;
                }
                // Access denied or an unknown inspection error is not evidence that a user exited.
            }
        }

        private static bool IsComplete(SharedRelayProcessRecord identity)
        {
            return identity != null && identity.ProcessId > 0 && identity.StartTimeUtcTicks > 0 &&
                !string.IsNullOrWhiteSpace(identity.ExecutablePath);
        }

        private FileStream Acquire(CancellationToken cancellationToken)
        {
            var timeout = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return new FileStream(Path.Combine(_directory, "shared-host.lock"),
                        FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException)
                {
                    if (timeout.Elapsed > TimeSpan.FromSeconds(30))
                    {
                        throw new TimeoutException("The shared Relay lifecycle is busy.");
                    }

                    cancellationToken.WaitHandle.WaitOne(50);
                }
            }
        }
    }
}
