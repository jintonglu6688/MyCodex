using System;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    internal enum MainFormSendDispatchMode
    {
        TurnStart = 0,
        TurnSteer,
        Queue
    }

    internal sealed class MainFormSendDispatchContext
    {
        public string ThreadId { get; set; }

        public bool IsThreadRunning { get; set; }

        public string ActiveTurnFollowUpMode { get; set; }

        public int InputItemCount { get; set; }

        public bool IsPlanCommand { get; set; }

        public bool HasSpecialCollaborationPayload { get; set; }

        public bool ThreadWasRecreatedDuringSend { get; set; }
    }

    internal sealed class MainFormSendDispatchDecision
    {
        public MainFormSendDispatchDecision(MainFormSendDispatchMode mode, string reason)
        {
            Mode = mode;
            Reason = reason ?? string.Empty;
        }

        public MainFormSendDispatchMode Mode { get; private set; }

        public string Reason { get; private set; }
    }

    internal static class MainFormSendDispatchPlanner
    {
        internal static MainFormSendDispatchDecision Choose(MainFormSendDispatchContext context)
        {
            if (context == null)
            {
                return Start("missing-context");
            }

            if (string.IsNullOrWhiteSpace(context.ThreadId))
            {
                return Start("missing-thread");
            }

            if (!context.IsThreadRunning)
            {
                return Start("not-running");
            }

            if (context.InputItemCount <= 0)
            {
                return Start("empty-input");
            }

            if (string.Equals(
                    ActiveTurnFollowUpMode.Normalize(context.ActiveTurnFollowUpMode),
                    ActiveTurnFollowUpMode.Queue,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new MainFormSendDispatchDecision(MainFormSendDispatchMode.Queue, "follow-up-queue");
            }

            if (context.IsPlanCommand)
            {
                return Start("plan-command");
            }

            if (context.HasSpecialCollaborationPayload)
            {
                return Start("collaboration-payload");
            }

            if (context.ThreadWasRecreatedDuringSend)
            {
                return Start("thread-recreated");
            }

            return new MainFormSendDispatchDecision(MainFormSendDispatchMode.TurnSteer, "active-turn");
        }

        internal static bool ShouldApplyVisibleSessionUi(string targetSessionId, string visibleSessionId)
        {
            return !string.IsNullOrWhiteSpace(targetSessionId) &&
                !string.IsNullOrWhiteSpace(visibleSessionId) &&
                string.Equals(targetSessionId, visibleSessionId, StringComparison.OrdinalIgnoreCase);
        }

        private static MainFormSendDispatchDecision Start(string reason)
        {
            return new MainFormSendDispatchDecision(MainFormSendDispatchMode.TurnStart, reason);
        }
    }
}
