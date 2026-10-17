using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.WinForms.Forms
{
    internal enum AsyncInputDispatchOutcome
    {
        Steered,
        StartedNewTurn,
        NewerTurnRunning
    }

    internal static class AsyncInputResponseDispatcher
    {
        internal static async Task<AsyncInputDispatchOutcome> DispatchAsync(
            IAppServerThreadClient client,
            string threadId,
            string originatingTurnId,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            Func<bool> isThreadRunning,
            Func<Task> startNewTurn)
        {
            if (!string.IsNullOrWhiteSpace(originatingTurnId))
            {
                var steer = await client.TrySteerTurnAsync(threadId, inputItems, null, originatingTurnId);
                if (steer != null && steer.Accepted)
                {
                    return AsyncInputDispatchOutcome.Steered;
                }

                if (steer != null && steer.Attempted)
                {
                    await startNewTurn();
                    return AsyncInputDispatchOutcome.StartedNewTurn;
                }
            }

            if (isThreadRunning())
            {
                return AsyncInputDispatchOutcome.NewerTurnRunning;
            }

            await startNewTurn();
            return AsyncInputDispatchOutcome.StartedNewTurn;
        }
    }
}
