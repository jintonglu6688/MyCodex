using System;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal static class InvalidEncryptedContentRepairWorkflow
    {
        internal static async Task<ThreadHistoryRepairResult> RepairWithAppServerRestartFallbackAsync(
            Func<Task> resetThreadAsync,
            Func<Task<ThreadHistoryRepairResult>> repairAsync,
            Func<Task> restartAppServerAsync)
        {
            if (repairAsync == null)
            {
                return ThreadHistoryRepairResult.NotRepaired("repair unavailable");
            }

            if (resetThreadAsync != null)
            {
                await resetThreadAsync().ConfigureAwait(true);
            }

            var firstResult = await repairAsync().ConfigureAwait(true);
            if (firstResult != null && (firstResult.Repaired || firstResult.IsFinal))
            {
                return firstResult;
            }

            if (restartAppServerAsync != null)
            {
                await restartAppServerAsync().ConfigureAwait(true);
            }

            var secondResult = await repairAsync().ConfigureAwait(true);
            return secondResult ?? firstResult ?? ThreadHistoryRepairResult.NotRepaired("repair failed");
        }
    }
}
