using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAppStateStore
    {
        AppStateSnapshot Load();

        void Save(AppStateSnapshot snapshot);
    }

    public interface IRuntimeConfigAppStateStore : IAppStateStore
    {
        AppStateSnapshot Load(AppConfigSnapshot appConfig);

        void Save(AppStateSnapshot snapshot, AppConfigSnapshot appConfig);
    }
}
