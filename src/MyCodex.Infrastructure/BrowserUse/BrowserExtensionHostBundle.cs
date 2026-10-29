using System;
using System.IO;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace MyCodex.Infrastructure.BrowserUse
{
    internal static class BrowserExtensionHostBundle
    {
        internal static string Load(string installation)
        {
            return Verify(installation, ReadPin());
        }

        internal static byte[] Read(string installation)
        {
            var pin = ReadPin();
            var bytes = File.ReadAllBytes(Verify(installation, pin));
            using (var hash = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""), pin.sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("BROWSER_HOST_CONTENT_INVALID");
            return bytes;
        }

        private static Pin ReadPin()
        {
            using (var resource = typeof(BrowserExtensionHostBundle).Assembly.GetManifestResourceStream("MyCodex.BrowserUse.ExtensionHostPin"))
            using (var reader = new StreamReader(resource ?? throw new InvalidDataException("BROWSER_HOST_PIN_MISSING")))
                return new JavaScriptSerializer().Deserialize<Pin>(reader.ReadToEnd());
        }

        internal static string Verify(string installation, Pin pin)
        {
            if (pin == null || !System.Text.RegularExpressions.Regex.IsMatch(pin.bundleId ?? "", "^h[0-9]+$") ||
                pin.fileName != "extension-host.exe" || pin.sha256 == null || pin.sha256.Length != 64 || pin.size <= 0)
                throw new InvalidDataException("BROWSER_HOST_PIN_INVALID");
            var directory = Path.GetFullPath(Path.Combine(installation, "runtime", "browser-host", pin.bundleId));
            var path = Path.Combine(directory, pin.fileName);
            RejectLinkedAncestors(path);
            if (!File.Exists(path) || new FileInfo(path).Length != pin.size || Directory.GetFileSystemEntries(directory).Length != 1)
                throw new InvalidDataException("BROWSER_HOST_INCOMPLETE");
            VerifyFile(path, pin.sha256);
            return path;
        }

        internal static void VerifyFile(string path, string expectedHash)
        {
            RejectLinkedAncestors(path);
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
                if (!string.Equals(BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""), expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("BROWSER_HOST_CONTENT_INVALID");
        }

        internal static void RejectLinkedAncestors(string path)
        {
            for (var current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Linked browser host path is unsupported.");
        }

        internal sealed class Pin
        {
            public string bundleId { get; set; }
            public string fileName { get; set; }
            public string sha256 { get; set; }
            public long size { get; set; }
        }
    }
}
