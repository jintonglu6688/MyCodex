namespace MyCodex.Application.RemoteRelay
{
    public interface IRemoteBindingStore
    {
        System.Collections.Generic.IReadOnlyList<RemoteDeviceBinding> ListActive(
            string tenantId,
            string hostId);

        RemoteDeviceBinding FindActive(string tenantId, string hostId, string deviceId);

        RemoteDeviceBinding FindPending(string claimId);

        void SavePending(RemoteDeviceBinding binding);

        void Activate(string claimId, int bindingVersion);

        void Revoke(string tenantId, string hostId, string deviceId);
    }
}
