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

            // Never search parent folders: a writable ancestor such as a drive root would let another user plant the Relay.
            throw new FileNotFoundException("Unable to locate mycodex-relay.exe.");
        }
    }
}
