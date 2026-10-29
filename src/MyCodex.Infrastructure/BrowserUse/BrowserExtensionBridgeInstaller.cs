using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.Persistence;

namespace MyCodex.Infrastructure.BrowserUse
{
    internal sealed class BrowserExtensionBridgeInstaller
    {
        private readonly string _root;
        private readonly Func<byte[]> _readVerifiedHost;
        private readonly IBrowserNativeMessagingRegistration _registration;

        internal BrowserExtensionBridgeInstaller(string localAppData, Func<byte[]> readVerifiedHost, IBrowserNativeMessagingRegistration registration)
        {
            _root = Path.GetFullPath(Path.Combine(localAppData, "MyCodex", "browser-host"));
            _readVerifiedHost = readVerifiedHost ?? throw new ArgumentNullException(nameof(readVerifiedHost));
            _registration = registration ?? throw new ArgumentNullException(nameof(registration));
        }

        internal string Install(BrowserExtensionBrowser browser, CancellationToken token)
        {
            var registryPath = BrowserNativeMessagingRegistration.KeyPath(browser);
            token.ThrowIfCancellationRequested();
            var hostBytes = _readVerifiedHost();
            if (hostBytes == null || hostBytes.Length == 0) throw new InvalidDataException("BROWSER_HOST_INCOMPLETE");
            BrowserExtensionHostBundle.RejectLinkedAncestors(_root);
            Directory.CreateDirectory(_root);
            using (var lease = AcquireLease(token))
                return Publish(browser, registryPath, hostBytes, token);
        }

        private FileStream AcquireLease(CancellationToken token)
        {
            var path = Path.Combine(_root, "install.lock");
            BrowserExtensionHostBundle.RejectLinkedAncestors(path);
            for (var attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) when (attempt < 50) { token.WaitHandle.WaitOne(100); }
            }
        }

        private string Publish(BrowserExtensionBrowser browser, string registryPath, byte[] hostBytes, CancellationToken token)
        {
            var original = _registration.Read(browser);
            var id = Guid.NewGuid().ToString("N");
            // A fresh generation leaves binaries used by an already-running browser untouched.
            var directory = Path.Combine(_root, "h1", id);
            var backup = Path.Combine(_root, "backups", id + ".json");
            var manifest = Path.Combine(directory, "com.openai.codexextension.json");
            BrowserExtensionHostBundle.RejectLinkedAncestors(directory);
            BrowserExtensionHostBundle.RejectLinkedAncestors(backup);
            Directory.CreateDirectory(directory);
            AtomicFileWriter.Write(Path.Combine(directory, "extension-host.exe"), stream => stream.Write(hostBytes, 0, hostBytes.Length));
            var serializer = new JavaScriptSerializer();
            AtomicFileWriter.WriteText(manifest, serializer.Serialize(new Dictionary<string, object>
            {
                { "name", "com.openai.codexextension" }, { "type", "stdio" },
                { "description", "MyCodex browser automation bridge" },
                { "path", Path.Combine(directory, "extension-host.exe") },
                { "allowed_origins", WindowsBrowserExtensionSetupService.ExtensionIds.Select(value => "chrome-extension://" + value + "/").ToArray() }
            }));
            AtomicFileWriter.WriteText(backup, serializer.Serialize(new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "registryKey", "HKEY_CURRENT_USER\\" + registryPath },
                { "valueExists", original.Exists }, { "valueKind", original.Kind.ToString() }, { "value", original.Data },
                { "installedManifest", manifest }
            }));
            token.ThrowIfCancellationRequested();
            if (!original.Matches(_registration.Read(browser))) throw new IOException("BROWSER_REGISTRATION_CHANGED");
            var installed = new BrowserNativeMessagingValue { Exists = true, Data = manifest, Kind = RegistryValueKind.String };
            try
            {
                _registration.Write(browser, installed);
                if (!installed.Matches(_registration.Read(browser))) throw new IOException("BROWSER_REGISTRATION_NOT_PUBLISHED");
            }
            catch
            {
                // Do not roll another application's later registration back over its own update.
                if (installed.Matches(_registration.Read(browser))) _registration.Write(browser, original);
                throw;
            }
            return backup;
        }
    }
}
