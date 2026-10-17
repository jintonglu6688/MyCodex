using System.Collections.Generic;
using MyCodex.Application.DTOs;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerPhase3DtoTests
    {
        [Test]
        public void McpResourceContent_IsBlobWhenBlobTextExists()
        {
            var content = new AppServerMcpResourceContent
            {
                Uri = "file:///demo",
                Blob = "aGVsbG8="
            };

            Assert.That(content.IsBlob, Is.True);
        }

        [Test]
        public void ToolCallRequest_AllowsDictionaryArgumentsAndMeta()
        {
            var request = new AppServerMcpToolCallRequest
            {
                ThreadId = "thread-1",
                Server = "github",
                Tool = "list_issues",
                Arguments = new Dictionary<string, object> { { "repo", "owner/name" } },
                Meta = new Dictionary<string, object> { { "source", "manual-test" } }
            };

            Assert.That(request.Arguments["repo"], Is.EqualTo("owner/name"));
            Assert.That(request.Meta["source"], Is.EqualTo("manual-test"));
        }
    }
}
