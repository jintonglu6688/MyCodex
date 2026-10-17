using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class EmbeddedRelayServiceTests
    {
        private const string RelayExecutablePath = @"C:\App\relay\mycodex-relay.exe";
        private const long ProcessStartTimeUtcTicks = 638877312001234567L;

        [Test]
        public void ResolveAvailableInternalPort_ReturnsBindableDistinctLoopbackPort()
        {
            var method = typeof(EmbeddedRelayService).GetMethod(
                "ResolveAvailableInternalPort",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            var port = (int)method.Invoke(null, null);
            Assert.That(port, Is.InRange(1, 65535));
            Assert.That(port, Is.Not.EqualTo(38443));
            var listener = new TcpListener(IPAddress.Loopback, port);
            try
            {
                Assert.That(() => listener.Start(), Throws.Nothing);
            }
            finally
            {
                listener.Stop();
            }
        }

        [Test]
        public async Task StartAsync_WhenTenantSecretCreated_ProtectsSecretAndSavesConfig()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(),
                EnsureTenantInfo = BuildLocalInfo(
                    new EmbeddedRelaySecret
                    {
                        TenantId = "tenant_local",
                        Value = "tenant-secret-value",
                        Source = "created"
                    })
            };
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var healthClient = new FakeEmbeddedRelayHealthClient { Healthy = true };
            var service = CreateService(configStore, cliClient, processController, healthClient, () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot();

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(result.TenantSecret, Is.EqualTo("tenant-secret-value"));
            Assert.That(configStore.ProtectedSecret, Is.EqualTo("tenant-secret-value"));
            Assert.That(configStore.SaveCount, Is.EqualTo(1));
            Assert.That(snapshot.RemoteServiceMode, Is.EqualTo("Embedded"));
            Assert.That(snapshot.RelayHost, Is.EqualTo("192.168.1.50"));
            Assert.That(snapshot.RelayPort, Is.EqualTo(38443));
            Assert.That(snapshot.UseTls, Is.True);
            Assert.That(snapshot.EmbeddedRelayCertificateSha256, Is.EqualTo(new string('a', 64)));
            Assert.That(snapshot.EmbeddedRelayInternalHost, Is.EqualTo("127.0.0.1"));
            Assert.That(snapshot.EmbeddedRelayInternalPort, Is.EqualTo(39221));
            Assert.That(snapshot.EmbeddedRelayInternalUseTls, Is.False);
            Assert.That(snapshot.TenantId, Is.EqualTo("tenant_local"));
            Guid parsedHostId;
            Assert.That(Guid.TryParseExact(snapshot.HostId, "D", out parsedHostId), Is.True);
            Assert.That(snapshot.HostId, Is.EqualTo(snapshot.HostId.ToLowerInvariant()));
            Assert.That(snapshot.ThisPcName, Is.Not.Empty);
            Assert.That(snapshot.EmbeddedRelayConfigPath, Does.EndWith("relay-config.json"));
            Assert.That(snapshot.EmbeddedRelayStatePath, Does.EndWith("relay-state.db"));
            Assert.That(snapshot.EmbeddedRelayProcessId, Is.EqualTo(4242));
            Assert.That(snapshot.EmbeddedRelayProcessExecutablePath, Is.EqualTo(Path.GetFullPath(RelayExecutablePath)));
            Assert.That(snapshot.EmbeddedRelayProcessStartTimeUtcTicks, Is.EqualTo(ProcessStartTimeUtcTicks));
            Assert.That(snapshot.EmbeddedRelayLastStartedAt, Is.Not.Null);
            Assert.That(processController.StartedConfigPath, Is.EqualTo(snapshot.EmbeddedRelayConfigPath));
            Assert.That(cliClient.RequestedListenPort, Is.EqualTo(38443));
            Assert.That(cliClient.RequestedPublicPort, Is.EqualTo(38443));
            Assert.That(cliClient.RequestedInternalPort, Is.EqualTo(39221));
            Assert.That(cliClient.RotateTenantSecretCallCount, Is.EqualTo(0));
            Assert.That(healthClient.LastHealthUrl, Is.EqualTo("http://127.0.0.1:39221/health"));
        }

        [Test]
        public async Task StartAsync_WhenExistingTenantHasNoRecoverableSecret_RotatesAndProtectsSecret()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(),
                EnsureTenantInfo = BuildLocalInfo(),
                RotatedSecret = new EmbeddedRelaySecret
                {
                    TenantId = "tenant_local",
                    Value = "rotated-secret-value",
                    Source = "rotated"
                }
            };
            var service = CreateService(
                configStore,
                cliClient,
                new FakeEmbeddedRelayProcessController { NextProcessId = 4242 },
                new FakeEmbeddedRelayHealthClient { Healthy = true },
                () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "Embedded",
                ExternalProfile = new RemoteServiceProfileSnapshot
                {
                    RelayHost = "relay.example.com",
                    RelayPort = 443,
                    UseTls = true,
                    TenantId = "tenant_external",
                    EncryptedAccessSecret = "protected-external"
                }
            };

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(result.TenantSecret, Is.EqualTo("rotated-secret-value"));
            Assert.That(cliClient.RotateTenantSecretCallCount, Is.EqualTo(1));
            Assert.That(cliClient.RotatedTenantId, Is.EqualTo("tenant_local"));
            Assert.That(configStore.ProtectedSecret, Is.EqualTo("rotated-secret-value"));
            Assert.That(snapshot.ExternalProfile.RelayHost, Is.EqualTo("relay.example.com"));
            Assert.That(snapshot.ExternalProfile.TenantId, Is.EqualTo("tenant_external"));
            Assert.That(snapshot.ExternalProfile.EncryptedAccessSecret, Is.EqualTo("protected-external"));
        }

        [Test]
        public async Task StartAsync_RecoveryPreservesInternalEndpointUsedByExistingRealtimeClients()
        {
            var info = BuildLocalInfo();
            info.InternalListenPort = 45678;
            info.InternalRelayUrl = "http://127.0.0.1:45678";
            info.InternalHealthUrl = info.InternalRelayUrl + "/health";
            var cli = new FakeEmbeddedRelayCliClient { InitInfo = info, EnsureTenantInfo = info };
            var health = new FakeEmbeddedRelayHealthClient { Healthy = true };
            var service = CreateService(new FakeRemoteServiceConfigStore(), cli,
                new FakeEmbeddedRelayProcessController { NextProcessId = 4242 }, health, () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot
            {
                EmbeddedRelayInternalPort = 45678,
                EncryptedAccessSecret = "preserved-secret"
            };

            var result = await service.StartAsync(snapshot, CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(cli.RequestedInternalPort, Is.EqualTo(45678));
            Assert.That(health.LastHealthUrl, Is.EqualTo("http://127.0.0.1:45678/health"));
            Assert.That(cli.RotateTenantSecretCallCount, Is.Zero);
        }

        [Test]
        public async Task StartAsync_WhenExistingTenantHasSavedSecret_DoesNotRotateSecret()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(),
                EnsureTenantInfo = BuildLocalInfo()
            };
            var service = CreateService(
                configStore,
                cliClient,
                new FakeEmbeddedRelayProcessController { NextProcessId = 4242 },
                new FakeEmbeddedRelayHealthClient { Healthy = true },
                () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "Embedded",
                EncryptedAccessSecret = "protected-existing-secret"
            };

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(cliClient.RotateTenantSecretCallCount, Is.EqualTo(0));
            Assert.That(configStore.ProtectedSecret, Is.Null);
            Assert.That(snapshot.EncryptedAccessSecret, Is.EqualTo("protected-existing-secret"));
        }

        [Test]
        public async Task StartAsync_EmbeddedPublicHostOverrideIsPublishedInsteadOfDetectedLanAddress()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(publicHost: "a.jtl.world", publicPort: 443),
                EnsureTenantInfo = BuildLocalInfo(
                    new EmbeddedRelaySecret
                    {
                        TenantId = "tenant_local",
                        Value = "tenant-secret-value",
                        Source = "created"
                    },
                    publicHost: "a.jtl.world",
                    publicPort: 443)
            };
            var service = CreateService(
                configStore,
                cliClient,
                new FakeEmbeddedRelayProcessController { NextProcessId = 4242 },
                new FakeEmbeddedRelayHealthClient { Healthy = true },
                () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RemoteServiceMode = "Embedded",
                RelayHost = "a.jtl.world",
                RelayPort = 443,
                EmbeddedRelayPublicHostOverride = "a.jtl.world",
                EmbeddedRelayPublicPortOverride = 443
            };

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(cliClient.RequestedPublicHost, Is.EqualTo("a.jtl.world"));
            Assert.That(cliClient.RequestedListenPort, Is.EqualTo(38443));
            Assert.That(cliClient.RequestedPublicPort, Is.EqualTo(443));
            Assert.That(snapshot.RelayHost, Is.EqualTo("a.jtl.world"));
            Assert.That(snapshot.RelayPort, Is.EqualTo(443));
            Assert.That(snapshot.EmbeddedRelayPublicHostOverride, Is.EqualTo("a.jtl.world"));
            Assert.That(snapshot.EmbeddedRelayPublicPortOverride, Is.EqualTo(443));
        }

        [Test]
        public async Task StartAsync_RefreshesEmbeddedRelayIdentityPinsOverLoopback()
        {
            RelayHostConfig metadataConfig = null;
            var configStore = new FakeRemoteServiceConfigStore();
            var service = CreateService(
                configStore,
                new FakeEmbeddedRelayCliClient
                {
                    InitInfo = BuildLocalInfo(),
                    EnsureTenantInfo = BuildLocalInfo(
                        new EmbeddedRelaySecret
                        {
                            TenantId = "tenant_local",
                            Value = "tenant-secret-value",
                            Source = "created"
                        })
                },
                new FakeEmbeddedRelayProcessController { NextProcessId = 4242 },
                new FakeEmbeddedRelayHealthClient { Healthy = true },
                () => "192.168.1.50",
                relayMetadataResolver: (config, cancellationToken) =>
                {
                    metadataConfig = config;
                    return Task.FromResult(new RelayMetadata
                    {
                        SigningPublicKey = "current-relay-key",
                        SigningKeyFingerprint = "current-relay-fingerprint"
                    });
                });
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RelaySigningPublicKey = "stale-relay-key",
                RelaySigningKeyFingerprint = "stale-relay-fingerprint",
                LastRegisteredAt = DateTime.UtcNow
            };

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(metadataConfig.ConnectionHost, Is.EqualTo("127.0.0.1"));
            Assert.That(metadataConfig.ConnectionPort, Is.EqualTo(39221));
            Assert.That(metadataConfig.ConnectionTlsRequired, Is.False);
            Assert.That(metadataConfig.RelaySigningPublicKey, Is.Null);
            Assert.That(metadataConfig.RelaySigningKeyFingerprint, Is.Null);
            Assert.That(snapshot.RelaySigningPublicKey, Is.EqualTo("current-relay-key"));
            Assert.That(snapshot.RelaySigningKeyFingerprint, Is.EqualTo("current-relay-fingerprint"));
            Assert.That(snapshot.LastRegisteredAt, Is.Null);
            Assert.That(configStore.SavedSnapshot, Is.SameAs(snapshot));
        }

        [Test]
        public async Task StartAsync_WhenEmbeddedRelayIdentityIsUnchanged_PreservesRegistration()
        {
            var registeredAt = DateTime.UtcNow;
            var configStore = new FakeRemoteServiceConfigStore();
            var service = CreateStartableService(
                configStore,
                new FakeEmbeddedRelayProcessController { NextProcessId = 4242 },
                new FakeEmbeddedRelayHealthClient { Healthy = true });
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RelaySigningPublicKey = "relay-signing-key",
                RelaySigningKeyFingerprint = "relay-signing-fingerprint",
                LastRegisteredAt = registeredAt
            };

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(snapshot.LastRegisteredAt, Is.EqualTo(registeredAt));
        }

        [Test]
        public void StartAsync_WhenEmbeddedRelayMetadataFails_CleansUpAndPreservesPins()
        {
            var expected = new InvalidOperationException("metadata failed");
            var configStore = new FakeRemoteServiceConfigStore();
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var service = CreateService(
                configStore,
                new FakeEmbeddedRelayCliClient
                {
                    InitInfo = BuildLocalInfo(),
                    EnsureTenantInfo = BuildLocalInfo(
                        new EmbeddedRelaySecret
                        {
                            TenantId = "tenant_local",
                            Value = "tenant-secret-value",
                            Source = "created"
                        })
                },
                processController,
                new FakeEmbeddedRelayHealthClient { Healthy = true },
                () => "192.168.1.50",
                relayMetadataResolver: (config, cancellationToken) => Task.FromException<RelayMetadata>(expected));
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RelaySigningPublicKey = "existing-relay-key",
                RelaySigningKeyFingerprint = "existing-relay-fingerprint"
            };

            var actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(expected));
            AssertFailedStartCleanup(snapshot, processController);
            Assert.That(snapshot.RelaySigningPublicKey, Is.EqualTo("existing-relay-key"));
            Assert.That(snapshot.RelaySigningKeyFingerprint, Is.EqualTo("existing-relay-fingerprint"));
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public async Task StartAsync_NonLoopbackWithoutTls_FailsBeforeSideEffects()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(tlsRequired: false),
                EnsureTenantInfo = BuildLocalInfo(
                    new EmbeddedRelaySecret
                    {
                        TenantId = "tenant_local",
                        Value = "tenant-secret-value",
                        Source = "created"
                    },
                    tlsRequired: false)
            };
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var healthClient = new FakeEmbeddedRelayHealthClient { Healthy = true };
            var service = CreateService(configStore, cliClient, processController, healthClient, () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot();

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(cliClient.EnsureTenantCallCount, Is.EqualTo(0));
            Assert.That(configStore.ProtectedSecret, Is.Null);
            Assert.That(processController.StartCallCount, Is.EqualTo(0));
            Assert.That(healthClient.CallCount, Is.EqualTo(0));
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
            Assert.That(snapshot.EmbeddedRelayProcessId, Is.EqualTo(0));
        }

        [Test]
        public async Task StartAsync_NonLoopbackWithoutCertificateFingerprint_FailsBeforeSideEffects()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var initInfo = BuildLocalInfo();
            initInfo.CertificateSha256 = null;
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = initInfo,
                EnsureTenantInfo = BuildLocalInfo(new EmbeddedRelaySecret
                {
                    TenantId = "tenant_local",
                    Value = "tenant-secret-value",
                    Source = "created"
                })
            };
            var validator = new FakeEmbeddedRelayCertificateValidator();
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var healthClient = new FakeEmbeddedRelayHealthClient { Healthy = true };
            var service = CreateService(configStore, cliClient, processController, healthClient, () => "192.168.1.50", validator);

            var result = await service.StartAsync(new RemoteServiceConfigSnapshot(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(validator.CallCount, Is.EqualTo(0));
            Assert.That(cliClient.EnsureTenantCallCount, Is.EqualTo(0));
            Assert.That(configStore.ProtectedSecret, Is.Null);
            Assert.That(processController.StartCallCount, Is.EqualTo(0));
            Assert.That(healthClient.CallCount, Is.EqualTo(0));
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
        }

        [TestCase("0.0.0.0", 39221, false)]
        [TestCase("127.0.0.1", 38443, false)]
        [TestCase("127.0.0.1", 39222, false)]
        [TestCase("127.0.0.1", 39221, true)]
        [TestCase("127.0.0.1", 0, false)]
        public async Task StartAsync_InvalidInternalEndpoint_FailsBeforeSideEffects(
            string internalHost,
            int internalPort,
            bool internalTlsRequired)
        {
            var initInfo = BuildLocalInfo();
            initInfo.InternalListenHost = internalHost;
            initInfo.InternalListenPort = internalPort;
            initInfo.InternalTlsRequired = internalTlsRequired;
            var internalRelayUrl = internalPort <= 0
                ? null
                : (internalTlsRequired ? "https://" : "http://") + internalHost + ":" + internalPort;
            initInfo.InternalRelayUrl = internalRelayUrl;
            initInfo.InternalHealthUrl = internalRelayUrl == null ? null : internalRelayUrl + "/health";
            var cliClient = new FakeEmbeddedRelayCliClient { InitInfo = initInfo };
            var validator = new FakeEmbeddedRelayCertificateValidator();
            var processController = new FakeEmbeddedRelayProcessController();
            var healthClient = new FakeEmbeddedRelayHealthClient();
            var service = CreateService(
                new FakeRemoteServiceConfigStore(),
                cliClient,
                processController,
                healthClient,
                () => "192.168.1.50",
                validator);

            var result = await service.StartAsync(new RemoteServiceConfigSnapshot(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(validator.CallCount, Is.EqualTo(0));
            Assert.That(cliClient.EnsureTenantCallCount, Is.EqualTo(0));
            Assert.That(processController.StartCallCount, Is.EqualTo(0));
            Assert.That(healthClient.CallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task StartAsync_InternalEndpointUrlMismatch_FailsBeforeSideEffects()
        {
            var initInfo = BuildLocalInfo();
            initInfo.InternalHealthUrl = "http://127.0.0.1:39222/health";
            var cliClient = new FakeEmbeddedRelayCliClient { InitInfo = initInfo };
            var processController = new FakeEmbeddedRelayProcessController();
            var service = CreateService(
                new FakeRemoteServiceConfigStore(),
                cliClient,
                processController,
                new FakeEmbeddedRelayHealthClient(),
                () => "192.168.1.50");

            var result = await service.StartAsync(new RemoteServiceConfigSnapshot(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(cliClient.EnsureTenantCallCount, Is.EqualTo(0));
            Assert.That(processController.StartCallCount, Is.EqualTo(0));
        }

        [Test]
        public async Task StartAsync_CertificateMismatch_FailsBeforeTenantSecretAndProcessSideEffects()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(),
                EnsureTenantInfo = BuildLocalInfo(new EmbeddedRelaySecret
                {
                    TenantId = "tenant_local",
                    Value = "tenant-secret-value",
                    Source = "created"
                })
            };
            var validator = new FakeEmbeddedRelayCertificateValidator
            {
                Failure = new InvalidOperationException("certificate mismatch")
            };
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var healthClient = new FakeEmbeddedRelayHealthClient { Healthy = true };
            var service = CreateService(configStore, cliClient, processController, healthClient, () => "192.168.1.50", validator);

            var result = await service.StartAsync(new RemoteServiceConfigSnapshot(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(validator.CallCount, Is.EqualTo(1));
            Assert.That(cliClient.EnsureTenantCallCount, Is.EqualTo(0));
            Assert.That(configStore.ProtectedSecret, Is.Null);
            Assert.That(processController.StartCallCount, Is.EqualTo(0));
            Assert.That(healthClient.CallCount, Is.EqualTo(0));
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
            Assert.That(
                string.Join("\n", service.GetDiagnostics(new RemoteServiceConfigSnapshot()).Lines),
                Does.Contain("certificate mismatch"));
        }

        [Test]
        public async Task StartAsync_MatchingCertificatePersistsFingerprintAndUsesLoopbackHealthUrl()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(),
                EnsureTenantInfo = BuildLocalInfo(new EmbeddedRelaySecret
                {
                    TenantId = "tenant_local",
                    Value = "tenant-secret-value",
                    Source = "created"
                })
            };
            var validator = new FakeEmbeddedRelayCertificateValidator();
            var healthClient = new FakeEmbeddedRelayHealthClient { Healthy = true };
            var service = CreateService(
                configStore,
                cliClient,
                new FakeEmbeddedRelayProcessController { NextProcessId = 4242 },
                healthClient,
                () => "192.168.1.50",
                validator);
            var snapshot = new RemoteServiceConfigSnapshot
            {
                EmbeddedRelayCertificateSha256 = new string('b', 64)
            };

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(validator.CertificatePath, Is.EqualTo(@"C:\Relay\embedded-relay-cert.pem"));
            Assert.That(validator.ReportedSha256, Is.EqualTo(new string('a', 64)));
            Assert.That(snapshot.EmbeddedRelayCertificateSha256, Is.EqualTo(new string('a', 64)));
            Assert.That(healthClient.LastHealthUrl, Is.EqualTo("http://127.0.0.1:39221/health"));
        }

        [TestCase("localhost")]
        [TestCase("127.42.0.1")]
        [TestCase("::1")]
        public async Task StartAsync_LoopbackWithoutTls_CanStart(string listenHost)
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(listenHost: listenHost, tlsRequired: false),
                EnsureTenantInfo = BuildLocalInfo(
                    new EmbeddedRelaySecret
                    {
                        TenantId = "tenant_local",
                        Value = "tenant-secret-value",
                        Source = "created"
                    },
                    listenHost,
                    false)
            };
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var healthClient = new FakeEmbeddedRelayHealthClient { Healthy = true };
            var service = CreateService(configStore, cliClient, processController, healthClient, () => "192.168.1.50");

            var result = await service.StartAsync(new RemoteServiceConfigSnapshot(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.True);
            Assert.That(processController.StartCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StartAsync_WhenNoPublicHost_ReturnsUserReadableError()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient();
            var service = CreateService(
                configStore,
                cliClient,
                new FakeEmbeddedRelayProcessController(),
                new FakeEmbeddedRelayHealthClient(),
                () => null);

            var result = await service.StartAsync(new RemoteServiceConfigSnapshot(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("无法确定本机局域网地址。"));
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
            Assert.That(cliClient.InitCallCount, Is.EqualTo(0));
        }

        [Test]
        public void GetDiagnostics_DoesNotIncludeSecret()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var service = CreateService(
                configStore,
                new FakeEmbeddedRelayCliClient(),
                new FakeEmbeddedRelayProcessController { RunningProcessIds = { 4242 } },
                new FakeEmbeddedRelayHealthClient(),
                () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot
            {
                RelayHost = "192.168.1.50",
                RelayPort = 38443,
                TenantId = "tenant_local",
                EncryptedAccessSecret = "encrypted-secret-value",
                EmbeddedRelayProcessId = 4242,
                EmbeddedRelayProcessExecutablePath = RelayExecutablePath,
                EmbeddedRelayProcessStartTimeUtcTicks = ProcessStartTimeUtcTicks,
                EmbeddedRelayConfigPath = @"C:\Relay\relay-config.json",
                EmbeddedRelayStatePath = @"C:\Relay\relay-state.db",
                EmbeddedRelayCertificateSha256 = new string('c', 64)
            };

            var diagnostics = service.GetDiagnostics(snapshot);
            var text = string.Join("\n", diagnostics.Lines);

            Assert.That(diagnostics.Status.State, Is.EqualTo(EmbeddedRelayRunState.Running));
            Assert.That(text, Does.Contain("PID=4242"));
            Assert.That(text, Does.Contain("tenantId=tenant_local"));
            Assert.That(text, Does.Contain("relayHost=192.168.1.50"));
            Assert.That(diagnostics.Status.CertificateSha256, Is.EqualTo(new string('c', 64)));
            Assert.That(text, Does.Contain("certificateSha256=" + new string('c', 64)));
            Assert.That(text, Does.Not.Contain("encrypted-secret-value"));
            Assert.That(text, Does.Not.Contain("tenant-secret-value"));
            Assert.That(text, Does.Not.Contain("secret"));
        }

        [Test]
        public async Task StopAsync_WhenLastProcessMissing_StopsProcessFromSavedConfigAndClearsSnapshot()
        {
            var savedSnapshot = new RemoteServiceConfigSnapshot
            {
                EmbeddedRelayEnabled = true,
                EmbeddedRelayProcessId = 4242,
                EmbeddedRelayProcessExecutablePath = RelayExecutablePath,
                EmbeddedRelayProcessStartTimeUtcTicks = ProcessStartTimeUtcTicks
            };
            var configStore = new FakeRemoteServiceConfigStore
            {
                LoadedSnapshot = savedSnapshot
            };
            var processController = new FakeEmbeddedRelayProcessController
            {
                RunningProcessIds = { 4242 }
            };
            var service = CreateService(
                configStore,
                new FakeEmbeddedRelayCliClient(),
                processController,
                new FakeEmbeddedRelayHealthClient(),
                () => "192.168.1.50");

            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(processController.StoppedIdentities[0].ProcessId, Is.EqualTo(4242));
            Assert.That(savedSnapshot.EmbeddedRelayProcessId, Is.EqualTo(0));
            Assert.That(savedSnapshot.EmbeddedRelayProcessExecutablePath, Is.Null);
            Assert.That(savedSnapshot.EmbeddedRelayProcessStartTimeUtcTicks, Is.EqualTo(0));
            Assert.That(savedSnapshot.EmbeddedRelayEnabled, Is.False);
            Assert.That(configStore.SaveCount, Is.EqualTo(1));
            Assert.That(configStore.SavedSnapshot, Is.SameAs(savedSnapshot));
        }

        [Test]
        public async Task StopAsync_AfterSameServiceStarted_ClearsPersistedIdentity()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var cliClient = new FakeEmbeddedRelayCliClient
            {
                InitInfo = BuildLocalInfo(),
                EnsureTenantInfo = BuildLocalInfo(
                    new EmbeddedRelaySecret
                    {
                        TenantId = "tenant_local",
                        Value = "tenant-secret-value",
                        Source = "created"
                    })
            };
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var service = CreateService(
                configStore,
                cliClient,
                processController,
                new FakeEmbeddedRelayHealthClient { Healthy = true },
                () => "192.168.1.50");
            var snapshot = new RemoteServiceConfigSnapshot();
            await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);

            Assert.That(processController.StoppedIdentities[0].ProcessId, Is.EqualTo(4242));
            Assert.That(snapshot.EmbeddedRelayEnabled, Is.False);
            Assert.That(snapshot.EmbeddedRelayProcessId, Is.EqualTo(0));
            Assert.That(snapshot.EmbeddedRelayProcessExecutablePath, Is.Null);
            Assert.That(snapshot.EmbeddedRelayProcessStartTimeUtcTicks, Is.EqualTo(0));
            Assert.That(configStore.SaveCount, Is.EqualTo(2));
        }

        [Test]
        public async Task StartAsync_WhenHealthCheckFails_ClearsInMemoryIdentity()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var service = CreateStartableService(
                configStore,
                processController,
                new FakeEmbeddedRelayHealthClient { Healthy = false });
            var snapshot = new RemoteServiceConfigSnapshot();

            var result = await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false);

            Assert.That(result.Success, Is.False);
            AssertFailedStartCleanup(snapshot, processController);
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public void StartAsync_WhenHealthWaitIsCanceled_CleansUpAndPreservesCancellation()
        {
            var configStore = new FakeRemoteServiceConfigStore();
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var service = CreateStartableService(
                configStore,
                processController,
                new FakeEmbeddedRelayHealthClient { Canceled = true });
            var snapshot = new RemoteServiceConfigSnapshot();

            var exception = Assert.CatchAsync<OperationCanceledException>(
                async () => await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false));

            Assert.That(exception, Is.TypeOf<TaskCanceledException>());
            AssertFailedStartCleanup(snapshot, processController);
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public void StartAsync_WhenHealthWaitThrows_CleansUpAndRethrows()
        {
            var expected = new InvalidOperationException("health wait failed");
            var configStore = new FakeRemoteServiceConfigStore();
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var service = CreateStartableService(
                configStore,
                processController,
                new FakeEmbeddedRelayHealthClient { Failure = expected });
            var snapshot = new RemoteServiceConfigSnapshot();

            var actual = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(expected));
            AssertFailedStartCleanup(snapshot, processController);
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
        }

        [Test]
        public void StartAsync_WhenSavingIdentityFails_CleansUpAndRethrows()
        {
            var expected = new IOException("save failed");
            var configStore = new FakeRemoteServiceConfigStore { SaveFailure = expected };
            var processController = new FakeEmbeddedRelayProcessController { NextProcessId = 4242 };
            var service = CreateStartableService(
                configStore,
                processController,
                new FakeEmbeddedRelayHealthClient { Healthy = true });
            var snapshot = new RemoteServiceConfigSnapshot();

            var actual = Assert.ThrowsAsync<IOException>(
                async () => await service.StartAsync(snapshot, CancellationToken.None).ConfigureAwait(false));

            Assert.That(actual, Is.SameAs(expected));
            AssertFailedStartCleanup(snapshot, processController);
            Assert.That(configStore.SaveCount, Is.EqualTo(0));
        }

        private static EmbeddedRelayService CreateStartableService(
            FakeRemoteServiceConfigStore configStore,
            FakeEmbeddedRelayProcessController processController,
            FakeEmbeddedRelayHealthClient healthClient)
        {
            return CreateService(
                configStore,
                new FakeEmbeddedRelayCliClient
                {
                    InitInfo = BuildLocalInfo(),
                    EnsureTenantInfo = BuildLocalInfo(
                        new EmbeddedRelaySecret
                        {
                            TenantId = "tenant_local",
                            Value = "tenant-secret-value",
                            Source = "created"
                        })
                },
                processController,
                healthClient,
                () => "192.168.1.50");
        }

        private static void AssertFailedStartCleanup(
            RemoteServiceConfigSnapshot snapshot,
            FakeEmbeddedRelayProcessController processController)
        {
            Assert.That(processController.StoppedIdentities, Has.Count.EqualTo(1));
            Assert.That(processController.StoppedIdentities[0], Is.SameAs(processController.StartedIdentity));
            Assert.That(snapshot.EmbeddedRelayEnabled, Is.False);
            Assert.That(snapshot.EmbeddedRelayProcessId, Is.EqualTo(0));
            Assert.That(snapshot.EmbeddedRelayProcessExecutablePath, Is.Null);
            Assert.That(snapshot.EmbeddedRelayProcessStartTimeUtcTicks, Is.EqualTo(0));
        }

        private static EmbeddedRelayService CreateService(
            IRemoteServiceConfigStore configStore,
            IEmbeddedRelayCliClient cliClient,
            IEmbeddedRelayProcessController processController,
            IEmbeddedRelayHealthClient healthClient,
            Func<string> publicHostResolver,
            IEmbeddedRelayCertificateValidator certificateValidator = null,
            Func<RelayHostConfig, CancellationToken, Task<RelayMetadata>> relayMetadataResolver = null)
        {
            return new EmbeddedRelayService(
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "embedded-relay-tests", Guid.NewGuid().ToString("N")),
                configStore,
                new FakeEmbeddedRelayBinaryLocator(),
                cliClient,
                processController,
                healthClient,
                publicHostResolver,
                certificateValidator ?? new FakeEmbeddedRelayCertificateValidator(),
                () => 39221,
                relayMetadataResolver ?? ((config, cancellationToken) => Task.FromResult(new RelayMetadata
                {
                    SigningPublicKey = "relay-signing-key",
                    SigningKeyFingerprint = "relay-signing-fingerprint"
                })));
        }

        private static EmbeddedRelayLocalInfo BuildLocalInfo(
            EmbeddedRelaySecret secret = null,
            string listenHost = "0.0.0.0",
            bool tlsRequired = true,
            string publicHost = "192.168.1.50",
            int publicPort = 38443)
        {
            return new EmbeddedRelayLocalInfo
            {
                Version = "0.1.0-dev",
                ConfigPath = @"C:\Relay\relay-config.json",
                StatePath = @"C:\Relay\relay-state.db",
                ListenHost = listenHost,
                ListenPort = 38443,
                InternalListenHost = tlsRequired ? "127.0.0.1" : null,
                InternalListenPort = tlsRequired ? 39221 : 0,
                InternalTlsRequired = false,
                InternalRelayUrl = tlsRequired ? "http://127.0.0.1:39221" : null,
                InternalHealthUrl = tlsRequired ? "http://127.0.0.1:39221/health" : null,
                PublicHost = publicHost,
                PublicPort = publicPort,
                TlsRequired = tlsRequired,
                CertificatePath = tlsRequired ? @"C:\Relay\embedded-relay-cert.pem" : null,
                CertificateSha256 = tlsRequired ? new string('a', 64) : null,
                RelayHost = publicHost,
                RelayPort = publicPort,
                RelayUrl = (tlsRequired ? "https" : "http") + "://" + publicHost + ":" + publicPort,
                HealthUrl = (tlsRequired ? "https" : "http") + "://" + publicHost + ":" + publicPort + "/health",
                Tenants = new[]
                {
                    new EmbeddedRelayTenant
                    {
                        TenantId = "tenant_local",
                        DisplayName = "Local",
                        Enabled = true
                    }
                },
                Secret = secret
            };
        }

        private sealed class FakeRemoteServiceConfigStore : IRemoteServiceConfigStore
        {
            public int SaveCount { get; private set; }

            public string ProtectedSecret { get; private set; }

            public RemoteServiceConfigSnapshot SavedSnapshot { get; private set; }

            public RemoteServiceConfigSnapshot LoadedSnapshot { get; set; }

            public Exception SaveFailure { get; set; }

            public RemoteServiceConfigSnapshot Load()
            {
                return LoadedSnapshot ?? new RemoteServiceConfigSnapshot();
            }

            public void Save(RemoteServiceConfigSnapshot snapshot)
            {
                if (SaveFailure != null)
                {
                    throw SaveFailure;
                }

                SaveCount++;
                SavedSnapshot = snapshot;
                LoadedSnapshot = snapshot;
            }

            public void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret)
            {
                ProtectedSecret = accessSecret;
                snapshot.EncryptedAccessSecret = "protected:" + accessSecret;
                snapshot.AccessSecretSuffix = accessSecret.Substring(accessSecret.Length - 4);
            }

            public string UnprotectAccessSecret(RemoteServiceConfigSnapshot snapshot)
            {
                return ProtectedSecret;
            }

            public void Clear()
            {
            }
        }

        private sealed class FakeEmbeddedRelayBinaryLocator : IEmbeddedRelayBinaryLocator
        {
            public string Resolve()
            {
                return RelayExecutablePath;
            }
        }

        private sealed class FakeEmbeddedRelayCliClient : IEmbeddedRelayCliClient
        {
            public int InitCallCount { get; private set; }

            public EmbeddedRelayLocalInfo InitInfo { get; set; }

            public EmbeddedRelayLocalInfo EnsureTenantInfo { get; set; }

            public EmbeddedRelayLocalInfo InfoValue { get; set; }

            public EmbeddedRelaySecret RotatedSecret { get; set; }

            public int RotateTenantSecretCallCount { get; private set; }

            public string RotatedTenantId { get; private set; }

            public int EnsureTenantCallCount { get; private set; }

            public int RequestedInternalPort { get; private set; }

            public string RequestedPublicHost { get; private set; }

            public int RequestedListenPort { get; private set; }

            public int RequestedPublicPort { get; private set; }

            public EmbeddedRelayLocalInfo Init(string configPath, string statePath, string publicHost, int listenPort, int publicPort, int internalPort)
            {
                InitCallCount++;
                RequestedPublicHost = publicHost;
                RequestedListenPort = listenPort;
                RequestedPublicPort = publicPort;
                RequestedInternalPort = internalPort;
                return InitInfo;
            }

            public EmbeddedRelayLocalInfo EnsureTenant(string configPath, string name)
            {
                EnsureTenantCallCount++;
                return EnsureTenantInfo;
            }

            public EmbeddedRelaySecret RotateTenantSecret(string configPath, string tenantId)
            {
                RotateTenantSecretCallCount++;
                RotatedTenantId = tenantId;
                return RotatedSecret;
            }

            public EmbeddedRelayLocalInfo Info(string configPath)
            {
                return InfoValue;
            }
        }

        private sealed class FakeEmbeddedRelayProcessController : IEmbeddedRelayProcessController
        {
            public int NextProcessId { get; set; }

            public int StartCallCount { get; private set; }

            public string StartedConfigPath { get; private set; }

            public HashSet<int> RunningProcessIds { get; } = new HashSet<int>();

            public List<EmbeddedRelayProcessIdentity> StoppedIdentities { get; } = new List<EmbeddedRelayProcessIdentity>();

            public EmbeddedRelayProcessIdentity StartedIdentity { get; private set; }

            public EmbeddedRelayProcessIdentity Start(
                string relayExecutablePath,
                string configPath,
                string workingDirectory,
                string stdoutLogPath,
                string stderrLogPath)
            {
                StartCallCount++;
                StartedConfigPath = configPath;
                RunningProcessIds.Add(NextProcessId);
                StartedIdentity = new EmbeddedRelayProcessIdentity(
                    NextProcessId,
                    Path.GetFullPath(relayExecutablePath),
                    ProcessStartTimeUtcTicks);
                return StartedIdentity;
            }

            public void Stop(EmbeddedRelayProcessIdentity identity)
            {
                if (identity == null)
                {
                    return;
                }

                StoppedIdentities.Add(identity);
                RunningProcessIds.Remove(identity.ProcessId);
            }

            public bool IsRunning(EmbeddedRelayProcessIdentity identity)
            {
                return identity != null && RunningProcessIds.Contains(identity.ProcessId);
            }
        }

        private sealed class FakeEmbeddedRelayCertificateValidator : IEmbeddedRelayCertificateValidator
        {
            public int CallCount { get; private set; }

            public string CertificatePath { get; private set; }

            public string ReportedSha256 { get; private set; }

            public Exception Failure { get; set; }

            public string Validate(string certificatePath, string reportedSha256)
            {
                CallCount++;
                CertificatePath = certificatePath;
                ReportedSha256 = reportedSha256;
                if (Failure != null)
                {
                    throw Failure;
                }

                return reportedSha256 == null ? null : reportedSha256.Trim().ToLowerInvariant();
            }
        }

        private sealed class FakeEmbeddedRelayHealthClient : IEmbeddedRelayHealthClient
        {
            public bool Healthy { get; set; }

            public string LastHealthUrl { get; private set; }

            public int CallCount { get; private set; }

            public bool Canceled { get; set; }

            public Exception Failure { get; set; }

            public Task<bool> WaitUntilHealthyAsync(string healthUrl, TimeSpan timeout, CancellationToken cancellationToken)
            {
                CallCount++;
                LastHealthUrl = healthUrl;
                if (Canceled)
                {
                    return Task.FromCanceled<bool>(new CancellationToken(true));
                }

                if (Failure != null)
                {
                    return Task.FromException<bool>(Failure);
                }

                return Task.FromResult(Healthy);
            }
        }
    }
}
