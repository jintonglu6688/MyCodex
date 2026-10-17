using System;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.Instances
{
    public enum TargetInstanceOperation
    {
        Status,
        Activate,
        ApplyRuntimeSettings,
        PrepareExit,
        AbortExit,
        CommitExit,
        RemoveTarget
    }

    public enum TargetExitPreparation
    {
        None,
        Pending,
        Ready,
        Rejected
    }

    public enum TargetInstanceLifecycle
    {
        Starting,
        Running,
        Closing
    }

    public sealed class TargetInstanceStatus
    {
        public TargetInstanceLifecycle Lifecycle { get; set; }
        public bool IsWorking { get; set; }
        public bool HasPendingInteraction { get; set; }
        public bool IsConnected { get; set; }
        public bool IsPreparingBundledRuntime { get; set; }
        public string ConnectionDetail { get; set; }
        public bool IsExitPrepared { get; set; }
        public int ActiveWorkCount { get; set; }
        public int UnsavedWindowCount { get; set; }
    }

    public sealed class TargetInstanceRequest
    {
        public TargetInstanceOperation Operation { get; set; }
        public string WorkspacePath { get; set; }
        public TargetRuntimeSettings RuntimeSettings { get; set; }
        public string ChannelId { get; set; }
        public string TargetId { get; set; }
        public string InstanceId { get; set; }
        public TargetInstanceRecord Caller { get; set; }
        public string OperationId { get; set; }
        public string RemovalTargetId { get; set; }
    }

    public sealed class TargetInstanceReply
    {
        public bool Success { get; set; }
        public bool SettingsSaved { get; set; }
        public string Error { get; set; }
        public TargetInstanceStatus Status { get; set; }
        public TargetInstanceRecord Instance { get; set; }
        public TargetExitPreparation ExitPreparation { get; set; }

        [ScriptIgnore]
        public Action AfterResponse { get; set; }

        public static TargetInstanceReply Failure(string error)
        {
            return new TargetInstanceReply { Error = error };
        }
    }

    public sealed class TargetInstanceRecord
    {
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; }
        public long StartTimeUtcTicks { get; set; }
        public string UserSid { get; set; }
        public string ChannelId { get; set; }
        public string TargetId { get; set; }
        public string InstanceId { get; set; }
    }
}
