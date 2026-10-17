using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAutomationModelResolver
    {
        Task<AutomationModelResolution> ResolveAsync(AutomationDefinitionSnapshot definition);
    }
}
