using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Interfaces
{
    public interface IAppServerWindowsSandboxClient
    {
        Task<AppServerWindowsSandboxReadiness> ReadWindowsSandboxReadinessAsync();

        Task<AppServerWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(
            AppServerWindowsSandboxSetupMode mode,
            string cwd = null);
    }
}
