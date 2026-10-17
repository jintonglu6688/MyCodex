using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    internal static class AuthenticationHostRelease
    {
        internal const string Tag = "auth-core-v0.3.8";
        internal const string SourceRevision = "bf6eb7cf0468d83a5da1094eea08e8b39f7dbeac";
        private const string BaseUrl = "https://api.gitcode.com/api/v5/repos/gcw_SpGZ48lW/mycodex-auth-host/releases/";
        private const long MaxArchiveBytes = 16 * 1024 * 1024;
        private static readonly SemaphoreSlim DownloadGate = new SemaphoreSlim(1, 1);

        internal sealed class Archive
        {
            internal string Target;
            internal string Sha256;
            internal string ExecutableSha256;
            internal string System;
            internal string Architecture;

            internal string FileName => "mycodex-auth-host-0.3.8-" + Target + ".tar.gz";
        }

        internal static Archive ForTarget(string system, string architecture)
        {
            var target = RemoteAuthenticationHost.TargetTriple(system, architecture);
            switch (target)
            {
                case "x86_64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "e54658c5cee0d0b068a8f315ab86821f3130fbb7304e86284fdefb722ed3e312",
                        ExecutableSha256 = "775d6e4470dd0efc0ec61e3fc9ef7a4ca659f3c14a0018864f51b5f7b3ef25f6" };
                case "aarch64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "d0b24044dfabfad193a72175cea621d38d69bf88d201a01f17b7d1db3cd4d877",
                        ExecutableSha256 = "1562a20aeafb0ac9ca6c86f4a2c3502602f3a29518178aec76b9fdd170d8a856" };
                case "aarch64-apple-darwin":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "b405099d132e2c41de3da75d1bcff2e051e694babf6bebad21cd79bc4d91a01f",
                        ExecutableSha256 = "8472370dcd9121f122929a44c35d83786d3cacb9739c3760266c1c5776ad2b6b" };
                default:
                    throw new AuthenticationCenterException("unsupported_platform");
            }
        }

        internal static bool IsInstalledRelease(string manifestText, string actualHash, Archive archive)
        {
            AuthenticationHostArtifact.LoadTarget(manifestText, actualHash, archive.Target);
            var values = new JavaScriptSerializer { MaxJsonLength = 65536 }
                .DeserializeObject(manifestText) as System.Collections.Generic.IDictionary<string, object>;
            return values != null && Equals(values["sourceRevision"], SourceRevision) &&
                Equals(values["sourceDirty"], false) &&
                string.Equals(actualHash, archive.ExecutableSha256, StringComparison.OrdinalIgnoreCase);
        }

        internal static async Task<string> ReadVerifiedBase64Async(Archive archive, CancellationToken cancellationToken)
        {
            await DownloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return await ReadVerifiedBase64CoreAsync(archive, cancellationToken).ConfigureAwait(false); }
            finally { DownloadGate.Release(); }
        }

        internal static async Task CleanupCacheAsync(string target, string localData = null)
        {
            if (target != "x86_64-unknown-linux-gnu" && target != "aarch64-unknown-linux-gnu" &&
                target != "aarch64-apple-darwin") return;
            await DownloadGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var root = CacheRoot(localData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
                if (!Directory.Exists(root)) return;
                // Do not follow redirected cache directories into user data.
                for (var parent = new DirectoryInfo(root); parent != null; parent = parent.Parent)
                    if ((parent.Attributes & FileAttributes.ReparsePoint) != 0) return;
                const string prefix = "auth-core-v";
                var installedVersion = Version.Parse(Tag.Substring(prefix.Length));
                foreach (var directory in Directory.EnumerateDirectories(root, prefix + "*"))
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) continue;
                    var versionText = Path.GetFileName(directory).Substring(prefix.Length);
                    if (!Version.TryParse(versionText, out var version) || version.ToString() != versionText ||
                        version > installedVersion) continue;
                    var path = Path.Combine(directory, "mycodex-auth-host-" + versionText + "-" + target + ".tar.gz");
                    if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                        File.Delete(path);
                    using (var entries = Directory.EnumerateFileSystemEntries(directory).GetEnumerator())
                        if (entries.MoveNext()) continue;
                    Directory.Delete(directory, false);
                }
            }
            // Cleanup must not turn a completed installation into a failed update.
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (System.Security.SecurityException) { }
            catch (AuthenticationCenterException) { }
            finally { DownloadGate.Release(); }
        }

        private static string CacheRoot(string localData)
        {
            if (string.IsNullOrWhiteSpace(localData)) throw new AuthenticationCenterException("backend_download_failed");
            return Path.Combine(localData, "MyCodex", "auth-core", "release-cache");
        }

        private static async Task<string> ReadVerifiedBase64CoreAsync(Archive archive, CancellationToken cancellationToken)
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var cache = Path.Combine(CacheRoot(localData), Tag);
            Directory.CreateDirectory(cache);
            var path = Path.Combine(cache, archive.FileName);
            if (!File.Exists(path) || !Verify(path, archive.Sha256))
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".download";
                try
                {
                    using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
                    using (var response = await client.GetAsync(BaseUrl + Tag + "/attach_files/" + archive.FileName + "/download",
                        HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
                    {
                        response.EnsureSuccessStatusCode();
                        if (response.Content.Headers.ContentLength > MaxArchiveBytes)
                            throw new AuthenticationCenterException("backend_download_failed");
                        using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var output = File.Create(temporary))
                        {
                            var buffer = new byte[65536];
                            int count;
                            while ((count = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                            {
                                if (output.Length + count > MaxArchiveBytes)
                                    throw new AuthenticationCenterException("backend_download_failed");
                                await output.WriteAsync(buffer, 0, count, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                    if (!Verify(temporary, archive.Sha256))
                        throw new AuthenticationCenterException("backend_integrity_failed");
                    if (File.Exists(path)) File.Delete(path);
                    File.Move(temporary, path);
                }
                catch (OperationCanceledException) { throw; }
                catch (AuthenticationCenterException) { throw; }
                catch (Exception) { throw new AuthenticationCenterException("backend_download_failed"); }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return Convert.ToBase64String(File.ReadAllBytes(path));
        }

        private static bool Verify(string path, string expected)
        {
            if (new FileInfo(path).Length > MaxArchiveBytes) return false;
            using (var stream = File.OpenRead(path))
            using (var hash = SHA256.Create())
                return string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", string.Empty),
                    expected, StringComparison.OrdinalIgnoreCase);
        }
    }
}
