using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public enum HookReviewPromptDecision
    {
        None,
        Announce,
        Show
    }

    /// <summary>
    /// Decides when hooks that need review are prompted for: once per exact set of key and hash,
    /// announced while the window cannot take a dialog and shown once it can.
    /// </summary>
    public sealed class HookReviewPromptState
    {
        private string _handledSignature;
        private string _announcedSignature;

        public static List<AppServerHookInfo> GetPending(AppServerHooksListResult list)
        {
            // Matches the official startup review: trust status alone decides, whether or not the hook is enabled.
            return list == null
                ? new List<AppServerHookInfo>()
                : list.Hooks.Where(HookStateEditPlanner.NeedsReview).ToList();
        }

        public static string GetSignature(IEnumerable<AppServerHookInfo> pending)
        {
            return string.Join("\n", pending
                .Select(hook => hook.Key + "\t" + hook.CurrentHash)
                .OrderBy(value => value, StringComparer.Ordinal));
        }

        public HookReviewPromptDecision Decide(string signature, bool canShowDialog)
        {
            if (string.IsNullOrEmpty(signature))
            {
                // Everything is reviewed, so the same hooks reappearing later should prompt again.
                _handledSignature = null;
                _announcedSignature = null;
                return HookReviewPromptDecision.None;
            }

            if (string.Equals(signature, _handledSignature, StringComparison.Ordinal))
            {
                return HookReviewPromptDecision.None;
            }

            if (canShowDialog)
            {
                _handledSignature = signature;
                return HookReviewPromptDecision.Show;
            }

            if (string.Equals(signature, _announcedSignature, StringComparison.Ordinal))
            {
                return HookReviewPromptDecision.None;
            }

            _announcedSignature = signature;
            return HookReviewPromptDecision.Announce;
        }

        public bool HasDeferredPrompt
        {
            get { return _announcedSignature != null && !string.Equals(_announcedSignature, _handledSignature, StringComparison.Ordinal); }
        }
    }
}
