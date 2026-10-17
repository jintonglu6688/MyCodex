namespace MyCodex.Application.DTOs
{
    public sealed class SessionSendResult
    {
        public SessionThreadRecreationResult RetryThreadRecreation { get; set; }

        public ThreadHistoryRepairResult InvalidEncryptedContentRepairResult { get; set; }

        public bool RecreatedThreadAfterMissingThread
        {
            get
            {
                return RetryThreadRecreation != null;
            }
        }

        public bool RepairedInvalidEncryptedContent
        {
            get
            {
                return InvalidEncryptedContentRepairResult != null &&
                       InvalidEncryptedContentRepairResult.Repaired;
            }
        }
    }
}
