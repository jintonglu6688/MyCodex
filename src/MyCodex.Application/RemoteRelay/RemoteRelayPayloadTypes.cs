namespace MyCodex.Application.RemoteRelay
{
    public static class RemoteRelayPayloadTypes
    {
        public const string Command = "remote.command";
        public const string Event = "remote.event";
        public const string Response = "remote.response";
    }

    public static class RemoteCommandTypes
    {
        public const string InteractionPendingList = "interaction.pending.list";
        public const string CodingWorkspacesList = "coding.workspaces.list";
        public const string CodingWorkspaceArchive = "coding.workspace.archive";
        public const string CodingWorkspaceDelete = "coding.workspace.delete";
        public const string CodingSessionsList = "coding.sessions.list";
        public const string CodingSessionArchive = "coding.session.archive";
        public const string CodingSessionDelete = "coding.session.delete";
        public const string CodingSessionRename = "coding.session.rename";
        public const string CodingSessionFork = "coding.session.fork";
        public const string CodingSessionCompact = "coding.session.compact";
        public const string CodingSessionStop = "coding.session.stop";
        public const string CodingSessionSettingsGet = "coding.session.settings.get";
        public const string CodingSessionSettingsUpdate = "coding.session.settings.update";
        public const string CodingMessagesList = "coding.messages.list";
        public const string CodingMessageContentRead = "coding.message.content.read";
        public const string CodingSessionCreate = "coding.session.create";
        public const string CodingMessageSend = "coding.message.send";
        public const string CodingAttachmentsPluginsList = "coding.attachments.plugins.list";
        public const string CodingAttachmentsSkillsList = "coding.attachments.skills.list";
        public const string OfficeWorkspacesList = "office.workspaces.list";
        public const string OfficeSessionsList = "office.sessions.list";
        public const string OfficeSessionArchive = "office.session.archive";
        public const string OfficeSessionDelete = "office.session.delete";
        public const string OfficeSessionRename = "office.session.rename";
        public const string OfficeSessionStop = "office.session.stop";
        public const string OfficeSessionSettingsGet = "office.session.settings.get";
        public const string OfficeSessionSettingsUpdate = "office.session.settings.update";
        public const string OfficeMessagesList = "office.messages.list";
        public const string OfficeMessageContentRead = "office.message.content.read";
        public const string OfficeSessionCreate = "office.session.create";
        public const string OfficeMessageSend = "office.message.send";
        public const string OfficeAttachmentsPluginsList = "office.attachments.plugins.list";
        public const string OfficeAttachmentsSkillsList = "office.attachments.skills.list";
        public const string ApprovalRespond = "approval.respond";
        public const string ToolInputRespond = "tool.input.respond";
        public const string ToolInputCancel = "tool.input.cancel";
        public const string PlanExecutionRespond = "plan.execution.respond";
        public const string HostSettingsGet = "host.settings.get";
        public const string HostCapabilitiesGet = "host.capabilities.get";
        public const string HostSettingsUpdate = "host.settings.update";
        public const string HostAccountGet = "host.account.get";
        public const string WriteLockAcquire = "remote.writeLock.acquire";
        public const string WriteLockRelease = "remote.writeLock.release";
        public const string WriteLockHeartbeat = "remote.writeLock.heartbeat";
    }

    public static class RemoteEventTypes
    {
        public const string InteractionPendingListResult = "interaction.pending.list.result";
        public const string CodingWorkspacesListResult = "coding.workspaces.list.result";
        public const string CodingWorkspaceOperationResult = "coding.workspace.operation.result";
        public const string CodingSessionsListResult = "coding.sessions.list.result";
        public const string CodingSessionOperationResult = "coding.session.operation.result";
        public const string CodingSessionThreadOperationResult = "coding.session.thread.operation.result";
        public const string CodingSessionStopResult = "coding.session.stop.result";
        public const string CodingSessionSettingsResult = "coding.session.settings.result";
        public const string CodingSessionSettingsUpdateResult = "coding.session.settings.update.result";
        public const string CodingMessagesListResult = "coding.messages.list.result";
        public const string CodingMessageContentResult = "coding.message.content.result";
        public const string CodingSessionCreated = "coding.session.created";
        public const string CodingMessageAccepted = "coding.message.accepted";
        public const string CodingAttachmentsPluginsListResult = "coding.attachments.plugins.list.result";
        public const string CodingAttachmentsSkillsListResult = "coding.attachments.skills.list.result";
        public const string CodingMessageDelta = "coding.message.delta";
        public const string CodingMessageCompleted = "coding.message.completed";
        public const string CodingMessageFailed = "coding.message.failed";
        public const string CodingSessionUpdated = "coding.session.updated";
        public const string CodingSessionHistoryChanged = "coding.session.history.changed";
        public const string OfficeWorkspacesListResult = "office.workspaces.list.result";
        public const string OfficeSessionsListResult = "office.sessions.list.result";
        public const string OfficeSessionOperationResult = "office.session.operation.result";
        public const string OfficeSessionStopResult = "office.session.stop.result";
        public const string OfficeSessionSettingsResult = "office.session.settings.result";
        public const string OfficeSessionSettingsUpdateResult = "office.session.settings.update.result";
        public const string OfficeMessagesListResult = "office.messages.list.result";
        public const string OfficeMessageContentResult = "office.message.content.result";
        public const string OfficeSessionCreated = "office.session.created";
        public const string OfficeMessageAccepted = "office.message.accepted";
        public const string OfficeAttachmentsPluginsListResult = "office.attachments.plugins.list.result";
        public const string OfficeAttachmentsSkillsListResult = "office.attachments.skills.list.result";
        public const string OfficeMessageCompleted = "office.message.completed";
        public const string OfficeMessageFailed = "office.message.failed";
        public const string OfficeSessionUpdated = "office.session.updated";
        public const string OfficeSessionHistoryChanged = "office.session.history.changed";
        public const string ApprovalRequested = "approval.requested";
        public const string ApprovalCompleted = "approval.completed";
        public const string ApprovalResponseResult = "approval.response.result";
        public const string ToolInputRequested = "tool.input.requested";
        public const string ToolInputCompleted = "tool.input.completed";
        public const string ToolInputResponseResult = "tool.input.response.result";
        public const string PlanExecutionRequested = "plan.execution.requested";
        public const string PlanExecutionCompleted = "plan.execution.completed";
        public const string PlanExecutionResponseResult = "plan.execution.response.result";
        public const string HostSettingsResult = "host.settings.result";
        public const string HostCapabilitiesResult = "host.capabilities.result";
        public const string HostSettingsUpdateResult = "host.settings.update.result";
        public const string HostAccountResult = "host.account.result";
        public const string WriteLockGranted = "remote.writeLock.granted";
        public const string WriteLockDenied = "remote.writeLock.denied";
        public const string RemoteError = "remote.error";
    }

    public static class RemoteErrorCodes
    {
        public const string UnsupportedCommand = "unsupported_command";
        public const string InvalidCommand = "invalid_command";
        public const string WorkspaceNotFound = "workspace_not_found";
        public const string SessionNotFound = "session_not_found";
        public const string SessionBusy = "session_busy";
        public const string TurnNotFound = "turn_not_found";
        public const string RequestNotFound = "request_not_found";
        public const string OperationNotAvailable = "operation_not_available";
        public const string WriteLockDenied = "write_lock_denied";
        public const string OperationFailed = "operation_failed";
        public const string InvalidCursor = "invalid_cursor";
        public const string HistoryChanged = "history_changed";
        public const string MessageNotFound = "message_not_found";
        public const string MessageContentChanged = "message_content_changed";
        public const string MobileUpgradeRequired = "mobile_upgrade_required";
        public const string ResponseTooLarge = "response_too_large";
    }
}
