using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient
    {
        public async Task<AuthenticationSessionHistoryState> ReadSessionHistoryAsync(CancellationToken cancellationToken)
        {
            return ReadSessionHistoryState(await _request("sessionHistory/get", Empty(), cancellationToken).ConfigureAwait(false));
        }

        public async Task<AuthenticationSessionHistoryPreview> PreviewSessionHistoryAsync(CancellationToken cancellationToken)
        {
            var result = await _request("sessionHistory/preview", Empty(), cancellationToken).ConfigureAwait(false);
            return new AuthenticationSessionHistoryPreview
            {
                ToMigrate = (int)Count(result, "toMigrate"),
                ToRestore = (int)Count(result, "toRestore")
            };
        }

        // Turning it on moves the earlier official sessions; turning it off moves them back.
        public async Task<AuthenticationSessionHistoryResult> SetSessionHistoryAsync(bool unified, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object> { { "unified", unified } };
            var result = await _request("sessionHistory/set", parameters, cancellationToken).ConfigureAwait(false);
            var state = ReadSessionHistoryState(result);
            if (state.Unified != unified || !result.TryGetValue("globalApplied", out var applied) || !(applied is bool))
                throw new AuthenticationCenterException("operation_unknown");
            return new AuthenticationSessionHistoryResult
            {
                State = state,
                GlobalApplied = (bool)applied,
                Moved = ReadSessionHistoryMove(result, "moved")
            };
        }

        public async Task<AuthenticationSessionRepairResult> RepairSessionHistoryAsync(string threadId,
            AuthenticationSessionRepairMode mode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(threadId)) throw new ArgumentException("A thread ID is required.", nameof(threadId));
            var parameters = new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "mode", mode == AuthenticationSessionRepairMode.Resume ? "resume"
                    : mode == AuthenticationSessionRepairMode.Rejected ? "rejected" : "compaction" }
            };
            var result = await _request("sessionHistory/repair", parameters, cancellationToken).ConfigureAwait(false);
            if (!result.TryGetValue("repaired", out var repaired) || !(repaired is bool))
                throw new AuthenticationCenterException("invalid_response");
            var skipped = Text(result, "skipped");
            if (skipped != null && skipped != "nothing_to_repair")
                return new AuthenticationSessionRepairResult { Skipped = skipped };
            return new AuthenticationSessionRepairResult
            {
                Repaired = (bool)repaired,
                Skipped = skipped,
                Reasoning = (int)Count(result, "reasoning"),
                Ids = (int)Count(result, "ids"),
                ConvertedCompactions = (int)Count(result, "convertedCompactions"),
                EncryptedParts = (int)Count(result, "encryptedParts"),
                ReplacedCompactions = (int)Count(result, "replacedCompactions"),
                RemainingCompactions = (int)Count(result, "remainingCompactions")
            };
        }

        private static AuthenticationSessionHistoryState ReadSessionHistoryState(IDictionary<string, object> value)
        {
            foreach (var key in new[] { "unified", "pending" })
            {
                if (!value.TryGetValue(key, out var flag) || !(flag is bool))
                    throw new AuthenticationCenterException("invalid_response");
            }
            return new AuthenticationSessionHistoryState
            {
                Unified = Flag(value, "unified"),
                Pending = Flag(value, "pending"),
                RepairOnResume = Flag(value, "repairOnResume")
            };
        }

        private static AuthenticationSessionHistoryMove ReadSessionHistoryMove(IDictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var item) || item == null) return null;
            if (!(item is IDictionary<string, object> move)) throw new AuthenticationCenterException("invalid_response");
            return new AuthenticationSessionHistoryMove
            {
                Sessions = (int)Count(move, "sessions"),
                IndexRows = (int)Count(move, "indexRows"),
                Error = Text(move, "error")
            };
        }

        private static long Count(IDictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var number) || !(number is int || number is long || number is decimal))
                throw new AuthenticationCenterException("invalid_response");
            var count = Convert.ToDecimal(number);
            if (count < 0 || count > long.MaxValue || decimal.Truncate(count) != count)
                throw new AuthenticationCenterException("invalid_response");
            return (long)count;
        }
    }
}
