using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class EmbeddedRelayService : IEmbeddedRelayService
    {
        private const int DefaultRelayPort = 38443;
        private readonly string _appDataDirectory;
        private readonly IRemoteServiceConfigStore _configStore;
        private readonly IEmbeddedRelayBinaryLocator _binaryLocator;
        private readonly IEmbeddedRelayCliClient _cliClient;
        private readonly IEmbeddedRelayProcessController _processController;
        private readonly IEmbeddedRelayHealthClient _healthClient;
        private readonly Func<string> _publicHostResolver;
        private readonly IEmbeddedRelayCertificateValidator _certificateValidator;
        private readonly Func<int> _internalPortResolver;
        private readonly Func<RelayHostConfig, CancellationToken, Task<RelayMetadata>> _relayMetadataResolver;
        private string _lastCertificateValidationDetail;

        public EmbeddedRelayService(
            AppConfigService appConfigService,
            IRemoteServiceConfigStore configStore,
            EmbeddedRelayBinaryLocator binaryLocator = null,
            EmbeddedRelayProcessController processController = null,
            EmbeddedRelayHealthClient healthClient = null)
            : this(
                (appConfigService ?? throw new ArgumentNullException(nameof(appConfigService))).GetSharedDataDirectory(),
                configStore,
                binaryLocator ?? new EmbeddedRelayBinaryLocator(),
                null,
                processController ?? new EmbeddedRelayProcessController(),
                healthClient ?? new EmbeddedRelayHealthClient(),
                ResolvePublicHost,
                new EmbeddedRelayCertificateValidator(),
                ResolveAvailableInternalPort)
        {
        }

        internal EmbeddedRelayService(
            string appDataDirectory,
            IRemoteServiceConfigStore configStore,
            IEmbeddedRelayBinaryLocator binaryLocator,
            IEmbeddedRelayCliClient cliClient,
            IEmbeddedRelayProcessController processController,
            IEmbeddedRelayHealthClient healthClient,
            Func<string> publicHostResolver,
            IEmbeddedRelayCertificateValidator certificateValidator,
            Func<int> internalPortResolver,
            Func<RelayHostConfig, CancellationToken, Task<RelayMetadata>> relayMetadataResolver = null)
        {
            if (string.IsNullOrWhiteSpace(appDataDirectory))
            {
                throw new ArgumentException("App data directory cannot be empty.", nameof(appDataDirectory));
            }

            _appDataDirectory = appDataDirectory;
            _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
            _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));
            _cliClient = cliClient;
            _processController = processController ?? throw new ArgumentNullException(nameof(processController));
            _healthClient = healthClient ?? throw new ArgumentNullException(nameof(healthClient));
            _publicHostResolver = publicHostResolver ?? throw new ArgumentNullException(nameof(publicHostResolver));
            _certificateValidator = certificateValidator ?? throw new ArgumentNullException(nameof(certificateValidator));
            _internalPortResolver = internalPortResolver ?? throw new ArgumentNullException(nameof(internalPortResolver));
            _relayMetadataResolver = relayMetadataResolver ?? new RelayHostClient().GetMetadataAsync;
        }

        public EmbeddedRelayStatus GetStatus(RemoteServiceConfigSnapshot snapshot)
        {
            var layout = EmbeddedRelayDirectoryLayout.Create(_appDataDirectory);
            if (snapshot == null ||
                string.IsNullOrWhiteSpace(snapshot.EmbeddedRelayConfigPath) ||
                string.IsNullOrWhiteSpace(snapshot.EmbeddedRelayStatePath))
            {
                return BuildStatus(snapshot, layout, EmbeddedRelayRunState.NotConfigured, "Embedded Relay is not configured.");
            }

            if (_processController.IsRunning(BuildProcessIdentity(snapshot)))
            {
                return BuildStatus(snapshot, layout, EmbeddedRelayRunState.Running, "Embedded Relay is running.");
            }

            return BuildStatus(snapshot, layout, EmbeddedRelayRunState.Stopped, "Embedded Relay is stopped.");
        }

        public async Task<EmbeddedRelayStartResult> StartAsync(RemoteServiceConfigSnapshot snapshot, CancellationToken cancellationToken)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            cancellationToken.ThrowIfCancellationRequested();
            RemoteServiceConfigPlanner.EnsureDefaults(snapshot, Environment.MachineName);
            if (_processController.IsRunning(BuildProcessIdentity(snapshot)))
            {
                return new EmbeddedRelayStartResult
                {
                    Success = true,
                    Status = GetStatus(snapshot)
                };
            }

            _lastCertificateValidationDetail = null;
            var relayExecutablePath = _binaryLocator.Resolve();
            var layout = EmbeddedRelayDirectoryLayout.Create(_appDataDirectory);
            Directory.CreateDirectory(layout.RootDirectory);

            var publicHost = string.IsNullOrWhiteSpace(snapshot.EmbeddedRelayPublicHostOverride)
                ? _publicHostResolver()
                : snapshot.EmbeddedRelayPublicHostOverride.Trim();
            if (string.IsNullOrWhiteSpace(publicHost))
            {
                return new EmbeddedRelayStartResult
                {
                    Success = false,
                    Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.NotConfigured, "无法确定本机局域网地址。"),
                    ErrorMessage = "无法确定本机局域网地址。"
                };
            }

            // Existing realtime clients reconnect to this endpoint after a helper recovery.
            var internalPort = snapshot.EmbeddedRelayInternalPort > 0 && snapshot.EmbeddedRelayInternalPort <= 65535 &&
                snapshot.EmbeddedRelayInternalPort != DefaultRelayPort
                ? snapshot.EmbeddedRelayInternalPort
                : _internalPortResolver();
            if (internalPort <= 0 || internalPort > 65535 || internalPort == DefaultRelayPort)
            {
                return BuildInvalidInternalEndpointResult(snapshot, layout);
            }

            var publicPort = snapshot.EmbeddedRelayPublicPortOverride > 0 &&
                snapshot.EmbeddedRelayPublicPortOverride <= 65535
                ? snapshot.EmbeddedRelayPublicPortOverride
                : DefaultRelayPort;
            var cliClient = _cliClient ?? new EmbeddedRelayCliClient(relayExecutablePath);
            var initInfo = cliClient.Init(
                layout.ConfigPath,
                layout.StatePath,
                publicHost,
                DefaultRelayPort,
                publicPort,
                internalPort);
            if (!IsLoopbackHost(initInfo == null ? null : initInfo.ListenHost) &&
                (initInfo == null || !initInfo.TlsRequired))
            {
                var message = SharedLocalization.Text(
                    "RemoteRelay.Error.NonLoopbackRequiresTls",
                    "Embedded Remote Service requires TLS for local-network access.");
                return new EmbeddedRelayStartResult
                {
                    Success = false,
                    Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, message),
                    ErrorMessage = message
                };
            }

            if (!IsLoopbackHost(initInfo == null ? null : initInfo.ListenHost) &&
                !IsValidSha256(initInfo == null ? null : initInfo.CertificateSha256))
            {
                var message = SharedLocalization.Text(
                    "RemoteRelay.Error.InvalidCertificateFingerprint",
                    "Embedded Remote Service did not return a valid certificate fingerprint.");
                return new EmbeddedRelayStartResult
                {
                    Success = false,
                    Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, message),
                    ErrorMessage = message
                };
            }

            var publicListenerIsLoopback = IsLoopbackHost(initInfo == null ? null : initInfo.ListenHost);
            var internalEndpointConfigured = HasInternalEndpoint(initInfo);
            if ((!publicListenerIsLoopback || internalEndpointConfigured) && !IsValidInternalEndpoint(initInfo, internalPort))
            {
                return BuildInvalidInternalEndpointResult(snapshot, layout);
            }

            string certificateSha256 = null;
            if (initInfo != null && initInfo.TlsRequired)
            {
                try
                {
                    certificateSha256 = _certificateValidator.Validate(
                        initInfo.CertificatePath,
                        initInfo.CertificateSha256);
                }
                catch (Exception ex)
                {
                    _lastCertificateValidationDetail = RelaySecretRedactor.Redact(ex.Message);
                    var message = SharedLocalization.Text(
                        "RemoteRelay.Error.CertificateValidationFailed",
                        "Embedded Remote Service certificate validation failed.");
                    return new EmbeddedRelayStartResult
                    {
                        Success = false,
                        Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, message),
                        ErrorMessage = message
                    };
                }
            }

            var tenantInfo = cliClient.EnsureTenant(layout.ConfigPath, "Local") ?? initInfo;
            var tenant = FirstEnabledTenant(tenantInfo);
            if (tenant == null)
            {
                return new EmbeddedRelayStartResult
                {
                    Success = false,
                    Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, "Embedded Relay did not return an enabled tenant."),
                    ErrorMessage = "Embedded Relay did not return an enabled tenant."
                };
            }

            var secret = tenantInfo.Secret;
            if ((secret == null || string.IsNullOrWhiteSpace(secret.Value)) &&
                string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret))
            {
                secret = cliClient.RotateTenantSecret(layout.ConfigPath, tenant.TenantId);
                if (secret == null ||
                    string.IsNullOrWhiteSpace(secret.Value) ||
                    !string.Equals(secret.TenantId, tenant.TenantId, StringComparison.Ordinal))
                {
                    return new EmbeddedRelayStartResult
                    {
                        Success = false,
                        Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, "Embedded Relay did not return a tenant secret."),
                        ErrorMessage = "Embedded Relay did not return a tenant secret."
                    };
                }
            }

            if (secret != null && !string.IsNullOrWhiteSpace(secret.Value))
            {
                _configStore.ProtectAccessSecret(snapshot, secret.Value);
            }
            else if (string.IsNullOrWhiteSpace(snapshot.EncryptedAccessSecret))
            {
                return new EmbeddedRelayStartResult
                {
                    Success = false,
                    Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, "Embedded Relay did not return a tenant secret."),
                    ErrorMessage = "Embedded Relay did not return a tenant secret."
                };
            }

            snapshot.RemoteServiceMode = "Embedded";
            snapshot.EmbeddedRelayEnabled = true;
            snapshot.RelayHost = tenantInfo.RelayHost;
            snapshot.RelayPort = tenantInfo.RelayPort;
            snapshot.UseTls = tenantInfo.TlsRequired;
            snapshot.EmbeddedRelayCertificateSha256 = certificateSha256;
            if (IsValidInternalEndpoint(initInfo, internalPort))
            {
                snapshot.EmbeddedRelayInternalHost = initInfo.InternalListenHost.Trim();
                snapshot.EmbeddedRelayInternalPort = initInfo.InternalListenPort;
                snapshot.EmbeddedRelayInternalUseTls = initInfo.InternalTlsRequired;
            }
            else
            {
                snapshot.EmbeddedRelayInternalHost = "localhost";
                snapshot.EmbeddedRelayInternalPort = initInfo.ListenPort;
                snapshot.EmbeddedRelayInternalUseTls = initInfo.TlsRequired;
            }
            snapshot.TenantId = tenant.TenantId;
            snapshot.EmbeddedRelayConfigPath = layout.ConfigPath;
            snapshot.EmbeddedRelayStatePath = layout.StatePath;

            // Persist newly issued credentials before a helper crash can lose them.
            if (_configStore is SharedRelayConfigStore)
            {
                _configStore.Save(snapshot);
            }

            var processIdentity = _processController.Start(
                relayExecutablePath,
                layout.ConfigPath,
                layout.RootDirectory,
                layout.StdoutLogPath,
                layout.StderrLogPath);
            snapshot.EmbeddedRelayProcessId = processIdentity.ProcessId;
            snapshot.EmbeddedRelayProcessExecutablePath = processIdentity.ExecutablePath;
            snapshot.EmbeddedRelayProcessStartTimeUtcTicks = processIdentity.StartTimeUtcTicks;
            snapshot.EmbeddedRelayLastStartedAt = DateTime.UtcNow;

            bool healthy;
            try
            {
                if (_configStore is SharedRelayConfigStore)
                {
                    _configStore.Save(snapshot);
                }

                healthy = await _healthClient.WaitUntilHealthyAsync(
                    BuildInternalHealthUrl(initInfo),
                    TimeSpan.FromSeconds(10),
                    cancellationToken).ConfigureAwait(false);
                if (healthy)
                {
                    await RefreshRelayIdentityPinsAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    _configStore.Save(snapshot);
                }
            }
            catch
            {
                CleanupStartedProcess(processIdentity, snapshot);
                throw;
            }

            if (!healthy)
            {
                CleanupStartedProcess(processIdentity, snapshot);
                return new EmbeddedRelayStartResult
                {
                    Success = false,
                    Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, "Embedded Relay health check failed."),
                    ErrorMessage = "Embedded Relay health check failed."
                };
            }

            return new EmbeddedRelayStartResult
            {
                Success = true,
                Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Running, "Embedded Relay is running."),
                TenantSecret = secret == null ? null : secret.Value
            };
        }

        private async Task RefreshRelayIdentityPinsAsync(
            RemoteServiceConfigSnapshot snapshot,
            CancellationToken cancellationToken)
        {
            var config = RemoteServiceConfigPlanner.BuildRelayHostConfig(snapshot, null);
            config.RelaySigningPublicKey = null;
            config.RelaySigningKeyFingerprint = null;
            var metadata = await _relayMetadataResolver(config, cancellationToken).ConfigureAwait(false);
            if (metadata == null ||
                string.IsNullOrWhiteSpace(metadata.SigningPublicKey) ||
                string.IsNullOrWhiteSpace(metadata.SigningKeyFingerprint))
            {
                throw new InvalidOperationException("Embedded Relay did not return a signing identity.");
            }

            if ((!string.IsNullOrWhiteSpace(snapshot.RelaySigningPublicKey) ||
                 !string.IsNullOrWhiteSpace(snapshot.RelaySigningKeyFingerprint)) &&
                (!string.Equals(snapshot.RelaySigningPublicKey, metadata.SigningPublicKey, StringComparison.Ordinal) ||
                 !string.Equals(snapshot.RelaySigningKeyFingerprint, metadata.SigningKeyFingerprint, StringComparison.Ordinal)))
            {
                snapshot.LastRegisteredAt = null;
            }

            snapshot.RelaySigningPublicKey = metadata.SigningPublicKey;
            snapshot.RelaySigningKeyFingerprint = metadata.SigningKeyFingerprint;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _configStore.Load() ?? new RemoteServiceConfigSnapshot();
            var identity = BuildProcessIdentity(snapshot);
            if (identity != null)
            {
                _processController.Stop(identity);
            }

            ClearProcessIdentity(snapshot);
            _configStore.Save(snapshot);
            return Task.CompletedTask;
        }

        public EmbeddedRelayDiagnosticInfo GetDiagnostics(RemoteServiceConfigSnapshot snapshot)
        {
            var status = GetStatus(snapshot);
            var lines = new List<string>
            {
                "state=" + status.State,
                "PID=" + status.ProcessId,
                "configPath=" + status.ConfigPath,
                "statePath=" + status.StatePath,
                "relayHost=" + status.RelayHost,
                "relayPort=" + status.RelayPort,
                "tlsRequired=" + status.TlsRequired,
                "relayUrl=" + status.RelayUrl,
                "internalHost=" + status.InternalHost,
                "internalPort=" + status.InternalPort,
                "internalTlsRequired=" + status.InternalTlsRequired,
                "healthUrl=" + status.HealthUrl,
                "tenantId=" + status.TenantId,
                "stdoutLogPath=" + status.StdoutLogPath,
                "stderrLogPath=" + status.StderrLogPath
            };
            if (!string.IsNullOrWhiteSpace(status.CertificateSha256))
            {
                lines.Add("certificateSha256=" + status.CertificateSha256.Trim().ToLowerInvariant());
            }

            if (!string.IsNullOrWhiteSpace(_lastCertificateValidationDetail))
            {
                lines.Add("certificateValidationDetail=" + _lastCertificateValidationDetail);
            }

            return new EmbeddedRelayDiagnosticInfo
            {
                Status = status,
                Lines = lines
            };
        }

        private static EmbeddedRelayTenant FirstEnabledTenant(EmbeddedRelayLocalInfo info)
        {
            return info == null || info.Tenants == null
                ? null
                : info.Tenants.FirstOrDefault(item => item != null && item.Enabled);
        }

        private static string ResolvePublicHost()
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (networkInterface.OperationalStatus != OperationalStatus.Up)
                {
                    continue;
                }

                var properties = networkInterface.GetIPProperties();
                foreach (var addressInfo in properties.UnicastAddresses)
                {
                    var address = addressInfo.Address;
                    if (address.AddressFamily != AddressFamily.InterNetwork)
                    {
                        continue;
                    }

                    var text = address.ToString();
                    if (IPAddress.IsLoopback(address) ||
                        text.StartsWith("127.", StringComparison.Ordinal) ||
                        text.StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    return text;
                }
            }

            return null;
        }

        private static bool IsLoopbackHost(string host)
        {
            var normalized = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
            if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            IPAddress address;
            return IPAddress.TryParse(normalized, out address) && IPAddress.IsLoopback(address);
        }

        private static bool IsValidSha256(string value)
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            if (normalized == null || normalized.Length != 64)
            {
                return false;
            }

            foreach (var character in normalized)
            {
                if ((character < '0' || character > '9') &&
                    (character < 'a' || character > 'f') &&
                    (character < 'A' || character > 'F'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HasInternalEndpoint(EmbeddedRelayLocalInfo info)
        {
            return info != null &&
                (!string.IsNullOrWhiteSpace(info.InternalListenHost) ||
                 info.InternalListenPort != 0 ||
                 info.InternalTlsRequired ||
                 !string.IsNullOrWhiteSpace(info.InternalRelayUrl) ||
                 !string.IsNullOrWhiteSpace(info.InternalHealthUrl));
        }

        private static bool IsValidInternalEndpoint(EmbeddedRelayLocalInfo info, int expectedPort)
        {
            if (info == null ||
                !string.Equals(info.InternalListenHost == null ? null : info.InternalListenHost.Trim(), "127.0.0.1", StringComparison.Ordinal) ||
                info.InternalListenPort <= 0 ||
                info.InternalListenPort > 65535 ||
                info.InternalListenPort == info.ListenPort ||
                info.InternalTlsRequired ||
                (expectedPort > 0 && info.InternalListenPort != expectedPort))
            {
                return false;
            }

            var relayUrl = "http://127.0.0.1:" + info.InternalListenPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Equals(info.InternalRelayUrl, relayUrl, StringComparison.Ordinal) &&
                string.Equals(info.InternalHealthUrl, relayUrl + "/health", StringComparison.Ordinal);
        }

        private static string BuildInternalHealthUrl(EmbeddedRelayLocalInfo info)
        {
            if (IsValidInternalEndpoint(info, 0))
            {
                return info.InternalHealthUrl;
            }

            var scheme = info != null && info.TlsRequired ? "https" : "http";
            var port = info == null ? 0 : info.ListenPort;
            return scheme + "://localhost:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/health";
        }

        private static int ResolveAvailableInternalPort()
        {
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                try
                {
                    listener.Start();
                    var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    if (port != DefaultRelayPort)
                    {
                        return port;
                    }
                }
                finally
                {
                    listener.Stop();
                }
            }

            throw new InvalidOperationException("Could not select a distinct Embedded Relay internal port.");
        }

        private EmbeddedRelayStartResult BuildInvalidInternalEndpointResult(
            RemoteServiceConfigSnapshot snapshot,
            EmbeddedRelayDirectoryLayout layout)
        {
            var message = SharedLocalization.Text(
                "RemoteRelay.Error.InvalidInternalEndpoint",
                "Embedded Remote Service did not return a safe loopback endpoint.");
            return new EmbeddedRelayStartResult
            {
                Success = false,
                Status = BuildStatus(snapshot, layout, EmbeddedRelayRunState.Failed, message),
                ErrorMessage = message
            };
        }

        private static EmbeddedRelayProcessIdentity BuildProcessIdentity(RemoteServiceConfigSnapshot snapshot)
        {
            return snapshot == null || snapshot.EmbeddedRelayProcessId <= 0
                ? null
                : new EmbeddedRelayProcessIdentity(
                    snapshot.EmbeddedRelayProcessId,
                    snapshot.EmbeddedRelayProcessExecutablePath,
                    snapshot.EmbeddedRelayProcessStartTimeUtcTicks);
        }

        private static void ClearProcessIdentity(RemoteServiceConfigSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            snapshot.EmbeddedRelayEnabled = false;
            snapshot.EmbeddedRelayProcessId = 0;
            snapshot.EmbeddedRelayProcessExecutablePath = null;
            snapshot.EmbeddedRelayProcessStartTimeUtcTicks = 0;
        }

        private void CleanupStartedProcess(
            EmbeddedRelayProcessIdentity identity,
            RemoteServiceConfigSnapshot snapshot)
        {
            try
            {
                _processController.Stop(identity);
            }
            finally
            {
                ClearProcessIdentity(snapshot);
                if (_configStore is SharedRelayConfigStore)
                {
                    _configStore.Save(snapshot);
                }
            }
        }

        private EmbeddedRelayStatus BuildStatus(
            RemoteServiceConfigSnapshot snapshot,
            EmbeddedRelayDirectoryLayout layout,
            EmbeddedRelayRunState state,
            string message)
        {
            var relayHost = snapshot == null ? null : snapshot.RelayHost;
            var relayPort = snapshot == null ? 0 : snapshot.RelayPort;
            var tlsRequired = snapshot != null && snapshot.UseTls;
            var relayUrl = BuildRelayUrl(relayHost, relayPort, tlsRequired);
            return new EmbeddedRelayStatus
            {
                State = state,
                Message = message,
                RelayHost = relayHost,
                RelayPort = relayPort,
                TlsRequired = tlsRequired,
                CertificateSha256 = snapshot == null ? null : snapshot.EmbeddedRelayCertificateSha256,
                InternalHost = snapshot == null ? null : snapshot.EmbeddedRelayInternalHost,
                InternalPort = snapshot == null ? 0 : snapshot.EmbeddedRelayInternalPort,
                InternalTlsRequired = snapshot != null && snapshot.EmbeddedRelayInternalUseTls,
                RelayUrl = relayUrl,
                HealthUrl = snapshot == null ||
                    string.IsNullOrWhiteSpace(snapshot.EmbeddedRelayInternalHost) ||
                    snapshot.EmbeddedRelayInternalPort <= 0
                    ? null
                    : (snapshot.EmbeddedRelayInternalUseTls ? "https://" : "http://") +
                        snapshot.EmbeddedRelayInternalHost.Trim() + ":" +
                        snapshot.EmbeddedRelayInternalPort.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "/health",
                TenantId = snapshot == null ? null : snapshot.TenantId,
                ProcessId = snapshot == null ? 0 : snapshot.EmbeddedRelayProcessId,
                ConfigPath = string.IsNullOrWhiteSpace(snapshot == null ? null : snapshot.EmbeddedRelayConfigPath)
                    ? layout.ConfigPath
                    : snapshot.EmbeddedRelayConfigPath,
                StatePath = string.IsNullOrWhiteSpace(snapshot == null ? null : snapshot.EmbeddedRelayStatePath)
                    ? layout.StatePath
                    : snapshot.EmbeddedRelayStatePath,
                StdoutLogPath = layout.StdoutLogPath,
                StderrLogPath = layout.StderrLogPath
            };
        }

        private static string BuildRelayUrl(string host, int port, bool tlsRequired)
        {
            if (string.IsNullOrWhiteSpace(host) || port <= 0)
            {
                return null;
            }

            return (tlsRequired ? "https://" : "http://") + host + ":" + port;
        }
    }
}
