using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.Instances;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.Updates
{
    public sealed class DeferredUpdateInstallerLaunch : IDisposable
    {
        private readonly string _directory;
        private readonly string _operationId;
        private bool _committed;

        internal DeferredUpdateInstallerLaunch(string directory, string operationId)
        {
            _directory = directory;
            _operationId = operationId;
        }

        public void Commit()
        {
            File.WriteAllText(Path.Combine(_directory, _operationId + ".commit"), _operationId, new UTF8Encoding(false));
            _committed = true;
        }

        public void Dispose()
        {
            if (_committed) return;
            Cancel();
        }

        public void Cancel()
        {
            File.WriteAllText(Path.Combine(_directory, _operationId + ".cancel"), _operationId, new UTF8Encoding(false));
            DeferredUpdateInstallerLauncher.RemoveOwnedMarker(_directory, _operationId);
        }
    }

    public static class DeferredUpdateInstallerLauncher
    {
        private const int MaximumMarkerBytes = 32768;

        private sealed class UpdateRequest
        {
            public int SchemaVersion { get; set; }
            public string OperationId { get; set; }
            public string InstallationDirectory { get; set; }
            public string InstallerPath { get; set; }
            public string InstallerSha256 { get; set; }
            public string MutexName { get; set; }
            public long ExpiresUtcTicks { get; set; }
            public TargetInstanceRecord Parent { get; set; }
            public TargetInstanceRecord Helper { get; set; }
            public string[] SharedRoots { get; set; }
        }

        public static async Task<DeferredUpdateInstallerLaunch> ScheduleAsync(string installerPath,
            TargetInstanceRecord parent, string[] sharedRoots)
        {
            if (parent == null || TargetInstanceSecurity.IsAlive(parent) != true || !TargetInstanceSecurity.IsSameInstallation(parent))
                throw new InvalidOperationException("The update owner identity could not be verified.");
            using (var current = Process.GetCurrentProcess())
                if (parent.ProcessId != current.Id) throw new InvalidOperationException("Only the current target can schedule its deferred update.");
            if (!string.Equals(Path.GetFileName(parent.ExecutablePath), "MyCodex.exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The released installer cannot update a development executable. Build the new development version instead.");
            var installer = Path.GetFullPath(installerPath ?? throw new ArgumentNullException(nameof(installerPath)));
            if (!File.Exists(installer) || !string.Equals(Path.GetExtension(installer), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The downloaded installer is unavailable.");
            var installation = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var script = Path.Combine(installation, "tools", "update", "Invoke-DeferredMyCodexUpdate.ps1");
            var guard = Path.Combine(installation, "tools", "installer", "Test-MyCodexResourceUsers.ps1");
            if (!File.Exists(script) || !File.Exists(guard)) throw new InvalidOperationException("The deferred update helper is unavailable.");
            var directory = MarkerDirectory(installation);
            Directory.CreateDirectory(directory);
            ThrowIfUpdatePending();
            var id = Guid.NewGuid().ToString("N");
            var request = new UpdateRequest
            {
                SchemaVersion = 1, OperationId = id, Parent = parent, InstallationDirectory = installation,
                InstallerPath = installer, InstallerSha256 = FileHash(installer), MutexName = TargetInstanceCoordinator.SharedUpdateMutexName,
                ExpiresUtcTicks = DateTime.UtcNow.AddMinutes(10).Ticks,
                SharedRoots = (sharedRoots ?? new string[0]).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
            };
            var marker = Path.Combine(directory, "active.json");
            var launch = new DeferredUpdateInstallerLaunch(directory, id);
            try
            {
                // CreateNew prevents two scheduling attempts from claiming the same installation marker.
                using (var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    var data = new UTF8Encoding(false).GetBytes(new JavaScriptSerializer().Serialize(request));
                    if (data.Length > MaximumMarkerBytes) throw new InvalidDataException("The update request is too large.");
                    stream.Write(data, 0, data.Length);
                }
                File.Copy(script, Path.Combine(directory, id + ".ps1"));
                File.Copy(guard, Path.Combine(directory, id + ".guard.ps1"));
                var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
                using (var helper = Process.Start(new ProcessStartInfo
                {
                    FileName = powershell, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + WindowsCommandLineArgumentEscaper.Quote(Path.Combine(directory, id + ".ps1")) +
                        " -RequestPath " + WindowsCommandLineArgumentEscaper.Quote(marker)
                }))
                {
                    if (helper == null) throw new InvalidOperationException("The deferred update helper did not start.");
                    var ready = Path.Combine(directory, id + ".ready");
                    var deadline = DateTime.UtcNow.AddSeconds(15);
                    while (DateTime.UtcNow < deadline && !helper.HasExited)
                    {
                        if (File.Exists(ready))
                        {
                            var actual = ReadMarker(marker);
                            if (actual.OperationId == id && actual.Helper != null && actual.Helper.ProcessId == helper.Id &&
                                TargetInstanceSecurity.IsAlive(actual.Helper) == true && actual.Helper.UserSid == parent.UserSid &&
                                string.Equals(actual.Helper.ExecutablePath, powershell, StringComparison.OrdinalIgnoreCase)) return launch;
                            throw new InvalidOperationException("The update helper identity could not be verified.");
                        }
                        await Task.Delay(100);
                    }
                    throw new InvalidOperationException("The update helper did not confirm readiness. Check the update-coordination log.");
                }
            }
            catch { launch.Dispose(); throw; }
        }

        public static void ThrowIfUpdatePending()
        {
            var installation = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var directory = MarkerDirectory(installation);
            ThrowIfUpdatePending(installation, directory, TargetInstanceSecurity.IsAlive, DateTime.UtcNow);
        }

        internal static void ThrowIfUpdatePending(string installation, string directory,
            Func<TargetInstanceRecord, bool?> isAlive, DateTime now)
        {
            var marker = Path.Combine(directory, "active.json");
            if (!File.Exists(marker)) return;
            var request = ReadMarker(marker);
            Guid operation;
            if (request.SchemaVersion != 1 || !Guid.TryParseExact(request.OperationId, "N", out operation) ||
                !string.Equals(request.InstallationDirectory, installation, StringComparison.OrdinalIgnoreCase) ||
                request.ExpiresUtcTicks > now.AddMinutes(11).Ticks || request.ExpiresUtcTicks <= 0)
                throw new InvalidDataException("The pending update marker cannot be verified.");
            // Helper liveness keeps protection after the bounded parent handoff lease, including a long installer wizard.
            if (request.ExpiresUtcTicks > now.Ticks && isAlive(request.Parent) != false ||
                request.Helper != null && isAlive(request.Helper) != false)
                throw new InvalidOperationException("An update is preparing or installing. Retry after it completes.");
            RemoveOwnedMarker(directory, request.OperationId);
        }

        internal static void RemoveOwnedMarker(string directory, string operationId)
        {
            var marker = Path.Combine(directory, "active.json");
            if (File.Exists(marker) && ReadMarker(marker).OperationId == operationId) File.Delete(marker);
        }

        private static UpdateRequest ReadMarker(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.Length < 1 || stream.Length > MaximumMarkerBytes) throw new InvalidDataException("Invalid pending update marker size.");
                using (var reader = new StreamReader(stream, new UTF8Encoding(false, true)))
                    return new JavaScriptSerializer { MaxJsonLength = MaximumMarkerBytes }.Deserialize<UpdateRequest>(reader.ReadToEnd()) ??
                        throw new InvalidDataException("Invalid pending update marker.");
            }
        }

        private static string MarkerDirectory(string installation)
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyCodex", "update-coordination", Hash(Encoding.UTF8.GetBytes(installation.ToUpperInvariant())));
        }

        private static string FileHash(string path)
        {
            using (var file = File.OpenRead(path))
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", string.Empty);
        }

        private static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
        }
    }
}
