namespace MyCodex.Application.CodingRemote
{
    public interface IRemoteConversationSnapshotInvalidator
    {
        void MarkTailChanged(string mode, string sessionId);

        void MarkHistoryRewritten(string mode, string sessionId);

        void Clear();
    }
}
