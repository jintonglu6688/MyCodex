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
        internal const string Tag = "auth-core-v0.4.0";
        internal const string SourceRevision = "3578e9083a058ab9c9a3cfb0b6be5e4d3201fc7f";
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

            internal string FileName => "mycodex-auth-host-0.4.0-" + Target + ".tar.gz";
        }

        internal static Archive ForTarget(string system, string architecture)
        {
            var target = RemoteAuthenticationHost.TargetTriple(system, architecture);
            switch (target)
            {
                case "x86_64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "06598119e1871e3423bb1800f104762bf906ff507c63071df5e12401cc9d0417",
                        ExecutableSha256 = "76faac1de93a56575422ea13476084485cfd15e186574d6f4204a7cccf2921c6" };
                case "aarch64-unknown-linux-gnu":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "3bdad643def2feb12b20a421b14c5a9157906cb1fe68edfcaaaa7efbf54e55d8",
                        ExecutableSha256 = "fa8d9520cce84c29cffc6df486efc07c14710ec82922175f3cd0cc3731f8d804" };
                case "aarch64-apple-darwin":
                    return new Archive { Target = target, System = system, Architecture = architecture,
                        Sha256 = "9d204ee5a898c357f0f7b21f63e7581721d9c54b0342a04b870f99481f8d4b6c",
                        ExecutableSha256 = "66159b5a8a8f3b448c7de5bc34c73ae8533bba923260cd3f1d57212dac247201" };
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
