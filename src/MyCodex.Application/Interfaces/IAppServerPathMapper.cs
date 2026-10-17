using MyCodex.Domain.Enums;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerPathMapper
    {
        AppServerHostPlatform HostPlatform { get; }

        bool TryMapToServerPath(string clientPath, out string serverPath);

        bool TryMapToClientPath(string serverPath, out string clientPath);
    }
}
