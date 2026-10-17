using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal sealed class TurnProgressSummaryState
    {
        public TurnProgressSummaryState()
        {
            PlanSteps = new List<AppServerTurnPlanStep>();
            McpToolProgressItems = new List<McpToolProgressEntry>();
            ChangedFiles = new List<string>();
            SkillNames = new List<string>();
            PendingSkillNames = new List<string>();
            CompletedReasoningSummaries = new List<ReasoningSummaryEntry>();
        }

        public string TurnId { get; set; }

        public string Explanation { get; set; }

        public List<AppServerTurnPlanStep> PlanSteps { get; private set; }

        public string PlanDeltaText { get; set; }

        public string ReasoningSummaryText { get; set; }

        public string ReasoningText { get; set; }

        public bool IsReasoningActive { get; set; }

        public string ActiveReasoningItemId { get; set; }

        public List<ReasoningSummaryEntry> CompletedReasoningSummaries { get; private set; }

        public string FileChangeDetailText { get; set; }

        public bool IsFileChangeActive { get; set; }

        public string DiffText { get; set; }

        public List<McpToolProgressEntry> McpToolProgressItems { get; private set; }

        public List<string> ChangedFiles { get; private set; }

        public List<string> SkillNames { get; private set; }

        public List<string> PendingSkillNames { get; private set; }

        public string StatusText { get; set; }

        public long? LastTurnTotalTokens { get; set; }

        public long? TotalTokens { get; set; }

        public bool IsFinalized { get; set; }
    }

    internal sealed class ReasoningSummaryEntry
    {
        public string ItemId { get; set; }

        public string Text { get; set; }
    }

    internal sealed class McpToolProgressEntry
    {
        public string ItemId { get; set; }

        public string ServerName { get; set; }

        public string ToolName { get; set; }

        public string Message { get; set; }

        public string Status { get; set; }

        public bool IsFinalized { get; set; }

        public DateTime UpdatedAt { get; set; }
    }
}
