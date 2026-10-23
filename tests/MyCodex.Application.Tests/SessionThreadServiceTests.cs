using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.InternalTools;
using MyCodex.Application.Services;
using MyCodex.Application.Tests.Fakes;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public class SessionThreadServiceTests
    {
        [TestCase(null)]
        [TestCase("default")]
        [TestCase("plan")]
        public async Task ModelSettingsLifecycle_CreationOverridesAndReturnToGlobalReachSendPath(string mode)
        {
            var client = new RecordingAppServerClient();
            client.ThreadIdsToCreate.Enqueue("model-check-thread");
            var service = new SessionThreadService(client, new WorkspaceService(), path => null);
            var session = new ChatSession();
            var workspace = CreateWorkspace();
            var official = new AppServerConfigSnapshot { ModelId = "global-model", ModelReasoningEffort = "medium" };
            var models = new[]
            {
                new AppServerModelOption { ModelId = "global-model", DefaultReasoningEffort = "medium" },
                new AppServerModelOption { ModelId = "session-model", DefaultReasoningEffort = "low" }
            };
            var defaultsService = new SessionDefaultsService();
            service.ModelSettingsResolver = (target, targetWorkspace) => Task.FromResult(
                SessionDefaultsService.ResolveSession(defaultsService.Resolve(new AppConfigSnapshot(), models, null, official), target, models));

            await service.RecreateThreadAsync(session, workspace, ApprovalMode.ReadOnly, "stale-model", "low");
            Assert.That(client.CreateThreadCalls[0].ModelId, Is.EqualTo("global-model"));
            Assert.That(client.CreateThreadCalls[0].ReasoningEffort, Is.EqualTo("medium"));
            var input = new[] { new AppServerUserInputItem { Type = "text", Text = "test" } };
            var collaboration = mode == null ? null : new AppServerCollaborationMode
            {
                Mode = mode, ModelId = "stale-model", ReasoningEffort = "low", DeveloperInstructions = "Keep mode instructions"
            };
            var expectedModels = new[] { "global-model", "session-model", "session-model", "session-model", "new-global" };
            var expectedEfforts = new[] { "medium", "low", "high", "high", "xhigh" };
            for (var step = 0; step < expectedModels.Length; step++)
            {
                if (step == 1) { session.ModelId = "session-model"; session.ReasoningEffort = "low"; }
                if (step == 2) { session.ReasoningEffort = "high"; }
                if (step == 3) { official.ModelId = "new-global"; official.ModelReasoningEffort = "xhigh"; }
                if (step == 4) { session.ModelId = null; session.ReasoningEffort = null; }
                await service.SendUserMessageAsync(session, workspace, input, ApprovalMode.ReadOnly, "stale-model", "low", collaboration);
                var sent = client.SendCalls[step];
                Assert.That(sent.ModelId, Is.EqualTo(expectedModels[step]), "model at step " + step);
                Assert.That(sent.ReasoningEffort, Is.EqualTo(expectedEfforts[step]), "effort at step " + step);
                if (mode != null)
                {
                    Assert.That(sent.CollaborationMode.ModelId, Is.EqualTo(expectedModels[step]));
                    Assert.That(sent.CollaborationMode.ReasoningEffort, Is.EqualTo(expectedEfforts[step]));
                    Assert.That(sent.CollaborationMode.DeveloperInstructions, Is.EqualTo("Keep mode instructions"));
                }
            }
            Assert.That(session.ModelId, Is.Null, "Following global settings must not pin the resolved model.");
            Assert.That(session.ReasoningEffort, Is.Null);
        }

        [TestCase(null)]
        [TestCase("plan")]
        public async Task ProviderSwitch_UsesSupportedDefaultOnCreateAndSend(string mode)
        {
            var client = new RecordingAppServerClient();
            client.ThreadIdsToCreate.Enqueue("switched-provider-thread");
            var service = new SessionThreadService(client, new WorkspaceService(), path => null);
            var session = new ChatSession();
            var workspace = CreateWorkspace();
            var model = new AppServerModelOption { ModelId = "qwen-3.8-max", DefaultReasoningEffort = "low" };
            model.ReasoningEfforts.Add(new AppServerModelEffortOption { EffortId = "low" });
            var models = new[] { model };
            var official = new AppServerConfigSnapshot { ModelId = model.ModelId, ModelReasoningEffort = "high" };
            service.ModelSettingsResolver = (target, targetWorkspace) =>
            {
                var defaults = new SessionDefaultsService().Resolve(new AppConfigSnapshot(), models, null, official);
                var settings = SessionDefaultsService.ResolveSession(defaults, target, models);
                Assert.That(SessionDefaultsService.IsReasoningEffortAvailable(settings.ModelId, settings.ReasoningEffort, models), Is.True);
                return Task.FromResult(settings);
            };

            await service.RecreateThreadAsync(session, workspace, ApprovalMode.ReadOnly, "gpt-6-sol", "high");
            await service.SendUserMessageAsync(session, workspace,
                new[] { new AppServerUserInputItem { Type = "text", Text = "test" } },
                ApprovalMode.ReadOnly, "gpt-6-sol", "high",
                mode == null ? null : new AppServerCollaborationMode { Mode = mode, ModelId = "gpt-6-sol", ReasoningEffort = "high" });

            Assert.That(client.CreateThreadCalls[0].ModelId, Is.EqualTo(model.ModelId));
            Assert.That(client.CreateThreadCalls[0].ReasoningEffort, Is.EqualTo("low"));
            Assert.That(client.SendCalls[0].ModelId, Is.EqualTo(model.ModelId));
            Assert.That(client.SendCalls[0].ReasoningEffort, Is.EqualTo("low"));
            if (mode != null)
            {
                Assert.That(client.SendCalls[0].CollaborationMode.ReasoningEffort, Is.EqualTo("low"));
            }
            Assert.That(session.ReasoningEffort, Is.Null, "Following global defaults must not create a session override.");
        }

        [Test]
        public async Task RecreateThreadAsync_UpdatesSessionAndThreadMetadata()
        {
            var client = new RecordingAppServerClient();
            client.ThreadIdsToCreate.Enqueue("thread-2");
            var service = new SessionThreadService(client, new WorkspaceService());
            var session = new ChatSession
            {
                ThreadId = "thread-1",
                Title = " Demo Session ",
                SessionPath = @"C:\sessions\thread-1.jsonl",
                LastActiveAt = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Local)
            };
            var workspace = CreateWorkspace();

            var result = await service.RecreateThreadAsync(session, workspace, ApprovalMode.Auto, "gpt-5.4-mini", "high");

            Assert.That(result.PreviousThreadId, Is.EqualTo("thread-1"));
            Assert.That(result.CurrentThreadId, Is.EqualTo("thread-2"));
            Assert.That(session.ThreadId, Is.EqualTo("thread-2"));
            Assert.That(session.SessionPath, Is.Null);
            Assert.That(client.CreateThreadCalls.Count, Is.EqualTo(1));
            Assert.That(client.CreateThreadCalls[0].ModelId, Is.EqualTo("gpt-5.4-mini"));
            Assert.That(client.CreateThreadCalls[0].ReasoningEffort, Is.EqualTo("high"));
            Assert.That(client.SetThreadNameCalls.Count, Is.EqualTo(1));
            Assert.That(client.SetThreadNameCalls[0].ThreadId, Is.EqualTo("thread-2"));
            Assert.That(client.SetThreadNameCalls[0].Name, Is.EqualTo(" Demo Session "));
            Assert.That(client.GitMetadataCalls.Count, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SendUserMessageAsync_RecreatesMissingThreadAndRetries(bool deferMetadata)
        {
            var client = new RecordingAppServerClient();
            client.ThreadIdsToCreate.Enqueue("thread-2");
            client.RemainingMissingThreadFailures = 1;
            var service = new SessionThreadService(client, new WorkspaceService());
            var session = new ChatSession
            {
                ThreadId = "thread-1",
                Title = "Demo Session"
            };
            var workspace = CreateWorkspace();
            var inputItems = new[]
            {
                new AppServerUserInputItem
                {
                    Type = "text",
                    Text = "hello"
                }
            };

            var result = await service.SendUserMessageAsync(session, workspace, inputItems, ApprovalMode.Auto, "gpt-5.4-mini", "medium", deferMetadata: deferMetadata);

            Assert.That(result.RecreatedThreadAfterMissingThread, Is.True);
            Assert.That(result.RetryThreadRecreation, Is.Not.Null);
            Assert.That(result.RetryThreadRecreation.PreviousThreadId, Is.EqualTo("thread-1"));
            Assert.That(result.RetryThreadRecreation.CurrentThreadId, Is.EqualTo("thread-2"));
            Assert.That(session.ThreadId, Is.EqualTo("thread-2"));
            Assert.That(client.CreateThreadCalls.Count, Is.EqualTo(1));
            Assert.That(client.CreateThreadCalls[0].ModelId, Is.EqualTo("gpt-5.4-mini"));
            Assert.That(client.CreateThreadCalls[0].ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(client.SendCalls.Count, Is.EqualTo(2));
            Assert.That(client.SendCalls[0].ThreadId, Is.EqualTo("thread-1"));
            Assert.That(client.SendCalls[0].ModelId, Is.EqualTo("gpt-5.4-mini"));
            Assert.That(client.SendCalls[0].ReasoningEffort, Is.EqualTo("medium"));
            Assert.That(client.SendCalls[1].ThreadId, Is.EqualTo("thread-2"));
            Assert.That(client.SendCalls[1].ModelId, Is.EqualTo("gpt-5.4-mini"));
            Assert.That(client.SendCalls[1].ReasoningEffort, Is.EqualTo("medium"));
            if (deferMetadata)
            {
                Assert.That(client.GitMetadataCalls, Is.Empty, "Recreation must not delay the retry with metadata writes.");
                await service.SynchronizeMetadataAsync(session, workspace, "connection", () => true);
            }
            Assert.That(client.GitMetadataCalls.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task SendUserMessageAsync_RepairsInvalidEncryptedContentAndRetries()
        {
            var client = new RecordingAppServerClient();
            client.RemainingInvalidEncryptedContentFailures = 1;
            var repairService = new RecordingThreadHistoryRepairService
            {
                Result = ThreadHistoryRepairResult.CreateRepaired(1)
            };
            var service = new SessionThreadService(client, new WorkspaceService(), null, repairService);
            var session = new ChatSession
            {
                ThreadId = "thread-1",
                SessionPath = @"C:\Users\me\.codex\sessions\thread-1.jsonl"
            };
            var workspace = CreateWorkspace();
            var inputItems = new[]
            {
                new AppServerUserInputItem
                {
                    Type = "text",
                    Text = "continue"
                }
            };

            var result = await service.SendUserMessageAsync(session, workspace, inputItems, ApprovalMode.Auto, "gpt-5.5", "high");

            Assert.That(result.RepairedInvalidEncryptedContent, Is.True);
            Assert.That(result.InvalidEncryptedContentRepairResult.RepairCount, Is.EqualTo(1));
            Assert.That(repairService.RepairCalls.Count, Is.EqualTo(1));
            Assert.That(repairService.RepairCalls[0], Is.SameAs(session));
            Assert.That(client.UnsubscribedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-1" }));
            Assert.That(client.SendCalls.Count, Is.EqualTo(2));
            Assert.That(client.SendCalls[0].ThreadId, Is.EqualTo("thread-1"));
            Assert.That(client.SendCalls[1].ThreadId, Is.EqualTo("thread-1"));
        }

        [Test]
        public void SendUserMessageAsync_InvalidEncryptedContentWithoutRepairUnsubscribesBeforeRethrowingOriginalError()
        {
            var client = new RecordingAppServerClient();
            client.RemainingInvalidEncryptedContentFailures = 1;
            var repairService = new RecordingThreadHistoryRepairService
            {
                Result = ThreadHistoryRepairResult.NotRepaired("no changes")
            };
            var service = new SessionThreadService(client, new WorkspaceService(), null, repairService);
            var session = new ChatSession
            {
                ThreadId = "thread-1",
                SessionPath = @"C:\Users\me\.codex\sessions\thread-1.jsonl"
            };
            var workspace = CreateWorkspace();
            var inputItems = new[]
            {
                new AppServerUserInputItem
                {
                    Type = "text",
                    Text = "continue"
                }
            };

            var ex = Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.SendUserMessageAsync(session, workspace, inputItems, ApprovalMode.Auto, "gpt-5.5", "high"));

            Assert.That(ex.Message, Does.Contain("invalid_encrypted_content"));
            Assert.That(client.SendCalls.Count, Is.EqualTo(1));
            Assert.That(client.UnsubscribedThreadIds.ToArray(), Is.EqualTo(new[] { "thread-1" }));
        }

        [Test]
        public async Task RecreateThreadAsync_PassesThreadToolContextToAppServerClient()
        {
            var client = new RecordingAppServerClient();
            client.ThreadIdsToCreate.Enqueue("thread-tools");
            var service = new SessionThreadService(client, new WorkspaceService());
            var session = new ChatSession { ThreadId = "thread-1" };
            var workspace = CreateWorkspace();
            var toolContext = CreateToolContext(workspace.RootPath);

            await service.RecreateThreadAsync(session, workspace, ApprovalMode.Auto, "gpt-5.4-mini", "medium", toolContext);

            Assert.That(client.CreateThreadCalls.Count, Is.EqualTo(1));
            Assert.That(client.CreateThreadCalls[0].ToolContext, Is.SameAs(toolContext));
        }

        [Test]
        public async Task SendUserMessageAsync_MissingThreadRetryPreservesThreadToolContext()
        {
            var client = new RecordingAppServerClient();
            client.ThreadIdsToCreate.Enqueue("thread-tools");
            client.RemainingMissingThreadFailures = 1;
            var service = new SessionThreadService(client, new WorkspaceService());
            var session = new ChatSession { ThreadId = "thread-1" };
            var workspace = CreateWorkspace();
            var toolContext = CreateToolContext(workspace.RootPath);
            var inputItems = new[]
            {
                new AppServerUserInputItem
                {
                    Type = "text",
                    Text = "create a document"
                }
            };

            await service.SendUserMessageAsync(session, workspace, inputItems, ApprovalMode.Auto, "gpt-5.4-mini", "medium", null, toolContext);

            Assert.That(client.CreateThreadCalls.Count, Is.EqualTo(1));
            Assert.That(client.CreateThreadCalls[0].ToolContext, Is.SameAs(toolContext));
        }

        [Test]
        public async Task SendUserMessageAsync_SendsWithoutRecreatingWhenThreadIsHealthy()
        {
            var client = new RecordingAppServerClient();
            var service = new SessionThreadService(client, new WorkspaceService());
            var session = new ChatSession
            {
                ThreadId = "thread-1",
                LastActiveAt = new DateTime(2024, 1, 1, 8, 0, 0, DateTimeKind.Local)
            };
            var workspace = CreateWorkspace();
            var inputItems = new[]
            {
                new AppServerUserInputItem
                {
                    Type = "text",
                    Text = "hello"
                }
            };

            var result = await service.SendUserMessageAsync(session, workspace, inputItems, ApprovalMode.ReadOnly, null, null);

            Assert.That(result.RecreatedThreadAfterMissingThread, Is.False);
            Assert.That(client.SendCalls.Count, Is.EqualTo(1));
            Assert.That(client.SendCalls[0].ThreadId, Is.EqualTo("thread-1"));
            Assert.That(client.CreateThreadCalls.Count, Is.EqualTo(0));
        }

        [Test]
        public async Task SendUserMessageAsync_ExistingThreadCanOmitModelOverride()
        {
            var client = new RecordingAppServerClient();
            var service = new SessionThreadService(client, new WorkspaceService());
            var session = new ChatSession
            {
                ThreadId = "thread-openai",
                ModelProviderId = "openai"
            };
            var workspace = CreateWorkspace();
            var inputItems = new[]
            {
                new AppServerUserInputItem
                {
                    Type = "text",
                    Text = "continue"
                }
            };

            await service.SendUserMessageAsync(session, workspace, inputItems, ApprovalMode.ReadOnly, null, null);

            Assert.That(client.SendCalls.Count, Is.EqualTo(1));
            Assert.That(client.SendCalls[0].ThreadId, Is.EqualTo("thread-openai"));
            Assert.That(client.SendCalls[0].ModelId, Is.Null);
            Assert.That(client.SendCalls[0].ReasoningEffort, Is.Null);
        }

        [Test]
        public async Task SendUserMessageAsync_PassesCollaborationModeToAppServerClient()
        {
            var client = new RecordingAppServerClient();
            var service = new SessionThreadService(client, new WorkspaceService());
            var session = new ChatSession
            {
                ThreadId = "thread-1"
            };
            var workspace = CreateWorkspace();
            var inputItems = new[]
            {
                new AppServerUserInputItem
                {
                    Type = "text",
                    Text = "draft a plan"
                }
            };
            var collaborationMode = new AppServerCollaborationMode
            {
                Mode = "plan",
                ModelId = "gpt-5.4-mini",
                ReasoningEffort = "medium"
            };

            await service.SendUserMessageAsync(session, workspace, inputItems, ApprovalMode.Auto, "gpt-5.4-mini", "medium", collaborationMode);

            Assert.That(client.SendCalls.Count, Is.EqualTo(1));
            Assert.That(client.SendCalls[0].CollaborationMode, Is.SameAs(collaborationMode));
        }

        [Test]
        public async Task TryUpdateThreadGitMetadataAsync_WhenGitReadIsSlow_ReturnsTaskWithoutBlockingCaller()
        {
            var client = new RecordingAppServerClient();
            var service = new SessionThreadService(
                client,
                new WorkspaceService(),
                path =>
                {
                    Thread.Sleep(1200);
                    return new WorkspaceGitInfoSnapshot
                    {
                        IsGitRepository = true,
                        Branch = "main",
                        Sha = "abc123",
                        OriginUrl = "https://example.invalid/repo.git"
                    };
                });
            var workspace = CreateWorkspace();
            var watch = Stopwatch.StartNew();

            var task = service.TryUpdateThreadGitMetadataAsync("thread-1", workspace);
            watch.Stop();

            Assert.That(watch.ElapsedMilliseconds, Is.LessThan(500));
            await task;
            Assert.That(client.GitMetadataCalls.Count, Is.EqualTo(1));
        }

        [Test]
        public void IsMissingThreadRolloutError_MatchesNestedMissingThreadFailure()
        {
            var exception = new InvalidOperationException(
                "wrapper",
                new Exception("failed to locate rollout for thread: demo"));

            Assert.That(SessionThreadService.IsMissingThreadRolloutError(exception), Is.True);
        }

        private static WorkspaceInfo CreateWorkspace()
        {
            return new WorkspaceInfo
            {
                Id = "workspace-1",
                Name = "Demo",
                RootPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
        }

        private static AppServerThreadToolContext CreateToolContext(string rootPath)
        {
            return new AppServerThreadToolContext
            {
                DynamicTools = new[]
                {
                    new AppServerDynamicToolSpec
                    {
                        Namespace = "office",
                        Name = "create_document",
                        Description = "Create an office document.",
                        InputSchema = new Dictionary<string, object>()
                    }
                },
                ExecutionContext = new InternalToolExecutionContext
                {
                    Mode = "Office",
                    WorkspacePath = rootPath,
                    OfficeRootPath = rootPath
                }
            };
        }

        [TestCase("default")]
        [TestCase("plan")]
        public async Task Send_UsesFreshOfficialSettingsForTurnAndCollaborationMode(string mode)
        {
            var client = new RecordingAppServerClient();
            var service = new SessionThreadService(client, new WorkspaceService(), path => null);
            var workspace = new WorkspaceInfo { RootPath = "/project" };
            var session = new ChatSession { ThreadId = "thread" };
            var reads = 0;
            service.ModelSettingsResolver = (target, targetWorkspace) =>
            {
                reads++;
                Assert.That(targetWorkspace, Is.SameAs(workspace));
                return Task.FromResult(new SessionDefaultsResolution { ModelId = "official-" + reads, ReasoningEffort = "high" });
            };
            var collaboration = new AppServerCollaborationMode
            {
                Mode = mode, ModelId = "stale", ReasoningEffort = "low", DeveloperInstructions = "Office instructions"
            };
            for (var turn = 1; turn <= 2; turn++)
            {
                await service.SendUserMessageAsync(session, workspace,
                    new[] { new AppServerUserInputItem { Type = "text", Text = "message" } },
                    null, "stale", "low", collaboration);
                var sent = client.SendCalls[turn - 1];
                Assert.That(sent.ModelId, Is.EqualTo("official-" + turn));
                Assert.That(sent.ReasoningEffort, Is.EqualTo("high"));
                Assert.That(sent.CollaborationMode.ModelId, Is.EqualTo(sent.ModelId));
                Assert.That(sent.CollaborationMode.ReasoningEffort, Is.EqualTo(sent.ReasoningEffort));
                Assert.That(sent.CollaborationMode.DeveloperInstructions, Is.EqualTo("Office instructions"));
            }
            Assert.That(collaboration.ModelId, Is.EqualTo("stale"), "Do not mutate a queued/shared payload.");
        }

        [Test]
        public void Send_DoesNotUseStaleModelWhenConfigRefreshFails()
        {
            var client = new RecordingAppServerClient();
            var service = new SessionThreadService(client, new WorkspaceService(), path => null);
            service.ModelSettingsResolver = (session, workspace) => Task.FromException<SessionDefaultsResolution>(new InvalidOperationException("read failed"));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await service.SendUserMessageAsync(
                new ChatSession { ThreadId = "thread" }, new WorkspaceInfo { RootPath = "/project" },
                new[] { new AppServerUserInputItem { Type = "text", Text = "message" } }, null, "stale", "low"));
            Assert.That(client.SendCalls, Is.Empty);
        }

        private sealed class RecordingAppServerClient : UnsupportedAppServerThreadClient
        {
            public event EventHandler<AppServerEventArgs> EventReceived
            {
                add { }
                remove { }
            }

            public Queue<string> ThreadIdsToCreate { get; } = new Queue<string>();

            public AppServerProtocolCapabilities ProtocolCapabilities { get; } = new AppServerProtocolCapabilities
            {
                ProtocolProfile = "modern",
                SupportsModernAppServerProtocol = true
            };

            public int RemainingMissingThreadFailures { get; set; }

            public int RemainingInvalidEncryptedContentFailures { get; set; }

            public List<CreateThreadCall> CreateThreadCalls { get; } = new List<CreateThreadCall>();

            public List<SendUserMessageCall> SendCalls { get; } = new List<SendUserMessageCall>();

            public List<SetThreadNameCall> SetThreadNameCalls { get; } = new List<SetThreadNameCall>();

            public List<GitMetadataCall> GitMetadataCalls { get; } = new List<GitMetadataCall>();

            public List<string> UnsubscribedThreadIds { get; } = new List<string>();

            public Task ConnectAsync()
            {
                throw new NotSupportedException();
            }

            public Task RestartAsync()
            {
                throw new NotSupportedException();
            }

            public override Task<string> CreateThreadAsync(
                string workspacePath,
                ApprovalMode? approvalMode,
                string modelId,
                string reasoningEffort,
                AppServerThreadToolContext toolContext = null)
            {
                CreateThreadCalls.Add(new CreateThreadCall
                {
                    WorkspacePath = workspacePath,
                    ApprovalMode = approvalMode,
                    ModelId = modelId,
                    ReasoningEffort = reasoningEffort,
                    ToolContext = toolContext
                });
                var threadId = ThreadIdsToCreate.Count > 0 ? ThreadIdsToCreate.Dequeue() : "created-thread";
                return Task.FromResult(threadId);
            }

            public override Task SendUserMessageAsync(
                string threadId,
                string workspacePath,
                IReadOnlyCollection<AppServerUserInputItem> inputItems,
                ApprovalMode? approvalMode,
                string modelId,
                string reasoningEffort,
                AppServerCollaborationMode collaborationMode = null)
            {
                SendCalls.Add(new SendUserMessageCall
                {
                    ThreadId = threadId,
                    WorkspacePath = workspacePath,
                    InputItems = inputItems,
                    ApprovalMode = approvalMode,
                    ModelId = modelId,
                    ReasoningEffort = reasoningEffort,
                    CollaborationMode = collaborationMode
                });

                if (RemainingMissingThreadFailures > 0)
                {
                    RemainingMissingThreadFailures--;
                    throw new InvalidOperationException("no rollout found for thread id");
                }

                if (RemainingInvalidEncryptedContentFailures > 0)
                {
                    RemainingInvalidEncryptedContentFailures--;
                    throw new InvalidOperationException("{\"type\":\"invalid_request_error\",\"code\":\"invalid_encrypted_content\",\"message\":\"Encrypted content could not be decrypted or parsed.\"}");
                }

                return Task.CompletedTask;
            }

            public bool HasInterruptibleTurn(string threadId)
            {
                throw new NotSupportedException();
            }

            public Task InterruptAsync(string threadId, string turnId = null)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerLoginResult> StartChatGptLoginAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerLoginResult> StartChatGptDeviceCodeLoginAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerLoginResult> StartOpenAiApiKeyLoginAsync(string apiKey)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerRateLimitsSnapshot> GetRateLimitsAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers)
            {
                throw new NotSupportedException();
            }

            public Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null, string expectedVersion = null, bool reloadUserConfig = false)
            {
                throw new NotSupportedException();
            }

            public Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync()
            {
                throw new NotSupportedException();
            }

            public Task ReloadMcpServerConfigAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(string serverName, int? timeoutSecs = null)
            {
                throw new NotSupportedException();
            }

            public Task<List<AppServerSkillsSnapshot>> ListSkillsAsync(IReadOnlyCollection<string> workspacePaths, bool forceReload)
            {
                throw new NotSupportedException();
            }

            public Task<bool> WriteSkillEnabledAsync(string path, bool enabled)
            {
                throw new NotSupportedException();
            }

            public Task<List<AppServerPluginSummary>> ListPluginsAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerPluginDetails> ReadPluginAsync(string marketplacePath, string pluginName)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerPluginInstallResult> InstallPluginAsync(string marketplacePath, string pluginName)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerPluginInstallResult> InstallRemotePluginAsync(string remoteMarketplaceName, string remotePluginId)
            {
                throw new NotSupportedException();
            }

            public Task UninstallPluginAsync(string pluginId)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync()
            {
                throw new NotSupportedException();
            }

            public Task<List<AppServerModelOption>> ListModelsAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerCommandExecResult> ExecuteCommandAsync(string commandText, string workspacePath, ApprovalMode approvalMode, string processId, long timeoutMs)
            {
                throw new NotSupportedException();
            }

            public Task TerminateCommandAsync(string processId)
            {
                throw new NotSupportedException();
            }

            public Task WriteCommandInputAsync(string processId, string input)
            {
                throw new NotSupportedException();
            }

            public override Task<List<AppServerThreadSummary>> ListThreadsAsync()
            {
                throw new NotSupportedException();
            }

            public override Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null)
            {
                throw new NotSupportedException();
            }

            public override Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerConversationSummary> GetConversationSummaryAsync(AppServerConversationSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult<AppServerConversationSummary>(null);
            }

            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            public override Task ArchiveThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task UnarchiveThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadSummary> ForkThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }



            public override Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerTurnSteerResult> TrySteerTurnAsync(string threadId, IReadOnlyCollection<AppServerUserInputItem> inputItems, string clientUserMessageId = null)
            {
                return Task.FromResult(AppServerTurnSteerResult.NotAttempted(null, "test-double"));
            }

            public override Task ApproveGuardianDeniedActionAsync(AppServerGuardianDeniedActionApprovalRequest request, CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.CompletedTask;
            }

            public override Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null)
            {
                throw new NotSupportedException();
            }

            public override Task DeleteThreadAsync(string threadId)
            {
                throw new NotSupportedException();
            }

            public override Task UnsubscribeThreadAsync(string threadId)
            {
                UnsubscribedThreadIds.Add(threadId);
                return Task.CompletedTask;
            }

            public override Task DeleteThreadFileAsync(string sessionPath)
            {
                throw new NotSupportedException();
            }

            public override Task SetThreadNameAsync(string threadId, string name)
            {
                SetThreadNameCalls.Add(new SetThreadNameCall
                {
                    ThreadId = threadId,
                    Name = name
                });
                return Task.CompletedTask;
            }

            public override Task<AppServerThreadGoal> SetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, string objective, long? tokenBudget = null)
            {
                throw new NotSupportedException();
            }

            public override Task<AppServerThreadGoal> GetThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task ClearThreadGoalAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
            {
                throw new NotSupportedException();
            }

            public override Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo)
            {
                GitMetadataCalls.Add(new GitMetadataCall
                {
                    ThreadId = threadId,
                    GitInfo = gitInfo
                });
                return Task.CompletedTask;
            }

            public Task ResolveApprovalRequestAsync(string requestId, AppServerApprovalDecision decision)
            {
                throw new NotSupportedException();
            }

            public Task ResolveToolUserInputRequestAsync(string requestId, IDictionary<string, string[]> answers)
            {
                throw new NotSupportedException();
            }

            public Task CancelToolUserInputRequestAsync(string requestId)
            {
                throw new NotSupportedException();
            }

            public sealed class CreateThreadCall
            {
                public string WorkspacePath { get; set; }

                public ApprovalMode? ApprovalMode { get; set; }

                public string ModelId { get; set; }

                public string ReasoningEffort { get; set; }

                public AppServerThreadToolContext ToolContext { get; set; }
            }

            public sealed class SendUserMessageCall
            {
                public string ThreadId { get; set; }

                public string WorkspacePath { get; set; }

                public IReadOnlyCollection<AppServerUserInputItem> InputItems { get; set; }

                public ApprovalMode? ApprovalMode { get; set; }

                public string ModelId { get; set; }

                public string ReasoningEffort { get; set; }

                public AppServerCollaborationMode CollaborationMode { get; set; }
            }

            public sealed class SetThreadNameCall
            {
                public string ThreadId { get; set; }

                public string Name { get; set; }
            }

            public sealed class GitMetadataCall
            {
                public string ThreadId { get; set; }

                public AppServerThreadGitInfoUpdate GitInfo { get; set; }
            }
        }

        private sealed class RecordingThreadHistoryRepairService : IThreadHistoryRepairService
        {
            public List<ChatSession> RepairCalls { get; } = new List<ChatSession>();

            public ThreadHistoryRepairResult Result { get; set; } = ThreadHistoryRepairResult.NotRepaired("not configured");

            public Task<ThreadHistoryRepairResult> TryRepairInvalidEncryptedContentAsync(ChatSession session)
            {
                RepairCalls.Add(session);
                return Task.FromResult(Result);
            }
        }
    }
}
