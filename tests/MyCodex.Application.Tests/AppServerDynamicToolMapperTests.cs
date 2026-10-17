using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.InternalTools;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerDynamicToolMapperTests
    {
        [Test]
        public void ToWire_MapsRequiredAndOptionalFields()
        {
            var spec = new AppServerDynamicToolSpec
            {
                Namespace = "office",
                Name = "create_document",
                Description = "Create an office document.",
                DeferLoading = true,
                InputSchema = new Dictionary<string, object>
                {
                    { "type", "object" },
                    { "required", new object[] { "kind" } }
                }
            };

            var wire = AppServerDynamicToolSpecMapper.ToWire(spec);

            Assert.That(wire["namespace"], Is.EqualTo("office"));
            Assert.That(wire["name"], Is.EqualTo("create_document"));
            Assert.That(wire["description"], Is.EqualTo("Create an office document."));
            Assert.That(wire["inputSchema"], Is.SameAs(spec.InputSchema));
            Assert.That(wire["deferLoading"], Is.EqualTo(true));
        }

        [Test]
        public void ToWire_OmitsEmptyNamespaceAndFalseDeferLoading()
        {
            var spec = new AppServerDynamicToolSpec
            {
                Name = "current_time",
                Description = "Read current time.",
                InputSchema = new Dictionary<string, object>()
            };

            var wire = AppServerDynamicToolSpecMapper.ToWire(spec);

            Assert.That(wire.ContainsKey("namespace"), Is.False);
            Assert.That(wire.ContainsKey("deferLoading"), Is.False);
            Assert.That(wire["name"], Is.EqualTo("current_time"));
        }

        [Test]
        public void FromInternalDefinition_CopiesDefinitionFields()
        {
            var definition = new InternalToolDefinition
            {
                Namespace = "office",
                Name = "read_document",
                Description = "Read an office document.",
                DeferLoading = true,
                InputSchema = new Dictionary<string, object> { { "type", "object" } }
            };

            var spec = AppServerDynamicToolSpec.FromInternalDefinition(definition);

            Assert.That(spec.Namespace, Is.EqualTo("office"));
            Assert.That(spec.Name, Is.EqualTo("read_document"));
            Assert.That(spec.Description, Is.EqualTo("Read an office document."));
            Assert.That(spec.DeferLoading, Is.True);
            Assert.That(spec.InputSchema, Is.SameAs(definition.InputSchema));
        }

        [Test]
        public void FromParams_MapsDynamicToolCallParams()
        {
            var arguments = new Dictionary<string, object>
            {
                { "kind", "presentation" },
                { "outputFileName", "promo.pptx" }
            };
            var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                { "threadId", "thread-1" },
                { "turnId", "turn-1" },
                { "callId", "call-1" },
                { "namespace", "office" },
                { "tool", "create_document" },
                { "arguments", arguments }
            };

            var request = AppServerDynamicToolCallMapper.FromParams(parameters);

            Assert.That(request.ThreadId, Is.EqualTo("thread-1"));
            Assert.That(request.TurnId, Is.EqualTo("turn-1"));
            Assert.That(request.CallId, Is.EqualTo("call-1"));
            Assert.That(request.Namespace, Is.EqualTo("office"));
            Assert.That(request.Name, Is.EqualTo("create_document"));
            Assert.That(request.Arguments, Is.SameAs(arguments));
        }

        [Test]
        public void ToWire_MapsResultContentItems()
        {
            var result = new InternalToolResult
            {
                Success = true
            };
            result.ContentItems.Add(InternalToolContentItem.TextItem("created"));
            result.ContentItems.Add(InternalToolContentItem.ImageItem("file:///D:/Documents/preview.png"));

            var wire = AppServerDynamicToolResponseMapper.ToWire(result);
            var contentItems = (object[])wire["contentItems"];
            var textItem = (IDictionary<string, object>)contentItems[0];
            var imageItem = (IDictionary<string, object>)contentItems[1];

            Assert.That(wire["success"], Is.EqualTo(true));
            Assert.That(textItem["type"], Is.EqualTo("inputText"));
            Assert.That(textItem["text"], Is.EqualTo("created"));
            Assert.That(imageItem["type"], Is.EqualTo("inputImage"));
            Assert.That(imageItem["imageUrl"], Is.EqualTo("file:///D:/Documents/preview.png"));
        }
    }
}
