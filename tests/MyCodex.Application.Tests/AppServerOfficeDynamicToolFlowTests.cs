using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.InternalTools;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerOfficeDynamicToolFlowTests
    {
        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void OfficeModeSource_DoesNotBuildInternalToolContext()
        {
            var source = RepositorySource.ReadSourceFile("MainForm.InternalTools.cs");

            Assert.That(source, Does.Contain("session.Mode == AppInteractionMode.Office"));
            Assert.That(source, Does.Contain("return null;"));
            Assert.That(source, Does.Not.Contain("OfficialOfficeCapabilityEnabled"));
        }

        [Test]
        public async Task OfficeModeWithoutInternalTools_DoesNotExecuteLegacySlashStyleOfficeTool()
        {
            using (var client = CreateClient(new InternalToolRegistry(new IInternalToolExecutor[0])))
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                writer.AutoFlush = true;
                FormTestDriver.SetField(client, "_inputWriter", writer);
                var contexts = FormTestDriver.Field<Dictionary<string, InternalToolExecutionContext>>(client, "_internalToolContexts");
                contexts["thread-office"] = new InternalToolExecutionContext
                {
                    Mode = "Office",
                    WorkspacePath = TestContext.CurrentContext.WorkDirectory,
                    OfficeRootPath = TestContext.CurrentContext.WorkDirectory
                };

                FormTestDriver.Invoke(client, "HandleServerRequest", BuildDynamicToolCallMessage("tool-request-office", "thread-office", "office", "create_document"));

                var result = ReadResultDictionary(await WaitForWrittenTextAsync(stream).ConfigureAwait(false));

                Assert.That(result["success"], Is.EqualTo(false));
                Assert.That(ReadFirstContentText(result), Does.Contain("office/create_document"));
            }
        }

        [Test]
        public async Task GenericDynamicToolFlow_StillExecutesNonOfficeTool()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "generic-flow");
            Directory.CreateDirectory(root);
            var executor = new RecordingInternalToolExecutor(InternalToolResult.FromText(true, "sample pong"));
            var registry = new InternalToolRegistry(new IInternalToolExecutor[] { executor });
            var toolContext = new AppServerThreadToolContext
            {
                DynamicTools = new[]
                {
                    new AppServerDynamicToolSpec
                    {
                        Namespace = "sample",
                        Name = "ping",
                        Description = "Ping a sample dynamic tool.",
                        InputSchema = new Dictionary<string, object>()
                    }
                },
                ExecutionContext = new InternalToolExecutionContext
                {
                    Mode = "Coding",
                    WorkspacePath = root
                }
            };
            var threadStart = AppServerThreadStartParameterBuilder.Build(
                root,
                ApprovalMode.Auto,
                "gpt-5.4-mini",
                "medium",
                toolContext);

            Assert.That(threadStart.ContainsKey("dynamicTools"), Is.True);

            using (var client = CreateClient(registry))
            using (var stream = new MemoryStream())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true))
            {
                writer.AutoFlush = true;
                FormTestDriver.SetField(client, "_inputWriter", writer);
                var contexts = FormTestDriver.Field<Dictionary<string, InternalToolExecutionContext>>(client, "_internalToolContexts");
                contexts["thread-generic"] = toolContext.ExecutionContext;

                FormTestDriver.Invoke(client, "HandleServerRequest", BuildDynamicToolCallMessage("tool-request-generic", "thread-generic", "sample", "ping"));

                var result = ReadResultDictionary(await WaitForWrittenTextAsync(stream).ConfigureAwait(false));

                Assert.That(result["success"], Is.EqualTo(true));
                Assert.That(ReadFirstContentText(result), Is.EqualTo("sample pong"));
                Assert.That(executor.Requests.Count, Is.EqualTo(1));
                Assert.That(executor.Requests[0].Namespace, Is.EqualTo("sample"));
                Assert.That(executor.Requests[0].Name, Is.EqualTo("ping"));
            }
        }

        private static AppServerClient CreateClient(IInternalToolRegistry registry)
        {
            var appDataPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            return new AppServerClient(
                new CodexRuntimeManager(new AppConfigService(appDataPath)),
                new StubConfigStore(),
                registry);
        }

        private static IDictionary<string, object> BuildDynamicToolCallMessage(
            string requestId,
            string threadId,
            string toolNamespace,
            string toolName)
        {
            return new Dictionary<string, object>
            {
                { "id", requestId },
                { "method", "item/tool/call" },
                {
                    "params",
                    new Dictionary<string, object>
                    {
                        { "threadId", threadId },
                        { "turnId", "turn-1" },
                        { "callId", "call-1" },
                        { "namespace", toolNamespace },
                        { "tool", toolName },
                        { "arguments", new Dictionary<string, object>() }
                    }
                }
            };
        }

        private static IDictionary<string, object> ReadResultDictionary(string output)
        {
            return AppServerJsonValue.GetDictionary(
                AppServerJsonValue.DeserializeDictionary(output.Trim()),
                "result");
        }

        private static string ReadFirstContentText(IDictionary<string, object> result)
        {
            var contentItems = (object[])result["contentItems"];
            var firstItem = (IDictionary<string, object>)contentItems[0];
            return Convert.ToString(firstItem["text"], CultureInfo.InvariantCulture);
        }

        private static async Task<string> WaitForWrittenTextAsync(MemoryStream stream)
        {
            for (var i = 0; i < 50; i++)
            {
                if (stream.Length > 0)
                {
                    break;
                }

                await Task.Delay(20).ConfigureAwait(false);
            }

            stream.Position = 0;
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private sealed class RecordingInternalToolExecutor : IInternalToolExecutor
        {
            private readonly InternalToolResult _result;

            public RecordingInternalToolExecutor(InternalToolResult result)
            {
                _result = result;
            }

            public IReadOnlyCollection<InternalToolDefinition> Definitions
            {
                get
                {
                    return new[]
                    {
                        new InternalToolDefinition
                        {
                            Namespace = "sample",
                            Name = "ping",
                            Description = "Ping a sample dynamic tool.",
                            InputSchema = new Dictionary<string, object>()
                        }
                    };
                }
            }

            public List<InternalToolRequest> Requests { get; } = new List<InternalToolRequest>();

            public Task<InternalToolResult> ExecuteAsync(
                InternalToolRequest request,
                InternalToolExecutionContext context)
            {
                Requests.Add(request);
                return Task.FromResult(_result);
            }
        }

        private sealed class StubConfigStore : IAppConfigStore
        {
            public AppConfigSnapshot Load()
            {
                return new AppConfigSnapshot();
            }

            public void Save(AppConfigSnapshot snapshot)
            {
            }
        }
    }
}
