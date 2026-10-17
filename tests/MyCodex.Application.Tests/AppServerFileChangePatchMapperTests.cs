using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerFileChangePatchMapperTests
    {
        [Test]
        public void BuildEvent_MapsPatchChangesAndMovePath()
        {
            using (UseCulture("en-US"))
            {
                var e = AppServerFileChangePatchMapper.BuildEvent(new Dictionary<string, object>
                {
                    { "threadId", "thread-1" },
                    { "turnId", "turn-1" },
                    { "itemId", "item-1" },
                    { "changes", new object[]
                        {
                            Change("a/src/Add.cs", "add", "+add"),
                            Change("b/src/Delete.cs", "delete", "-delete"),
                            Change("src/Update.cs", "update", "@@ update"),
                            Change("src/Old.cs", "update", "@@ move", "src/New.cs")
                        }
                    }
                });

                Assert.That(e.EventType, Is.EqualTo("file_change_patch_updated"));
                Assert.That(e.ThreadId, Is.EqualTo("thread-1"));
                Assert.That(e.TurnId, Is.EqualTo("turn-1"));
                Assert.That(e.ItemId, Is.EqualTo("item-1"));
                Assert.That(e.Content, Is.EqualTo("Processing src/Add.cs\r\nsrc/Delete.cs\r\nsrc/Update.cs\r\nAnd 2 more files"));
                Assert.That(e.Details, Does.Contain("Added src/Add.cs"));
                Assert.That(e.Details, Does.Contain("Deleted src/Delete.cs"));
                Assert.That(e.Details, Does.Contain("Modified src/Update.cs"));
                Assert.That(e.Details, Does.Contain("Moved src/Old.cs -> src/New.cs"));
                Assert.That(e.ChangedFiles, Is.EqualTo(new[] { "src/Add.cs", "src/Delete.cs", "src/Update.cs", "src/Old.cs", "src/New.cs" }));
            }
        }

        [Test]
        public void BuildEvent_ConcatenatesNonEmptyDiffs()
        {
            var e = AppServerFileChangePatchMapper.BuildEvent(new Dictionary<string, object>
            {
                { "threadId", "thread-1" },
                { "turnId", "turn-1" },
                { "itemId", "item-1" },
                { "changes", new object[]
                    {
                        Change("src/One.cs", "update", "@@ one"),
                        Change("src/Two.cs", "update", "  "),
                        Change("src/Three.cs", "update", "@@ three")
                    }
                }
            });

            Assert.That(e.DiffText, Is.EqualTo("@@ one" + Environment.NewLine + Environment.NewLine + "@@ three"));
        }

        private static IDictionary<string, object> Change(string path, string kind, string diff, string movePath = null)
        {
            var kindObject = new Dictionary<string, object>
            {
                { "type", kind }
            };
            if (movePath != null)
            {
                kindObject["move_path"] = movePath;
            }

            return new Dictionary<string, object>
            {
                { "path", path },
                { "kind", kindObject },
                { "diff", diff }
            };
        }

        private static IDisposable UseCulture(string cultureName)
        {
            return new CultureScope(cultureName);
        }

        private sealed class CultureScope : IDisposable
        {
            private readonly CultureInfo _originalCulture;
            private readonly CultureInfo _originalUiCulture;
            private readonly CultureInfo _originalDefaultCulture;
            private readonly CultureInfo _originalDefaultUiCulture;

            public CultureScope(string cultureName)
            {
                _originalCulture = CultureInfo.CurrentCulture;
                _originalUiCulture = CultureInfo.CurrentUICulture;
                _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
                _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                CultureInfo.DefaultThreadCurrentCulture = culture;
                CultureInfo.DefaultThreadCurrentUICulture = culture;
            }

            public void Dispose()
            {
                CultureInfo.CurrentCulture = _originalCulture;
                CultureInfo.CurrentUICulture = _originalUiCulture;
                CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
            }
        }
    }
}
