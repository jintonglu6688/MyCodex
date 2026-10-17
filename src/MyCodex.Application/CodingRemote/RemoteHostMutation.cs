using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Application.CodingRemote
{
    public static class RemoteHostMutationTypes
    {
        public const string WorkspaceCreated = "workspace.created";
        public const string WorkspaceArchived = "workspace.archived";
        public const string WorkspaceDeleted = "workspace.deleted";
        public const string SessionCreated = "session.created";
        public const string SessionThreadUpdated = "session.thread.updated";
        public const string SessionArchived = "session.archived";
        public const string SessionDeleted = "session.deleted";
        public const string SessionRenamed = "session.renamed";
        public const string SessionSettingsUpdated = "session.settings.updated";
        public const string SessionForkCreated = "session.fork.created";
        public const string SessionCompactRequested = "session.compact.requested";
        public const string HostSettingsUpdated = "host.settings.updated";
    }

    public sealed class RemoteHostMutation
    {
        public RemoteHostMutation()
        {
            AffectedSessionIds = new List<string>();
        }

        public string MutationType { get; set; }

        public string RuntimeProfileId { get; set; }

        public string WorkspaceId { get; set; }

        public string SessionId { get; set; }

        public List<string> AffectedSessionIds { get; set; }

        public string StatusText { get; set; }
    }

    public interface IRemoteHostMutationSink
    {
        Task PublishAsync(RemoteHostMutation mutation, CancellationToken cancellationToken);
    }
}
