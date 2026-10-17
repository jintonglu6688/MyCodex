using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerSkillClient
    {
        Task<List<AppServerSkillsSnapshot>> ListSkillsAsync(
            IReadOnlyCollection<string> workspacePaths,
            bool forceReload);

        Task<bool> WriteSkillEnabledAsync(string path, bool enabled);
    }
}
