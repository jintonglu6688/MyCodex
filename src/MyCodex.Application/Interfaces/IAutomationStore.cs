using System;
using MyCodex.Domain.Entities;

namespace MyCodex.Application.Interfaces
{
    public interface IAutomationStore
    {
        AutomationStateSnapshot Load();

        void Save(AutomationStateSnapshot snapshot);

        AutomationStateSnapshot Mutate(Func<AutomationStateSnapshot, AutomationStateSnapshot> mutation);
    }
}
