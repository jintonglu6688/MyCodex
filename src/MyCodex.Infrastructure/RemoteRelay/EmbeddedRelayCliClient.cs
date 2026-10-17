using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;
using MyCodex.Shared.Helpers;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal interface IEmbeddedRelayCliClient
    {
        EmbeddedRelayLocalInfo Init(string configPath, string statePath, string publicHost, int listenPort, int publicPort, int internalPort);

        EmbeddedRelayLocalInfo EnsureTenant(string configPath, string name);

        EmbeddedRelaySecret RotateTenantSecret(string configPath, string tenantId);

        EmbeddedRelayLocalInfo Info(string configPath);
    }

    public sealed class EmbeddedRelayCliClient : IEmbeddedRelayCliClient
    {
        private readonly string _relayExecutablePath;

        public EmbeddedRelayCliClient(string relayExecutablePath)
        {
            if (string.IsNullOrWhiteSpace(relayExecutablePath))
            {
                throw new ArgumentException("Relay executable path cannot be empty.", nameof(relayExecutablePath));
            }

            _relayExecutablePath = relayExecutablePath;
        }

        public EmbeddedRelayLocalInfo Init(string configPath, string statePath, string publicHost, int listenPort, int publicPort, int internalPort)
        {
            return Run(BuildInitArguments(configPath, statePath, publicHost, listenPort, publicPort, internalPort));
        }

        internal static string BuildInitArguments(string configPath, string statePath, string publicHost, int listenPort, int publicPort, int internalPort)
        {
            return "local init --config " + Quote(configPath) +
                " --state " + Quote(statePath) +
                " --listen-host 0.0.0.0 --listen-port " + listenPort.ToString(CultureInfo.InvariantCulture) +
                " --internal-listen-host 127.0.0.1 --internal-listen-port " + internalPort.ToString(CultureInfo.InvariantCulture) +
                " --public-host " + Quote(publicHost) +
                " --public-port " + publicPort.ToString(CultureInfo.InvariantCulture) +
                " --embedded-tls --json";
        }

        public EmbeddedRelayLocalInfo EnsureTenant(string configPath, string name)
        {
            return Run("local ensure-tenant --config " + Quote(configPath) + " --name " + Quote(name) + " --json");
        }

        public EmbeddedRelaySecret RotateTenantSecret(string configPath, string tenantId)
        {
            return ParseRotatedSecret(RunText(BuildRotateTenantSecretArguments(configPath, tenantId)));
        }

        internal static string BuildRotateTenantSecretArguments(string configPath, string tenantId)
        {
            return "tenant rotate-secret --config " + Quote(configPath) + " --tenant " + Quote(tenantId);
        }

        internal static EmbeddedRelaySecret ParseRotatedSecret(string output)
        {
            string tenantId = null;
            string tenantSecret = null;
            using (var reader = new StringReader(output ?? string.Empty))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim();
                    if (string.Equals(key, "tenantId", StringComparison.Ordinal))
                    {
                        tenantId = value;
                    }
                    else if (string.Equals(key, "tenantSecret", StringComparison.Ordinal))
                    {
                        tenantSecret = value;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(tenantSecret))
            {
                throw new InvalidOperationException("Relay CLI did not return a rotated tenant secret.");
            }

            return new EmbeddedRelaySecret
            {
                TenantId = tenantId,
                Value = tenantSecret,
                Source = "rotated"
            };
        }

        public EmbeddedRelayLocalInfo Info(string configPath)
        {
            return Run("local info --config " + Quote(configPath) + " --json");
        }

        public static EmbeddedRelayLocalInfo ParseLocalInfo(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Relay CLI returned empty JSON.");
            }

            try
            {
                var serializer = new JavaScriptSerializer();
                var root = serializer.DeserializeObject(json) as IDictionary;
                if (root == null)
                {
                    throw new InvalidOperationException("Relay CLI returned invalid JSON.");
                }

                return new EmbeddedRelayLocalInfo
                {
                    Version = GetString(root, "version"),
                    ConfigPath = GetString(root, "configPath"),
                    StatePath = GetString(root, "statePath"),
                    ListenHost = GetString(root, "listenHost"),
                    ListenPort = GetInt(root, "listenPort"),
                    InternalListenHost = GetString(root, "internalListenHost"),
                    InternalListenPort = GetInt(root, "internalListenPort"),
                    InternalTlsRequired = GetBool(root, "internalTlsRequired"),
                    InternalRelayUrl = GetString(root, "internalRelayUrl"),
                    InternalHealthUrl = GetString(root, "internalHealthUrl"),
                    PublicHost = GetString(root, "publicHost"),
                    PublicPort = GetInt(root, "publicPort"),
                    TlsRequired = GetBool(root, "tlsRequired"),
                    CertificatePath = GetString(root, "certificatePath"),
                    CertificateSha256 = GetString(root, "certificateSha256"),
                    RelayHost = GetString(root, "relayHost"),
                    RelayPort = GetInt(root, "relayPort"),
                    RelayUrl = GetString(root, "relayUrl"),
                    HealthUrl = GetString(root, "healthUrl"),
                    Tenants = GetTenants(root),
                    Secret = GetSecret(root)
                };
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Relay CLI returned invalid JSON.", ex);
            }
        }

        private EmbeddedRelayLocalInfo Run(string arguments)
        {
            return ParseLocalInfo(RunText(arguments));
        }

        private string RunText(string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = _relayExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (var process = Process.Start(startInfo))
            {
                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException("Relay CLI failed: " + (stderr ?? string.Empty).Trim());
                }

                return stdout;
            }
        }

        private static string Quote(string value)
        {
            return WindowsCommandLineArgumentEscaper.Quote(value ?? string.Empty, true);
        }

        private static EmbeddedRelayTenant[] GetTenants(IDictionary root)
        {
            if (root == null || !root.Contains("tenants"))
            {
                return new EmbeddedRelayTenant[0];
            }

            var tenants = root["tenants"] as object[];
            if (tenants == null)
            {
                return new EmbeddedRelayTenant[0];
            }

            var result = new EmbeddedRelayTenant[tenants.Length];
            for (var index = 0; index < tenants.Length; index++)
            {
                var item = tenants[index] as IDictionary;
                result[index] = new EmbeddedRelayTenant
                {
                    TenantId = GetString(item, "tenantId"),
                    DisplayName = GetString(item, "displayName"),
                    Enabled = GetBool(item, "enabled")
                };
            }

            return result;
        }

        private static EmbeddedRelaySecret GetSecret(IDictionary root)
        {
            if (root == null || !root.Contains("secret"))
            {
                return null;
            }

            var secret = root["secret"] as IDictionary;
            if (secret == null)
            {
                return null;
            }

            return new EmbeddedRelaySecret
            {
                TenantId = GetString(secret, "tenantId"),
                Value = GetString(secret, "value"),
                Source = GetString(secret, "source")
            };
        }

        private static string GetString(IDictionary dictionary, string key)
        {
            if (dictionary == null || !dictionary.Contains(key) || dictionary[key] == null)
            {
                return null;
            }

            return Convert.ToString(dictionary[key], CultureInfo.InvariantCulture);
        }

        private static int GetInt(IDictionary dictionary, string key)
        {
            if (dictionary == null || !dictionary.Contains(key) || dictionary[key] == null)
            {
                return 0;
            }

            return Convert.ToInt32(dictionary[key], CultureInfo.InvariantCulture);
        }

        private static bool GetBool(IDictionary dictionary, string key)
        {
            if (dictionary == null || !dictionary.Contains(key) || dictionary[key] == null)
            {
                return false;
            }

            return Convert.ToBoolean(dictionary[key], CultureInfo.InvariantCulture);
        }
    }
}
