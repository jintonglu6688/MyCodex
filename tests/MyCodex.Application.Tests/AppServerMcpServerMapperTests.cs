using System;
using System.Collections.Generic;
using System.Globalization;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerMcpServerMapperTests
    {
        [Test]
        public void BuildMcpServerStatuses_MergesStatusAndConfigPayloadsByServerName()
        {
            WithCulture("en-US", () =>
            {
                var statusResult = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "statuses",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            {
                                "github",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    {
                                        "status",
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "type", "active" },
                                            { "message", "healthy" }
                                        }
                                    },
                                    {
                                        "authStatus",
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "state", "oAuth" }
                                        }
                                    },
                                    { "tools", new object[] { "search", "issues" } },
                                    { "resources", new Dictionary<string, object>(StringComparer.Ordinal) { { "repo", "demo" } } },
                                    { "resourceTemplates", "3" }
                                }
                            }
                        }
                    }
                };
                var configResult = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    {
                        "config",
                        new Dictionary<string, object>(StringComparer.Ordinal)
                        {
                            {
                                "mcp_servers",
                                new Dictionary<string, object>(StringComparer.Ordinal)
                                {
                                    {
                                        "github",
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "command", "node" },
                                            { "args", new object[] { "server.js", "--stdio" } },
                                            { "enabled", true },
                                            { "required", true },
                                            { "oauth_resource", "https://github.com" },
                                            { "enabled_tools", new object[] { "search", "issues" } },
                                            { "disabled_tools", "admin" },
                                            {
                                                "env_vars",
                                                new Dictionary<string, object>(StringComparer.Ordinal)
                                                {
                                                    { "GITHUB_TOKEN", true },
                                                    { "HTTP_PROXY", true }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                };

                var result = AppServerMcpServerMapper.BuildMcpServerStatuses(statusResult, configResult);

                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].ServerName, Is.EqualTo("github"));
                Assert.That(result[0].Status, Is.EqualTo("active"));
                Assert.That(result[0].AuthStatus, Is.EqualTo("oAuth"));
                Assert.That(result[0].SupportsOAuth, Is.True);
                Assert.That(result[0].Enabled, Is.True);
                Assert.That(result[0].ToolCount, Is.EqualTo(2));
                Assert.That(result[0].ResourceCount, Is.EqualTo(1));
                Assert.That(result[0].ResourceTemplateCount, Is.EqualTo(3));
                Assert.That(result[0].SourceSummary, Is.EqualTo("Command"));
                Assert.That(result[0].SourceDetail, Is.EqualTo("node server.js --stdio"));
                Assert.That(result[0].IsRequired, Is.True);
                Assert.That(result[0].OAuthResource, Is.EqualTo("https://github.com"));
                Assert.That(result[0].EnabledToolsText, Is.EqualTo("search, issues"));
                Assert.That(result[0].DisabledToolsText, Is.EqualTo("admin"));
                Assert.That(result[0].EnvironmentText, Is.EqualTo("2 items: GITHUB_TOKEN, HTTP_PROXY"));
                Assert.That(result[0].DetailText, Does.Contain("Runtime: Active"));
                Assert.That(result[0].DetailText, Does.Contain("Auth: OAuth"));
                Assert.That(result[0].DetailText, Does.Contain("Entry: node server.js --stdio"));
            });
        }

        [Test]
        public void BuildMcpServerStatuses_CapturesResourceDetailsFromStatusPayload()
        {
            var statusResult = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "statuses",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "codex_apps",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                {
                                    "resources",
                                    new object[]
                                    {
                                        new Dictionary<string, object>(StringComparer.Ordinal)
                                        {
                                            { "uri", "app://codex_apps/accounts" },
                                            { "name", "accounts" },
                                            { "title", "Accounts" },
                                            { "description", "Connected account state" },
                                            { "mimeType", "application/json" }
                                        },
                                        "app://codex_apps/tools"
                                    }
                                },
                                {
                                    "resourceTemplates",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        {
                                            "workspace",
                                            new Dictionary<string, object>(StringComparer.Ordinal)
                                            {
                                                { "uriTemplate", "app://codex_apps/workspaces/{id}" },
                                                { "description", "Workspace details" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var result = AppServerMcpServerMapper.BuildMcpServerStatuses(statusResult, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].ResourceCount, Is.EqualTo(2));
            Assert.That(result[0].ResourceTemplateCount, Is.EqualTo(1));
            Assert.That(result[0].Resources, Has.Count.EqualTo(2));
            Assert.That(result[0].Resources[0].Uri, Is.EqualTo("app://codex_apps/accounts"));
            Assert.That(result[0].Resources[0].Name, Is.EqualTo("accounts"));
            Assert.That(result[0].Resources[0].Title, Is.EqualTo("Accounts"));
            Assert.That(result[0].Resources[0].Description, Is.EqualTo("Connected account state"));
            Assert.That(result[0].Resources[0].MimeType, Is.EqualTo("application/json"));
            Assert.That(result[0].Resources[1].Uri, Is.EqualTo("app://codex_apps/tools"));
            Assert.That(result[0].ResourceTemplates, Has.Count.EqualTo(1));
            Assert.That(result[0].ResourceTemplates[0].Uri, Is.EqualTo("app://codex_apps/workspaces/{id}"));
            Assert.That(result[0].ResourceTemplates[0].Name, Is.EqualTo("workspace"));
            Assert.That(result[0].ResourceTemplates[0].IsTemplate, Is.True);
        }

        [Test]
        public void BuildMcpServerStatuses_DoesNotInferEnabledFromStatusOnlyPayload()
        {
            var statusResult = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "statuses",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "codex_app",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                { "status", "idle" },
                                { "authStatus", "unsupported" }
                            }
                        }
                    }
                }
            };

            var result = AppServerMcpServerMapper.BuildMcpServerStatuses(statusResult, null);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].ServerName, Is.EqualTo("codex_app"));
            Assert.That(result[0].Status, Is.EqualTo("idle"));
            Assert.That(result[0].Enabled, Is.Null);
        }

        [Test]
        public void BuildMcpServerStatuses_UsesConfigOnlyPayloadAndSortsByServerName()
        {
            var configResult = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                {
                    "config",
                    new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        {
                            "mcpServers",
                            new Dictionary<string, object>(StringComparer.Ordinal)
                            {
                                {
                                    "zeta",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "url", "https://zeta.example/mcp" },
                                        { "enabled", false }
                                    }
                                },
                                {
                                    "alpha",
                                    new Dictionary<string, object>(StringComparer.Ordinal)
                                    {
                                        { "url", "https://alpha.example/mcp" }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var result = AppServerMcpServerMapper.BuildMcpServerStatuses(null, configResult);

            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0].ServerName, Is.EqualTo("alpha"));
            Assert.That(result[0].SourceSummary, Is.EqualTo("HTTP"));
            Assert.That(result[0].SourceDetail, Is.EqualTo("https://alpha.example/mcp"));
            Assert.That(result[0].Enabled, Is.True);
            Assert.That(result[1].ServerName, Is.EqualTo("zeta"));
            Assert.That(result[1].Enabled, Is.False);
        }

        [Test]
        public void BuildMcpServerStatuses_HandlesSinglePayloadFallback()
        {
            WithCulture("en-US", () =>
            {
                var statusResult = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "name", "local" },
                    { "status", "idle" },
                    { "authStatus", "unsupported" },
                    { "url", "http://localhost:3000/mcp" },
                    { "enabled_tools", "read" }
                };

                var result = AppServerMcpServerMapper.BuildMcpServerStatuses(statusResult, null);

                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].ServerName, Is.EqualTo("local"));
                Assert.That(result[0].Status, Is.EqualTo("idle"));
                Assert.That(result[0].AuthStatus, Is.EqualTo("unsupported"));
                Assert.That(result[0].SupportsOAuth, Is.False);
                Assert.That(result[0].SourceSummary, Is.EqualTo("HTTP"));
                Assert.That(result[0].SourceDetail, Is.EqualTo("http://localhost:3000/mcp"));
                Assert.That(result[0].EnabledToolsText, Is.EqualTo("read"));
                Assert.That(result[0].DetailText, Does.Contain("Runtime: Idle"));
                Assert.That(result[0].DetailText, Does.Contain("Auth: Unsupported"));
            });
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
