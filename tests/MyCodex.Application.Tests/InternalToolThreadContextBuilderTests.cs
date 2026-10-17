using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.InternalTools;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class InternalToolThreadContextBuilderTests
    {
        [Test]
        public void BuildForSession_ReturnsNullForOfficeSession()
        {
            var registry = new InternalToolRegistry(new IInternalToolExecutor[]
            {
                new StubToolExecutor(new InternalToolDefinition
                {
                    Namespace = "office",
                    Name = "create_document",
                    Description = "Create a document.",
                    InputSchema = new Dictionary<string, object>(),
                    Modes = new[] { "Office" }
                })
            });
            var session = new ChatSession { Mode = AppInteractionMode.Office };
            var workspace = new WorkspaceInfo { RootPath = @"D:\Documents" };

            Assert.That(InternalToolThreadContextBuilder.BuildForSession(registry, session, workspace), Is.Null);
        }

        [Test]
        public void BuildForSession_ReturnsNullForOfficeWorkspaceWhenSessionModeIsStale()
        {
            var registry = new InternalToolRegistry(new IInternalToolExecutor[]
            {
                new StubToolExecutor(new InternalToolDefinition
                {
                    Namespace = "office",
                    Name = "create_document",
                    Description = "Create a document.",
                    InputSchema = new Dictionary<string, object>(),
                    Modes = new[] { "Office" }
                })
            });
            var session = new ChatSession
            {
                Mode = AppInteractionMode.Coding,
                WorkspaceId = @"office:D:\Documents"
            };
            var workspace = new WorkspaceInfo
            {
                Id = @"office:D:\Documents",
                Kind = WorkspaceKind.Office,
                RootPath = @"D:\Documents"
            };

            Assert.That(InternalToolThreadContextBuilder.BuildForSession(registry, session, workspace), Is.Null);
        }

        [Test]
        public void BuildForSession_ReturnsNullForCodingSession()
        {
            var registry = new InternalToolRegistry(new IInternalToolExecutor[]
            {
                new StubToolExecutor(new InternalToolDefinition
                {
                    Namespace = "office",
                    Name = "create_document",
                    Description = "Create a document.",
                    InputSchema = new Dictionary<string, object>(),
                    Modes = new[] { "Office" }
                })
            });
            var session = new ChatSession { Mode = AppInteractionMode.Coding };
            var workspace = new WorkspaceInfo { RootPath = @"D:\Repo" };

            Assert.That(InternalToolThreadContextBuilder.BuildForSession(registry, session, workspace), Is.Null);
        }

        private sealed class StubToolExecutor : IInternalToolExecutor
        {
            public StubToolExecutor(params InternalToolDefinition[] definitions)
            {
                Definitions = definitions;
            }

            public IReadOnlyCollection<InternalToolDefinition> Definitions { get; private set; }

            public Task<InternalToolResult> ExecuteAsync(
                InternalToolRequest request,
                InternalToolExecutionContext context)
            {
                return Task.FromResult(InternalToolResult.FromText(true, "ok"));
            }
        }
    }
}
