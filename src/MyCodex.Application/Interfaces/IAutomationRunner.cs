using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAutomationRunner
    {
        Task<AutomationRunSnapshot> StartAsync(AutomationRunRequest request);

        void CancelRunning(string reason);

        bool CompleteByThread(string threadId, string turnId, AutomationRunCompletion completion);
    }
}
