// Compiled as MyCodex.Application.Tests.exe for existing Infrastructure friend access.
// Uses the released Relay and production host crypto/channel; phone role is a protocol client, not Android.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.RemoteRelay;
using MyCodex.Infrastructure.RemoteRelay;

internal static class ReleasedRelayRoutingProbe
{
    private static readonly CngSecureRemoteCrypto Crypto = new CngSecureRemoteCrypto();
    private static readonly SecureRelayFrameCodec Codec = new SecureRelayFrameCodec();
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    private static readonly List<string> Evidence = new List<string>();
    private static string _root;
    private static CancellationToken _token;
    private static string _stage = "initialization";

    private static int Main(string[] args)
    {
        _root = Path.GetFullPath(args[1]);
        try
        {
            using (var budget = new CancellationTokenSource(TimeSpan.FromSeconds(90)))
            {
                _token = budget.Token;
                RunAsync(Path.GetFullPath(args[0])).GetAwaiter().GetResult();
            }
            Record("PASS: released binary encrypted multi-HostId routing, sequential phone-role connections and ordinary restart.");
            return 0;
        }
        catch (Exception ex)
        {
            // Relay responses and exceptions may contain credentials: record only stage and exception type.
            Record("FAIL: " + _stage + "; " + ex.GetType().Name + "; " + ex.StackTrace);
            return 1;
        }
        finally
        {
            File.WriteAllLines(Path.Combine(_root, "result.txt"), Evidence, new UTF8Encoding(false));
        }
    }

    private static async Task RunAsync(string executable)
    {
        var configPath = Path.Combine(_root, "service", "relay.json");
        var statePath = Path.Combine(_root, "service", "relay.db");
        Directory.CreateDirectory(Path.GetDirectoryName(configPath));
        var publicPort = FreePort();
        var internalPort = FreePort();
        while (internalPort == publicPort) internalPort = FreePort();
        _stage = "loopback local init";
        var initArguments = "local init --config " + Quote(configPath) + " --state " + Quote(statePath) +
            " --listen-host 127.0.0.1 --listen-port " + publicPort +
            " --internal-listen-host 127.0.0.1 --internal-listen-port " + internalPort +
            " --public-host 127.0.0.1 --public-port " + publicPort + " --embedded-tls --json";
        var init = EmbeddedRelayCliClient.ParseLocalInfo(RunCli(executable, initArguments));
        Check(init.ListenHost == "127.0.0.1" && init.InternalListenHost == "127.0.0.1", "Loopback listeners required.");
        var certificate = new EmbeddedRelayCertificateValidator().Validate(init.CertificatePath, init.CertificateSha256);
        var cli = new EmbeddedRelayCliClient(executable);
        var tenant = cli.EnsureTenant(configPath, "Isolated routing probe");
        Check(tenant.Secret != null && !string.IsNullOrWhiteSpace(tenant.Secret.Value), "Initial tenant secret required.");
        var relayConfig = new RelayHostConfig
        {
            RelayHost = "127.0.0.1", RelayPort = publicPort, TlsRequired = true,
            ConnectionHost = "127.0.0.1", ConnectionPort = internalPort, ConnectionTlsRequired = false,
            CertificateSha256 = certificate, TenantId = tenant.Secret.TenantId, TenantSecret = tenant.Secret.Value
        };
        var originalConfigBytes = File.ReadAllBytes(configPath);
        var processes = new EmbeddedRelayProcessController();
        EmbeddedRelayProcessIdentity process = null;
        try
        {
            process = processes.Start(executable, configPath, Path.GetDirectoryName(configPath),
                Path.Combine(_root, "relay.out.log"), Path.Combine(_root, "relay.err.log"));
            using (var http = new HttpClient(new HttpClientHandler { UseProxy = false }))
            using (var identityA = new EphemeralIdentity())
            using (var identityB = new EphemeralIdentity())
            using (var phoneIdentity = new EphemeralIdentity())
            {
                http.Timeout = TimeSpan.FromSeconds(8);
                var transport = new HttpRelayTransport(http);
                var clientA = new RelayHostClient(transport, identityA);
                var clientB = new RelayHostClient(transport, identityB);
                _stage = "pinned Relay metadata";
                await WaitAsync(async () =>
                {
                    try { await clientA.GetMetadataAsync(relayConfig, _token); return true; }
                    catch (HttpRequestException) { return false; }
                });
                var originalPin = relayConfig.RelaySigningKeyFingerprint;
                var originalSigningKey = relayConfig.RelaySigningPublicKey;
                Record("PASS: pinned released Relay started; TLS certificate validated; both listeners loopback.");

                var hostA = identityA.Identity;
                var hostB = identityB.Identity;
                Check(hostA.IdentityId != hostB.IdentityId && hostA.SigningPublicKey != hostB.SigningPublicKey &&
                    hostA.AgreementPublicKey != hostB.AgreementPublicKey, "Independent host identities required.");
                _stage = "concurrent same-endpoint host enrollment";
                var enrolled = await Task.WhenAll(clientA.EnrollHostAsync(relayConfig, hostA, _token),
                    clientB.EnrollHostAsync(relayConfig, hostB, _token));
                Check(enrolled[0].HostId == hostA.IdentityId && enrolled[1].HostId == hostB.IdentityId, "Enrollment route mismatch.");
                Record("PASS: two independent HostIds and CNG identities enrolled concurrently on one endpoint.");

                var bindingsA = new JsonRemoteBindingStore(Path.Combine(_root, "target-a-bindings.json"));
                var bindingsB = new JsonRemoteBindingStore(Path.Combine(_root, "target-b-bindings.json"));
                _stage = "first host pairing";
                await PairAsync(transport, relayConfig, clientA, identityA, phoneIdentity, bindingsA, "PC · Windows");
                Check(bindingsB.ListActive(relayConfig.TenantId, hostB.IdentityId).Count == 0, "New target inherited pairing.");
                using (var a = await HostConnection.OpenAsync(transport, relayConfig, identityA, bindingsA, phoneIdentity.Identity.IdentityId))
                {
                    var aGeneration = a.Generation;
                    _stage = "host A encrypted route";
                    using (var phone = await PhoneConnection.OpenAsync(transport, relayConfig, identityA.Identity, phoneIdentity))
                    {
                        await ExchangeAsync(a, phone, "A-before-B");
                        _stage = "new target pairing while A remains connected";
                        await PairAsync(transport, relayConfig, clientB, identityB, phoneIdentity, bindingsB, "PC · WSL · Ubuntu development");
                        await ExchangeAsync(a, phone, "A-after-B-pairing");
                    }
                    await a.WaitForReconnectAsync(aGeneration);
                    Record("PASS: B first pairing did not interrupt A; each target owns its binding.");
                    using (var b = await HostConnection.OpenAsync(transport, relayConfig, identityB, bindingsB, phoneIdentity.Identity.IdentityId))
                    {
                        var bGeneration = b.Generation;
                        _stage = "sequential phone-role A to B";
                        using (var phone = await PhoneConnection.OpenAsync(transport, relayConfig, identityB.Identity, phoneIdentity))
                        {
                            var previousA = a.Requests;
                            await ExchangeAsync(b, phone, "B-after-switch");
                            Check(a.Requests == previousA, "B request reached A.");
                        }
                        await b.WaitForReconnectAsync(bGeneration);
                        _stage = "original binding reuse";
                        using (var phone = await PhoneConnection.OpenAsync(transport, relayConfig, identityA.Identity, phoneIdentity))
                        {
                            var previousB = b.Requests;
                            await ExchangeAsync(a, phone, "A-reconnected-existing-binding");
                            Check(b.Requests == previousB, "A request reached B.");
                        }
                        Record("PASS: A to B to A sequential connections; encrypted requests isolated; existing A binding reused.");
                        _stage = "A departure while B remains connected";
                        using (var phone = await PhoneConnection.OpenAsync(transport, relayConfig, identityB.Identity, phoneIdentity))
                        {
                            await ExchangeAsync(b, phone, "B-before-A-departure");
                            a.Dispose();
                            await ExchangeAsync(b, phone, "B-after-A-departure");
                            Check(processes.IsRunning(process), "A departure stopped shared Relay.");
                            var devices = await clientB.ListDevicesAsync(relayConfig, hostB.IdentityId, _token);
                            Check(devices.Any(d => d.DeviceId == phoneIdentity.Identity.IdentityId && !d.Revoked), "B binding revoked.");
                        }
                    }
                }
                _stage = "identity and tenant secret preservation";
                await clientA.GetMetadataAsync(relayConfig, _token);
                Check(relayConfig.RelaySigningKeyFingerprint == originalPin && relayConfig.RelaySigningPublicKey == originalSigningKey,
                    "Relay signing pins rotated.");
                var finalInfo = cli.Info(configPath);
                Check(finalInfo.CertificateSha256 == certificate && originalConfigBytes.SequenceEqual(File.ReadAllBytes(configPath)),
                    "Relay config or certificate changed.");
                // Reusing the original management bearer proves that adding/departing targets did not rotate it.
                await clientB.EnrollHostAsync(relayConfig, hostB, _token);
                Check(bindingsA.ListActive(relayConfig.TenantId, hostA.IdentityId).Count == 1 &&
                    bindingsB.ListActive(relayConfig.TenantId, hostB.IdentityId).Count == 1, "Bindings lost.");
                Record("PASS: A departure preserved B encrypted connection, original tenant secret, certificate and signing pins.");

                _stage = "ordinary Relay stop/init/start with original config and database";
                var originalProcess = process;
                processes.Stop(originalProcess);
                Check(!processes.IsRunning(originalProcess), "Original Relay process remained alive.");
                process = null;
                var restartInfo = EmbeddedRelayCliClient.ParseLocalInfo(RunCli(executable, initArguments));
                Check(restartInfo.ConfigPath == init.ConfigPath && restartInfo.StatePath == init.StatePath &&
                    restartInfo.ListenHost == "127.0.0.1" && restartInfo.InternalListenHost == "127.0.0.1" &&
                    restartInfo.ListenPort == publicPort && restartInfo.InternalListenPort == internalPort,
                    "Restart changed the shared service config, state or endpoints.");
                Check(new EmbeddedRelayCertificateValidator().Validate(restartInfo.CertificatePath, restartInfo.CertificateSha256) == certificate,
                    "Restart rotated the TLS certificate.");
                cli.EnsureTenant(configPath, "Isolated routing probe");
                Check(originalConfigBytes.SequenceEqual(File.ReadAllBytes(configPath)), "Restart altered Relay config.");
                process = processes.Start(executable, configPath, Path.GetDirectoryName(configPath),
                    Path.Combine(_root, "relay.out.log"), Path.Combine(_root, "relay.err.log"));
                Check(process.ProcessId != originalProcess.ProcessId || process.StartTimeUtcTicks != originalProcess.StartTimeUtcTicks,
                    "Restart did not create a fresh verified Relay process.");
                await WaitAsync(async () =>
                {
                    try { await clientA.GetMetadataAsync(relayConfig, _token); return true; }
                    catch (HttpRequestException) { return false; }
                });
                Check(relayConfig.RelaySigningKeyFingerprint == originalPin && relayConfig.RelaySigningPublicKey == originalSigningKey,
                    "Restart rotated Relay signing pins.");
                // The original bearer is reused; no secret or binding is replaced after restart.
                await clientB.EnrollHostAsync(relayConfig, hostB, _token);
                var restoredA = await clientA.ListDevicesAsync(relayConfig, hostA.IdentityId, _token);
                var restoredB = await clientB.ListDevicesAsync(relayConfig, hostB.IdentityId, _token);
                Check(restoredA.Any(d => d.DeviceId == phoneIdentity.Identity.IdentityId && !d.Revoked) &&
                    restoredB.Any(d => d.DeviceId == phoneIdentity.Identity.IdentityId && !d.Revoked), "Restart lost original Relay bindings.");
                _stage = "encrypted original A/B binding reuse after ordinary Relay restart";
                using (var restartedA = await HostConnection.OpenAsync(transport, relayConfig, identityA, bindingsA, phoneIdentity.Identity.IdentityId))
                using (var restartedB = await HostConnection.OpenAsync(transport, relayConfig, identityB, bindingsB, phoneIdentity.Identity.IdentityId))
                {
                    using (var phone = await PhoneConnection.OpenAsync(transport, relayConfig, hostA, phoneIdentity))
                        await ExchangeAsync(restartedA, phone, "A-original-binding-after-restart");
                    using (var phone = await PhoneConnection.OpenAsync(transport, relayConfig, hostB, phoneIdentity))
                        await ExchangeAsync(restartedB, phone, "B-original-binding-after-restart");
                }
                Check(originalConfigBytes.SequenceEqual(File.ReadAllBytes(configPath)), "Restarted traffic altered Relay config.");
                Record("PASS: real stop/init/start reused original config/database, certificate, signing pins, bearer and encrypted A/B bindings.");
            }
        }
        finally
        {
            if (process != null)
            {
                processes.Stop(process);
                Check(!processes.IsRunning(process), "Probe Relay process remained alive.");
                Record("PASS: probe-owned Relay stopped with PID/path/start-time verification.");
            }
            Array.Clear(originalConfigBytes, 0, originalConfigBytes.Length);
            relayConfig.TenantSecret = null;
        }
    }

    private static async Task PairAsync(HttpRelayTransport transport, RelayHostConfig config, RelayHostClient client,
        EphemeralIdentity host, EphemeralIdentity device, JsonRemoteBindingStore bindings, string name)
    {
        var workflow = new SecureRemotePairingWorkflow(client, host, bindings);
        var invite = await workflow.CreatePairingInviteAsync(config, host.Identity.IdentityId, name, _token);
        var p = invite.Package;
        using (var material = new SecretBytes())
        {
            var claim = new PairingClaim
            {
                ProtocolVersion = 1, ClaimId = Guid.NewGuid().ToString("D"), InviteId = p.InviteId,
                TenantId = p.TenantId, HostId = p.HostId, DeviceId = device.Identity.IdentityId,
                DeviceName = "Isolated phone role", Platform = "android", AppVersion = "probe",
                DeviceSigningPublicKey = Decode(device.Identity.SigningPublicKey),
                DeviceAgreementPublicKey = Decode(device.Identity.AgreementPublicKey), DeviceKeyVersion = 1,
                ClientNonce = Random(32), CreatedAt = Now()
            };
            var inviteSecret = material.Add(Decode(p.InviteSecret));
            using (var mac = new HMACSHA256(inviteSecret))
                claim.InviteProof = material.Add(mac.ComputeHash(material.Add(SecureRemoteTranscript.EncodePairingClaim(claim))));
            claim.DeviceSignature = material.Add(device.Sign(material.Add(SecureRemoteTranscript.EncodePairingClaimForSignature(claim))));
            var plaintext = material.Add(SecureRemoteTranscript.EncodePairingClaimPlaintext(claim));
            var header = new PairingClaimHeader
            {
                ProtocolVersion = 1, InviteId = p.InviteId, TenantId = p.TenantId, HostId = p.HostId,
                ClaimId = claim.ClaimId, DeviceId = claim.DeviceId, DeviceAgreementPublicKey = device.Identity.AgreementPublicKey,
                DeviceKeyVersion = 1, ClientNonce = Encode(claim.ClientNonce), CiphertextLength = plaintext.Length + 16
            };
            var aad = material.Add(SecureRemoteTranscript.EncodeVisiblePairingClaimHeader(header));
            byte[] salt, info;
            using (var hash = SHA256.Create())
            {
                salt = material.Add(hash.ComputeHash(inviteSecret));
                using (var stream = new MemoryStream())
                {
                    var packageTranscript = material.Add(SecureRemoteTranscript.EncodePairingPackage(p));
                    stream.Write(packageTranscript, 0, packageTranscript.Length);
                    var signature = material.Add(Decode(p.HostSignature));
                    stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte(0); stream.WriteByte((byte)signature.Length);
                    stream.Write(signature, 0, signature.Length); stream.Write(aad, 0, aad.Length);
                    info = material.Add(hash.ComputeHash(material.Add(stream.ToArray())));
                }
            }
            var shared = material.Add(device.DeriveAgreement(Decode(p.HostAgreementPublicKey)));
            var baseKey = material.Add(Crypto.HkdfSha256(shared, salt, info, 32));
            var claimKey = material.Add(Derive(baseKey, "claim encryption", 32));
            var nonce = Random(12);
            byte[] tag;
            var encrypted = Crypto.EncryptAesGcm(claimKey, nonce, plaintext, aad, out tag);
            await PostAsync(transport, config, "/v1/pairing/claims", new Dictionary<string, object>
            {
                { "header", WireObject(header) }, { "nonce", Encode(nonce) }, { "ciphertext", Encode(Join(encrypted, tag)) }
            });
            var opaque = (await client.ListPairingClaimsAsync(config, p.HostId, _token)).Single(c => c.ClaimId == claim.ClaimId);
            var verified = workflow.VerifyClaim(invite, opaque);
            material.Add(verified.PairingBaseKey); material.Add(verified.VerifiedPlaintext);
            Check(bindings.ListActive(config.TenantId, p.HostId).Count == 0, "Pending claim displayed as paired.");
            await workflow.ApprovePairingClaimAsync(config, verified, _token);
            Check(bindings.FindActive(config.TenantId, p.HostId, claim.DeviceId) != null, "Approved binding missing.");
        }
    }

    private static async Task ExchangeAsync(HostConnection host, PhoneConnection phone, string marker)
    {
        var previous = host.Requests;
        await phone.SendRequestAsync(marker);
        await WaitAsync(() => Task.FromResult(host.Requests == previous + 1 || host.Failure != null));
        Check(host.Failure == null, "Host channel failed.");
        await host.PublishAsync(marker);
        var json = await phone.ReadEventAsync();
        Check(json.Contains(marker), "Encrypted response marker mismatch.");
    }

    private sealed class HostConnection : IDisposable
    {
        private ClientWebSocket _socket = new ClientWebSocket();
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private SecureRelayChannel _channel;
        private HttpRelayTransport _transport;
        private RelayHostConfig _config;
        private EphemeralIdentity _identity;
        private JsonRemoteBindingStore _bindings;
        private string _deviceId;
        private Task _receive;
        private int _requests;
        private int _generation;
        private bool _disposed;
        public Exception Failure { get; private set; }
        public int Requests { get { return Volatile.Read(ref _requests); } }
        public int Generation { get { return Volatile.Read(ref _generation); } }
        public Task WaitForReconnectAsync(int generation)
        {
            return WaitAsync(() => Task.FromResult(Generation > generation || Failure != null));
        }
        public static async Task<HostConnection> OpenAsync(HttpRelayTransport transport, RelayHostConfig config,
            EphemeralIdentity identity, JsonRemoteBindingStore bindings, string deviceId)
        {
            var result = new HostConnection();
            try
            {
                result._transport = transport; result._config = config; result._identity = identity;
                result._bindings = bindings; result._deviceId = deviceId;
                await result.ConnectAsync();
                result._receive = result.ReceiveAsync();
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        private async Task ConnectAsync()
        {
            var binding = _bindings.FindActive(_config.TenantId, _identity.Identity.IdentityId, _deviceId);
            var ticket = await new SecureRelayAuthenticator(_transport, _identity).GetTicketAsync(_config,
                RelayTicketPurpose.WebSocketHost, binding.HostId, _deviceId, _lifetime.Token);
            _socket.Options.SetRequestHeader("Authorization", "Bearer " + ticket);
            await _socket.ConnectAsync(SocketUri(_config, "host", binding.HostId, _deviceId), _lifetime.Token);
            _channel = new SecureRelayChannel(_socket, _identity, _bindings, null, binding);
            Interlocked.Increment(ref _generation);
        }
        private async Task ReceiveAsync()
        {
            try
            {
                while (!_lifetime.IsCancellationRequested)
                {
                    while (!_lifetime.IsCancellationRequested)
                    {
                        var text = await ReleasedRelayRoutingProbe.ReceiveAsync(_socket, _lifetime.Token);
                        if (text == null) break;
                        await _channel.HandleAsync(text, _lifetime.Token);
                        if (Codec.Deserialize(text) is SecureRelayEnvelope) Interlocked.Increment(ref _requests);
                    }
                    _channel.Dispose(); _channel = null; _socket.Dispose();
                    // Match the production realtime client's retry after a normal peer disconnect.
                    await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
                    _socket = new ClientWebSocket();
                    await ConnectAsync();
                }
            }
            catch (Exception ex) { if (!_lifetime.IsCancellationRequested) Failure = ex; }
        }
        public async Task PublishAsync(string marker)
        {
            await WaitAsync(() => Task.FromResult((_channel != null && _channel.IsEstablished) || Failure != null));
            Check(Failure == null, "Host handshake failed.");
            await _channel.PublishAsync(new RemoteRelayEvent { EventType = "probe", Payload = new Dictionary<string, object> { { "marker", marker } } }, _token);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            if (_channel != null) _channel.Dispose();
            _socket.Abort();
            if (_receive != null) _receive.GetAwaiter().GetResult();
            _socket.Dispose();
            _lifetime.Dispose();
        }
    }

    private sealed class PhoneConnection : IDisposable
    {
        private static int _active;
        private readonly ClientWebSocket _socket = new ClientWebSocket();
        private readonly SecretBytes _material = new SecretBytes();
        private SessionHello _hello;
        private byte[] _sendKey, _receiveKey, _sendPrefix, _receivePrefix;
        private ulong _sequence, _received;
        private bool _disposed;

        public static async Task<PhoneConnection> OpenAsync(HttpRelayTransport transport, RelayHostConfig config,
            RemoteEndpointIdentity host, EphemeralIdentity device)
        {
            Check(Interlocked.Increment(ref _active) == 1, "Overlapping phone-role connections.");
            var result = new PhoneConnection();
            try
            {
                var ticket = await DeviceTicketAsync(transport, config, host.IdentityId, device);
                result._socket.Options.SetRequestHeader("Authorization", "Bearer " + ticket);
                await result._socket.ConnectAsync(SocketUri(config, "device", host.IdentityId, device.Identity.IdentityId), _token);
                await result.HandshakeAsync(config, host, device);
                return result;
            }
            catch { result.Dispose(); throw; }
        }
        private async Task HandshakeAsync(RelayHostConfig config, RemoteEndpointIdentity host, EphemeralIdentity device)
        {
            using (var ephemeral = Crypto.CreateEphemeralAgreementKey())
            {
                _hello = new SessionHello
                {
                    ProtocolVersion = 1, FrameType = SecureRelayFrameTypes.ClientHello, SessionId = Encode(Random(32)),
                    TenantId = config.TenantId, HostId = host.IdentityId, DeviceId = device.Identity.IdentityId,
                    Direction = RelayEnvelopeDirections.MobileToWindows, BindingVersion = 1, KeyVersion = 1,
                    EphemeralPublicKey = Encode(CngSecureRemoteCrypto.ExportSec1PublicKey(ephemeral)), Nonce = Encode(Random(32)), CreatedAt = Now()
                };
                _hello.Signature = Encode(device.Sign(SecureRemoteTranscript.EncodeSessionHello(_hello)));
                await SendAsync(_socket, _hello);
                var helloText = await ReceiveAsync(_socket, _token);
                if (helloText == null) Record("Diagnostic: Relay closed device before Server Hello.");
                var server = Codec.Deserialize(helloText) as SessionHello;
                Check(server != null && server.FrameType == SecureRelayFrameTypes.ServerHello && server.ProtocolVersion == 1 &&
                    server.SessionId == _hello.SessionId && server.TenantId == config.TenantId && server.HostId == host.IdentityId &&
                    server.DeviceId == device.Identity.IdentityId && server.Direction == RelayEnvelopeDirections.WindowsToMobile &&
                    server.KeyVersion == host.KeyVersion && server.BindingVersion == 1, "Server Hello route mismatch.");
                Check(Crypto.VerifyRawEcdsa(Decode(host.SigningPublicKey), SecureRemoteTranscript.EncodeServerHelloSignature(_hello, server),
                    Decode(server.Signature)), "Host signature mismatch.");
                var complete = _material.Add(SecureRemoteTranscript.EncodeCompleteSessionHello(_hello, server));
                var shared = _material.Add(Crypto.DeriveAgreement(ephemeral, Decode(server.EphemeralPublicKey)));
                byte[] salt;
                using (var hash = SHA256.Create()) salt = _material.Add(hash.ComputeHash(Join(Decode(_hello.Nonce), Decode(server.Nonce))));
                var master = _material.Add(Crypto.HkdfSha256(shared, salt, complete, 32));
                _sendKey = _material.Add(Derive(master, "android to windows key", 32));
                _receiveKey = _material.Add(Derive(master, "windows to android key", 32));
                _sendPrefix = _material.Add(Derive(master, "android to windows nonce prefix", 4));
                _receivePrefix = _material.Add(Derive(master, "windows to android nonce prefix", 4));
                var confirmation = _material.Add(Derive(master, "session confirmation", 32));
                await SendAsync(_socket, new SessionConfirmationFrame
                {
                    ProtocolVersion = 1, FrameType = SecureRelayFrameTypes.Confirmation, SessionId = _hello.SessionId,
                    TenantId = config.TenantId, HostId = host.IdentityId, DeviceId = _hello.DeviceId,
                    Direction = RelayEnvelopeDirections.MobileToWindows, SenderRole = SecureRelayRoles.Android,
                    Confirmation = Confirm(confirmation, complete, SecureRelayRoles.Android)
                });
                var confirmationText = await ReceiveAsync(_socket, _token);
                if (confirmationText == null) Record("Diagnostic: Relay closed device before host confirmation.");
                var response = Codec.Deserialize(confirmationText) as SessionConfirmationFrame;
                Check(response != null && response.ProtocolVersion == 1 && response.SessionId == _hello.SessionId &&
                    response.TenantId == config.TenantId && response.HostId == host.IdentityId && response.DeviceId == _hello.DeviceId &&
                    response.Direction == RelayEnvelopeDirections.WindowsToMobile && response.SenderRole == SecureRelayRoles.Windows &&
                    SecureRemotePairingRules.FixedTimeEquals(Decode(response.Confirmation), Decode(Confirm(confirmation, complete, SecureRelayRoles.Windows))),
                    "Host key confirmation mismatch.");
            }
        }
        public async Task SendRequestAsync(string marker)
        {
            var envelope = new SecureRelayEnvelope
            {
                ProtocolVersion = 1, FrameType = SecureRelayFrameTypes.Envelope, SessionId = _hello.SessionId,
                TenantId = _hello.TenantId, HostId = _hello.HostId, DeviceId = _hello.DeviceId,
                Direction = RelayEnvelopeDirections.MobileToWindows, Kind = SecureRelayKinds.Request, MessageId = Encode(Random(16)),
                Sequence = ++_sequence, CreatedAt = Now(), PayloadEncoding = RelayPayloadEncodings.EncryptedJson
            };
            var nonce = Nonce(_sendPrefix, envelope.Sequence);
            envelope.Nonce = Encode(nonce);
            byte[] tag;
            var plaintext = _material.Add(Encoding.UTF8.GetBytes(Json.Serialize(new Dictionary<string, object>
            {
                { "schemaVersion", 1 }, { "payloadType", RemoteRelayPayloadTypes.Command }, { "requestId", marker },
                { "deviceId", _hello.DeviceId }, { "commandType", "probe" }, { "payload", new Dictionary<string, object>() }
            })));
            var encrypted = Crypto.EncryptAesGcm(_sendKey, nonce, plaintext, SecureRemoteTranscript.EncodeEnvelopeAad(envelope), out tag);
            envelope.Ciphertext = Encode(Join(encrypted, tag));
            await SendAsync(_socket, envelope);
        }
        public async Task<string> ReadEventAsync()
        {
            var envelope = Codec.Deserialize(await ReceiveAsync(_socket, _token)) as SecureRelayEnvelope;
            Check(envelope != null && envelope.ProtocolVersion == 1 && envelope.SessionId == _hello.SessionId &&
                envelope.TenantId == _hello.TenantId && envelope.HostId == _hello.HostId && envelope.DeviceId == _hello.DeviceId &&
                envelope.Direction == RelayEnvelopeDirections.WindowsToMobile && envelope.Kind == SecureRelayKinds.Event &&
                envelope.PayloadEncoding == RelayPayloadEncodings.EncryptedJson && envelope.Sequence == ++_received &&
                SecureRemotePairingRules.FixedTimeEquals(Decode(envelope.Nonce), Nonce(_receivePrefix, envelope.Sequence)), "Event route/sequence mismatch.");
            var combined = Decode(envelope.Ciphertext);
            Check(combined.Length >= 16, "Missing AES-GCM tag.");
            var plaintext = _material.Add(Crypto.DecryptAesGcm(_receiveKey, Decode(envelope.Nonce), combined.Take(combined.Length - 16).ToArray(),
                combined.Skip(combined.Length - 16).ToArray(), SecureRemoteTranscript.EncodeEnvelopeAad(envelope)));
            return new UTF8Encoding(false, true).GetString(plaintext);
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_socket.State == WebSocketState.Open)
            {
                using (var closing = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    try { _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "switch", closing.Token).GetAwaiter().GetResult(); }
                    catch { _socket.Abort(); }
                }
            }
            _socket.Dispose(); _material.Dispose();
            Interlocked.Decrement(ref _active);
        }
    }

    private static async Task<string> DeviceTicketAsync(HttpRelayTransport transport, RelayHostConfig config, string hostId, EphemeralIdentity device)
    {
        var id = device.Identity.IdentityId;
        var value = await PostAsync(transport, config, "/v1/auth/challenges", new Dictionary<string, object>
        {
            { "subjectType", "device" }, { "subjectId", id }, { "tenantId", config.TenantId },
            { "hostId", hostId }, { "deviceId", id }, { "purpose", "websocket_device" }
        });
        var challenge = ReadContract<AuthChallenge>(Json.Serialize(value));
        Check(challenge.ProtocolVersion == 1 && challenge.SubjectType == "device" && challenge.SubjectId == id &&
            challenge.TenantId == config.TenantId && challenge.HostId == hostId && challenge.DeviceId == id &&
            challenge.Purpose == "websocket_device" && challenge.RelaySigningKeyFingerprint == config.RelaySigningKeyFingerprint &&
            Decode(challenge.Challenge).Length == 32 && challenge.IssuedAt <= Now() + 300000 && challenge.ExpiresAt > Now() &&
            challenge.ExpiresAt > challenge.IssuedAt && challenge.ExpiresAt - challenge.IssuedAt <= 60000 &&
            Crypto.VerifyRawEcdsa(Decode(config.RelaySigningPublicKey), SecureRemoteTranscript.EncodeRelayChallenge(challenge), Decode(challenge.RelaySignature)),
            "Device challenge identity/route/signature mismatch.");
        var response = await PostAsync(transport, config, "/v1/auth/prove", new Dictionary<string, object>
        {
            { "challenge", value }, { "subjectSignature", Encode(device.Sign(SecureRemoteTranscript.EncodeRelayProof(challenge))) }
        });
        Check((string)response["purpose"] == "websocket_device" && Decode((string)response["ticket"]).Length == 32 &&
            Convert.ToInt64(response["expiresAt"]) > Now() && Convert.ToInt64(response["expiresAt"]) <= Now() + 360000, "Device ticket mismatch.");
        return (string)response["ticket"];
    }

    private sealed class EphemeralIdentity : IRemoteIdentityStore, IDisposable
    {
        private readonly CngKey _signing = CngKey.Create(CngAlgorithm.ECDsaP256);
        private readonly CngKey _agreement = Crypto.CreateEphemeralAgreementKey();
        public RemoteEndpointIdentity Identity { get; private set; }
        public EphemeralIdentity()
        {
            Identity = new RemoteEndpointIdentity { IdentityId = Guid.NewGuid().ToString("D"), KeyVersion = 1,
                SigningPublicKey = Encode(CngSecureRemoteCrypto.ExportSec1PublicKey(_signing)),
                AgreementPublicKey = Encode(CngSecureRemoteCrypto.ExportSec1PublicKey(_agreement)) };
        }
        public RemoteEndpointIdentity LoadOrCreateHostIdentity(string id) { Check(id == Identity.IdentityId, "Identity route mismatch."); return Identity; }
        public byte[] Sign(byte[] transcript) { using (var signer = new ECDsaCng(_signing)) { signer.HashAlgorithm = CngAlgorithm.Sha256; return signer.SignData(transcript); } }
        public byte[] DeriveAgreement(byte[] peer) { return Crypto.DeriveAgreement(_agreement, peer); }
        public void Dispose() { _signing.Dispose(); _agreement.Dispose(); }
    }
    private sealed class SecretBytes : IDisposable
    {
        private readonly List<byte[]> _values = new List<byte[]>();
        public byte[] Add(byte[] value) { _values.Add(value); return value; }
        public void Dispose() { foreach (var value in _values) Array.Clear(value, 0, value.Length); }
    }
    private static async Task<IDictionary<string, object>> PostAsync(HttpRelayTransport transport, RelayHostConfig config, string path, object body)
    {
        var request = new RelayHttpRequest("POST", HttpRelayTransport.BuildUrl(config, path), Json.Serialize(body));
        request.Headers["Content-Type"] = "application/json";
        var response = await transport.SendAsync(request, _token);
        Check(response.StatusCode >= 200 && response.StatusCode < 300, "Probe HTTP status " + response.StatusCode);
        return (IDictionary<string, object>)Json.DeserializeObject(response.Body);
    }
    private static T ReadContract<T>(string json) { using (var s = new MemoryStream(Encoding.UTF8.GetBytes(json))) return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s); }
    private static object WireObject<T>(T value)
    {
        using (var s = new MemoryStream()) { new DataContractJsonSerializer(typeof(T)).WriteObject(s, value); return Json.DeserializeObject(Encoding.UTF8.GetString(s.ToArray())); }
    }
    private static async Task SendAsync(ClientWebSocket socket, object frame) { var bytes = Encoding.UTF8.GetBytes(Codec.Serialize(frame)); await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _token); }
    private static async Task<string> ReceiveAsync(ClientWebSocket socket, CancellationToken token)
    {
        using (var stream = new MemoryStream())
        {
            var bytes = new byte[8192];
            while (true)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), token);
                if (result.MessageType == WebSocketMessageType.Close) return null;
                Check(result.MessageType == WebSocketMessageType.Text && stream.Length + result.Count <= SecureRelayChannel.MaxWireFrameBytes, "Invalid frame.");
                stream.Write(bytes, 0, result.Count);
                if (result.EndOfMessage) return new UTF8Encoding(false, true).GetString(stream.ToArray());
            }
        }
    }
    private static Uri SocketUri(RelayHostConfig config, string role, string host, string device) { return new Uri("ws://127.0.0.1:" + config.ConnectionPort + "/v1/ws?connection=" + role + "&tenantId=" + Uri.EscapeDataString(config.TenantId) + "&hostId=" + host + "&deviceId=" + device); }
    private static async Task WaitAsync(Func<Task<bool>> condition) { while (!await condition()) await Task.Delay(25, _token); }
    private static byte[] Random(int count) { var value = new byte[count]; using (var random = RandomNumberGenerator.Create()) random.GetBytes(value); return value; }
    private static byte[] Join(byte[] a, byte[] b) { var value = new byte[a.Length + b.Length]; Buffer.BlockCopy(a, 0, value, 0, a.Length); Buffer.BlockCopy(b, 0, value, a.Length, b.Length); return value; }
    private static byte[] Nonce(byte[] prefix, ulong sequence) { var nonce = new byte[12]; Buffer.BlockCopy(prefix, 0, nonce, 0, 4); for (var i = 0; i < 8; i++) nonce[11 - i] = (byte)(sequence >> (8 * i)); return nonce; }
    private static byte[] Derive(byte[] master, string label, int length) { return Crypto.HkdfSha256(master, new byte[0], Encoding.UTF8.GetBytes(label), length); }
    private static string Confirm(byte[] key, byte[] transcript, string role) { using (var mac = new HMACSHA256(key)) return Encode(mac.ComputeHash(SecureRemoteTranscript.EncodeConfirmationTranscript(transcript, role))); }
    private static byte[] Decode(string value) { return SecureRemoteBase64Url.Decode(value); }
    private static string Encode(byte[] value) { return SecureRemoteBase64Url.Encode(value); }
    private static long Now() { return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds(); }
    private static int FreePort() { var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); listener.Start(); try { return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; } finally { listener.Stop(); } }
    private static void Check(bool success, string message) { if (!success) throw new InvalidOperationException(message); }
    private static void Record(string message) { Evidence.Add(message); Console.WriteLine(message); }
    private static string Quote(string value) { return "\"" + value.Replace("\"", "\\\"") + "\""; }
    private static string RunCli(string executable, string arguments)
    {
        using (var process = Process.Start(new ProcessStartInfo { FileName = executable, Arguments = arguments,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Relay CLI timed out."); }
            Check(process.ExitCode == 0, "Relay CLI failed.");
            stderr.GetAwaiter().GetResult();
            return stdout.GetAwaiter().GetResult();
        }
    }
}
