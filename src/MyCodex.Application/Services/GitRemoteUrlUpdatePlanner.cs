using System;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Helpers;

namespace MyCodex.Application.Services
{
    // Remote URLs are read through the Git output sanitizer, so an untouched value may hold "***"
    // in place of a credential: write back only what the user changed.
    public static class GitRemoteUrlUpdatePlanner
    {
        public const string RedactionMarker = "***";

        public static GitRemoteUrlUpdatePlan Plan(GitRemoteEntry original, string fetchUrl, string separatePushUrl)
        {
            var originalFetchUrl = original == null ? null : TextValueHelper.TrimToNull(original.FetchUrl);
            var originalPushUrl = original == null ? null : SeparatePushUrl(original.FetchUrl, original.PushUrl);
            var newFetchUrl = TextValueHelper.TrimToNull(fetchUrl);
            var newPushUrl = SeparatePushUrl(fetchUrl, separatePushUrl);
            return new GitRemoteUrlUpdatePlan
            {
                FetchUrl = string.Equals(newFetchUrl, originalFetchUrl, StringComparison.Ordinal) ? null : newFetchUrl,
                UpdatePushUrl = !string.Equals(newPushUrl, originalPushUrl, StringComparison.Ordinal),
                PushUrl = newPushUrl
            };
        }

        public static bool ContainsRedactedCredential(string url)
        {
            return url != null && url.IndexOf(RedactionMarker, StringComparison.Ordinal) >= 0;
        }

        // `git remote -v` lists the fetch URL as the push URL when no separate one is configured.
        private static string SeparatePushUrl(string fetchUrl, string pushUrl)
        {
            var push = TextValueHelper.TrimToNull(pushUrl);
            return string.Equals(push, TextValueHelper.TrimToNull(fetchUrl), StringComparison.Ordinal) ? null : push;
        }
    }

    public sealed class GitRemoteUrlUpdatePlan
    {
        // Null when the fetch URL is unchanged.
        public string FetchUrl { get; set; }

        public bool UpdatePushUrl { get; set; }

        // Null removes the separate push URL.
        public string PushUrl { get; set; }

        public bool HasChanges
        {
            get { return FetchUrl != null || UpdatePushUrl; }
        }
    }
}
