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
        internal const string Tag = "auth-core-v0.3.10";
        internal const string SourceRevision = "18ccacaa411f43d6d98c3a76ef0dd822d3d51e1e";
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

            internal string FileName => "mycodex-auth-host-0.3.10-" + Target + ".tar.gz";
        }

        internal static Archive ForTarget(string system, string architecture)
        {
            var target = RemoteAuthenticationHost.TargetTriple(system, architecture);
            switch (target)
            {
                case "x86_64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "a10bde14fd5573b4420fe1b7435ba16d1b0a5f476b200081bff1e852d2d11b7c",
                        ExecutableSha256 = "a6b52dfb15104d2e5b146fea6e0a4b885f4e9e32e336ae69ed9bc961b662073e" };
                case "aarch64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "80f3111843735c85809cbd2f97902941ca763cc1a610c4c3a569bff3d5034fa1",
                        ExecutableSha256 = "2edf334f308ae2df465034714fa17fb1bdf4549f91d0c2ec28b6d5e916a75450" };
                case "aarch64-apple-darwin":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "146030dc8ef01b90cb1009bb530a19b2f460eaafe48859ff8212b48af0062f5e",
                        ExecutableSha256 = "4817ca25385b0f140e63711045eaef4993dd5671d24919df480b0b2f8cb74028" };
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
