using System;

namespace MyCodex.Application.CodingRemote
{
    public sealed class RemoteWriteLockResult
    {
        public bool IsGranted { get; set; }

        public string HolderDeviceId { get; set; }

        public string OperationId { get; set; }

        public DateTime ExpiresAtUtc { get; set; }

        public string Reason { get; set; }
    }
}
