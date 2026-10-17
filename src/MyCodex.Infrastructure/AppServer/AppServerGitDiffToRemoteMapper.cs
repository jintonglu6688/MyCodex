using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerGitDiffToRemoteMapper
    {
        internal static AppServerGitDiffToRemoteResult BuildResult(IDictionary<string, object> result)
        {
            return new AppServerGitDiffToRemoteResult
            {
                Sha = AppServerJsonValue.GetString(result, "sha") ?? string.Empty,
                Diff = AppServerJsonValue.GetString(result, "diff") ?? string.Empty
            };
        }
    }
}
