using System;
using System.IO;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal interface IEmbeddedRelayBinaryLocator
    {
        string Resolve();
    }

    public sealed class EmbeddedRelayBinaryLocator : IEmbeddedRelayBinaryLocator
    {
        public string Resolve()
        {
            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var bundled = Path.Combine(baseDirectory, "runtime", "relay", "mycodex-relay.exe");
            if (File.Exists(bundled))
            {
                return bundled;
            }

            var legacyBundled = Path.Combine(baseDirectory, "relay", "mycodex-relay.exe");
            if (File.Exists(legacyBundled))
            {
                return legacyBundled;
            }

            var current = new DirectoryInfo(baseDirectory);
            for (var depth = 0; current != null && depth <= 6; depth++)
            {
                var sibling = Path.Combine(
                    current.FullName,
                    "MyCodex-relay",
                    "dist",
                    "windows-x64",
                    "mycodex-relay.exe");
                if (File.Exists(sibling))
                {
                    return sibling;
                }

                current = current.Parent;
            }

            throw new FileNotFoundException("Unable to locate mycodex-relay.exe.");
        }
    }
}
