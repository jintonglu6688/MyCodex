using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IConfigurableScreenReaderService : IScreenReaderService
    {
        void ApplyConfig(AppConfigSnapshot snapshot);

        // Release tracked output without stopping a channel that another target may share.
        bool StopOwnedOutput();
    }
}
