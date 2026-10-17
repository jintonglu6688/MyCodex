using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.AppServer
{
    public static class AppServerThreadStartParameterBuilder
    {
        public static IDictionary<string, object> Build(
            string serverWorkspacePath,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerThreadToolContext toolContext = null)
        {
            var parameters = new Dictionary<string, object>
            {
                { "cwd", serverWorkspacePath },
                { "experimentalRawEvents", false },
                { "persistExtendedHistory", false }
            };

            if (approvalMode.HasValue)
            {
                parameters["approvalPolicy"] = MapApprovalPolicy(approvalMode.Value);
                parameters["approvalsReviewer"] = MapApprovalsReviewer(approvalMode.Value);
                parameters["sandbox"] = MapSandboxMode(approvalMode.Value);
            }

            AddOptionalString(parameters, "model", modelId);
            AddOptionalString(parameters, "effort", reasoningEffort);
            AddProjectTrustOverride(parameters, serverWorkspacePath, approvalMode);

            if (toolContext != null &&
                toolContext.DynamicTools != null &&
                toolContext.DynamicTools.Count > 0)
            {
                parameters["dynamicTools"] = AppServerDynamicToolSpecMapper.ToWireArray(toolContext.DynamicTools);
            }

            return parameters;
        }

        internal static void AddProjectTrustOverride(
            IDictionary<string, object> parameters,
            string serverWorkspacePath,
            ApprovalMode? approvalMode)
        {
            if (parameters == null ||
                string.IsNullOrWhiteSpace(serverWorkspacePath) ||
                approvalMode == ApprovalMode.ReadOnly ||
                approvalMode == ApprovalMode.Custom)
            {
                return;
            }

            IDictionary<string, object> config = null;
            object existingConfig;
            if (parameters.TryGetValue("config", out existingConfig))
            {
                config = existingConfig as IDictionary<string, object>;
            }

            if (config == null)
            {
                config = new Dictionary<string, object>();
                parameters["config"] = config;
            }

            config["projects"] = new Dictionary<string, object>
            {
                {
                    serverWorkspacePath.Trim(),
                    new Dictionary<string, object>
                    {
                        { "trust_level", "trusted" }
                    }
                }
            };
        }

        internal static string MapApprovalsReviewer(ApprovalMode approvalMode)
        {
            return "user";
        }

        private static string MapApprovalPolicy(ApprovalMode approvalMode)
        {
            return "on-request";
        }

        private static string MapSandboxMode(ApprovalMode approvalMode)
        {
            switch (approvalMode)
            {
                case ApprovalMode.ReadOnly:
                case ApprovalMode.Custom:
                    return "read-only";
                case ApprovalMode.FullAccess:
                case ApprovalMode.FullAccessAutoApprove:
                    return "danger-full-access";
                default:
                    return "workspace-write";
            }
        }

        private static void AddOptionalString(IDictionary<string, object> target, string key, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            target[key] = value;
        }
    }
}
