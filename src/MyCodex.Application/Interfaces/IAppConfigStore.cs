using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAppConfigStore
    {
        AppConfigSnapshot Load();

        void Save(AppConfigSnapshot snapshot);
    }
}
