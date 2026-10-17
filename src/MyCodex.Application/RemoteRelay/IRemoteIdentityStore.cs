namespace MyCodex.Application.RemoteRelay
{
    public interface IRemoteIdentityStore
    {
        RemoteEndpointIdentity LoadOrCreateHostIdentity(string hostId);

        byte[] Sign(byte[] transcript);

        byte[] DeriveAgreement(byte[] peerPublicKey);
    }
}
