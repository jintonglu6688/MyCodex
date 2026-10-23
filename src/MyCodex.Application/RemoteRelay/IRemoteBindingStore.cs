namespace MyCodex.Application.RemoteRelay
{
    public interface IRemoteBindingStore
    {
        System.Collections.Generic.IReadOnlyList<RemoteDeviceBinding> ListActive(
            string tenantId,
            string hostId);

        RemoteDeviceBinding FindActive(string tenantId, string hostId, string deviceId);

        void SavePending(RemoteDeviceBinding binding);

        void Activate(string claimId, int bindingVersion);

        void Revoke(string tenantId, string hostId, string deviceId);
    }
}
