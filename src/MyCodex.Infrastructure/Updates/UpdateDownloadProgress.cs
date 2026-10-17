namespace MyCodex.Infrastructure.Updates
{
    public sealed class UpdateDownloadProgress
    {
        public int Percentage { get; set; }

        public long BytesReceived { get; set; }

        public long TotalBytesToReceive { get; set; }
    }
}
