using System.Collections.Generic;

namespace MyCodex.Application.CodingRemote
{
    public sealed class CodingRemoteApprovalResponseRequest
    {
        public string RequestId { get; set; }

        public string Decision { get; set; }
    }

    public sealed class CodingRemoteApprovalResponseResult
    {
        public string RequestId { get; set; }

        public string Decision { get; set; }

        public bool Completed { get; set; }

        public string StatusText { get; set; }
    }

    public sealed class CodingRemoteToolInputRespondRequest
    {
        public string RequestId { get; set; }

        public IDictionary<string, string[]> Answers { get; set; }
    }

    public sealed class CodingRemoteToolInputCancelRequest
    {
        public string RequestId { get; set; }
    }

    public sealed class CodingRemoteToolInputResponseResult
    {
        public string RequestId { get; set; }

        public bool Completed { get; set; }

        public bool Canceled { get; set; }

        public string StatusText { get; set; }
    }

    public sealed class CodingRemotePlanExecutionResponseRequest
    {
        public string RequestId { get; set; }

        public string ThreadId { get; set; }

        public string Action { get; set; }
    }

    public sealed class CodingRemotePlanExecutionResponseResult
    {
        public string RequestId { get; set; }

        public string ThreadId { get; set; }

        public string Action { get; set; }

        public bool Completed { get; set; }

        public string StatusText { get; set; }
    }
}
