using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IRemoteServiceConfigStore
    {
        RemoteServiceConfigSnapshot Load();

        void Save(RemoteServiceConfigSnapshot snapshot);

        void ProtectAccessSecret(RemoteServiceConfigSnapshot snapshot, string accessSecret);

        string UnprotectAccessSecret(RemoteServiceConfigSnapshot snapshot);

        void Clear();
    }
}

