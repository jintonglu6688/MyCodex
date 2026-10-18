using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class McpManagementFormTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void NativeMcpMutationsUseCoreAndImportIsAnExplicitKeyboardAction()
        {
            WithCulture("en-US", () =>
            {
                var writes = 0;
                var imports = 0;
                var enabled = true;
                var native = new AuthenticationCenterClient("Test", (method, args, token) =>
                {
                    IDictionary<string, object> result = new Dictionary<string, object>();
                    if (method == "gui/mcp/list") result = new Dictionary<string, object>
                    {
                        { "version", "v" + writes }, { "servers", new object[] { new Dictionary<string, object>
                            {
                                { "id", "demo" }, { "enabled", enabled },
                                { "config", new Dictionary<string, object> { { "command", "test" }, { "enabled", enabled } } }
                            } } }
                    };
                    else if (method == "gui/mcp/save") { writes++; enabled = (bool)args["enabled"]; result["saved"] = true; }
                    else if (method == "gui/mcp/import") { imports++; result["imported"] = 0; }
                    else Assert.Fail("Unexpected method: " + method);
                    return Task.FromResult(result);
                });
                var runtime = new RecordingMcpClient();
                var config = new RecordingMcpConfigClient();
                var coordinator = new AuthenticationMcpCoordinator(native, runtime, new RuntimeOperationGate().TryEnterTransition);
                using (var form = new McpManagementForm(new RecordingMcpConnectionClient(), config, runtime, null, null, coordinator))
                {
                    var list = FormTestDriver.FindControl<ListView>(form, "MCP Servers");
                    var ignored = form.Handle;
                    ignored = list.Handle;
                    FormTestDriver.InvokeAndWait(form, "LoadServerStatusesAsync", true);
                    list.Items[0].Selected = true;
                    Assert.That(imports + writes, Is.Zero, "Reading and selecting must not import or write.");
                    var import = FormTestDriver.FindControl<Button>(form, "Import global settings");
                    Assert.That(import.TabStop, Is.True);
                    Assert.That(import.TabIndex, Is.GreaterThan(0));
                    FormTestDriver.InvokeAndWait(form, "SetSelectedServerEnabledAsync", false);
                    Assert.That(writes, Is.EqualTo(1));
                    Assert.That(config.WriteConfigBatchCallCount, Is.Zero);
                    Assert.That(runtime.ReloadMcpConfigCallCount, Is.EqualTo(1));
                    Assert.That(list.Items.Count, Is.EqualTo(1), "Disabled archive must remain visible.");
                    FormTestDriver.InvokeAndWait(form, "ImportNativeMcpAsync");
                    Assert.That(imports, Is.EqualTo(1));
                    Assert.That(runtime.ReloadMcpConfigCallCount, Is.EqualTo(2));
                    Assert.That(form.MinimumSize.Width, Is.GreaterThanOrEqualTo(980));
                    runtime.FailReload = true;
                    FormTestDriver.InvokeAndWait(form, "ImportNativeMcpAsync");
                    Assert.That(coordinator.ReloadPending, Is.True);
                    form.Show();
                    var retry = FormTestDriver.FindControl<Button>(form, "Retry loading MCP");
                    retry.Focus();
                    runtime.FailReload = false;
                    FormTestDriver.InvokeAndWait(form, "RetryNativeMcpAsync");
                    Assert.That(FormTestDriver.Field<Label>(form, "_statusLabel").Text, Is.EqualTo("MCP configuration loaded."));
                    Assert.That(list.Focused, Is.True);
                    Assert.That(imports, Is.EqualTo(2), "Retry reload must not repeat import.");
                }
            });
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void McpManagementForm_SourceCancelsLifetimeWorkBeforeClose()
        {
            var source = RepositorySource.ReadSourceFile("McpManagementForm.cs");

            Assert.That(source, Does.Contain("CancellationTokenSource _lifetimeCancellation"));
            Assert.That(source, Does.Contain("_lifetimeCancellation.Cancel();"));
            Assert.That(source, Does.Contain("SetBusy(true);"));
            Assert.That(source, Does.Contain("if (!await EnsureAppServerAvailableForActionAsync"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpManagementForm_ShowsAdvancedDiagnosticsInsteadOfProtocolButtons()
        {
            WithCulture("en-US", () =>
            {
                using (var form = new McpManagementForm(CreateClient()))
                {
                    var diagnosticsButton = FormTestDriver.FindControl<Button>(form, "Advanced Diagnostics");

                    Assert.That(diagnosticsButton.TabIndex, Is.GreaterThanOrEqualTo(0));
                    Assert.That(diagnosticsButton.AccessibleName, Is.EqualTo("Advanced Diagnostics"));
                    Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, "View Resource"), Is.Null);
                    Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, "Test Tool"), Is.Null);
                }
            });
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void McpManagementForm_OnlyReloadsOfficialConfigAfterAnExplicitMcpMutation()
        {
            var source = RepositorySource.ReadSourceFile("McpManagementForm.cs");

            Assert.That(source, Does.Not.Contain("_reloadButton"));
            Assert.That(source, Does.Not.Contain("Keys.F5 && e.Control"));
            Assert.That(source, Does.Not.Contain("config.toml"));
            Assert.That(source, Does.Not.Contain("IAppServerFileSystemClient"));
            Assert.That(source, Does.Not.Contain("WatchFileSystemAsync"));
            Assert.That(source, Does.Not.Contain("WriteConfigValueAsync"));
            Assert.That(source, Does.Contain("_configClient.ReadConfigAsync(null, true)"));
            Assert.That(source, Does.Contain("AppServerConfigWriteGuard.Evaluate(snapshot, edits)"));
            Assert.That(source, Does.Contain("expectedVersion: guard.ExpectedVersion"));
            Assert.That(source, Does.Contain("await _mcpClient.ReloadMcpServerConfigAsync();"));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpManagementForm_DisableUsesFreshGuardedBatchWriteThenOfficialReload()
        {
            WithCulture("en-US", () =>
            {
                var server = new AppServerMcpServerStatus
                {
                    ServerName = "demo",
                    Enabled = true,
                    ConfigValues = new Dictionary<string, object>
                    {
                        { "enabled", true }
                    }
                };
                var connectionClient = new RecordingMcpConnectionClient();
                var configClient = new RecordingMcpConfigClient();
                var mcpClient = new RecordingMcpClient(server);
                using (var form = new McpManagementForm(connectionClient, configClient, mcpClient, null, null))
                {
                    configClient.ConfigSnapshot.Origins["mcp_servers.demo"] = new AppServerConfigOrigin { Version = "v17" };
                    var servers = FormTestDriver.FindControl<ListView>(form, "MCP Servers");
                    var ignored = form.Handle;
                    ignored = servers.Handle;
                    FormTestDriver.InvokeAndWait(form, "LoadServerStatusesAsync", true);
                    servers.Items[0].Selected = true;

                    FormTestDriver.InvokeAndWait(form, "SetSelectedServerEnabledAsync", false);

                    Assert.Multiple(() =>
                    {
                        Assert.That(configClient.ReadConfigCallCount, Is.EqualTo(1));
                        Assert.That(configClient.WriteConfigBatchCallCount, Is.EqualTo(1));
                        Assert.That(configClient.LastExpectedVersion, Is.EqualTo("v17"));
                        Assert.That(configClient.LastReloadUserConfig, Is.True);
                        Assert.That(configClient.LastConfigEdits, Has.Count.EqualTo(1));
                        Assert.That(configClient.LastConfigEdits[0].KeyPath, Is.EqualTo("mcp_servers.demo.enabled"));
                        Assert.That(configClient.LastConfigEdits[0].Value, Is.False);
                        Assert.That(mcpClient.ReloadMcpConfigCallCount, Is.EqualTo(1));
                    });
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpManagementForm_ReauthenticationNotificationShowsAndSpeaksGuidanceWithoutMovingFocus()
        {
            WithCulture("en-US", () =>
            {
                using (var client = CreateRecordingClient())
                using (var screenReader = new RecordingScreenReaderService())
                using (var form = new McpManagementForm(client, screenReader))
                {
                    var filter = FormTestDriver.FindControl<ComboBox>(form, "Filter");
                    form.ActiveControl = filter;
                    var ignored = form.Handle;

                    client.RaiseReauthenticationRequired("github");
                    client.RaiseReauthenticationRequired("GITHUB");

                    Assert.That(
                        FormTestDriver.Field<Label>(form, "_statusLabel").Text,
                        Is.EqualTo("Authentication for github has expired. Refresh status, select the server, then choose Login.")
                            .IgnoreCase);
                    Assert.That(screenReader.SpokenTexts, Is.EqualTo(new[]
                    {
                        "Authentication for github has expired. Refresh status, select the server, then choose Login."
                    }));
                    Assert.That(form.ActiveControl, Is.SameAs(filter));
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpManagementForm_ReauthenticationNotificationSelectsServerAfterRefreshWithoutStartingLogin()
        {
            WithCulture("en-US", () =>
            {
                using (var client = CreateRecordingClient(
                    new AppServerMcpServerStatus { ServerName = "other", Enabled = true, SupportsOAuth = true },
                    new AppServerMcpServerStatus { ServerName = "github", Enabled = true, SupportsOAuth = true }))
                using (var screenReader = new RecordingScreenReaderService())
                using (var form = new McpManagementForm(client, screenReader))
                {
                    var filter = FormTestDriver.FindControl<ComboBox>(form, "Filter");
                    form.ActiveControl = filter;
                    var ignored = form.Handle;
                    var serverList = FormTestDriver.FindControl<ListView>(form, "MCP Servers");
                    ignored = serverList.Handle;
                    client.RaiseReauthenticationRequired("github");
                    Assert.That(client.StartOauthLoginCallCount, Is.EqualTo(0));

                    FormTestDriver.InvokeAndWait(form, "LoadServerStatusesAsync", true);

                    Assert.Multiple(() =>
                    {
                        Assert.That(client.ConnectCallCount, Is.EqualTo(1));
                        Assert.That(client.ListMcpServerStatusesCallCount, Is.EqualTo(1));
                        Assert.That(client.StartOauthLoginCallCount, Is.EqualTo(0));
                        Assert.That(serverList.SelectedItems, Has.Count.EqualTo(1));
                        Assert.That(
                            ((AppServerMcpServerStatus)serverList.SelectedItems[0].Tag).ServerName,
                            Is.EqualTo("github"));
                        Assert.That(form.ActiveControl, Is.SameAs(filter));
                    });
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpManagementForm_PreOpenReauthenticationGuidanceSurvivesUntilFirstRefresh()
        {
            WithCulture("en-US", () =>
            {
                var state = new McpReauthenticationState();
                var reauthenticationEvent = new AppServerEventArgs(
                    "mcp_startup_status_updated",
                    "MCP server authentication expired: github.",
                    reason: "reauthenticationRequired",
                    mcpServerName: "github",
                    itemStatus: "failed");
                Assert.That(
                    AppServerEventRouting.Resolve(reauthenticationEvent),
                    Is.EqualTo(AppServerEventRoute.McpStartupStatusUpdated));
                state.Record(reauthenticationEvent);

                using (var client = CreateRecordingClient(
                    new AppServerMcpServerStatus { ServerName = "other", Enabled = true, SupportsOAuth = true },
                    new AppServerMcpServerStatus { ServerName = "github", Enabled = true, SupportsOAuth = true }))
                using (var screenReader = new RecordingScreenReaderService())
                {
                    using (var form = new McpManagementForm(client, screenReader, state))
                    {
                        var filter = FormTestDriver.FindControl<ComboBox>(form, "Filter");
                        form.ActiveControl = filter;
                        var ignored = form.Handle;
                        var serverList = FormTestDriver.FindControl<ListView>(form, "MCP Servers");
                        ignored = serverList.Handle;

                        FormTestDriver.InvokeAndWait(form, "LoadServerStatusesAsync", true);

                        Assert.Multiple(() =>
                        {
                            Assert.That(
                                FormTestDriver.Field<Label>(form, "_statusLabel").Text,
                                Is.EqualTo("Authentication for github has expired. Refresh status, select the server, then choose Login."));
                            Assert.That(serverList.SelectedItems, Has.Count.EqualTo(1));
                            Assert.That(
                                ((AppServerMcpServerStatus)serverList.SelectedItems[0].Tag).ServerName,
                                Is.EqualTo("github"));
                            Assert.That(client.StartOauthLoginCallCount, Is.EqualTo(0));
                            Assert.That(screenReader.SpokenTexts, Is.Empty);
                            Assert.That(form.ActiveControl, Is.SameAs(filter));
                        });
                    }
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpManagementForm_SharedStateConsumesMainNotificationAfterFormSubscriberWithoutLeavingPendingState()
        {
            WithCulture("en-US", () =>
            {
                var state = new McpReauthenticationState();
                McpReauthenticationNotification mainNotification = null;
                using (var client = CreateRecordingClient())
                using (var screenReader = new RecordingScreenReaderService())
                using (var form = new McpManagementForm(client, screenReader, state))
                {
                    var filter = FormTestDriver.FindControl<ComboBox>(form, "Filter");
                    form.ActiveControl = filter;
                    var ignored = form.Handle;

                    client.EventReceived += (sender, e) => mainNotification = state.Record(e);
                    client.RaiseReauthenticationRequired("github");
                    System.Windows.Forms.Application.DoEvents();

                    Assert.Multiple(() =>
                    {
                        Assert.That(mainNotification, Is.Not.Null);
                        Assert.That(mainNotification.ShouldSpeak, Is.True);
                        Assert.That(state.ConsumePending(), Is.Null);
                        Assert.That(
                            FormTestDriver.Field<Label>(form, "_statusLabel").Text,
                            Is.EqualTo("Authentication for github has expired. Refresh status, select the server, then choose Login."));
                        Assert.That(client.StartOauthLoginCallCount, Is.EqualTo(0));
                        Assert.That(screenReader.SpokenTexts, Is.Empty);
                        Assert.That(form.ActiveControl, Is.SameAs(filter));
                    });
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpDiagnosticsForm_ContainsProtocolDiagnosticsActions()
        {
            WithCulture("en-US", () =>
            {
                var server = new AppServerMcpServerStatus
                {
                    ServerName = "codex_apps",
                    ToolCount = 89,
                    ResourceCount = 5
                };

                using (var form = new McpDiagnosticsForm(CreateClient(), server))
                {
                    Assert.That(FormTestDriver.FindControl<Button>(form, "Read Resource"), Is.Not.Null);
                    Assert.That(FormTestDriver.FindControl<Button>(form, "Test Tool Call"), Is.Not.Null);
                    Assert.That(FormTestDriver.FindControl<TextBox>(form, "Diagnostics details").Text, Does.Contain("codex_apps"));
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpResourceViewerForm_ShowsTextAndBlobContentsReadOnly()
        {
            WithCulture("en-US", () =>
            {
                var result = new AppServerMcpResourceReadResult();
                result.Contents.Add(new AppServerMcpResourceContent { Uri = "file:///a.md", MimeType = "text/markdown", Text = "# A" });
                result.Contents.Add(new AppServerMcpResourceContent { Uri = "file:///b.bin", MimeType = "application/octet-stream", Blob = "AAECAw==" });

                using (var form = new McpResourceViewerForm("github", "resource://demo", result))
                {
                    var textBox = FormTestDriver.FindControl<TextBox>(form, "Resource content");

                    Assert.That(textBox.ReadOnly, Is.True);
                    Assert.That(textBox.Text, Does.Contain("# A"));
                    Assert.That(textBox.Text, Does.Contain("Blob content"));
                    Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, "Close"), Is.Null);
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void McpToolTestForm_DoesNotShowPureCloseButton()
        {
            WithCulture("en-US", () =>
            {
                using (var form = new McpToolTestForm(CreateClient(), "thread-1", "github"))
                {
                    Assert.That(FormTestDriver.FindControlOrDefault<Button>(form, "Close"), Is.Null);
                    Assert.That(FormTestDriver.FindControl<Button>(form, "Run Tool"), Is.Not.Null);
                }
            });
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ResourceUriInputForm_WithKnownResourcesShowsChoicesAndSelectsFirstUri()
        {
            WithCulture("en-US", () =>
            {
                var resources = new[]
                {
                    new AppServerMcpResourceSummary
                    {
                        Uri = "app://codex_apps/accounts",
                        Title = "Accounts",
                        Description = "Connected account state"
                    },
                    new AppServerMcpResourceSummary
                    {
                        Uri = "app://codex_apps/tools",
                        Name = "tools"
                    }
                };

                using (var form = new ResourceUriInputForm(resources))
                {
                    var listBox = FormTestDriver.FindControl<ListBox>(form, "Resources");
                    var textBox = FormTestDriver.FindControl<TextBox>(form, "Resource URI");

                    Assert.That(listBox.Items, Has.Count.EqualTo(2));
                    Assert.That(listBox.SelectedIndex, Is.EqualTo(0));
                    Assert.That(textBox.Text, Is.EqualTo("app://codex_apps/accounts"));
                    Assert.That(form.ResourceUri, Is.EqualTo("app://codex_apps/accounts"));
                }
            });
        }

        [Test]
        public void BuildResourceReadFailureMessage_ForUnexpectedResponseTypeExplainsUnsupportedResource()
        {
            WithCulture("en-US", () =>
            {
                var message = McpManagementForm.BuildResourceReadFailureMessageForTests(
                    "codex_apps",
                    "skill://plugin_connector_1/github",
                    new InvalidOperationException("resources/read failed for codex_apps (skill://plugin_connector_1/github): Unexpected response type"));

                Assert.That(message, Does.Contain("cannot be displayed"));
                Assert.That(message, Does.Contain("codex_apps"));
                Assert.That(message, Does.Contain("skill://plugin_connector_1/github"));
                Assert.That(message, Does.Not.StartWith("resources/read failed"));
            });
        }

        private static void WithCulture(string cultureName, Action action)
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;

            try
            {
                var culture = new CultureInfo(cultureName);
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = culture;
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        private static AppServerClient CreateClient()
        {
            var appDataPath = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            return new AppServerClient(
                new CodexRuntimeManager(new AppConfigService(appDataPath)),
                new StubConfigStore());
        }

        private static RecordingAppServerClient CreateRecordingClient(params AppServerMcpServerStatus[] servers)
        {
            var appDataPath = System.IO.Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
            return new RecordingAppServerClient(appDataPath, servers);
        }

        private sealed class RecordingScreenReaderService : IScreenReaderService
        {
            public RecordingScreenReaderService()
            {
                SpokenTexts = new List<string>();
            }

            public List<string> SpokenTexts { get; private set; }

            public bool IsAvailable { get { return true; } }

            public bool IsInitialized { get { return true; } }

            public string LastError { get { return null; } }

            public bool EnsureInitialized() { return true; }

            public bool IsReaderRunning() { return true; }

            public int GetReaderState() { return 1; }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                SpokenTexts.Add(text);
                return true;
            }

            public bool Stop(bool useSlave) { return true; }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingAppServerClient : AppServerClient, IAppServerClient
        {
            private readonly List<AppServerMcpServerStatus> _servers;

            public RecordingAppServerClient(string appDataPath, IEnumerable<AppServerMcpServerStatus> servers)
                : base(
                    new CodexRuntimeManager(new AppConfigService(appDataPath)),
                    new StubConfigStore())
            {
                _servers = new List<AppServerMcpServerStatus>(servers ?? new AppServerMcpServerStatus[0]);
                ProtocolCapabilities = new AppServerProtocolCapabilities
                {
                    SupportsMcpManagement = true,
                    SupportsMcpOAuth = true
                };
            }

            public new event EventHandler<AppServerEventArgs> EventReceived;

            public new AppServerProtocolCapabilities ProtocolCapabilities { get; private set; }

            public int ConnectCallCount { get; private set; }

            public int ListMcpServerStatusesCallCount { get; private set; }

            public int StartOauthLoginCallCount { get; private set; }

            public new Task ConnectAsync()
            {
                ConnectCallCount++;
                return Task.CompletedTask;
            }

            public new Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync()
            {
                ListMcpServerStatusesCallCount++;
                return Task.FromResult(new List<AppServerMcpServerStatus>(_servers));
            }

            public new Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(
                string serverName,
                int? timeoutSecs = null)
            {
                StartOauthLoginCallCount++;
                return Task.FromResult(new AppServerMcpServerOauthLoginResult());
            }

            public void RaiseReauthenticationRequired(string serverName)
            {
                var handler = EventReceived;
                if (handler != null)
                {
                    handler(
                        this,
                        new AppServerEventArgs(
                            "mcp_startup_status_updated",
                            "MCP server authentication expired: " + serverName + ".",
                            reason: "reauthenticationRequired",
                            mcpServerName: serverName,
                            itemStatus: "failed"));
                }
            }
        }

        private sealed class RecordingMcpConnectionClient : IAppServerConnectionClient
        {
            public event EventHandler<AppServerEventArgs> EventReceived
            {
                add { }
                remove { }
            }

            public AppServerProtocolCapabilities ProtocolCapabilities { get; } = new AppServerProtocolCapabilities
            {
                SupportsMcpManagement = true,
                SupportsMcpOAuth = true
            };

            public Task ConnectAsync() { return Task.CompletedTask; }

            public Task RestartAsync() { return Task.CompletedTask; }
        }

        private sealed class RecordingMcpConfigClient : IAppServerConfigClient
        {
            public RecordingMcpConfigClient()
            {
                ConfigSnapshot = new AppServerConfigSnapshot();
            }

            public int ReadConfigCallCount { get; private set; }

            public int WriteConfigBatchCallCount { get; private set; }

            public IReadOnlyList<AppServerConfigWriteEdit> LastConfigEdits { get; private set; }

            public string LastExpectedVersion { get; private set; }

            public bool LastReloadUserConfig { get; private set; }

            public AppServerConfigSnapshot ConfigSnapshot { get; private set; }

            public Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers)
            {
                ReadConfigCallCount++;
                return Task.FromResult(ConfigSnapshot);
            }

            public Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers, CancellationToken cancellationToken)
            {
                return ReadConfigAsync(workspacePath, includeLayers);
            }

            public Task WriteConfigValueAsync(string keyPath, object value) { throw new NotSupportedException(); }

            public Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null, string expectedVersion = null, bool reloadUserConfig = false)
            {
                WriteConfigBatchCallCount++;
                LastConfigEdits = new List<AppServerConfigWriteEdit>(edits);
                LastExpectedVersion = expectedVersion;
                LastReloadUserConfig = reloadUserConfig;
                return Task.CompletedTask;
            }

            public Task<AppServerModelProviderCapabilities> ReadModelProviderCapabilitiesAsync() { throw new NotSupportedException(); }

            public Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync() { throw new NotSupportedException(); }

            public Task<List<AppServerModelOption>> ListModelsAsync() { throw new NotSupportedException(); }

            public Task<List<AppServerModelOption>> ListModelsAsync(CancellationToken cancellationToken) { throw new NotSupportedException(); }

            public Task<AppServerPermissionProfileListResult> ListPermissionProfilesAsync(string workspacePath = null, int? limit = null, string cursor = null, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
        }

        private sealed class RecordingMcpClient : IAppServerMcpClient
        {
            public bool FailReload;
            private readonly List<AppServerMcpServerStatus> _servers;

            public RecordingMcpClient(params AppServerMcpServerStatus[] servers)
            {
                _servers = new List<AppServerMcpServerStatus>(servers ?? new AppServerMcpServerStatus[0]);
            }

            public int ReloadMcpConfigCallCount { get; private set; }

            public Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync()
            {
                return Task.FromResult(new List<AppServerMcpServerStatus>(_servers));
            }

            public Task ReloadMcpServerConfigAsync()
            {
                ReloadMcpConfigCallCount++;
                if (FailReload) throw new InvalidOperationException("Synthetic reload failure.");
                return Task.CompletedTask;
            }

            public Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(string serverName, int? timeoutSecs = null) { throw new NotSupportedException(); }

            public Task<AppServerMcpResourceReadResult> ReadMcpResourceAsync(string server, string uri, string threadId = null, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }

            public Task<AppServerMcpToolCallResult> CallMcpToolAsync(AppServerMcpToolCallRequest request, CancellationToken cancellationToken = default(CancellationToken)) { throw new NotSupportedException(); }
        }

        private sealed class StubConfigStore : IAppConfigStore
        {
            public MyCodex.Domain.Entities.AppConfigSnapshot Load()
            {
                return new MyCodex.Domain.Entities.AppConfigSnapshot();
            }

            public void Save(MyCodex.Domain.Entities.AppConfigSnapshot snapshot)
            {
            }
        }
    }
}
