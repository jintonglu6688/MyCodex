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
        private readonly HashSet<string> _threadViewsChecked = new HashSet<string>(StringComparer.Ordinal);
        private string _handledSignature;
        private string _announcedSignature;
        private string _contextKey;

        /// <summary>True once per change of the shown workspace or session, so switching re-checks but redraws do not.</summary>
        public bool EnterContext(string workspacePath, string threadId)
        {
            var key = (workspacePath ?? string.Empty) + "\n" + (threadId ?? string.Empty);
            if (string.Equals(key, _contextKey, StringComparison.Ordinal))
            {
                return false;
            }

            _contextKey = key;
            return true;
        }

        /// <summary>
        /// A resumed session is loaded only when its first turn starts; that is the first moment the thread view
        /// can include project hooks the workspace view missed, so each current thread gets one such check.
        /// </summary>
        public bool NeedsThreadViewCheck(string startedThreadId, string currentThreadId)
        {
            return !string.IsNullOrWhiteSpace(startedThreadId) &&
                string.Equals(startedThreadId, currentThreadId, StringComparison.Ordinal) &&
                !_threadViewsChecked.Contains(startedThreadId);
        }

        public void MarkThreadViewChecked(string threadId)
        {
            _threadViewsChecked.Add(threadId);
        }

        /// <summary>A reconnect unloads every thread, so each needs its thread-view check again.</summary>
        public void ResetThreadViews()
        {
            _threadViewsChecked.Clear();
        }

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
