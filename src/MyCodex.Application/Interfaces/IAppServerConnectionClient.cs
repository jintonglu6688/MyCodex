using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerConnectionClient
    {
        event EventHandler<AppServerEventArgs> EventReceived;

        AppServerProtocolCapabilities ProtocolCapabilities { get; }

        Task ConnectAsync();

        Task RestartAsync();
    }
}
