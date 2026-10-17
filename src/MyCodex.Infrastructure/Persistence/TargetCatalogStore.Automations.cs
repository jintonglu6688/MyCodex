using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed partial class TargetCatalogStore
    {
        public IReadOnlyList<PendingAutomationAssignment> ListPendingAutomations()
        {
            return WithLock(() =>
            {
                var pending = ReadPendingAutomations();
                var assignments = ReadAutomationAssignments();
                return (IReadOnlyList<PendingAutomationAssignment>)pending.Definitions
                    .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Id))
                    .Select(item => new { Definition = item, Assignment = assignments.FirstOrDefault(
                        entry => string.Equals(entry.PendingId, item.Id, StringComparison.OrdinalIgnoreCase)) })
                    .Where(item => item.Assignment == null || !item.Assignment.Completed)
                    .Select(item => new PendingAutomationAssignment
                    {
                        Definition = item.Definition,
                        ReservedTargetId = item.Assignment == null ? null : item.Assignment.TargetId
                    }).ToList();
            });
        }

        public AutomationDefinitionSnapshot AssignPendingAutomation(string pendingId, string targetId, string workspacePath)
        {
            return WithLock(() =>
            {
                var target = Find(ReadCatalog(), targetId);
                var definition = ReadPendingAutomations().Definitions.FirstOrDefault(item => item != null &&
                    string.Equals(item.Id, pendingId, StringComparison.OrdinalIgnoreCase));
                if (definition == null)
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "Automation.Assignment.NotFound", "The pending automation no longer exists."));
                }

                var assigned = ClonePendingAutomation(definition);
                assigned.Enabled = false;
                assigned.NextRunAt = null;
                assigned.Context = assigned.Context ?? new AutomationContextSnapshot();
                assigned.Context.RuntimeProfileId = target.RuntimeScopeId;
                assigned.Context.OfficeRuntimeScopeId = assigned.Context.InteractionMode == AppInteractionMode.Office
                    ? target.RuntimeScopeId : null;
                assigned.Context.WorkspacePath = (workspacePath ?? string.Empty).Trim();
                assigned.Context.OfficeRootPath = assigned.Context.InteractionMode == AppInteractionMode.Office
                    ? assigned.Context.WorkspacePath : null;
                assigned.Context.WorkspaceId = null;
                assigned.Context.SessionId = null;
                assigned.Context.ThreadId = null;
                new AutomationTargetScopePlanner(target.RuntimeScopeId, target.Kind != "Windows").Validate(assigned);
                var paths = CreatePaths(target.Id);
                ValidateTargetData(paths, target);

                var assignments = ReadAutomationAssignments();
                var reservation = assignments.FirstOrDefault(item =>
                    string.Equals(item.PendingId, pendingId, StringComparison.OrdinalIgnoreCase));
                if (reservation != null && !string.Equals(reservation.TargetId, target.Id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(SharedLocalization.Text(
                        "Automation.Assignment.Reserved", "This automation is already being assigned to another target. Retry with that target."));
                }

                if (reservation == null)
                {
                    reservation = new AutomationAssignmentRecord
                    {
                        PendingId = pendingId, TargetId = target.Id, AutomationId = Guid.NewGuid().ToString("N")
                    };
                    assignments.Add(reservation);
                    WriteAtomic(AutomationAssignmentsPath, assignments);
                }

                assigned.Id = reservation.AutomationId;
                var store = new JsonAutomationStore(paths);
                AutomationDefinitionSnapshot result = null;
                store.Mutate(snapshot =>
                {
                    result = snapshot.Definitions.FirstOrDefault(item =>
                        string.Equals(item.Id, assigned.Id, StringComparison.OrdinalIgnoreCase));
                    if (result == null)
                    {
                        snapshot.Definitions.Add(assigned);
                        result = assigned;
                    }

                    return snapshot;
                });
                _saveCheckpoint?.Invoke("automation-assigned");
                reservation.Completed = true;
                WriteAtomic(AutomationAssignmentsPath, assignments);
                // Keep the original pending file and run history; retry never overwrites the target's saved edits.
                return result;
            });
        }

        private string AutomationAssignmentsPath => Path.Combine(_targetsRoot, "pending-automation-assignments.json");

        private AutomationStateSnapshot ReadPendingAutomations()
        {
            var path = Path.Combine(_targetsRoot, "pending-automations.json");
            RejectReparsePath(path);
            return File.Exists(path) ? Read<AutomationStateSnapshot>(path) : new AutomationStateSnapshot();
        }

        private List<AutomationAssignmentRecord> ReadAutomationAssignments()
        {
            RejectReparsePath(AutomationAssignmentsPath);
            return File.Exists(AutomationAssignmentsPath)
                ? Read<List<AutomationAssignmentRecord>>(AutomationAssignmentsPath) : new List<AutomationAssignmentRecord>();
        }

        private static AutomationDefinitionSnapshot ClonePendingAutomation(AutomationDefinitionSnapshot definition)
        {
            var serializer = new DataContractJsonSerializer(typeof(AutomationDefinitionSnapshot));
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, definition);
                stream.Position = 0;
                return (AutomationDefinitionSnapshot)serializer.ReadObject(stream);
            }
        }

        [DataContract]
        private sealed class AutomationAssignmentRecord
        {
            [DataMember] public string PendingId { get; set; }
            [DataMember] public string TargetId { get; set; }
            [DataMember] public string AutomationId { get; set; }
            [DataMember] public bool Completed { get; set; }
        }
    }
}
