using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerInteractionRequestMapperTests
    {
        [Test]
        public void BuildPendingApprovalRequest_MapsCommandApprovalFields()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "threadId", "thread-1" },
                    { "turnId", "turn-1" },
                    { "itemId", "item-1" },
                    { "cwd", "/mnt/e/Projects/demo" },
                    { "command", "dotnet test" },
                    { "commandActions", new object[] { "run", "read" } },
                    { "availableDecisions", new object[] { "accept" } }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingApprovalRequest(
                    rawRequestId: 7,
                    requestId: "approval-1",
                    kind: AppServerApprovalRequestKind.Command,
                    parameters: parameters,
                    mapServerPathToClient: MapServerPathToClient);

                Assert.That(request.RawRequestId, Is.EqualTo(7));
                Assert.That(request.RequestId, Is.EqualTo("approval-1"));
                Assert.That(request.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(request.TurnId, Is.EqualTo("turn-1"));
                Assert.That(request.ItemId, Is.EqualTo("item-1"));
                Assert.That(request.CommandText, Is.EqualTo("dotnet test"));
                Assert.That(request.WorkingDirectory, Is.EqualTo(@"E:\Projects\demo"));
                Assert.That(request.Content, Is.EqualTo("Received command approval request: dotnet test"));
                Assert.That(request.Details, Is.EqualTo("Command actions: 2"));
                Assert.That(request.Classification.Category, Is.EqualTo(ApprovalRequestCategory.CommandTest));
                Assert.That(request.Classification.DisplayCommand, Is.EqualTo("dotnet test"));
                Assert.That(request.CanApproveForSession, Is.False);
            });
        }

        [Test]
        public void BuildPendingApprovalRequest_BuildsPermissionDetails()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "reason", "Need broader access" },
                    {
                        "permissions",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            {
                                "fileSystem",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    { "read", new object[] { @"E:\ReadOnly" } },
                                    { "write", new object[] { @"E:\Writable" } }
                                }
                            },
                            {
                                "network",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    { "enabled", true }
                                }
                            }
                        }
                    }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingApprovalRequest(
                    rawRequestId: "raw",
                    requestId: "approval-2",
                    kind: AppServerApprovalRequestKind.Permissions,
                    parameters: parameters,
                    mapServerPathToClient: null);

                Assert.That(request.Content, Is.EqualTo("Received permission approval request: Need broader access"));
                Assert.That(request.Details, Does.Contain(@"Read: E:\ReadOnly"));
                Assert.That(request.Details, Does.Contain(@"Write: E:\Writable"));
                Assert.That(request.Details, Does.Contain("Network: allowed"));
                Assert.That(request.Classification.Category, Is.EqualTo(ApprovalRequestCategory.PermissionMixed));
                Assert.That(request.Classification.Confidence, Is.EqualTo(ApprovalRequestConfidence.ExactFromProtocol));
                Assert.That(request.CanApproveForSession, Is.True);
            });
        }

        [Test]
        public void BuildPendingApprovalRequest_BuildsFileChangeDetails()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "grantRoot", @"E:\MyCodex" },
                    {
                        "changes",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "path", @"src\old file.txt" },
                                { "kind", "delete" }
                            }
                        }
                    }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingApprovalRequest(
                    rawRequestId: "raw",
                    requestId: "approval-3",
                    kind: AppServerApprovalRequestKind.FileChange,
                    parameters: parameters,
                    mapServerPathToClient: null);

                Assert.That(request.Details, Does.Contain(@"Deleted src\old file.txt"));
                Assert.That(request.Details, Does.Contain(@"Write root: E:\MyCodex"));
                Assert.That(request.Classification.Category, Is.EqualTo(ApprovalRequestCategory.FileDelete));
                Assert.That(request.Classification.Targets, Is.EqualTo(new[] { @"src\old file.txt" }));
            });
        }

        [Test]
        public void BuildPendingApprovalRequest_BuildsFileChangeDetailsFromExpandedFieldNames()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "changes",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "targetPath", @"src\new file.txt" },
                                { "action", "write" }
                            },
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "relativePath", @"src\old file.txt" },
                                { "status", "unlink" }
                            }
                        }
                    }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingApprovalRequest(
                    rawRequestId: "raw",
                    requestId: "approval-expanded-file-change",
                    kind: AppServerApprovalRequestKind.FileChange,
                    parameters: parameters,
                    mapServerPathToClient: null);

                Assert.That(request.Details, Does.Contain(@"Added src\new file.txt"));
                Assert.That(request.Details, Does.Contain(@"Deleted src\old file.txt"));
                Assert.That(request.Classification.Category, Is.EqualTo(ApprovalRequestCategory.FileMixed));
                Assert.That(request.Classification.Targets, Is.EqualTo(new[] { @"src\new file.txt", @"src\old file.txt" }));
            });
        }

        [Test]
        public void BuildPendingApprovalRequest_BuildsSingleFileChangeDetailsFromTopLevelFields()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "targetPath", @"approval-flow-test.txt" },
                    { "action", "write_file" }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingApprovalRequest(
                    rawRequestId: "raw",
                    requestId: "approval-top-level-file-change",
                    kind: AppServerApprovalRequestKind.FileChange,
                    parameters: parameters,
                    mapServerPathToClient: null);

                Assert.That(request.Details, Is.EqualTo(@"Added approval-flow-test.txt"));
                Assert.That(request.Classification.Category, Is.EqualTo(ApprovalRequestCategory.FileCreate));
                Assert.That(request.Classification.Targets, Is.EqualTo(new[] { "approval-flow-test.txt" }));
            });
        }

        [Test]
        public void BuildPendingToolUserInputRequest_ParsesQuestionOptions()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "threadId", "thread-1" },
                    { "turnId", "turn-1" },
                    { "itemId", "tool-1" },
                    {
                        "questions",
                        new object[]
                        {
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "question", "Ignored because id is missing" }
                            },
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "id", "target" },
                                { "header", "Pick target" },
                                { "question", "Which target should be used?" },
                                { "description", "Choose where the generated files should be created." },
                                { "isOther", true },
                                { "isSecret", false },
                                {
                                    "options",
                                    new object[]
                                    {
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "label", "Local" },
                                            { "description", "Use local target" }
                                        },
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "description", "Ignored because label is missing" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingToolUserInputRequest(
                    rawRequestId: 9,
                    requestId: "tool-input-1",
                    parameters: parameters);

                Assert.That(request.Content, Is.EqualTo("Received tool input request: Pick target"));
                Assert.That(request.Details, Is.EqualTo("Question count: 1"));
                Assert.That(request.Questions, Has.Count.EqualTo(1));
                Assert.That(request.Questions[0].Id, Is.EqualTo("target"));
                Assert.That(request.Questions[0].Description, Is.EqualTo("Choose where the generated files should be created."));
                Assert.That(request.Questions[0].IsOther, Is.True);
                Assert.That(request.Questions[0].Options, Has.Count.EqualTo(1));
                Assert.That(request.Questions[0].Options[0].Label, Is.EqualTo("Local"));
            });
        }

        [Test]
        public void BuildPendingMcpElicitationRequest_MapsFormSchemaToQuestions()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "serverName", "browser-use" },
                    { "threadId", "thread-1" },
                    { "turnId", "turn-1" },
                    { "mode", "form" },
                    { "message", "Choose browser options." },
                    {
                        "requestedSchema",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "type", "object" },
                            { "required", new object[] { "browser" } },
                            {
                                "properties",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    {
                                        "browser",
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "type", "string" },
                                            { "title", "Browser" },
                                            { "description", "Browser target." },
                                            { "enum", new object[] { "chrome", "edge" } },
                                            { "enumNames", new object[] { "Chrome", "Microsoft Edge" } }
                                        }
                                    },
                                    {
                                        "headless",
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "type", "boolean" },
                                            { "title", "Headless" }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(
                    rawRequestId: 11,
                    requestId: "mcp-input-1",
                    parameters: parameters);

                Assert.That(request.IsMcpElicitation, Is.True);
                Assert.That(request.McpServerName, Is.EqualTo("browser-use"));
                Assert.That(request.Content, Is.EqualTo("MCP server browser-use requested additional input: Choose browser options."));
                Assert.That(request.Questions, Has.Count.EqualTo(2));
                Assert.That(request.Questions[0].Id, Is.EqualTo("browser"));
                Assert.That(request.Questions[0].Description, Does.Contain("Browser target."));
                Assert.That(request.Questions[0].Description, Does.Contain("Required."));
                Assert.That(request.Questions[0].Options[1].Label, Is.EqualTo("Microsoft Edge"));
                Assert.That(request.Questions[0].Options[1].Value, Is.EqualTo("edge"));
                Assert.That(request.Questions[1].Options[0].Value, Is.EqualTo("true"));
                Assert.That(request.Questions[1].Options[1].Value, Is.EqualTo("false"));
            });
        }

        [Test]
        public void BuildPendingMcpElicitationRequest_MapsEmptyObjectSchemaToConfirmation()
        {
            WithCulture("en-US", () =>
            {
                var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "serverName", "codex_apps" },
                    { "mode", "form" },
                    { "message", "GitHub access will make future reviews more complete." },
                    {
                        "requestedSchema",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>(StringComparer.Ordinal) }
                        }
                    }
                };

                var request = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(
                    rawRequestId: 12,
                    requestId: "mcp-confirmation-1",
                    parameters: parameters);

                Assert.That(request.IsMcpElicitationConfirmation, Is.True);
                Assert.That(request.Questions, Is.Empty);
                Assert.That(
                    request.Content,
                    Is.EqualTo("MCP server codex_apps requested confirmation: GitHub access will make future reviews more complete."));

                var response = AppServerInteractionRequestMapper.BuildToolUserInputResponse(
                    request,
                    new Dictionary<string, string[]>(StringComparer.Ordinal));

                Assert.That(response["action"], Is.EqualTo("accept"));
                Assert.That((IDictionary<string, object>)response["content"], Is.Empty);
            });
        }

        [Test]
        public void BuildPendingMcpElicitationRequest_DoesNotTreatExtendedFormAsConfirmation()
        {
            var extendedFormRequest = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(
                rawRequestId: 13,
                requestId: "mcp-unsupported-1",
                parameters: new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "serverName", "codex_apps" },
                    { "mode", "openai/form" },
                    { "message", "Choose an image." }
                });
            var invalidSchemaRequest = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(
                rawRequestId: 14,
                requestId: "mcp-unsupported-2",
                parameters: new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "serverName", "codex_apps" },
                    { "mode", "form" },
                    {
                        "requestedSchema",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            { "type", "string" }
                        }
                    }
                });

            Assert.That(extendedFormRequest.IsMcpElicitationConfirmation, Is.False);
            Assert.That(extendedFormRequest.Questions, Is.Empty);
            Assert.That(invalidSchemaRequest.IsMcpElicitationConfirmation, Is.False);
            Assert.That(invalidSchemaRequest.Questions, Is.Empty);
        }

        [Test]
        public void BuildToolUserInputResponse_MapsMcpFormAnswersToTypedContent()
        {
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "serverName", "browser-use" },
                { "mode", "form" },
                {
                    "requestedSchema",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        { "type", "object" },
                        {
                            "properties",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "count", new Dictionary<string, object>(StringComparer.Ordinal) { { "type", "integer" } } },
                                { "enabled", new Dictionary<string, object>(StringComparer.Ordinal) { { "type", "boolean" } } },
                                {
                                    "tags",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "type", "array" },
                                        { "items", new Dictionary<string, object>(StringComparer.Ordinal) { { "type", "string" } } }
                                    }
                                }
                            }
                        }
                    }
                }
            };
            var request = AppServerInteractionRequestMapper.BuildPendingMcpElicitationRequest(12, "mcp-input-2", parameters);
            var response = AppServerInteractionRequestMapper.BuildToolUserInputResponse(
                request,
                new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    { "count", new[] { "3" } },
                    { "enabled", new[] { "true" } },
                    { "tags", new[] { "alpha, beta" } }
                });

            Assert.That(response["action"], Is.EqualTo("accept"));
            var content = (IDictionary<string, object>)response["content"];
            Assert.That(content["count"], Is.EqualTo(3L));
            Assert.That(content["enabled"], Is.EqualTo(true));
            Assert.That((object[])content["tags"], Is.EqualTo(new object[] { "alpha", "beta" }));
        }

        [Test]
        public void BuildMcpElicitationCancelResponse_UsesProtocolAction()
        {
            var response = AppServerInteractionRequestMapper.BuildMcpElicitationCancelResponse();

            Assert.That(response["action"], Is.EqualTo("cancel"));
        }

        [Test]
        public void BuildApprovalResolvedMessage_FormatsCommandResolution()
        {
            WithCulture("en-US", () =>
            {
                var request = new AppServerPendingApprovalRequest
                {
                    Kind = AppServerApprovalRequestKind.Command,
                    CommandText = "git status"
                };

                var message = AppServerInteractionRequestMapper.BuildApprovalResolvedMessage(
                    request,
                    AppServerApprovalDecision.AllowForSession);

                Assert.That(message, Is.EqualTo("Allowed for this session command approval: git status"));
            });
        }

        [Test]
        public void BuildPermissionsApprovalResponse_MapsDecisionToScope()
        {
            var permissions = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "network", new Dictionary<string, object>(StringComparer.Ordinal) { { "enabled", true } } }
            };
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "permissions", permissions }
            };

            var allowForSession = AppServerInteractionRequestMapper.BuildPermissionsApprovalResponse(
                parameters,
                AppServerApprovalDecision.AllowForSession);
            var allowOnce = AppServerInteractionRequestMapper.BuildPermissionsApprovalResponse(
                parameters,
                AppServerApprovalDecision.AllowOnce);

            Assert.That(allowForSession["permissions"], Is.SameAs(permissions));
            Assert.That(allowForSession["scope"], Is.EqualTo("session"));
            Assert.That(allowOnce["scope"], Is.EqualTo("turn"));
        }

        [Test]
        public void MapApprovalDecision_UsesServerDecisionNames()
        {
            Assert.That(
                AppServerInteractionRequestMapper.MapCommandApprovalDecision(AppServerApprovalDecision.AllowOnce),
                Is.EqualTo("accept"));
            Assert.That(
                AppServerInteractionRequestMapper.MapCommandApprovalDecision(AppServerApprovalDecision.AllowForSession),
                Is.EqualTo("acceptForSession"));
            Assert.That(
                AppServerInteractionRequestMapper.MapFileChangeApprovalDecision(AppServerApprovalDecision.Decline),
                Is.EqualTo("decline"));
        }

        private static string MapServerPathToClient(string path)
        {
            return string.Equals(path, "/mnt/e/Projects/demo", StringComparison.Ordinal)
                ? @"E:\Projects\demo"
                : path;
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            }
        }
    }
}
