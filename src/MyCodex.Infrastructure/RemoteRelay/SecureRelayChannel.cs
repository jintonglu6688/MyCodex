using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Infrastructure.RemoteRelay
{
    internal sealed class SecureRelayChannel : IDisposable
    {
        internal const int MaxBusinessPlaintextBytes = RemoteRelayPayloadJsonSerializer.MaxBusinessJsonCharacters;
        internal const int MaxWireFrameBytes = 15444672;
        private const ulong MaxSequence = uint.MaxValue;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly ClientWebSocket _socket;
        private readonly IRemoteIdentityStore _identityStore;
        private readonly IRemoteBindingStore _bindingStore;
        private readonly RemoteCommandDispatcher _dispatcher;
        private readonly RemoteDeviceBinding _binding;
        private readonly string _tenantId;
        private readonly string _hostId;
        private readonly string _deviceId;
        private readonly CngSecureRemoteCrypto _crypto = new CngSecureRemoteCrypto();
        private readonly SecureRelayFrameCodec _codec = new SecureRelayFrameCodec();
        private ChannelState _state;
        private DateTime? _handshakeDeadlineUtc;
        private System.Security.Cryptography.CngKey _ephemeralKey;
        private SessionHello _clientHello;
        private SessionHello _serverHello;
        private byte[] _completeTranscript;
        private byte[] _confirmationKey;
        private byte[] _receiveKey;
        private byte[] _sendKey;
        private byte[] _receiveNoncePrefix;
        private byte[] _sendNoncePrefix;
        private ulong _nextSendSequence;
        private ulong _highestReceiveSequence;

        public SecureRelayChannel(
            ClientWebSocket socket,
            IRemoteIdentityStore identityStore,
            IRemoteBindingStore bindingStore,
            RemoteCommandDispatcher dispatcher,
            RemoteDeviceBinding binding)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _identityStore = identityStore ?? throw new ArgumentNullException(nameof(identityStore));
            _bindingStore = bindingStore ?? throw new ArgumentNullException(nameof(bindingStore));
            _dispatcher = dispatcher;
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
            _tenantId = binding.TenantId;
            _hostId = binding.HostId;
            _deviceId = binding.DeviceId;
            _state = ChannelState.AwaitingClientHello;
            _handshakeDeadlineUtc = null;
        }

        public bool IsEstablished { get { return _state == ChannelState.Established; } }

        public DateTime? HandshakeDeadlineUtc { get { return _handshakeDeadlineUtc; } }

        public async Task HandleAsync(string text, CancellationToken cancellationToken)
        {
            try
            {
                var frame = _codec.Deserialize(text);
                var hello = frame as SessionHello;
                if (hello != null)
                {
                    if (_state == ChannelState.Established)
                    {
                        if (!string.Equals(hello.FrameType, SecureRelayFrameTypes.ClientHello, StringComparison.Ordinal) ||
                            string.Equals(hello.SessionId, _clientHello.SessionId, StringComparison.Ordinal))
                        {
                            throw new FormatException("The Relay session Hello is invalid.");
                        }

                        await ResetForNewHelloAsync(cancellationToken).ConfigureAwait(false);
                    }
                    await HandleClientHelloAsync(hello, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var confirmation = frame as SessionConfirmationFrame;
                if (confirmation != null)
                {
                    await HandleClientConfirmationAsync(confirmation, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var envelope = frame as SecureRelayEnvelope;
                if (envelope != null)
                {
                    await HandleEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
                    return;
                }

                throw new FormatException("The Relay frame is invalid.");
            }
            catch
            {
                Close();
                throw;
            }
        }

        public Task PublishAsync(RemoteRelayEvent remoteEvent, CancellationToken cancellationToken)
        {
            if (remoteEvent == null || !IsEstablished)
            {
                return Task.FromResult(0);
            }

            var payload = RemoteRelayPayloadJsonSerializer.SerializeEvent(remoteEvent);
            return SendBusinessAsync(SecureRelayKinds.Event, payload, cancellationToken);
        }

        private async Task HandleClientHelloAsync(SessionHello hello, CancellationToken cancellationToken)
        {
            if (_state != ChannelState.AwaitingClientHello ||
                !string.Equals(hello.FrameType, SecureRelayFrameTypes.ClientHello, StringComparison.Ordinal) ||
                !string.Equals(hello.Direction, RelayEnvelopeDirections.MobileToWindows, StringComparison.Ordinal))
            {
                throw new FormatException("The Relay session state is invalid.");
            }

            ValidateHelloRoute(hello, _binding.DeviceKeyVersion, _binding.BindingVersion);
            var deviceSigning = DecodeExact(_binding.DeviceSigningPublicKey, 65);
            var deviceAgreement = DecodeExact(_binding.DeviceAgreementPublicKey, 65);
            var ephemeral = DecodeExact(hello.EphemeralPublicKey, 65);
            var nonce = DecodeExact(hello.Nonce, 32);
            var signature = DecodeExact(hello.Signature, 64);
            try
            {
                if (SecureRemotePairingRules.FixedTimeEquals(ephemeral, deviceAgreement))
                {
                    throw new FormatException("The session key is invalid.");
                }

                var transcript = SecureRemoteTranscript.EncodeSessionHello(hello);
                try
                {
                    if (!_crypto.IsValidP256PublicKey(ephemeral) || !_crypto.VerifyRawEcdsa(deviceSigning, transcript, signature))
                    {
                        throw new CryptographicException("The device session signature is invalid.");
                    }
                }
                finally
                {
                    Array.Clear(transcript, 0, transcript.Length);
                }

                _identityStore.LoadOrCreateHostIdentity(_hostId);
                _ephemeralKey = _crypto.CreateEphemeralAgreementKey();
                var localEphemeral = CngSecureRemoteCrypto.ExportSec1PublicKey(_ephemeralKey);
                var serverNonce = RandomBytes(32);
                try
                {
                    _clientHello = hello;
                    _serverHello = new SessionHello
                    {
                        ProtocolVersion = 1,
                        FrameType = SecureRelayFrameTypes.ServerHello,
                        SessionId = hello.SessionId,
                        TenantId = _tenantId,
                        HostId = _hostId,
                        DeviceId = _deviceId,
                        Direction = RelayEnvelopeDirections.WindowsToMobile,
                        BindingVersion = _binding.BindingVersion,
                        KeyVersion = 1,
                        EphemeralPublicKey = SecureRemoteBase64Url.Encode(localEphemeral),
                        Nonce = SecureRemoteBase64Url.Encode(serverNonce),
                        CreatedAt = UtcNowMilliseconds()
                    };
                    var signingTranscript = SecureRemoteTranscript.EncodeServerHelloSignature(_clientHello, _serverHello);
                    try
                    {
                        _serverHello.Signature = SecureRemoteBase64Url.Encode(_identityStore.Sign(signingTranscript));
                    }
                    finally
                    {
                        Array.Clear(signingTranscript, 0, signingTranscript.Length);
                    }

                    _completeTranscript = SecureRemoteTranscript.EncodeCompleteSessionHello(_clientHello, _serverHello);
                    var sharedSecret = _crypto.DeriveAgreement(_ephemeralKey, ephemeral);
                    try
                    {
                        DeriveSessionMaterial(nonce, serverNonce, sharedSecret);
                    }
                    finally
                    {
                        Array.Clear(sharedSecret, 0, sharedSecret.Length);
                    }

                    await SendFrameAsync(_serverHello, cancellationToken).ConfigureAwait(false);
                    _handshakeDeadlineUtc = DateTime.UtcNow.AddSeconds(8);
                    _state = ChannelState.AwaitingClientConfirmation;
                }
                finally
                {
                    Array.Clear(localEphemeral, 0, localEphemeral.Length);
                    Array.Clear(serverNonce, 0, serverNonce.Length);
                }
            }
            finally
            {
                Array.Clear(deviceSigning, 0, deviceSigning.Length);
                Array.Clear(deviceAgreement, 0, deviceAgreement.Length);
                Array.Clear(ephemeral, 0, ephemeral.Length);
                Array.Clear(nonce, 0, nonce.Length);
                Array.Clear(signature, 0, signature.Length);
            }
        }

        private async Task HandleClientConfirmationAsync(SessionConfirmationFrame confirmation, CancellationToken cancellationToken)
        {
            if (_state != ChannelState.AwaitingClientConfirmation ||
                !HasExpectedRoute(confirmation.SessionId, confirmation.TenantId, confirmation.HostId, confirmation.DeviceId) ||
                confirmation.ProtocolVersion != 1 ||
                !string.Equals(confirmation.FrameType, SecureRelayFrameTypes.Confirmation, StringComparison.Ordinal) ||
                !string.Equals(confirmation.Direction, RelayEnvelopeDirections.MobileToWindows, StringComparison.Ordinal) ||
                !string.Equals(confirmation.SenderRole, SecureRelayRoles.Android, StringComparison.Ordinal))
            {
                throw new FormatException("The session confirmation is invalid.");
            }

            ValidateConfirmation(confirmation, SecureRelayRoles.Android);
            var response = new SessionConfirmationFrame
            {
                ProtocolVersion = 1,
                FrameType = SecureRelayFrameTypes.Confirmation,
                SessionId = _clientHello.SessionId,
                TenantId = _tenantId,
                HostId = _hostId,
                DeviceId = _deviceId,
                Direction = RelayEnvelopeDirections.WindowsToMobile,
                SenderRole = SecureRelayRoles.Windows,
                Confirmation = CreateConfirmation(SecureRelayRoles.Windows)
            };
            await SendFrameAsync(response, cancellationToken).ConfigureAwait(false);
            _state = ChannelState.Established;
            _handshakeDeadlineUtc = null;
        }

        private async Task HandleEnvelopeAsync(SecureRelayEnvelope envelope, CancellationToken cancellationToken)
        {
            if (_state != ChannelState.Established ||
                envelope.ProtocolVersion != 1 ||
                !string.Equals(envelope.FrameType, SecureRelayFrameTypes.Envelope, StringComparison.Ordinal) ||
                !HasExpectedRoute(envelope.SessionId, envelope.TenantId, envelope.HostId, envelope.DeviceId) ||
                !string.Equals(envelope.Direction, RelayEnvelopeDirections.MobileToWindows, StringComparison.Ordinal) ||
                !string.Equals(envelope.Kind, SecureRelayKinds.Request, StringComparison.Ordinal) ||
                !string.Equals(envelope.PayloadEncoding, RelayPayloadEncodings.EncryptedJson, StringComparison.Ordinal) ||
                envelope.Sequence == 0 || envelope.Sequence > MaxSequence || envelope.Sequence != _highestReceiveSequence + 1)
            {
                throw new FormatException("The encrypted Relay envelope is invalid.");
            }

            var nonce = DecodeExact(envelope.Nonce, 12);
            var ciphertextAndTag = DecodeExactAtLeast(envelope.Ciphertext, 16, MaxBusinessPlaintextBytes + 16);
            try
            {
                var expectedNonce = CreateNonce(_receiveNoncePrefix, envelope.Sequence);
                try
                {
                    if (!SecureRemotePairingRules.FixedTimeEquals(nonce, expectedNonce))
                    {
                        throw new CryptographicException("The envelope nonce is invalid.");
                    }
                }
                finally { Array.Clear(expectedNonce, 0, expectedNonce.Length); }

                var cipherLength = ciphertextAndTag.Length - 16;
                var ciphertext = new byte[cipherLength];
                var tag = new byte[16];
                Buffer.BlockCopy(ciphertextAndTag, 0, ciphertext, 0, cipherLength);
                Buffer.BlockCopy(ciphertextAndTag, cipherLength, tag, 0, tag.Length);
                var aad = SecureRemoteTranscript.EncodeEnvelopeAad(envelope);
                byte[] plaintext = null;
                try
                {
                    plaintext = _crypto.DecryptAesGcm(_receiveKey, nonce, ciphertext, tag, aad);
                    if (plaintext.Length > MaxBusinessPlaintextBytes)
                    {
                        throw new FormatException("The Relay business payload is too large.");
                    }

                    var json = StrictUtf8(plaintext);
                    RemoteRelayCommand command;
                    if (!RemoteRelayResponseEnvelopePlanner.TryDeserializeRemoteCommand(json, _deviceId, out command) ||
                        !HasActiveBinding())
                    {
                        throw new FormatException("The Relay command is invalid or revoked.");
                    }

                    _highestReceiveSequence = envelope.Sequence;
                    if (_dispatcher == null)
                    {
                        return;
                    }

                    var result = await _dispatcher.DispatchAsync(command, cancellationToken).ConfigureAwait(false);
                    Trace.TraceInformation(
                        "[RemoteRelay] Command dispatched. Device={0}; Type={1}; Request={2}; Result={3}",
                        RemoteRelayDiagnostics.ShortId(_deviceId),
                        command.CommandType ?? "-",
                        RemoteRelayDiagnostics.ShortId(command.RequestId),
                        result == null ? "-" : result.EventType ?? "-");
                    await SendBusinessAsync(SecureRelayKinds.Response, SerializeResponseSafely(result, command), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Array.Clear(ciphertext, 0, ciphertext.Length);
                    Array.Clear(tag, 0, tag.Length);
                    Array.Clear(aad, 0, aad.Length);
                    if (plaintext != null) Array.Clear(plaintext, 0, plaintext.Length);
                }
            }
            finally
            {
                Array.Clear(nonce, 0, nonce.Length);
                Array.Clear(ciphertextAndTag, 0, ciphertextAndTag.Length);
            }
        }

        private async Task SendBusinessAsync(string kind, string payload, CancellationToken cancellationToken)
        {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var plaintext = new UTF8Encoding(false, true).GetBytes(payload);
            try
            {
                if (plaintext.Length > MaxBusinessPlaintextBytes) throw new InvalidOperationException("The Relay business payload is too large.");
                await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    if (_state != ChannelState.Established || !HasActiveBinding() || _nextSendSequence >= MaxSequence)
                    {
                        throw new InvalidOperationException("The secure Relay channel is unavailable.");
                    }

                    var sequence = ++_nextSendSequence;
                    await EncryptAndSendAsync(kind, sequence, plaintext, cancellationToken).ConfigureAwait(false);
                }
                finally { _sendGate.Release(); }
            }
            finally { Array.Clear(plaintext, 0, plaintext.Length); }
        }

        private static string SerializeResponseSafely(RemoteRelayEvent response, RemoteRelayCommand command)
        {
            try
            {
                var json = RemoteRelayPayloadJsonSerializer.SerializeResponse(response);
                if (new UTF8Encoding(false, true).GetByteCount(json) <= MaxBusinessPlaintextBytes)
                {
                    return json;
                }
            }
            catch
            {
                // A response must never end the authenticated channel merely because it is too large.
            }

            return RemoteRelayPayloadJsonSerializer.SerializeResponse(new RemoteRelayEvent
            {
                RequestId = command == null ? null : command.RequestId,
                OperationId = command == null ? null : command.OperationId,
                EventType = RemoteEventTypes.RemoteError,
                Payload = new Dictionary<string, object>
                {
                    { "code", RemoteErrorCodes.ResponseTooLarge },
                    { "message", "The remote response is too large." }
                }
            });
        }

        private async Task EncryptAndSendAsync(string kind, ulong sequence, byte[] plaintext, CancellationToken cancellationToken)
        {
            var nonce = CreateNonce(_sendNoncePrefix, sequence);
            var envelope = new SecureRelayEnvelope
            {
                ProtocolVersion = 1, FrameType = SecureRelayFrameTypes.Envelope, SessionId = _clientHello.SessionId,
                TenantId = _tenantId, HostId = _hostId, DeviceId = _deviceId, Direction = RelayEnvelopeDirections.WindowsToMobile,
                Kind = kind, MessageId = SecureRemoteBase64Url.Encode(RandomBytes(16)), Sequence = sequence,
                CreatedAt = UtcNowMilliseconds(), PayloadEncoding = RelayPayloadEncodings.EncryptedJson, Nonce = SecureRemoteBase64Url.Encode(nonce)
            };
            var aad = SecureRemoteTranscript.EncodeEnvelopeAad(envelope);
            byte[] tag = null;
            byte[] ciphertext = null;
            try
            {
                ciphertext = _crypto.EncryptAesGcm(_sendKey, nonce, plaintext, aad, out tag);
                var combined = new byte[ciphertext.Length + tag.Length];
                Buffer.BlockCopy(ciphertext, 0, combined, 0, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, combined, ciphertext.Length, tag.Length);
                envelope.Ciphertext = SecureRemoteBase64Url.Encode(combined);
                Array.Clear(combined, 0, combined.Length);
                await SendFrameCoreAsync(envelope, cancellationToken).ConfigureAwait(false);
            }
            catch { Close(); throw; }
            finally
            {
                Array.Clear(nonce, 0, nonce.Length); Array.Clear(aad, 0, aad.Length);
                if (tag != null) Array.Clear(tag, 0, tag.Length);
                if (ciphertext != null) Array.Clear(ciphertext, 0, ciphertext.Length);
            }
        }

        private async Task SendFrameAsync(object frame, CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await SendFrameCoreAsync(frame, cancellationToken).ConfigureAwait(false); }
            finally { _sendGate.Release(); }
        }

        private async Task SendFrameCoreAsync(object frame, CancellationToken cancellationToken)
        {
            var text = _codec.Serialize(frame);
            var bytes = Encoding.UTF8.GetBytes(text);
            try { await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false); }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        private void DeriveSessionMaterial(byte[] clientNonce, byte[] serverNonce, byte[] sharedSecret)
        {
            byte[] salt;
            using (var hash = SHA256.Create()) { salt = hash.ComputeHash(Join(clientNonce, serverNonce)); }
            var master = _crypto.HkdfSha256(sharedSecret, salt, _completeTranscript, 32);
            try
            {
                _receiveKey = Derive(master, "android to windows key", 32);
                _sendKey = Derive(master, "windows to android key", 32);
                _receiveNoncePrefix = Derive(master, "android to windows nonce prefix", 4);
                _sendNoncePrefix = Derive(master, "windows to android nonce prefix", 4);
                _confirmationKey = Derive(master, "session confirmation", 32);
            }
            finally { Array.Clear(salt, 0, salt.Length); Array.Clear(master, 0, master.Length); }
        }

        private byte[] Derive(byte[] master, string label, int length) { return _crypto.HkdfSha256(master, new byte[0], Encoding.UTF8.GetBytes(label), length); }
        private string CreateConfirmation(string role)
        {
            var transcript = SecureRemoteTranscript.EncodeConfirmationTranscript(_completeTranscript, role);
            try { using (var hmac = new HMACSHA256(_confirmationKey)) return SecureRemoteBase64Url.Encode(hmac.ComputeHash(transcript)); }
            finally { Array.Clear(transcript, 0, transcript.Length); }
        }
        private void ValidateConfirmation(SessionConfirmationFrame frame, string role)
        {
            var actual = DecodeExact(frame.Confirmation, 32); var expected = DecodeExact(CreateConfirmation(role), 32);
            try { if (!SecureRemotePairingRules.FixedTimeEquals(actual, expected)) throw new CryptographicException("The session confirmation is invalid."); }
            finally { Array.Clear(actual, 0, actual.Length); Array.Clear(expected, 0, expected.Length); }
        }
        private void ValidateHelloRoute(SessionHello hello, int keyVersion, int bindingVersion)
        {
            var sessionId = DecodeExact(hello.SessionId, 32);
            try
            {
                if (hello.ProtocolVersion != 1 || !HasExpectedRoute(hello.SessionId, hello.TenantId, hello.HostId, hello.DeviceId) || hello.KeyVersion != keyVersion || hello.BindingVersion != bindingVersion)
                {
                    throw new FormatException("The session Hello is invalid.");
                }
            }
            finally { Array.Clear(sessionId, 0, sessionId.Length); }
        }
        private bool HasExpectedRoute(string sessionId, string tenant, string host, string device) { return !string.IsNullOrWhiteSpace(sessionId) && string.Equals(tenant, _tenantId, StringComparison.Ordinal) && string.Equals(host, _hostId, StringComparison.Ordinal) && string.Equals(device, _deviceId, StringComparison.Ordinal); }
        private bool HasActiveBinding()
        {
            var active = _bindingStore.FindActive(_tenantId, _hostId, _deviceId);
            return active != null && active.BindingVersion == _binding.BindingVersion && active.DeviceKeyVersion == _binding.DeviceKeyVersion && string.Equals(active.DeviceSigningPublicKey, _binding.DeviceSigningPublicKey, StringComparison.Ordinal) && string.Equals(active.DeviceAgreementPublicKey, _binding.DeviceAgreementPublicKey, StringComparison.Ordinal);
        }
        private static byte[] CreateNonce(byte[] prefix, ulong sequence) { var result = new byte[12]; Buffer.BlockCopy(prefix, 0, result, 0, 4); for (var i=0;i<8;i++) result[11-i]=(byte)(sequence>>(8*i)); return result; }
        private static byte[] DecodeExact(string value, int length) { var result=SecureRemoteBase64Url.Decode(value); if(result.Length!=length){Array.Clear(result,0,result.Length);throw new FormatException("The Relay binary field is invalid.");} return result; }
        private static byte[] DecodeExactAtLeast(string value, int minimum, int maximum) { var result=SecureRemoteBase64Url.Decode(value); if(result.Length<minimum||result.Length>maximum){Array.Clear(result,0,result.Length);throw new FormatException("The Relay binary field is invalid.");} return result; }
        private static byte[] RandomBytes(int length) { var result=new byte[length]; using(var rng=RandomNumberGenerator.Create()) rng.GetBytes(result); return result; }
        private static string StrictUtf8(byte[] value) { return new UTF8Encoding(false, true).GetString(value); }
        private static long UtcNowMilliseconds() { return new DateTimeOffset(DateTime.UtcNow).ToUnixTimeMilliseconds(); }
        private static byte[] Join(byte[] first, byte[] second) { var result=new byte[first.Length+second.Length];Buffer.BlockCopy(first,0,result,0,first.Length);Buffer.BlockCopy(second,0,result,first.Length,second.Length);return result; }
        private async Task ResetForNewHelloAsync(CancellationToken cancellationToken)
        {
            await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                _state = ChannelState.AwaitingClientHello;
                _handshakeDeadlineUtc = null;
                _clientHello = null; _serverHello = null; _nextSendSequence = 0; _highestReceiveSequence = 0;
                Clear(ref _completeTranscript); Clear(ref _confirmationKey); Clear(ref _receiveKey); Clear(ref _sendKey); Clear(ref _receiveNoncePrefix); Clear(ref _sendNoncePrefix);
                if (_ephemeralKey != null) { _ephemeralKey.Dispose(); _ephemeralKey = null; }
            }
            finally { _sendGate.Release(); }
        }
        public void Close() { if (_state == ChannelState.Closed) return; _state=ChannelState.Closed; Clear(ref _completeTranscript);Clear(ref _confirmationKey);Clear(ref _receiveKey);Clear(ref _sendKey);Clear(ref _receiveNoncePrefix);Clear(ref _sendNoncePrefix);if(_ephemeralKey!=null){_ephemeralKey.Dispose();_ephemeralKey=null;} try{_socket.Abort();}catch(ObjectDisposedException){} }
        public void Dispose() { Close(); }
        private static void Clear(ref byte[] value) { if(value!=null){Array.Clear(value,0,value.Length);value=null;} }
        private enum ChannelState { AwaitingClientHello, AwaitingClientConfirmation, Established, Closed }
    }
}
