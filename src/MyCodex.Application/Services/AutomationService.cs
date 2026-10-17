using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Application.Services
{
    public sealed class AutomationService : IAutomationService
    {
        private readonly IAutomationStore _store;
        private readonly AutomationScheduleCalculator _scheduleCalculator;
        private readonly Func<DateTime> _now;
        private readonly Func<string> _newId;

        public AutomationService(IAutomationStore store, AutomationScheduleCalculator scheduleCalculator)
            : this(store, scheduleCalculator, () => DateTime.Now, () => Guid.NewGuid().ToString("N"))
        {
        }

        public AutomationService(
            IAutomationStore store,
            AutomationScheduleCalculator scheduleCalculator,
            Func<DateTime> now,
            Func<string> newId)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _scheduleCalculator = scheduleCalculator ?? throw new ArgumentNullException(nameof(scheduleCalculator));
            _now = now ?? throw new ArgumentNullException(nameof(now));
            _newId = newId ?? throw new ArgumentNullException(nameof(newId));
        }

        public IReadOnlyList<AutomationDefinitionSnapshot> ListDefinitions()
        {
            return Load().Definitions.ToList();
        }

        public IReadOnlyList<AutomationRunSnapshot> ListRuns()
        {
            return Load().Runs.ToList();
        }

        public AutomationDefinitionSnapshot Create(AutomationCreationRequest request)
        {
            ValidateCreate(request);
            var now = _now();
            var definition = new AutomationDefinitionSnapshot
            {
                Id = _newId(),
                Name = request.Name.Trim(),
                Description = TrimToNull(request.Description),
                Kind = request.Kind,
                Enabled = true,
                Prompt = request.Prompt.Trim(),
                InputItems = CloneInputItems(request.InputItems),
                Schedule = request.Schedule ?? new AutomationScheduleSnapshot(),
                Context = request.Context ?? new AutomationContextSnapshot(),
                ModelId = TrimToNull(request.ModelId),
                ReasoningEffort = TrimToNull(request.ReasoningEffort),
                ApprovalMode = request.ApprovalMode,
                AllowNoFindingsAutoArchive = request.AllowNoFindingsAutoArchive,
                CreatedAt = now,
                UpdatedAt = now
            };
            definition.NextRunAt = _scheduleCalculator.CalculateNextRun(definition, now);
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                snapshot.Definitions.Add(definition);
                return snapshot;
            });
            return definition;
        }

        public AutomationDefinitionSnapshot Update(AutomationUpdateRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Id))
            {
                throw new ArgumentException(SharedLocalization.Text("AutomationService.Error.IdRequired", "Automation ID cannot be empty."));
            }

            ValidateCommon(request.Name, request.Prompt, request.Context);
            AutomationDefinitionSnapshot definition = null;
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                definition = FindDefinition(snapshot, request.Id);
                definition.Name = request.Name.Trim();
                definition.Description = TrimToNull(request.Description);
                definition.Kind = request.Kind;
                definition.Prompt = request.Prompt.Trim();
                definition.InputItems = CloneInputItems(request.InputItems);
                definition.Schedule = request.Schedule ?? new AutomationScheduleSnapshot();
                definition.Context = request.Context ?? new AutomationContextSnapshot();
                definition.ModelId = TrimToNull(request.ModelId);
                definition.ReasoningEffort = TrimToNull(request.ReasoningEffort);
                definition.ApprovalMode = request.ApprovalMode;
                definition.AllowNoFindingsAutoArchive = request.AllowNoFindingsAutoArchive;
                definition.UpdatedAt = _now();
                definition.NextRunAt = definition.Enabled
                    ? _scheduleCalculator.CalculateNextRun(definition, definition.UpdatedAt)
                    : null;
                return snapshot;
            });
            return definition;
        }

        public void Disable(string automationId)
        {
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                var definition = FindDefinition(snapshot, automationId);
                definition.Enabled = false;
                definition.NextRunAt = null;
                definition.UpdatedAt = _now();
                return snapshot;
            });
        }

        public void Enable(string automationId)
        {
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                var definition = FindDefinition(snapshot, automationId);
                definition.Enabled = true;
                definition.UpdatedAt = _now();
                definition.NextRunAt = _scheduleCalculator.CalculateNextRun(definition, definition.UpdatedAt);
                return snapshot;
            });
        }

        public void Delete(string automationId)
        {
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                var definition = FindDefinition(snapshot, automationId);
                snapshot.Definitions.Remove(definition);
                snapshot.Runs.RemoveAll(item =>
                    item != null &&
                    item.Status != AutomationRunStatus.Running &&
                    string.Equals(item.AutomationId, automationId, StringComparison.OrdinalIgnoreCase));
                return snapshot;
            });
        }

        public void MarkRunRead(string runId)
        {
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                FindRun(snapshot, runId).Unread = false;
                return snapshot;
            });
        }

        public void ArchiveRun(string runId)
        {
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                var run = FindRun(snapshot, runId);
                run.Archived = true;
                run.Unread = false;
                run.NeedsAttention = false;
                return snapshot;
            });
        }

        public void DeleteRun(string runId)
        {
            _store.Mutate(snapshot =>
            {
                snapshot = Normalize(snapshot);
                snapshot.Runs.Remove(FindRun(snapshot, runId));
                return snapshot;
            });
        }

        private AutomationStateSnapshot Load()
        {
            return Normalize(_store.Load());
        }

        private static AutomationStateSnapshot Normalize(AutomationStateSnapshot snapshot)
        {
            snapshot = snapshot ?? new AutomationStateSnapshot();
            snapshot.Definitions = snapshot.Definitions ?? new List<AutomationDefinitionSnapshot>();
            snapshot.Runs = snapshot.Runs ?? new List<AutomationRunSnapshot>();
            foreach (var definition in snapshot.Definitions)
            {
                if (definition != null)
                {
                    definition.InputItems = CloneInputItems(definition.InputItems);
                }
            }

            return snapshot;
        }

        private static AutomationDefinitionSnapshot FindDefinition(AutomationStateSnapshot snapshot, string automationId)
        {
            if (string.IsNullOrWhiteSpace(automationId))
            {
                throw new ArgumentException(SharedLocalization.Text("AutomationService.Error.IdRequired", "Automation ID cannot be empty."));
            }

            var definition = snapshot.Definitions.FirstOrDefault(item =>
                item != null &&
                string.Equals(item.Id, automationId, StringComparison.OrdinalIgnoreCase));
            if (definition == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text("AutomationService.Error.NotFound", "Automation was not found."));
            }

            return definition;
        }

        private static AutomationRunSnapshot FindRun(AutomationStateSnapshot snapshot, string runId)
        {
            var run = !string.IsNullOrWhiteSpace(runId)
                ? snapshot.Runs.FirstOrDefault(item =>
                    item != null &&
                    string.Equals(item.Id, runId, StringComparison.OrdinalIgnoreCase))
                : null;
            if (run == null)
            {
                throw new InvalidOperationException(SharedLocalization.Text("AutomationService.Error.RunNotFound", "Automation run was not found."));
            }

            return run;
        }

        private static void ValidateCreate(AutomationCreationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateCommon(request.Name, request.Prompt, request.Context);
        }

        private static void ValidateCommon(string name, string prompt, AutomationContextSnapshot context)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException(SharedLocalization.Text("AutomationService.Error.NameRequired", "Automation name cannot be empty."));
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException(SharedLocalization.Text("AutomationService.Error.PromptRequired", "Automation prompt cannot be empty."));
            }

            if (context == null || string.IsNullOrWhiteSpace(context.WorkspacePath))
            {
                throw new ArgumentException(SharedLocalization.Text("AutomationService.Error.WorkspacePathRequired", "Automation workspace path cannot be empty."));
            }
        }

        private static string TrimToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static List<AutomationInputItemSnapshot> CloneInputItems(IEnumerable<AutomationInputItemSnapshot> inputItems)
        {
            var clones = new List<AutomationInputItemSnapshot>();
            if (inputItems == null)
            {
                return clones;
            }

            foreach (var item in inputItems)
            {
                if (item == null)
                {
                    continue;
                }

                clones.Add(new AutomationInputItemSnapshot
                {
                    Type = item.Type,
                    Name = TrimToNull(item.Name),
                    DisplayName = TrimToNull(item.DisplayName),
                    Path = TrimToNull(item.Path)
                });
            }

            return clones;
        }
    }
}
