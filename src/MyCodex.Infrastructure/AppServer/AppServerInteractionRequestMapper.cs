using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerInteractionRequestMapper
    {
        internal static AppServerPendingApprovalRequest BuildPendingApprovalRequest(
            object rawRequestId,
            string requestId,
            AppServerApprovalRequestKind kind,
            IDictionary<string, object> parameters,
            Func<string, string> mapServerPathToClient)
        {
            var threadId = AppServerJsonValue.GetString(parameters, "threadId");
            var turnId = AppServerJsonValue.GetString(parameters, "turnId");
            var itemId = AppServerJsonValue.GetString(parameters, "itemId");
            var reason = AppServerJsonValue.GetString(parameters, "reason");
            var workingDirectory = MapServerPathToClient(mapServerPathToClient, AppServerJsonValue.GetString(parameters, "cwd"));
            var commandText = AppServerJsonValue.GetString(parameters, "command");
            var details = BuildApprovalDetails(kind, parameters);
            var content = BuildApprovalRequestMessage(kind, commandText, reason);
            var classification = ApprovalRequestClassifier.Classify(kind, commandText, details);

            return new AppServerPendingApprovalRequest
            {
                RawRequestId = rawRequestId,
                RequestId = requestId,
                Kind = kind,
                Parameters = parameters,
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = itemId,
                CommandText = commandText,
                WorkingDirectory = workingDirectory,
                Reason = reason,
                Details = details,
                Content = content,
                Classification = classification,
                CanApproveForSession = kind != AppServerApprovalRequestKind.Command || HasAvailableDecision(parameters, "acceptForSession")
            };
        }

        internal static AppServerPendingToolUserInputRequest BuildPendingToolUserInputRequest(
            object rawRequestId,
            string requestId,
            IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValue.GetString(parameters, "threadId");
            var turnId = AppServerJsonValue.GetString(parameters, "turnId");
            var itemId = AppServerJsonValue.GetString(parameters, "itemId");
            var questions = BuildToolUserInputQuestions(parameters);
            var content = BuildToolUserInputMessage(questions);

            return new AppServerPendingToolUserInputRequest
            {
                RawRequestId = rawRequestId,
                RequestId = requestId,
                ThreadId = threadId,
                TurnId = turnId,
                ItemId = itemId,
                Questions = questions,
                Content = content,
                Details = LF("AppServerClient.ToolInput.QuestionCount.Template", "Question count: {0}", questions.Count)
            };
        }

        internal static List<AppServerToolUserInputQuestion> BuildAsyncUserInputQuestions(
            IDictionary<string, object> item)
        {
            var result = new List<AppServerToolUserInputQuestion>();
            var index = 0;
            foreach (var questionObject in AppServerJsonValueReader.GetArray(item, "questions"))
            {
                var question = questionObject as IDictionary<string, object>;
                var title = AppServerJsonValue.GetString(question, "title");
                if (string.IsNullOrWhiteSpace(title))
                {
                    continue;
                }

                index++;
                var mapped = new AppServerToolUserInputQuestion
                {
                    Id = "async-" + index.ToString(CultureInfo.InvariantCulture),
                    Question = title.Trim()
                };

                foreach (var optionObject in AppServerJsonValueReader.GetArray(question, "options"))
                {
                    var label = Convert.ToString(optionObject, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(label))
                    {
                        mapped.Options.Add(new AppServerToolUserInputOption
                        {
                            Label = label.Trim(),
                            Value = label.Trim()
                        });
                    }
                }

                result.Add(mapped);
            }

            return result;
        }

        internal static AppServerPendingToolUserInputRequest BuildPendingMcpElicitationRequest(
            object rawRequestId,
            string requestId,
            IDictionary<string, object> parameters)
        {
            var threadId = AppServerJsonValue.GetString(parameters, "threadId");
            var turnId = AppServerJsonValue.GetString(parameters, "turnId");
            var mode = (AppServerJsonValue.GetString(parameters, "mode") ?? string.Empty).Trim().ToLowerInvariant();
            var serverName = AppServerJsonValue.GetString(parameters, "serverName");
            var message = AppServerJsonValue.GetString(parameters, "message");
            var url = AppServerJsonValue.GetString(parameters, "url");
            var questions = string.Equals(mode, "form", StringComparison.Ordinal)
                ? BuildMcpElicitationQuestions(parameters)
                : new List<AppServerToolUserInputQuestion>();
            var requestedSchema = AppServerJsonValue.GetDictionary(parameters, "requestedSchema");
            object requestedSchemaValue;
            var hasRequestedSchema = parameters != null &&
                parameters.TryGetValue("requestedSchema", out requestedSchemaValue) &&
                requestedSchemaValue != null;
            var requestedSchemaType = (AppServerJsonValue.GetString(requestedSchema, "type") ?? string.Empty).Trim();
            var isConfirmation = string.Equals(mode, "form", StringComparison.Ordinal) &&
                (!hasRequestedSchema ||
                    (string.Equals(requestedSchemaType, "object", StringComparison.OrdinalIgnoreCase) &&
                    AppServerJsonValue.GetDictionary(requestedSchema, "properties").Count == 0));

            return new AppServerPendingToolUserInputRequest
            {
                RawRequestId = rawRequestId,
                RequestId = requestId,
                Parameters = parameters,
                ThreadId = threadId,
                TurnId = turnId,
                Questions = AppServerMcpToolApprovalMapper.IsToolApproval(parameters)
                    ? AppServerMcpToolApprovalMapper.BuildChoices(parameters) : questions,
                Content = BuildMcpElicitationMessage(serverName, message, mode, isConfirmation),
                Details = string.IsNullOrWhiteSpace(url) ? message : url,
                IsMcpElicitation = true,
                IsMcpElicitationConfirmation = isConfirmation,
                McpElicitationMode = mode,
                McpServerName = serverName,
                McpMessage = message,
                McpUrl = url
            };
        }

        internal static IDictionary<string, object> BuildToolUserInputResponse(
            AppServerPendingToolUserInputRequest pendingRequest,
            IDictionary<string, string[]> answers)
        {
            if (pendingRequest != null && pendingRequest.IsMcpElicitation)
            {
                if (AppServerMcpToolApprovalMapper.IsToolApproval(pendingRequest.Parameters))
                    return AppServerMcpToolApprovalMapper.BuildResponse(pendingRequest.Parameters, answers);

                var response = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "action", "accept" }
                };

                if (string.Equals(pendingRequest.McpElicitationMode, "form", StringComparison.OrdinalIgnoreCase))
                {
                    response["content"] = BuildMcpElicitationContent(pendingRequest, answers);
                }

                return response;
            }

            var answerMap = new Dictionary<string, object>(StringComparer.Ordinal);
            if (answers != null)
            {
                foreach (var pair in answers)
                {
                    if (string.IsNullOrWhiteSpace(pair.Key))
                    {
                        continue;
                    }

                    answerMap[pair.Key] = new Dictionary<string, object>
                    {
                        { "answers", pair.Value ?? Array.Empty<string>() }
                    };
                }
            }

            return new Dictionary<string, object>
            {
                { "answers", answerMap }
            };
        }

        internal static IDictionary<string, object> BuildMcpElicitationCancelResponse()
        {
            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "action", "cancel" }
            };
        }

        internal static IDictionary<string, object> BuildApprovalResponse(
            AppServerPendingApprovalRequest pendingRequest,
            AppServerApprovalDecision decision)
        {
            switch (pendingRequest.Kind)
            {
                case AppServerApprovalRequestKind.Command:
                    return new Dictionary<string, object>
                    {
                        { "decision", MapCommandApprovalDecision(decision) }
                    };
                case AppServerApprovalRequestKind.FileChange:
                    return new Dictionary<string, object>
                    {
                        { "decision", MapFileChangeApprovalDecision(decision) }
                    };
                case AppServerApprovalRequestKind.Permissions:
                    return BuildPermissionsApprovalResponse(pendingRequest.Parameters, decision);
                default:
                    return new Dictionary<string, object>();
            }
        }

        internal static string BuildApprovalResolvedMessage(AppServerPendingApprovalRequest pendingRequest, AppServerApprovalDecision decision)
        {
            string actionText;
            switch (decision)
            {
                case AppServerApprovalDecision.AllowForSession:
                    actionText = L("AppServerClient.Approval.Action.AllowForSession", "Allowed for this session");
                    break;
                case AppServerApprovalDecision.Decline:
                    actionText = L("AppServerClient.Approval.Action.Rejected", "Rejected");
                    break;
                default:
                    actionText = L("AppServerClient.Approval.Action.AllowOnce", "Allowed once");
                    break;
            }

            switch (pendingRequest.Kind)
            {
                case AppServerApprovalRequestKind.Command:
                    return LF(
                        "AppServerClient.Approval.Command.Resolved.Template",
                        "{0} command approval: {1}",
                        actionText,
                        string.IsNullOrWhiteSpace(pendingRequest.CommandText)
                            ? L("AppServerClient.Value.UnknownCommand", "(Unknown command)")
                            : pendingRequest.CommandText);
                case AppServerApprovalRequestKind.FileChange:
                    return actionText + L("AppServerClient.Approval.FileChange.ResolvedSuffix", " file-change approval.");
                case AppServerApprovalRequestKind.Permissions:
                    return actionText + L("AppServerClient.Approval.Permissions.ResolvedSuffix", " permission approval.");
                default:
                    return actionText + L("AppServerClient.Approval.ResolvedSuffix", " approval request.");
            }
        }

        internal static string BuildApprovalResolvedMessage(
            AppServerPendingApprovalRequest pendingRequest,
            AppServerApprovalDecision decision,
            string source)
        {
            var baseMessage = BuildApprovalResolvedMessage(pendingRequest, decision);
            return string.IsNullOrWhiteSpace(source)
                ? baseMessage
                : baseMessage + " (" + source + ")";
        }

        internal static string MapCommandApprovalDecision(AppServerApprovalDecision decision)
        {
            switch (decision)
            {
                case AppServerApprovalDecision.AllowForSession:
                    return "acceptForSession";
                case AppServerApprovalDecision.Decline:
                    return "decline";
                default:
                    return "accept";
            }
        }

        internal static string MapFileChangeApprovalDecision(AppServerApprovalDecision decision)
        {
            switch (decision)
            {
                case AppServerApprovalDecision.AllowForSession:
                    return "acceptForSession";
                case AppServerApprovalDecision.Decline:
                    return "decline";
                default:
                    return "accept";
            }
        }

        internal static IDictionary<string, object> BuildPermissionsApprovalResponse(
            IDictionary<string, object> parameters,
            AppServerApprovalDecision decision)
        {
            return new Dictionary<string, object>
            {
                { "permissions", AppServerJsonValue.GetDictionary(parameters, "permissions") },
                { "scope", decision == AppServerApprovalDecision.AllowForSession ? "session" : "turn" }
            };
        }

        internal static bool HasAvailableDecision(IDictionary<string, object> parameters, string decision)
        {
            var decisions = GetList(parameters, "availableDecisions");
            if (decisions == null || decisions.Count == 0)
            {
                return true;
            }

            foreach (var value in decisions)
            {
                var text = value as string;
                if (string.Equals(text, decision, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildApprovalRequestMessage(
            AppServerApprovalRequestKind kind,
            string commandText,
            string reason)
        {
            switch (kind)
            {
                case AppServerApprovalRequestKind.Command:
                    return LF(
                        "AppServerClient.Approval.Command.Requested.Template",
                        "Received command approval request: {0}",
                        string.IsNullOrWhiteSpace(commandText)
                            ? L("AppServerClient.Value.UnknownCommand", "(Unknown command)")
                            : commandText);
                case AppServerApprovalRequestKind.FileChange:
                    return string.IsNullOrWhiteSpace(reason)
                        ? L("AppServerClient.Approval.FileChange.Requested", "Received file-change approval request.")
                        : L("AppServerClient.Approval.FileChange.RequestedWithReason.Prefix", "Received file-change approval request: ") + reason;
                case AppServerApprovalRequestKind.Permissions:
                    return string.IsNullOrWhiteSpace(reason)
                        ? L("AppServerClient.Approval.Permissions.Requested", "Received permission approval request.")
                        : L("AppServerClient.Approval.Permissions.RequestedWithReason.Prefix", "Received permission approval request: ") + reason;
                default:
                    return L("AppServerClient.Approval.Requested", "Received approval request.");
            }
        }

        private static string BuildApprovalDetails(AppServerApprovalRequestKind kind, IDictionary<string, object> parameters)
        {
            switch (kind)
            {
                case AppServerApprovalRequestKind.Command:
                    var commandActions = GetList(parameters, "commandActions");
                    return commandActions != null && commandActions.Count > 0
                        ? L("AppServerClient.Approval.CommandActions.Prefix", "Command actions: ") + commandActions.Count
                        : string.Empty;
                case AppServerApprovalRequestKind.FileChange:
                    return BuildFileChangeApprovalDetails(parameters);
                case AppServerApprovalRequestKind.Permissions:
                    return BuildPermissionsDetails(AppServerJsonValue.GetDictionary(parameters, "permissions"));
                default:
                    return string.Empty;
            }
        }

        private static string BuildFileChangeApprovalDetails(IDictionary<string, object> parameters)
        {
            var lines = new List<string>();
            AddFileChangeApprovalLines(lines, AppServerJsonValueReader.GetArray(parameters, "changes"));
            if (lines.Count == 0)
            {
                var line = BuildFileChangeApprovalLine(parameters);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }

            if (lines.Count > 8)
            {
                var remaining = lines.Count - 8;
                lines = lines.GetRange(0, 8);
                lines.Add(L("AppServerClient.FileChange.MoreFiles.Prefix", "And ") + remaining + L("AppServerClient.FileChange.MoreFiles.Suffix", " more files"));
            }

            var grantRoot = AppServerJsonValue.GetString(parameters, "grantRoot");
            if (!string.IsNullOrWhiteSpace(grantRoot))
            {
                lines.Add(L("AppServerClient.Approval.WriteRoot.Prefix", "Write root: ") + grantRoot);
            }

            return string.Join(Environment.NewLine, lines.ToArray()).Trim();
        }

        private static void AddFileChangeApprovalLines(ICollection<string> lines, IEnumerable<object> changeObjects)
        {
            if (lines == null || changeObjects == null)
            {
                return;
            }

            foreach (var changeObject in changeObjects)
            {
                var change = changeObject as IDictionary<string, object>;
                if (change == null)
                {
                    continue;
                }

                var line = BuildFileChangeApprovalLine(change);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                }
            }
        }

        private static string BuildFileChangeApprovalLine(IDictionary<string, object> change)
        {
            var path = AppServerTurnPayloadMapper.NormalizeChangedFilePath(AppServerJsonValueReader.GetFirstNonEmptyString(
                change,
                "path",
                "filePath",
                "targetPath",
                "relativePath",
                "displayPath",
                "sourcePath",
                "oldPath",
                "file",
                "name",
                "uri"));
            var movePath = AppServerTurnPayloadMapper.NormalizeChangedFilePath(AppServerJsonValueReader.GetFirstNonEmptyString(
                change,
                "move_path",
                "movePath",
                "toPath",
                "newPath",
                "destinationPath"));
            var kind = NormalizeFileChangeKind(AppServerJsonValueReader.GetFirstNonEmptyString(
                change,
                "kind",
                "operation",
                "type",
                "changeType",
                "action",
                "status",
                "event",
                "change"));

            switch (kind)
            {
                case "add":
                case "added":
                case "create":
                case "created":
                case "new":
                case "write":
                case "written":
                case "write_file":
                case "create_file":
                    return L("AppServerClient.FileChange.LinePrefix.Added", "Added ") + (path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"));
                case "delete":
                case "deleted":
                case "remove":
                case "removed":
                case "unlink":
                case "delete_file":
                case "remove_file":
                    return L("AppServerClient.FileChange.LinePrefix.Deleted", "Deleted ") + (path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"));
                case "update":
                case "updated":
                case "modify":
                case "modified":
                case "edit":
                case "edited":
                case "patch":
                case "patched":
                case "rewrite":
                    return !string.IsNullOrWhiteSpace(movePath)
                        ? LF("AppServerClient.FileChange.Line.Moved.Template", "Moved {0} -> {1}", path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"), movePath)
                        : L("AppServerClient.FileChange.LinePrefix.Modified", "Modified ") + (path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"));
                case "move":
                case "moved":
                case "rename":
                case "renamed":
                    return LF("AppServerClient.FileChange.Line.Moved.Template", "Moved {0} -> {1}", path ?? L("AppServerClient.Value.UnknownFile", "(Unknown file)"), string.IsNullOrWhiteSpace(movePath) ? L("AppServerClient.Value.UnknownFile", "(Unknown file)") : movePath);
                default:
                    return string.IsNullOrWhiteSpace(path)
                        ? string.Empty
                        : L("AppServerClient.FileChange.LinePrefix.Processed", "Processed ") + path;
            }
        }

        private static string NormalizeFileChangeKind(string kind)
        {
            return (kind ?? string.Empty).Trim().Replace('-', '_').ToLowerInvariant();
        }

        private static List<AppServerToolUserInputQuestion> BuildToolUserInputQuestions(IDictionary<string, object> parameters)
        {
            var result = new List<AppServerToolUserInputQuestion>();
            foreach (var questionObject in AppServerJsonValueReader.GetArray(parameters, "questions"))
            {
                var question = questionObject as IDictionary<string, object>;
                if (question == null)
                {
                    continue;
                }

                var id = AppServerJsonValue.GetString(question, "id");
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                var item = new AppServerToolUserInputQuestion
                {
                    Id = id,
                    Header = AppServerJsonValue.GetString(question, "header"),
                    Question = AppServerJsonValue.GetString(question, "question"),
                    Description = AppServerJsonValueReader.GetFirstNonEmptyString(question, "description", "summary", "details"),
                    IsOther = AppServerJsonValueReader.GetNullableBool(question, "isOther").GetValueOrDefault(),
                    IsSecret = AppServerJsonValueReader.GetNullableBool(question, "isSecret").GetValueOrDefault()
                };

                foreach (var optionObject in AppServerJsonValueReader.GetArray(question, "options"))
                {
                    var option = optionObject as IDictionary<string, object>;
                    if (option == null)
                    {
                        continue;
                    }

                    var label = AppServerJsonValue.GetString(option, "label");
                    if (string.IsNullOrWhiteSpace(label))
                    {
                        continue;
                    }

                    item.Options.Add(new AppServerToolUserInputOption
                    {
                        Label = label,
                        Value = AppServerJsonValue.GetString(option, "value"),
                        Description = AppServerJsonValue.GetString(option, "description")
                    });
                }

                result.Add(item);
            }

            return result;
        }

        private static List<AppServerToolUserInputQuestion> BuildMcpElicitationQuestions(IDictionary<string, object> parameters)
        {
            var result = new List<AppServerToolUserInputQuestion>();
            var requestedSchema = AppServerJsonValue.GetDictionary(parameters, "requestedSchema");
            var properties = AppServerJsonValue.GetDictionary(requestedSchema, "properties");
            var required = new HashSet<string>(StringComparer.Ordinal);
            var requiredValues = GetList(requestedSchema, "required");
            if (requiredValues != null)
            {
                foreach (var value in requiredValues)
                {
                    var text = Convert.ToString(value, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        required.Add(text);
                    }
                }
            }

            foreach (var pair in properties)
            {
                var propertySchema = pair.Value as IDictionary<string, object>;
                if (propertySchema == null || string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                var title = AppServerJsonValueReader.GetFirstNonEmptyString(propertySchema, "title") ?? pair.Key;
                var description = BuildMcpElicitationQuestionDescription(propertySchema, required.Contains(pair.Key));
                var question = new AppServerToolUserInputQuestion
                {
                    Id = pair.Key,
                    Header = title,
                    Question = title,
                    Description = description
                };

                var type = (AppServerJsonValue.GetString(propertySchema, "type") ?? string.Empty).Trim().ToLowerInvariant();
                if (string.Equals(type, "boolean", StringComparison.Ordinal))
                {
                    question.Options.Add(new AppServerToolUserInputOption
                    {
                        Label = L("AppServerClient.McpElicitation.Boolean.True", "True"),
                        Value = "true"
                    });
                    question.Options.Add(new AppServerToolUserInputOption
                    {
                        Label = L("AppServerClient.McpElicitation.Boolean.False", "False"),
                        Value = "false"
                    });
                }
                else if (!string.Equals(type, "array", StringComparison.Ordinal))
                {
                    AddMcpElicitationOptions(question, propertySchema);
                }

                result.Add(question);
            }

            return result;
        }

        private static string BuildMcpElicitationQuestionDescription(IDictionary<string, object> schema, bool required)
        {
            var parts = new List<string>();
            var description = AppServerJsonValue.GetString(schema, "description");
            if (!string.IsNullOrWhiteSpace(description))
            {
                parts.Add(description.Trim());
            }

            if (required)
            {
                parts.Add(L("AppServerClient.McpElicitation.Required", "Required."));
            }

            var type = (AppServerJsonValue.GetString(schema, "type") ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(type, "array", StringComparison.Ordinal))
            {
                var allowedValues = GetMcpElicitationAllowedValues(AppServerJsonValue.GetDictionary(schema, "items"));
                if (allowedValues.Count > 0)
                {
                    parts.Add(L("AppServerClient.McpElicitation.AllowedValues.Prefix", "Allowed values: ") + string.Join("; ", allowedValues.ToArray()));
                }

                parts.Add(L("AppServerClient.McpElicitation.ArrayInputHint", "Enter multiple values separated by commas."));
            }

            return string.Join(Environment.NewLine, parts.ToArray());
        }

        private static void AddMcpElicitationOptions(AppServerToolUserInputQuestion question, IDictionary<string, object> schema)
        {
            var options = GetMcpElicitationOptions(schema);
            foreach (var option in options)
            {
                question.Options.Add(option);
            }
        }

        private static List<AppServerToolUserInputOption> GetMcpElicitationOptions(IDictionary<string, object> schema)
        {
            var result = new List<AppServerToolUserInputOption>();
            var oneOfValues = AppServerJsonValueReader.GetArray(schema, "oneOf").Concat(AppServerJsonValueReader.GetArray(schema, "anyOf"));
            foreach (var item in oneOfValues)
            {
                var itemSchema = item as IDictionary<string, object>;
                if (itemSchema == null)
                {
                    continue;
                }

                var value = AppServerJsonValue.GetString(itemSchema, "const");
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                result.Add(new AppServerToolUserInputOption
                {
                    Label = AppServerJsonValueReader.GetFirstNonEmptyString(itemSchema, "title") ?? value,
                    Value = value,
                    Description = AppServerJsonValue.GetString(itemSchema, "description")
                });
            }

            if (result.Count > 0)
            {
                return result;
            }

            var enumValues = GetList(schema, "enum");
            if (enumValues == null || enumValues.Count == 0)
            {
                return result;
            }

            var enumNames = GetList(schema, "enumNames");
            for (var i = 0; i < enumValues.Count; i++)
            {
                var value = Convert.ToString(enumValues[i], CultureInfo.InvariantCulture);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var label = value;
                if (enumNames != null && i < enumNames.Count)
                {
                    var enumName = Convert.ToString(enumNames[i], CultureInfo.InvariantCulture);
                    if (!string.IsNullOrWhiteSpace(enumName))
                    {
                        label = enumName;
                    }
                }

                result.Add(new AppServerToolUserInputOption
                {
                    Label = label,
                    Value = value
                });
            }

            return result;
        }

        private static List<string> GetMcpElicitationAllowedValues(IDictionary<string, object> schema)
        {
            return GetMcpElicitationOptions(schema)
                .Select(option => string.IsNullOrWhiteSpace(option.Label) ? option.Value : option.Label)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToList();
        }

        private static IDictionary<string, object> BuildMcpElicitationContent(
            AppServerPendingToolUserInputRequest pendingRequest,
            IDictionary<string, string[]> answers)
        {
            var content = new Dictionary<string, object>(StringComparer.Ordinal);
            var requestedSchema = AppServerJsonValue.GetDictionary(pendingRequest.Parameters, "requestedSchema");
            var properties = AppServerJsonValue.GetDictionary(requestedSchema, "properties");
            if (answers == null)
            {
                return content;
            }

            foreach (var pair in answers)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                var schema = properties.ContainsKey(pair.Key)
                    ? properties[pair.Key] as IDictionary<string, object>
                    : null;
                content[pair.Key] = ConvertMcpElicitationValue(schema, pair.Value);
            }

            return content;
        }

        private static object ConvertMcpElicitationValue(IDictionary<string, object> schema, string[] values)
        {
            var normalizedValues = values ?? Array.Empty<string>();
            var type = (schema == null ? null : AppServerJsonValue.GetString(schema, "type") ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(type, "array", StringComparison.Ordinal))
            {
                var result = new List<object>();
                var itemSchema = AppServerJsonValue.GetDictionary(schema, "items");
                foreach (var value in SplitMcpElicitationArrayValues(normalizedValues))
                {
                    result.Add(ConvertMcpElicitationSingleValue(itemSchema, value));
                }

                return result.ToArray();
            }

            var firstValue = normalizedValues.Length == 0 ? string.Empty : normalizedValues[0];
            return ConvertMcpElicitationSingleValue(schema, firstValue);
        }

        private static object ConvertMcpElicitationSingleValue(IDictionary<string, object> schema, string value)
        {
            var type = (schema == null ? null : AppServerJsonValue.GetString(schema, "type") ?? string.Empty).Trim().ToLowerInvariant();
            if (string.Equals(type, "integer", StringComparison.Ordinal))
            {
                long integerValue;
                return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integerValue)
                    ? (object)integerValue
                    : value;
            }

            if (string.Equals(type, "number", StringComparison.Ordinal))
            {
                double numberValue;
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out numberValue)
                    ? (object)numberValue
                    : value;
            }

            if (string.Equals(type, "boolean", StringComparison.Ordinal))
            {
                bool boolValue;
                return bool.TryParse(value, out boolValue)
                    ? (object)boolValue
                    : value;
            }

            return value;
        }

        private static IEnumerable<string> SplitMcpElicitationArrayValues(string[] values)
        {
            foreach (var value in values ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var parts = value.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var item = part.Trim();
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        yield return item;
                    }
                }
            }
        }

        private static string BuildToolUserInputMessage(IReadOnlyList<AppServerToolUserInputQuestion> questions)
        {
            if (questions == null || questions.Count == 0)
            {
                return L("AppServerClient.ToolInput.Requested", "Received tool input request.");
            }

            var firstQuestion = questions[0];
            var header = !string.IsNullOrWhiteSpace(firstQuestion.Header) ? firstQuestion.Header : firstQuestion.Question;
            if (string.IsNullOrWhiteSpace(header))
            {
                return LF(
                    "AppServerClient.ToolInput.RequestedWithQuestionCount.Template",
                    "Received tool input request with {0} questions.",
                    questions.Count);
            }

            return questions.Count == 1
                ? L("AppServerClient.ToolInput.RequestedWithHeader.Prefix", "Received tool input request: ") + header
                : LF(
                    "AppServerClient.ToolInput.RequestedWithHeaderAndMore.Template",
                    "Received tool input request: {0} and {1} more questions.",
                    header,
                    questions.Count);
        }

        private static string BuildMcpElicitationMessage(
            string serverName,
            string message,
            string mode,
            bool isConfirmation)
        {
            var source = string.IsNullOrWhiteSpace(serverName)
                ? L("AppServerClient.McpElicitation.UnknownServer", "MCP server")
                : serverName;
            var prompt = string.IsNullOrWhiteSpace(message)
                ? isConfirmation
                    ? L("AppServerClient.McpElicitation.NoConfirmationMessage", "Confirmation is required.")
                    : L("AppServerClient.McpElicitation.NoMessage", "Additional input is required.")
                : message.Trim();

            return LF(
                string.Equals(mode, "url", StringComparison.OrdinalIgnoreCase)
                    ? "AppServerClient.McpElicitation.UrlRequested.Template"
                    : isConfirmation
                        ? "AppServerClient.McpElicitation.ConfirmationRequested.Template"
                    : "AppServerClient.McpElicitation.FormRequested.Template",
                string.Equals(mode, "url", StringComparison.OrdinalIgnoreCase)
                    ? "MCP server {0} requested opening a URL: {1}"
                    : isConfirmation
                        ? "MCP server {0} requested confirmation: {1}"
                    : "MCP server {0} requested additional input: {1}",
                source,
                prompt);
        }

        private static string BuildPermissionsDetails(IDictionary<string, object> permissions)
        {
            if (permissions == null || permissions.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            var fileSystem = AppServerJsonValue.GetDictionary(permissions, "fileSystem");
            var network = AppServerJsonValue.GetDictionary(permissions, "network");
            if (fileSystem.Count > 0)
            {
                var readRoots = GetList(fileSystem, "read");
                var writeRoots = GetList(fileSystem, "write");
                if (readRoots != null && readRoots.Count > 0)
                {
                    builder.Append(L("AppServerClient.Permissions.Read.Prefix", "Read: "))
                        .Append(string.Join("; ", readRoots.Cast<object>()))
                        .AppendLine();
                }

                if (writeRoots != null && writeRoots.Count > 0)
                {
                    builder.Append(L("AppServerClient.Permissions.Write.Prefix", "Write: "))
                        .Append(string.Join("; ", writeRoots.Cast<object>()))
                        .AppendLine();
                }
            }

            if (network.Count > 0 && AppServerJsonValueReader.GetNullableBool(network, "enabled").GetValueOrDefault())
            {
                builder.Append(L("AppServerClient.Permissions.NetworkAllowed", "Network: allowed"));
            }

            return builder.ToString().Trim();
        }

        private static IList<object> GetList(IDictionary<string, object> source, string key)
        {
            object value;
            if (source != null && source.TryGetValue(key, out value) && value != null)
            {
                var list = value as IList<object>;
                if (list != null)
                {
                    return list;
                }

                var array = value as object[];
                if (array != null)
                {
                    return array;
                }
            }

            return null;
        }

        private static string MapServerPathToClient(Func<string, string> mapServerPathToClient, string path)
        {
            return mapServerPathToClient == null ? path : mapServerPathToClient(path);
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? Array.Empty<object>());
        }
    }

    internal sealed class AppServerPendingApprovalRequest
    {
        public object RawRequestId { get; set; }

        public string RequestId { get; set; }

        public AppServerApprovalRequestKind Kind { get; set; }

        public IDictionary<string, object> Parameters { get; set; }

        public string ThreadId { get; set; }

        public string TurnId { get; set; }

        public string ItemId { get; set; }

        public string CommandText { get; set; }

        public string WorkingDirectory { get; set; }

        public string Reason { get; set; }

        public string Details { get; set; }

        public string Content { get; set; }

        public ApprovalRequestClassification Classification { get; set; }

        public bool CanApproveForSession { get; set; }
    }

    internal sealed class AppServerPendingToolUserInputRequest
    {
        public object RawRequestId { get; set; }

        public string RequestId { get; set; }

        public string ThreadId { get; set; }

        public string TurnId { get; set; }

        public string ItemId { get; set; }

        public IDictionary<string, object> Parameters { get; set; }

        public IReadOnlyList<AppServerToolUserInputQuestion> Questions { get; set; }

        public string Content { get; set; }

        public string Details { get; set; }

        public bool IsMcpElicitation { get; set; }

        public bool IsMcpElicitationConfirmation { get; set; }

        public string McpElicitationMode { get; set; }

        public string McpServerName { get; set; }

        public string McpMessage { get; set; }

        public string McpUrl { get; set; }
    }
}
