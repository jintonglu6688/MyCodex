using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MyCodex.Application.InternalTools;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class InternalToolRegistryTests
    {
        [Test]
        public void ListDefinitions_ReturnsToolsMatchingMode()
        {
            var registry = new InternalToolRegistry(new IInternalToolExecutor[]
            {
                new StubToolExecutor(new InternalToolDefinition
                {
                    Namespace = "office",
                    Name = "create_document",
                    Description = "Create an office document.",
                    InputSchema = new Dictionary<string, object>(),
                    Modes = new[] { "Office" }
                }),
                new StubToolExecutor(new InternalToolDefinition
                {
                    Namespace = "coding",
                    Name = "inspect_project",
                    Description = "Inspect a coding project.",
                    InputSchema = new Dictionary<string, object>(),
                    Modes = new[] { "Coding" }
                })
            });

            var definitions = registry.ListDefinitions("Office");

            Assert.That(definitions.Count, Is.EqualTo(1));
            Assert.That(definitions[0].Namespace, Is.EqualTo("office"));
            Assert.That(definitions[0].Name, Is.EqualTo("create_document"));
        }

        [Test]
        public void ListDefinitions_ReturnsGlobalToolsForAnyMode()
        {
            var registry = new InternalToolRegistry(new IInternalToolExecutor[]
            {
                new StubToolExecutor(new InternalToolDefinition
                {
                    Namespace = "shared",
                    Name = "summarize_text",
                    Description = "Summarize text.",
                    InputSchema = new Dictionary<string, object>()
                })
            });

            Assert.That(registry.ListDefinitions("Office").Count, Is.EqualTo(1));
            Assert.That(registry.ListDefinitions("Coding").Count, Is.EqualTo(1));
        }

        [Test]
        public void Constructor_RejectsDuplicateNamespaceAndName()
        {
            var first = new StubToolExecutor(new InternalToolDefinition
            {
                Namespace = "office",
                Name = "read_document",
                Description = "Read an office document.",
                InputSchema = new Dictionary<string, object>()
            });
            var second = new StubToolExecutor(new InternalToolDefinition
            {
                Namespace = "office",
                Name = "read_document",
                Description = "Read another office document.",
                InputSchema = new Dictionary<string, object>()
            });

            Assert.That(
                () => new InternalToolRegistry(new IInternalToolExecutor[] { first, second }),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void TryGetExecutor_FindsNamespacedAndUnnamespacedTools()
        {
            var office = new StubToolExecutor(new InternalToolDefinition
            {
                Namespace = "office",
                Name = "edit_document",
                Description = "Edit an office document.",
                InputSchema = new Dictionary<string, object>()
            });
            var shared = new StubToolExecutor(new InternalToolDefinition
            {
                Name = "current_time",
                Description = "Read current time.",
                InputSchema = new Dictionary<string, object>()
            });
            var registry = new InternalToolRegistry(new IInternalToolExecutor[] { office, shared });

            IInternalToolExecutor executor;
            Assert.That(registry.TryGetExecutor("office", "edit_document", out executor), Is.True);
            Assert.That(executor, Is.SameAs(office));

            Assert.That(registry.TryGetExecutor(null, "current_time", out executor), Is.True);
            Assert.That(executor, Is.SameAs(shared));

            Assert.That(registry.TryGetExecutor("missing", "edit_document", out executor), Is.False);
            Assert.That(executor, Is.Null);
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
