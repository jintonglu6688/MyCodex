using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.Updates;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Codex
{
    internal sealed class CodexRuntimePackage
    {
        public CodexRuntimePackage(string version, string targetTriple, string fileName, string integrity, string shaSum)
        {
            Version = version;
            TargetTriple = targetTriple;
            FileName = fileName;
            Integrity = integrity;
            ShaSum = shaSum;
        }

        public string Version { get; }
        public string TargetTriple { get; }
        public string FileName { get; }
        public string Integrity { get; }
        public string ShaSum { get; }
        public string MirrorUrl => CodexRuntimePackageClient.MirrorRegistry + "/@openai/codex/-/" + FileName;
        public string OfficialUrl => CodexRuntimePackageClient.OfficialRegistry + "/@openai/codex/-/" + FileName;
    }

    // Registry discovery and transfer share a fixed package identity; installation never re-resolves latest.
    internal sealed class CodexRuntimePackageClient
    {
        internal const string OfficialRegistry = "https://registry.npmjs.org";
        internal const string MirrorRegistry = "https://registry.npmmirror.com";
        private readonly Func<string, CancellationToken, Task<IDictionary<string, object>>> _readJson;
        private readonly Func<string, string, IProgress<UpdateDownloadProgress>, CancellationToken, Task> _download;
        private bool _officialMetadataFailed;

        public CodexRuntimePackageClient(
            Func<string, CancellationToken, Task<IDictionary<string, object>>> readJson = null,
            Func<string, string, IProgress<UpdateDownloadProgress>, CancellationToken, Task> download = null)
        {
            _readJson = readJson ?? ReadJsonAsync;
            _download = download ?? ((url, path, progress, token) =>
                UpdateFileTransfer.DownloadFileAsync(new Uri(url), path, progress, token));
        }

        public async Task<CodexRuntimePackage> CheckLatestAsync(string targetTriple, CancellationToken token)
        {
            _officialMetadataFailed = false;
            var latest = await ReadMetadataAsync("latest", token).ConfigureAwait(false);
            var version = JsonValueReader.GetString(latest, "version");
            if (version == null || !Regex.IsMatch(version, @"\A(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\z", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.LatestVersionMissing",
                    "Codex runtime metadata did not include a stable version."));

            string suffix;
            switch (targetTriple)
            {
                case "x86_64-pc-windows-msvc": suffix = "win32-x64"; break;
                case "aarch64-pc-windows-msvc": suffix = "win32-arm64"; break;
                default: throw new ArgumentException("Unsupported bundled runtime platform.", nameof(targetTriple));
            }

            var packageVersion = version + "-" + suffix;
            var metadata = await ReadMetadataAsync(packageVersion, token).ConfigureAwait(false);
            var fileName = "codex-" + packageVersion + ".tgz";
            var dist = JsonValueReader.GetDictionary(metadata, "dist");
            var tarball = JsonValueReader.GetString(dist, "tarball");
            Uri uri;
            if (!string.Equals(JsonValueReader.GetString(metadata, "version"), packageVersion, StringComparison.Ordinal) ||
                !Uri.TryCreate(tarball, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps ||
                (uri.Host != "registry.npmjs.org" && uri.Host != "registry.npmmirror.com") ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 ||
                !string.Equals(Uri.UnescapeDataString(uri.AbsolutePath), "/@openai/codex/-/" + fileName, StringComparison.Ordinal))
                throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                    SharedLocalization.Text("CodexRuntimeUpdate.Error.UnexpectedPackageName.Template",
                        "Unexpected Codex runtime package name '{0}'. Expected '{1}'."), tarball, fileName));

            var integrity = JsonValueReader.GetString(dist, "integrity");
            var shaSum = JsonValueReader.GetString(dist, "shasum");
            if (string.IsNullOrWhiteSpace(integrity) && string.IsNullOrWhiteSpace(shaSum))
                throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.IntegrityCheckFailed",
                    "Codex runtime package integrity check failed."));
            // The mirror serves both this metadata and the package, so its hash proves nothing by itself.
            // npm's own signature over the hash does; the unsigned SHA-1 is then left unused.
            if (_officialMetadataFailed)
            {
                if (!VerifyRegistrySignature(packageVersion, integrity, JsonValueReader.GetArray(dist, "signatures")))
                    throw new InvalidOperationException(SharedLocalization.Text("CodexRuntimeUpdate.Error.IntegrityCheckFailed",
                        "Codex runtime package integrity check failed."));
                shaSum = null;
            }

            token.ThrowIfCancellationRequested();
            return new CodexRuntimePackage(version, targetTriple, fileName, integrity, shaSum);
        }

        // npm signs "<name>@<version>:<integrity>" with its registry key, and mirrors republish that signature.
        // shortcut: one pinned npm registry key (it has no expiry date), add the next key here when npm rotates it.
        private const string NpmRegistryKeyId = "SHA256:DhQ8wR5APBvFHLF/+Tc+AYvPOdTpcIDqOhxsBHRwC7U";
        private const string NpmRegistryPublicKey =
            "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEY6Ya7W++7aUPzvMTrezH6Ycx3c+HOKYCcNGybJZSCJq/fd7Qa8uuAKtdIkUQtQiEKERhAmE5lMMJhP8OkDOa2g==";

        internal static bool VerifyRegistrySignature(string packageVersion, string integrity, IEnumerable<object> signatures)
        {
            if (string.IsNullOrWhiteSpace(packageVersion) || string.IsNullOrWhiteSpace(integrity) || signatures == null)
            {
                return false;
            }

            // The key is a P-256 SubjectPublicKeyInfo; its last 65 bytes are the uncompressed point.
            var keyInfo = Convert.FromBase64String(NpmRegistryPublicKey);
            var publicKey = new byte[65];
            Buffer.BlockCopy(keyInfo, keyInfo.Length - publicKey.Length, publicKey, 0, publicKey.Length);
            var message = System.Text.Encoding.UTF8.GetBytes("@openai/codex@" + packageVersion + ":" + integrity);
            var crypto = new RemoteRelay.CngSecureRemoteCrypto();
            foreach (var item in signatures)
            {
                var signature = item as IDictionary<string, object>;
                byte[] rawSignature;
                if (signature != null &&
                    string.Equals(JsonValueReader.GetString(signature, "keyid"), NpmRegistryKeyId, StringComparison.Ordinal) &&
                    TryReadDerEcdsaSignature(JsonValueReader.GetString(signature, "sig"), out rawSignature) &&
                    crypto.VerifyRawEcdsa(publicKey, message, rawSignature))
                {
                    return true;
                }
            }

            return false;
        }

        // DER "SEQUENCE { INTEGER r, INTEGER s }" to the fixed 32+32 byte form the verifier takes.
        private static bool TryReadDerEcdsaSignature(string base64, out byte[] rawSignature)
        {
            rawSignature = new byte[64];
            byte[] der;
            try
            {
                der = Convert.FromBase64String(base64 ?? string.Empty);
            }
            catch (FormatException)
            {
                return false;
            }

            // A P-256 signature is at most 72 bytes, so every length fits the one-byte form.
            if (der.Length < 8 || der.Length > 72 || der[0] != 0x30 || der[1] != der.Length - 2)
            {
                return false;
            }

            var offset = 2;
            for (var part = 0; part < 2; part++)
            {
                if (offset + 2 > der.Length || der[offset] != 0x02)
                {
                    return false;
                }

                int length = der[offset + 1];
                offset += 2;
                if (length == 0 || offset + length > der.Length)
                {
                    return false;
                }

                // A leading zero only keeps the integer positive.
                var start = offset;
                offset += length;
                while (length > 32 && der[start] == 0)
                {
                    start++;
                    length--;
                }

                if (length > 32)
                {
                    return false;
                }

                Buffer.BlockCopy(der, start, rawSignature, part * 32 + 32 - length, length);
            }

            return offset == der.Length;
        }

        private async Task<IDictionary<string, object>> ReadMetadataAsync(string version, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var path = "/%40openai%2Fcodex/" + version;
            if (!_officialMetadataFailed)
            {
                try { return await _readJson(OfficialRegistry + path, token).ConfigureAwait(false); }
                catch (Exception error) when (CanRetry(error, token))
                {
                    // Startup waits for this check; one official timeout per check is enough.
                    _officialMetadataFailed = true;
                }
            }

            return await _readJson(MirrorRegistry + path, token).ConfigureAwait(false);
        }

        public async Task DownloadVerifiedAsync(CodexRuntimePackage package, string path,
            IProgress<UpdateDownloadProgress> progress, CancellationToken token, IProgress<string> status = null)
        {
            if (package == null) throw new ArgumentNullException(nameof(package));
            foreach (var url in new[] { package.MirrorUrl, package.OfficialUrl })
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    status?.Report(string.Format(CultureInfo.CurrentCulture,
                        SharedLocalization.Text("CodexRuntimeUpdate.Progress.Downloading.Template",
                            "Downloading Codex runtime {0}..."), package.Version));
                    await _download(url, path, progress, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    status?.Report(SharedLocalization.Text("CodexRuntimeUpdate.Progress.Verifying",
                        "Verifying Codex runtime package..."));
                    if (!VerifyPackageIntegrity(path, package.Integrity, package.ShaSum))
                        throw new InvalidDataException(SharedLocalization.Text("CodexRuntimeUpdate.Error.IntegrityCheckFailed",
                            "Codex runtime package integrity check failed."));
                    return;
                }
                catch (Exception error) when (CanRetry(error, token))
                {
                    if (File.Exists(path)) File.Delete(path);
                    if (url == package.OfficialUrl) throw;
                }
            }
        }

        private static bool CanRetry(Exception error, CancellationToken token)
        {
            return !token.IsCancellationRequested &&
                (error is WebException || error is IOException || error is InvalidDataException || error is OperationCanceledException);
        }

        private static async Task<IDictionary<string, object>> ReadJsonAsync(string url, CancellationToken token)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var client = new WebClient())
            using (timeout.Token.Register(client.CancelAsync))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    var json = await client.DownloadStringTaskAsync(new Uri(url)).ConfigureAwait(false);
                    return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                }
                catch (WebException) when (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }
            }
        }

        // A published SHA-256/384/512 SRI hash is authoritative: a mismatch must not be rescued by
        // the weaker SHA-1 shasum. SHA-1 is used only when no strong SRI hash is published.
        internal static bool VerifyPackageIntegrity(string filePath, string integrity, string shaSum)
        {
            bool hasStrongHash;
            if (TestSriIntegrity(filePath, integrity, out hasStrongHash))
            {
                return true;
            }

            return !hasStrongHash && TestSha1(filePath, shaSum);
        }

        private static bool TestSriIntegrity(string filePath, string integrity, out bool hasStrongHash)
        {
            hasStrongHash = false;
            if (string.IsNullOrWhiteSpace(integrity))
            {
                return false;
            }

            var weakMatch = false;
            foreach (var token in integrity.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = token.IndexOf("-", StringComparison.Ordinal);
                if (separator <= 0 || separator >= token.Length - 1)
                {
                    continue;
                }

                var algorithmName = token.Substring(0, separator).ToUpperInvariant();
                var expected = token.Substring(separator + 1);
                var actual = ComputeHashBase64(filePath, algorithmName);
                if (actual == null)
                {
                    continue;
                }

                var isStrong = !string.Equals(algorithmName, "SHA1", StringComparison.Ordinal);
                hasStrongHash |= isStrong;
                if (string.Equals(actual, expected, StringComparison.Ordinal))
                {
                    if (isStrong)
                    {
                        return true;
                    }

                    weakMatch = true;
                }
            }

            return weakMatch && !hasStrongHash;
        }

        private static bool TestSha1(string filePath, string shaSum)
        {
            if (string.IsNullOrWhiteSpace(shaSum))
            {
                return false;
            }

            return string.Equals(
                ComputeHashHex(filePath, "SHA1"),
                shaSum.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeHashBase64(string filePath, string algorithmName)
        {
            var hash = ComputeHash(filePath, algorithmName);
            return hash == null ? null : Convert.ToBase64String(hash);
        }

        private static string ComputeHashHex(string filePath, string algorithmName)
        {
            var hash = ComputeHash(filePath, algorithmName);
            if (hash == null)
            {
                return null;
            }

            return BitConverter.ToString(hash).Replace("-", string.Empty);
        }

        private static byte[] ComputeHash(string filePath, string algorithmName)
        {
            HashAlgorithm algorithm;
            switch (algorithmName)
            {
                case "SHA1":
                    algorithm = SHA1.Create();
                    break;
                case "SHA256":
                    algorithm = SHA256.Create();
                    break;
                case "SHA384":
                    algorithm = SHA384.Create();
                    break;
                case "SHA512":
                    algorithm = SHA512.Create();
                    break;
                default:
                    return null;
            }

            using (algorithm)
            using (var stream = File.OpenRead(filePath))
            {
                return algorithm.ComputeHash(stream);
            }
        }

    }
}
