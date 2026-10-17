using System;
using System.Collections;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.AppServer
{
    internal static class AppServerGuardianDeniedActionMapper
    {
        internal static AppServerGuardianDeniedActionApprovalRequest BuildRequest(
            AppServerGuardianApprovalReviewEvent review, IDictionary<string, object> action)
        {
            if (!review.IsCompleted || review.Status != "denied" || review.DecisionSource != "agent" ||
                string.IsNullOrWhiteSpace(review.ThreadId) || string.IsNullOrWhiteSpace(review.ReviewId))
            {
                return null;
            }

            var coreAction = MapAction(action);
            if (coreAction == null)
            {
                return null;
            }

            // The override endpoint takes a core GuardianAssessmentEvent, not the v2 notification.
            return new AppServerGuardianDeniedActionApprovalRequest
            {
                ThreadId = review.ThreadId,
                Action = review.Action,
                RiskLevel = review.RiskLevel,
                Rationale = review.Rationale,
                Event = new Dictionary<string, object>
                {
                    { "id", review.ReviewId },
                    { "target_item_id", review.TargetItemId },
                    { "turn_id", review.TurnId ?? string.Empty },
                    { "started_at_ms", review.StartedAtMs ?? 0 },
                    { "completed_at_ms", review.CompletedAtMs },
                    { "status", "denied" },
                    { "risk_level", review.RiskLevel },
                    { "user_authorization", review.UserAuthorization },
                    { "rationale", review.Rationale },
                    { "decision_source", "agent" },
                    { "action", coreAction }
                }
            };
        }

        private static IDictionary<string, object> MapAction(IDictionary<string, object> action)
        {
            var type = AppServerJsonValue.GetString(action, "type");
            string coreType;
            switch (type)
            {
                case "command": coreType = "command"; break;
                case "execve": coreType = "execve"; break;
                case "writeStdin": coreType = "write_stdin"; break;
                case "applyPatch": coreType = "apply_patch"; break;
                case "networkAccess": coreType = "network_access"; break;
                case "mcpToolCall": coreType = "mcp_tool_call"; break;
                case "requestPermissions": coreType = "request_permissions"; break;
                default: return null;
            }

            var result = new Dictionary<string, object>(action) { ["type"] = coreType };
            Rename(result, "approvalId", "approval_id");
            Rename(result, "processId", "process_id");
            Rename(result, "toolName", "tool_name");
            Rename(result, "connectorId", "connector_id");
            Rename(result, "connectorName", "connector_name");
            Rename(result, "toolTitle", "tool_title");
            if (type == "writeStdin")
            {
                var cwd = AppServerJsonValue.GetString(action, "cwd");
                Uri uri;
                if (cwd != null && cwd.StartsWith("/", StringComparison.Ordinal))
                {
                    // Preserve POSIX path bytes such as backslashes and literal percent signs.
                    result["cwd"] = "file://" + string.Join("/", Array.ConvertAll(cwd.Split('/'), Uri.EscapeDataString));
                }
                else if (Uri.TryCreate(cwd, UriKind.Absolute, out uri) && uri.IsFile)
                {
                    result["cwd"] = uri.AbsoluteUri;
                }
                else
                {
                    return null;
                }
            }

            if (type == "networkAccess")
            {
                var protocol = AppServerJsonValue.GetString(action, "protocol");
                result["protocol"] = protocol == "socks5Tcp" ? "socks5_tcp" :
                    protocol == "socks5Udp" ? "socks5_udp" : protocol;
            }

            if (type == "command" || type == "execve")
            {
                var source = AppServerJsonValue.GetString(action, "source");
                if (source != "shell" && source != "unifiedExec")
                {
                    return null;
                }

                result["source"] = source == "unifiedExec" ? "unified_exec" : source;
            }

            if (type == "requestPermissions")
            {
                var permissions = AppServerJsonValue.GetDictionary(action, "permissions");
                if (permissions == null)
                {
                    return null;
                }

                var mapped = new Dictionary<string, object>(permissions);
                Rename(mapped, "fileSystem", "file_system");
                var fileSystem = AppServerJsonValue.GetDictionary(mapped, "file_system");
                if (fileSystem != null)
                {
                    var mappedFileSystem = new Dictionary<string, object>();
                    object entries;
                    if (!fileSystem.TryGetValue("entries", out entries) || entries == null)
                    {
                        var legacyEntries = new List<object>();
                        AddLegacyEntries(fileSystem, "read", legacyEntries);
                        AddLegacyEntries(fileSystem, "write", legacyEntries);
                        entries = legacyEntries;
                    }

                    mappedFileSystem["entries"] = entries;
                    object depth;
                    if (fileSystem.TryGetValue("globScanMaxDepth", out depth) && depth != null)
                    {
                        mappedFileSystem["glob_scan_max_depth"] = depth;
                    }

                    mapped["file_system"] = mappedFileSystem;
                }

                result["permissions"] = mapped;
            }

            return result;
        }

        private static void AddLegacyEntries(IDictionary<string, object> fileSystem, string access, List<object> entries)
        {
            object paths;
            if (!fileSystem.TryGetValue(access, out paths) || paths == null || paths is string)
            {
                return;
            }

            foreach (var path in (IEnumerable)paths)
            {
                entries.Add(new Dictionary<string, object>
                {
                    { "path", new Dictionary<string, object> { { "type", "path" }, { "path", path } } },
                    { "access", access }
                });
            }
        }

        private static void Rename(IDictionary<string, object> value, string from, string to)
        {
            object item;
            if (value.TryGetValue(from, out item))
            {
                value.Remove(from);
                value[to] = item;
            }
        }
    }
}
