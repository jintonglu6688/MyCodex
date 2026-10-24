using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.InternalTools;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Markets;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient : IAppServerClient, IAppServerLocalThreadCatalogClient, IAppServerSubAgentClient, IDisposable
    {
        private const int DefaultRequestTimeoutMs = 60000;
        private const int LongRunningRequestTimeoutMs = 300000;
        private const int CommandRequestTimeoutBufferMs = 30000;
        private const int WslNetworkStartupProbeTimeoutMilliseconds = 12000;
        private const int WslNetworkActionProbeTimeoutMilliseconds = 15000;
        private readonly object _syncRoot = new object();
        private readonly SemaphoreSlim _connectionLifecycleLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);
        private readonly JavaScriptSerializer _serializer = AppServerJsonValue.CreateSerializer();
        private readonly AppServerProtocolTransport _protocolTransport;
        private readonly AppServerClientOperations _operations;
        private readonly AppServerThreadOperations _threadOperations;
        private readonly RuntimeThreadScanCatalog _runtimeThreadScanCatalog;
        private readonly AppServerDynamicRequestCoordinator _dynamicRequestCoordinator;
        private readonly AppServerNotificationEventProcessor _notificationEventProcessor;
        private readonly Dictionary<string, CancellationTokenSource> _localCommandCancellationSources =
            new Dictionary<string, CancellationTokenSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Action<string, string>> _commandOutputReceivers =
            new Dictionary<string, Action<string, string>>(StringComparer.Ordinal);
        private readonly HashSet<string> _loadedThreadIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _activeTurnIds = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, ApprovalMode> _threadApprovalModes = new Dictionary<string, ApprovalMode>(StringComparer.Ordinal);
        private IInternalToolRegistry _internalToolRegistry = new InternalToolRegistry(null);
        private readonly Dictionary<string, InternalToolExecutionContext> _internalToolContexts =
            new Dictionary<string, InternalToolExecutionContext>(StringComparer.OrdinalIgnoreCase);
        private readonly AppServerPendingRequestRegistry _pendingRequestRegistry = new AppServerPendingRequestRegistry();
        private readonly CodexRuntimeManager _runtimeManager;
        private readonly IAppConfigStore _appConfigStore;
        private readonly CustomApprovalPolicyEvaluator _customApprovalPolicyEvaluator = new CustomApprovalPolicyEvaluator();
        private readonly AppServerRuntimeStartupCoordinator _runtimeStartupCoordinator;
        private readonly CodexSessionFileStore _sessionFileStore;
        private readonly IWslNetworkReadinessProbe _wslNetworkReadinessProbe;
        private AppServerPathMappingPolicy _pathMappingPolicy =
            AppServerPathMappingPolicy.ForHostPlatform(AppServerHostPlatform.Windows);
        private AppServerCommandLinePolicy _commandLinePolicy =
            AppServerCommandLinePolicy.ForHostPlatform(AppServerHostPlatform.Windows, null);

        private Process _process;
        private StreamWriter _inputWriter;
        private CancellationTokenSource _lifetimeCts;
        private bool _isConnected;
        private bool _connectionLostHandled;
        private long _connectionEpoch;
        private string _connectedRuntimeKey;

        public event EventHandler<AppServerEventArgs> EventReceived;

        public AppServerProtocolCapabilities ProtocolCapabilities { get; private set; }

        public AppServerClient(
            CodexRuntimeManager runtimeManager,
            IAppConfigStore appConfigStore,
            IInternalToolRegistry internalToolRegistry = null)
            : this(
                runtimeManager,
                appConfigStore,
                new WslNetworkReadinessProbe(),
                internalToolRegistry)
        {
        }

        internal AppServerClient(
            CodexRuntimeManager runtimeManager,
            IAppConfigStore appConfigStore,
            IWslNetworkReadinessProbe wslNetworkReadinessProbe,
            IInternalToolRegistry internalToolRegistry = null)
        {
            _runtimeManager = runtimeManager ?? throw new ArgumentNullException(nameof(runtimeManager));
            _appConfigStore = appConfigStore ?? throw new ArgumentNullException(nameof(appConfigStore));
            _wslNetworkReadinessProbe = wslNetworkReadinessProbe ?? throw new ArgumentNullException(nameof(wslNetworkReadinessProbe));
            _internalToolRegistry = internalToolRegistry ?? new InternalToolRegistry(null);
            _runtimeStartupCoordinator = new AppServerRuntimeStartupCoordinator(
                _wslNetworkReadinessProbe,
                GetCurrentHostPlatform);
            _sessionFileStore = new CodexSessionFileStore(GetCurrentHostPlatform, _appConfigStore.Load);
            _runtimeThreadScanCatalog = new RuntimeThreadScanCatalog(_appConfigStore);
            _protocolTransport = new AppServerProtocolTransport(
                () => _inputWriter,
                _serializer,
                _writeLock,
                _syncRoot,
                CreateRequestTimeoutException,
                GetConnectionEpoch);
            _operations = new AppServerClientOperations(
                EnsureConnected,
                () => ProtocolCapabilities,
                SendRequestAsync,
                MapRequiredClientPathToServer,
                MapClientOrServerPathToServer,
                MapServerPathToClient,
                ResolveCurrentPluginSkillPath,
                TraceSkillsListDiagnostics,
                TracePluginListDiagnostics);
            _threadOperations = new AppServerThreadOperations(
                EnsureConnected,
                () => ProtocolCapabilities,
                SendRequestAsync,
                MapRequiredClientPathToServer,
                MapServerPathToClient,
                EnsureThreadLoadedAsync,
                EnsureWslNetworkReadyForConnectedActionAsync,
                ResolveLocalCodexHomePathsForThreadScanAsync,
                ListRuntimeThreadSummariesForThreadScanAsync,
                GetConversationSummaryAsync,
                GetLocallyKnownLoadedThreadIds,
                RememberLoadedThreadId,
                ForgetThreadState);
            _dynamicRequestCoordinator = new AppServerDynamicRequestCoordinator(
                _syncRoot,
                _pendingRequestRegistry,
                () => _internalToolRegistry,
                GetThreadApprovalMode,
                GetInternalToolContext,
                GetConnectionEpoch,
                SendServerRequestResponseAsync,
                SendErrorResponseAsync,
                OnEventReceived,
                MapServerPathToClient,
                EvaluateCustomApproval);
            _notificationEventProcessor = new AppServerNotificationEventProcessor(
                new AppServerNotificationEventContext(
                    OnEventReceived,
                    RememberLoadedThreadId,
                    ForgetThreadState,
                    RememberActiveTurnId,
                    ForgetActiveTurnId,
                    MapServerPathToClient,
                    ClearResolvedInteractionRequest));
        }

        public Task ConnectAsync()
        {
            return Task.Run((Func<Task>)ConnectSerializedAsync);
        }

        private async Task ConnectSerializedAsync()
        {
            await _connectionLifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ConnectCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _connectionLifecycleLock.Release();
            }
        }

        private async Task ConnectCoreAsync()
        {
            ThrowIfDisposed();
            var appConfig = _appConfigStore.Load();
            ThrowIfDisposed();
            var launchInfo = _runtimeManager.ResolveLaunchInfo(appConfig);
            try
            {
                await ConnectRuntimeAsync(appConfig, launchInfo).ConfigureAwait(false);
            }
            finally
            {
                AppServerRuntimeStartupCoordinator.DeleteSensitiveTempFiles(launchInfo);
            }
        }

        private async Task ConnectRuntimeAsync(AppConfigSnapshot appConfig, AppServerLaunchInfo launchInfo)
        {
            ThrowIfDisposed();
            var runtimeKey = BuildRuntimeConnectionKey(appConfig, launchInfo);
            BindApprovalRuntime(runtimeKey);
            if (_isConnected)
            {
                if (string.Equals(_connectedRuntimeKey, runtimeKey, StringComparison.Ordinal))
                {
                    AppServerRuntimeStartupCoordinator.DeleteSensitiveTempFiles(launchInfo);
                    return;
                }

                DisconnectForRuntimeChange();
            }

            CleanupProcessResources();
            await _runtimeStartupCoordinator.EnsureWslNetworkReadyAsync(
                appConfig,
                launchInfo,
                WslNetworkStartupProbeTimeoutMilliseconds,
                true,
                "startup").ConfigureAwait(false);
            lock (_processStartGate)
            {
                ThrowIfDisposed();
                var processStartResult = _runtimeStartupCoordinator.StartProcess(appConfig, launchInfo, OnProcessExited);
                ApplyRuntimeProcessStartResult(processStartResult);
            }
            IDictionary<string, object> initializeResult = null;
            try
            {
                StartBackgroundReaders();

                initializeResult = await SendRequestAsync("initialize", new Dictionary<string, object>
                {
                    { "clientInfo", new Dictionary<string, object>
                        {
                            { "name", "mycodex" },
                            { "title", AppConstants.AppName },
                            { "version", GetClientVersion() }
                        }
                    },
                    { "capabilities", new Dictionary<string, object> { { "experimentalApi", true } } }
                }).ConfigureAwait(false);

                await SendNotificationAsync("initialized").ConfigureAwait(false);
                lock (_processStartGate)
                {
                    ThrowIfDisposed();
                    _isConnected = true;
                    _connectedRuntimeKey = runtimeKey;
                }
            }
            finally
            {
                AppServerRuntimeStartupCoordinator.DeleteSensitiveTempFiles(launchInfo);
            }

            var platformText = AppServerJsonValueReader.GetString(initializeResult, "platformOs") ?? L("AppServerClient.Value.Unknown", "Unknown");
            var userAgentText = AppServerJsonValueReader.GetString(initializeResult, "userAgent") ?? L("AppServerClient.Value.Unknown", "Unknown");
            ProtocolCapabilities = AppServerProtocolMetadataMapper.BuildProtocolCapabilities(appConfig, launchInfo, initializeResult);
            lock (_processStartGate)
            {
                ThrowIfDisposed();
                InitializeComputerUse(launchInfo);
            }
            Trace.TraceInformation(
                "Connected to Codex App Server. HostPlatform={0}; Platform={1}; Agent={2}; RuntimeSource={3}; RuntimeVersion={4}; ProtocolProfile={5}; RuntimeProfile={6}; CodexHome={7}",
                GetCurrentHostPlatform(),
                platformText,
                userAgentText,
                ProtocolCapabilities.RuntimeSource,
                ProtocolCapabilities.RuntimeVersion,
                ProtocolCapabilities.ProtocolProfile,
                BuildDiagnosticRuntimeProfileId(appConfig),
                BuildDiagnosticCodexHomeText(appConfig));

            try
            {
                await RefreshLoadedThreadIdsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to refresh loaded thread IDs: {0}", ex.Message);
                lock (_syncRoot)
                {
                    _loadedThreadIds.Clear();
                }
            }

            OnEventReceived(new AppServerEventArgs(
                "status",
                LF(
                    "AppServerClient.Status.Connected.Template",
                    "Connected to Codex App Server. Platform: {0}, Agent: {1}",
                    platformText,
                    userAgentText)));
        }

        public async Task RestartAsync()
        {
            await _connectionLifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                ThrowIfDisposed();
                IList<TaskCompletionSource<IDictionary<string, object>>> pendingRequests;
                lock (_syncRoot)
                {
                    pendingRequests = _protocolTransport.DrainPendingRequests();
                    _activeTurnIds.Clear();
                    _authenticationLoginPending = false;
                    _pendingRequestRegistry.Clear();
                    _loadedThreadIds.Clear();
                    _isConnected = false;
                    _connectedRuntimeKey = null;
                    ProtocolCapabilities = null;
                    _connectionLostHandled = true;
                }

                CleanupProcessResources();

                for (var i = 0; i < pendingRequests.Count; i++)
                {
                    pendingRequests[i].TrySetException(new InvalidOperationException(L("AppServerClient.Error.Restarted", "Codex App Server restarted.")));
                }

                await ConnectCoreAsync().ConfigureAwait(false);
            }
            finally
            {
                _connectionLifecycleLock.Release();
            }
        }

        public async Task<string> CreateThreadAsync(
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerThreadToolContext toolContext = null)
        {
            EnsureConnected();
            var serverWorkspacePath = MapRequiredClientPathToServer(workspacePath, "workspace");

            var parameters = AppServerThreadStartParameterBuilder.Build(
                serverWorkspacePath,
                approvalMode,
                modelId,
                reasoningEffort,
                toolContext);
            var result = await SendRequestAsync("thread/start", parameters).ConfigureAwait(false);

            var thread = AppServerJsonValueReader.GetDictionary(result, "thread");
            var threadId = AppServerJsonValueReader.GetString(thread, "id");
            if (string.IsNullOrWhiteSpace(threadId))
            {
                throw new InvalidOperationException(L("AppServerClient.Error.ThreadIdMissing", "App Server did not return a thread ID."));
            }

            lock (_syncRoot)
            {
                _loadedThreadIds.Add(threadId);
                if (approvalMode.HasValue)
                {
                    _threadApprovalModes[threadId] = approvalMode.Value;
                }
                else
                {
                    _threadApprovalModes.Remove(threadId);
                }

                if (toolContext != null && toolContext.ExecutionContext != null)
                {
                    _internalToolContexts[threadId] = toolContext.ExecutionContext;
                }
                else
                {
                    _internalToolContexts.Remove(threadId);
                }
            }

            var registeredToolsEvent = CreateInternalToolsRegisteredEvent(threadId, toolContext);
            if (registeredToolsEvent != null)
            {
                OnEventReceived(registeredToolsEvent);
            }

            return threadId;
        }

        private async Task SendUserMessageForAuthenticationGateAsync(
            string threadId,
            string workspacePath,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            ApprovalMode? approvalMode,
            string modelId,
            string reasoningEffort,
            AppServerCollaborationMode collaborationMode = null)
        {
            EnsureConnected();
            await EnsureThreadLoadedAsync(threadId, workspacePath, approvalMode).ConfigureAwait(false);
            await EnsureWslNetworkReadyForConnectedActionAsync("turn/start").ConfigureAwait(false);
            await RefreshSkillInventoryBeforeTurnStartAsync(workspacePath).ConfigureAwait(false);

            var serverWorkspacePath = MapRequiredClientPathToServer(workspacePath, "workspace");
            var serializedInputs = BuildUserInputs(inputItems);
            if (serializedInputs.Count == 0)
            {
                throw new InvalidOperationException(L("AppServerClient.Error.ValidInputRequired", "At least one valid input item is required."));
            }

            var parameters = new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "cwd", serverWorkspacePath },
                { "input", serializedInputs.ToArray() }
            };
            if (approvalMode.HasValue)
            {
                parameters["approvalPolicy"] = MapApprovalPolicy(approvalMode.Value);
                parameters["approvalsReviewer"] = AppServerThreadStartParameterBuilder.MapApprovalsReviewer(approvalMode.Value);
                parameters["sandboxPolicy"] = BuildSandboxPolicy(approvalMode.Value, workspacePath);
            }

            AddOptionalString(parameters, "model", modelId);
            AddOptionalString(parameters, "effort", reasoningEffort);
            AppServerCollaborationParameters.AddTo(parameters, collaborationMode);

            var result = await SendRequestAsync("turn/start", parameters, LongRunningRequestTimeoutMs).ConfigureAwait(false);

            var turn = AppServerJsonValueReader.GetDictionary(result, "turn");
            var turnId = AppServerJsonValueReader.GetString(turn, "id");
            if (!string.IsNullOrWhiteSpace(turnId))
            {
                lock (_syncRoot)
                {
                    _activeTurnIds[threadId] = turnId;
                    if (approvalMode.HasValue)
                    {
                        _threadApprovalModes[threadId] = approvalMode.Value;
                    }
                    else
                    {
                        _threadApprovalModes.Remove(threadId);
                    }
                }
            }
        }

        public bool HasInterruptibleTurn(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return false;
            }

            lock (_syncRoot)
            {
                string activeTurnId;
                return _activeTurnIds.TryGetValue(threadId, out activeTurnId) &&
                       !string.IsNullOrWhiteSpace(activeTurnId);
            }
        }

        public async Task ResolveApprovalRequestAsync(string requestId, AppServerApprovalDecision decision)
        {
            await _dynamicRequestCoordinator.ResolveApprovalRequestAsync(requestId, decision).ConfigureAwait(false);
        }

        public async Task ResolveToolUserInputRequestAsync(string requestId, IDictionary<string, string[]> answers)
        {
            await _dynamicRequestCoordinator.ResolveToolUserInputRequestAsync(requestId, answers).ConfigureAwait(false);
        }

        public async Task CancelToolUserInputRequestAsync(string requestId)
        {
            await _dynamicRequestCoordinator.CancelToolUserInputRequestAsync(requestId).ConfigureAwait(false);
        }

        private List<object> BuildUserInputs(IReadOnlyCollection<AppServerUserInputItem> inputItems)
        {
            var serializedInputs = new List<object>();
            if (inputItems == null)
            {
                return serializedInputs;
            }

            foreach (var inputItem in inputItems)
            {
                if (inputItem == null || string.IsNullOrWhiteSpace(inputItem.Type))
                {
                    continue;
                }

                switch ((inputItem.Type ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "text":
                        if (!string.IsNullOrWhiteSpace(inputItem.Text))
                        {
                            serializedInputs.Add(new Dictionary<string, object>
                            {
                                { "type", "text" },
                                { "text", inputItem.Text }
                            });
                        }
                        break;
                    case "localimage":
                        if (!string.IsNullOrWhiteSpace(inputItem.Path))
                        {
                            var serverPath = MapRequiredClientPathToServer(inputItem.Path, "input");
                            serializedInputs.Add(new Dictionary<string, object>
                            {
                                { "type", "localImage" },
                                { "path", serverPath }
                            });
                        }
                        break;
                    case "image":
                        if (!string.IsNullOrWhiteSpace(inputItem.Url))
                        {
                            serializedInputs.Add(new Dictionary<string, object>
                            {
                                { "type", "image" },
                                { "url", inputItem.Url }
                            });
                        }
                        break;
                    case "mention":
                        if (!string.IsNullOrWhiteSpace(inputItem.Path))
                        {
                            var resolvedInputPath = ResolveCurrentPluginSkillPath(inputItem.Path, "turn/input", inputItem.Name);
                            var serverPath = MapRequiredClientPathToServer(resolvedInputPath, "mention");
                            TraceSkillPluginInputPathDiagnostic("mention", inputItem.Name, inputItem.Path, resolvedInputPath, serverPath);
                            serializedInputs.Add(new Dictionary<string, object>
                            {
                                { "type", "mention" },
                                { "name", string.IsNullOrWhiteSpace(inputItem.Name) ? Path.GetFileName(resolvedInputPath) : inputItem.Name },
                                { "path", serverPath }
                            });
                        }
                        break;
                    case "skill":
                        if (!string.IsNullOrWhiteSpace(inputItem.Path))
                        {
                            var resolvedInputPath = ResolveCurrentPluginSkillPath(inputItem.Path, "turn/input", inputItem.Name);
                            var serverPath = MapClientOrServerPathToServer(resolvedInputPath);
                            TraceSkillPluginInputPathDiagnostic("skill", inputItem.Name, inputItem.Path, resolvedInputPath, serverPath);
                            serializedInputs.Add(new Dictionary<string, object>
                            {
                                { "type", "skill" },
                                { "name", string.IsNullOrWhiteSpace(inputItem.Name) ? Path.GetFileName(resolvedInputPath) : inputItem.Name },
                                { "path", serverPath }
                            });
                        }
                        break;
                }
            }

            return serializedInputs;
        }

        private async Task RefreshSkillInventoryBeforeTurnStartAsync(string workspacePath)
        {
            var watch = Stopwatch.StartNew();
            try
            {
                if (ProtocolCapabilities != null && !ProtocolCapabilities.SupportsSkills)
                {
                    return;
                }

                var snapshots = await ListSkillsAsync(
                    string.IsNullOrWhiteSpace(workspacePath)
                        ? new string[0]
                        : new[] { workspacePath },
                    true).ConfigureAwait(false);
                var skillCount = snapshots == null
                    ? 0
                    : snapshots.Sum(item => item == null || item.Skills == null ? 0 : item.Skills.Count);
                var errorCount = snapshots == null
                    ? 0
                    : snapshots.Sum(item => item == null || item.Errors == null ? 0 : item.Errors.Count);
                var config = LoadAppConfigForDiagnostics();
                Trace.TraceInformation(
                    "Skill/plugin diagnostic. Operation=skill-inventory/refresh; RuntimeProfile={0}; HostPlatform={1}; RuntimeSource={2}; Workspace={3}; SkillCount={4}; ErrorCount={5}; Result=complete",
                    BuildDiagnosticRuntimeProfileId(config),
                    GetCurrentHostPlatform(),
                    config == null ? string.Empty : config.CodexRuntimeSource,
                    AbbreviateForLog(workspacePath, 500),
                    skillCount,
                    errorCount);
            }
            catch (Exception ex)
            {
                var config = LoadAppConfigForDiagnostics();
                Trace.TraceWarning(
                    "Skill/plugin diagnostic. Operation=skill-inventory/refresh; RuntimeProfile={0}; HostPlatform={1}; RuntimeSource={2}; Workspace={3}; Result=failed; Message={4}",
                    BuildDiagnosticRuntimeProfileId(config),
                    GetCurrentHostPlatform(),
                    config == null ? string.Empty : config.CodexRuntimeSource,
                    AbbreviateForLog(workspacePath, 500),
                    AbbreviateForLog(ex.Message, 1000));
            }
            finally { Trace.TraceInformation("[SendTiming] Phase=skills; ElapsedMs={0}", watch.ElapsedMilliseconds); }
        }

        public async Task InterruptAsync(string threadId, string turnId = null)
        {
            EnsureConnected();

            if (string.IsNullOrWhiteSpace(turnId))
            {
                lock (_syncRoot)
                {
                    _activeTurnIds.TryGetValue(threadId, out turnId);
                }
            }

            if (string.IsNullOrWhiteSpace(turnId))
            {
                OnEventReceived(new AppServerEventArgs(
                    "status",
                    L("AppServerClient.Status.NoInterruptibleResponse", "There is no in-progress response to interrupt."),
                    threadId: threadId));
                return;
            }

            try
            {
                await SendRequestAsync("turn/interrupt", new Dictionary<string, object>
                {
                    { "threadId", threadId },
                    { "turnId", turnId }
                }).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                if (!IsNoActiveTurnToInterruptError(ex))
                {
                    throw;
                }

                ForgetActiveTurnId(threadId);
                OnEventReceived(new AppServerEventArgs(
                    "status",
                    L("AppServerClient.Status.NoInterruptibleResponse", "There is no in-progress response to interrupt."),
                    threadId: threadId));
            }
        }

        private static bool IsNoActiveTurnToInterruptError(Exception ex)
        {
            return ex != null &&
                   ex.Message != null &&
                   ex.Message.IndexOf("no active turn", StringComparison.OrdinalIgnoreCase) >= 0 &&
                   ex.Message.IndexOf("interrupt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public async Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
        {
            using (refreshToken ? RuntimeOperationGate.Shared.EnterOperation() : null)
            {
                return await _operations.GetAuthenticationStateAsync(refreshToken).ConfigureAwait(false);
            }
        }

        public async Task<AppServerLoginResult> StartChatGptLoginAsync()
        {
            return await StartAuthenticationLoginWithGateAsync(_operations.StartChatGptLoginAsync).ConfigureAwait(false);
        }

        public async Task<AppServerLoginResult> StartChatGptDeviceCodeLoginAsync()
        {
            return await StartAuthenticationLoginWithGateAsync(_operations.StartChatGptDeviceCodeLoginAsync).ConfigureAwait(false);
        }

        public async Task<AppServerLoginResult> StartOpenAiApiKeyLoginAsync(string apiKey)
        {
            return await _operations.StartOpenAiApiKeyLoginAsync(apiKey).ConfigureAwait(false);
        }

        public async Task<AppServerLoginCancelResult> CancelLoginAsync(string loginId)
        {
            using (RuntimeOperationGate.Shared.EnterOperation())
            {
                var result = await _operations.CancelLoginAsync(loginId).ConfigureAwait(false);
                lock (_syncRoot)
                {
                    if (string.Equals(loginId, _authenticationPendingLoginId, StringComparison.Ordinal) &&
                        (string.Equals(result?.Status, "canceled", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(result?.Status, "cancelled", StringComparison.OrdinalIgnoreCase)))
                    {
                        _authenticationLoginPending = false;
                        _authenticationPendingLoginId = null;
                    }
                }
                return result;
            }
        }

        public async Task<AppServerRateLimitsSnapshot> GetRateLimitsAsync()
        {
            EnsureConnected();
            await EnsureWslNetworkReadyForConnectedActionAsync("account/rateLimits/read").ConfigureAwait(false);

            Exception lastException = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    var result = await SendRequestAsync("account/rateLimits/read", new Dictionary<string, object>()).ConfigureAwait(false);
                    return AppServerAccountConfigMapper.BuildRateLimitsSnapshot(result);
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    if (attempt >= 3 || !IsTransientRateLimitsReadException(ex))
                    {
                        throw;
                    }

                    InvalidateWslNetworkReadiness();
                    Trace.TraceWarning(
                        "Rate limits read failed transiently; retrying. Attempt={0}; Message={1}",
                        attempt,
                        AbbreviateForLog(ex.Message, 1000));
                    await EnsureWslNetworkReadyForConnectedActionAsync("account/rateLimits/read retry").ConfigureAwait(false);
                    await Task.Delay(attempt * 750).ConfigureAwait(false);
                }
            }

            throw lastException ?? new InvalidOperationException(L("AppServerClient.Error.ServerReturnedError", "App Server returned an error."));
        }

        public async Task<AppServerAccountTokenUsageSnapshot> GetAccountTokenUsageAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _operations.GetAccountTokenUsageAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await ConsumeRateLimitResetCreditAsync(idempotencyKey, null, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
            string idempotencyKey,
            string creditId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _operations.ConsumeRateLimitResetCreditAsync(idempotencyKey, creditId, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers)
        {
            return await ReadConfigAsync(workspacePath, includeLayers, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers, CancellationToken cancellationToken)
        {
            return await _operations.ReadConfigAsync(workspacePath, includeLayers, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerPermissionProfileListResult> ListPermissionProfilesAsync(
            string workspacePath = null,
            int? limit = null,
            string cursor = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _operations.ListPermissionProfilesAsync(workspacePath, limit, cursor, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerCommandExecResult> ExecuteCommandAsync(string commandText, string workspacePath, ApprovalMode approvalMode, string processId, long timeoutMs)
        {
            return await ExecuteCommandAsync(commandText, workspacePath, approvalMode, processId, timeoutMs, null).ConfigureAwait(false);
        }

        private async Task<AppServerCommandExecResult> ExecuteCommandForAuthenticationGateAsync(string commandText, string workspacePath, ApprovalMode approvalMode, string processId, long timeoutMs, Action<string, string> outputReceived)
        {
            EnsureConnected();

            if (string.IsNullOrWhiteSpace(commandText))
            {
                throw new ArgumentException(L("AppServerClient.Error.CommandRequired", "Command cannot be empty."), nameof(commandText));
            }

            if (string.IsNullOrWhiteSpace(processId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ProcessIdRequired", "Process ID cannot be empty."), nameof(processId));
            }

            var command = _commandLinePolicy.BuildStandaloneCommand(commandText);
            var streamLocalWindowsOutput = outputReceived != null && IsCurrentHostPlatform(AppServerHostPlatform.Windows);
            if (streamLocalWindowsOutput && IsUnsandboxedApprovalMode(approvalMode))
            {
                // Full Access already maps to danger-full-access, so direct local execution grants nothing extra.
                return await ExecuteLocalWindowsCommandStreamingAsync(
                    command,
                    workspacePath,
                    processId,
                    timeoutMs,
                    outputReceived).ConfigureAwait(false);
            }

            var parameters = new Dictionary<string, object>
            {
                { "command", command },
                { "cwd", MapRequiredClientPathToServer(workspacePath, "workspace") },
                { "processId", processId },
                { "timeoutMs", timeoutMs },
                { "sandboxPolicy", BuildSandboxPolicy(approvalMode, workspacePath) }
            };
            if (streamLocalWindowsOutput)
            {
                // Sandboxed Windows commands run through the App Server; stream output back to the caller.
                parameters["streamStdoutStderr"] = true;
                lock (_syncRoot)
                {
                    _commandOutputReceivers[processId] = outputReceived;
                }
            }

            IDictionary<string, object> result;
            try
            {
                result = await SendRequestAsync("command/exec", parameters, BuildCommandRequestTimeoutMs(timeoutMs)).ConfigureAwait(false);
            }
            finally
            {
                if (streamLocalWindowsOutput)
                {
                    lock (_syncRoot)
                    {
                        _commandOutputReceivers.Remove(processId);
                    }
                }
            }

            return new AppServerCommandExecResult
            {
                ExitCode = AppServerJsonValueReader.GetNullableInt(result, "exitCode") ?? -1,
                StdOut = AppServerJsonValueReader.GetString(result, "stdout") ?? string.Empty,
                StdErr = AppServerJsonValueReader.GetString(result, "stderr") ?? string.Empty
            };
        }

        private async Task<AppServerCommandExecResult> ExecuteLocalWindowsCommandStreamingAsync(
            string[] command,
            string workspacePath,
            string processId,
            long timeoutMs,
            Action<string, string> outputReceived)
        {
            if (command == null || command.Length == 0 || string.IsNullOrWhiteSpace(command[0]))
            {
                throw new ArgumentException(L("AppServerClient.Error.CommandRequired", "Command cannot be empty."), nameof(command));
            }

            var cancellation = new CancellationTokenSource();
            lock (_syncRoot)
            {
                _localCommandCancellationSources[processId] = cancellation;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = command[0],
                    Arguments = BuildLocalWindowsCommandArguments(command),
                    WorkingDirectory = MapRequiredClientPathToServer(workspacePath, "workspace"),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var result = await Task.Run(
                    () => MyCodex.Infrastructure.Processes.ProcessExecutionRunner.Run(new MyCodex.Infrastructure.Processes.ProcessExecutionRequest
                    {
                        StartInfo = startInfo,
                        TimeoutMilliseconds = timeoutMs > 0 && timeoutMs < int.MaxValue ? (int)timeoutMs : 120000,
                        TimeoutMessage = L("AppServerClient.Error.CommandTimedOut", "Command timed out."),
                        CancellationToken = cancellation.Token,
                        OutputReceived = (text, isError) => outputReceived(text, isError ? "stderr" : "stdout")
                    }),
                    cancellation.Token).ConfigureAwait(false);

                return new AppServerCommandExecResult
                {
                    ExitCode = result.ExitCode,
                    StdOut = result.StandardOutput ?? string.Empty,
                    StdErr = result.StandardError ?? string.Empty
                };
            }
            catch (OperationCanceledException)
            {
                return new AppServerCommandExecResult
                {
                    ExitCode = -1,
                    StdOut = string.Empty,
                    StdErr = string.Empty
                };
            }
            finally
            {
                lock (_syncRoot)
                {
                    CancellationTokenSource registeredCancellation;
                    if (_localCommandCancellationSources.TryGetValue(processId, out registeredCancellation) &&
                        ReferenceEquals(registeredCancellation, cancellation))
                    {
                        _localCommandCancellationSources.Remove(processId);
                    }
                }

                cancellation.Dispose();
            }
        }

        private static string BuildLocalWindowsCommandArguments(string[] command)
        {
            if (command == null || command.Length <= 1)
            {
                return string.Empty;
            }

            if (IsCmdExecutable(command[0]) &&
                command.Length >= 3 &&
                (string.Equals(command[1], "/c", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(command[1], "/k", StringComparison.OrdinalIgnoreCase)))
            {
                var commandText = command[2] ?? string.Empty;
                if (command.Length == 3)
                {
                    return command[1] + " " + commandText;
                }

                return command[1] + " " + commandText + " " + string.Join(" ", command.Skip(3).ToArray());
            }

            return GitCommandLineBuilder.BuildArguments(command.Skip(1).ToArray());
        }

        private static bool IsUnsandboxedApprovalMode(ApprovalMode approvalMode)
        {
            return approvalMode == ApprovalMode.FullAccess || approvalMode == ApprovalMode.FullAccessAutoApprove;
        }

        private bool TryDeliverCommandOutputToReceiver(AppServerEventArgs args)
        {
            if (args == null ||
                string.IsNullOrWhiteSpace(args.ProcessId) ||
                !string.Equals(args.EventType, "command_exec_output", StringComparison.Ordinal))
            {
                return false;
            }

            Action<string, string> receiver;
            lock (_syncRoot)
            {
                if (!_commandOutputReceivers.TryGetValue(args.ProcessId, out receiver))
                {
                    return false;
                }
            }

            // This runs on the stdout reader; a failing UI callback must not look like a lost connection.
            try
            {
                receiver(args.Content, string.Equals(args.OutputStream, "stderr", StringComparison.OrdinalIgnoreCase) ? "stderr" : "stdout");
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Command output receiver failed. ErrorType={0}", ex.GetType().FullName);
            }

            return true;
        }

        private static bool IsCmdExecutable(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var name = Path.GetFileName(fileName.Trim());
            return string.Equals(name, "cmd.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "cmd", StringComparison.OrdinalIgnoreCase);
        }

        public async Task TerminateCommandAsync(string processId)
        {
            EnsureConnected();

            if (string.IsNullOrWhiteSpace(processId))
            {
                return;
            }

            CancellationTokenSource localCancellation;
            lock (_syncRoot)
            {
                _localCommandCancellationSources.TryGetValue(processId, out localCancellation);
            }

            if (localCancellation != null)
            {
                localCancellation.Cancel();
                return;
            }

            await SendRequestAsync("command/exec/terminate", new Dictionary<string, object>
            {
                { "processId", processId }
            }).ConfigureAwait(false);
        }

        public async Task WriteCommandInputAsync(string processId, string input)
        {
            EnsureConnected();

            await SendRequestAsync(
                "command/exec/write",
                BuildCommandInputWriteParameters(processId, input))
                .ConfigureAwait(false);
        }

        public async Task ResizeCommandAsync(
            string processId,
            AppServerCommandTerminalSize size,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureConnected();
            if (string.IsNullOrWhiteSpace(processId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ProcessIdRequired", "Process ID cannot be empty."), nameof(processId));
            }

            if (size == null)
            {
                throw new ArgumentNullException(nameof(size));
            }

            await SendRequestAsync(
                "command/exec/resize",
                new Dictionary<string, object>
                {
                    { "processId", processId.Trim() },
                    {
                        "size",
                        new Dictionary<string, object>
                        {
                            { "rows", size.Rows },
                            { "cols", size.Cols }
                        }
                    }
                },
                cancellationToken)
                .ConfigureAwait(false);
        }

        public async Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null, string expectedVersion = null, bool reloadUserConfig = false)
        {
            await _operations.WriteConfigBatchAsync(edits, filePath, expectedVersion, reloadUserConfig).ConfigureAwait(false);
        }

        public async Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync()
        {
            return await _operations.ListMcpServerStatusesAsync().ConfigureAwait(false);
        }

        public async Task ReloadMcpServerConfigAsync()
        {
            await _operations.ReloadMcpServerConfigAsync().ConfigureAwait(false);
        }

        public async Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(string serverName, int? timeoutSecs = null)
        {
            return await _operations.StartMcpServerOauthLoginAsync(serverName, timeoutSecs).ConfigureAwait(false);
        }

        public Task<AppServerMcpResourceReadResult> ReadMcpResourceAsync(
            string server,
            string uri,
            string threadId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _operations.ReadMcpResourceAsync(server, uri, threadId, cancellationToken);
        }

        public Task<AppServerMcpToolCallResult> CallMcpToolAsync(
            AppServerMcpToolCallRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _operations.CallMcpToolAsync(request, cancellationToken);
        }

        public async Task<List<AppServerSkillsSnapshot>> ListSkillsAsync(IReadOnlyCollection<string> workspacePaths, bool forceReload)
        {
            return await _operations.ListSkillsAsync(workspacePaths, forceReload).ConfigureAwait(false);
        }

        public async Task<bool> WriteSkillEnabledAsync(string path, bool enabled)
        {
            return await _operations.WriteSkillEnabledAsync(path, enabled).ConfigureAwait(false);
        }

        public async Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path)
        {
            return await _operations.ReadDirectoryAsync(path).ConfigureAwait(false);
        }

        public async Task<AppServerFileSystemReadResult> ReadFileAsync(string path)
        {
            return await _operations.ReadFileAsync(path).ConfigureAwait(false);
        }

        public async Task<AppServerFileSystemMetadata> GetMetadataAsync(string path)
        {
            return await _operations.GetMetadataAsync(path).ConfigureAwait(false);
        }

        public async Task WriteFileAsync(string path, byte[] data)
        {
            await _operations.WriteFileAsync(path, data).ConfigureAwait(false);
        }

        public async Task CreateDirectoryAsync(string path, bool? recursive = null)
        {
            await _operations.CreateDirectoryAsync(path, recursive).ConfigureAwait(false);
        }

        public async Task RemoveAsync(string path, bool? recursive = null, bool? force = null)
        {
            await _operations.RemoveAsync(path, recursive, force).ConfigureAwait(false);
        }

        public async Task CopyAsync(string sourcePath, string destinationPath, bool recursive)
        {
            await _operations.CopyAsync(sourcePath, destinationPath, recursive).ConfigureAwait(false);
        }

        public async Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId)
        {
            return await _operations.WatchFileSystemAsync(path, watchId).ConfigureAwait(false);
        }

        public async Task UnwatchFileSystemAsync(string watchId)
        {
            await _operations.UnwatchFileSystemAsync(watchId).ConfigureAwait(false);
        }

        public async Task<AppServerFuzzyFileSearchSessionEvent> StartFuzzyFileSearchAsync(AppServerFuzzyFileSearchRequest request, CancellationToken cancellationToken)
        {
            return await _operations.StartFuzzyFileSearchAsync(request, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerWindowsSandboxReadiness> ReadWindowsSandboxReadinessAsync()
        {
            return await _operations.ReadWindowsSandboxReadinessAsync().ConfigureAwait(false);
        }

        public async Task<AppServerWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(
            AppServerWindowsSandboxSetupMode mode,
            string cwd = null)
        {
            return await _operations.StartWindowsSandboxSetupAsync(mode, cwd).ConfigureAwait(false);
        }

        public async Task<List<AppServerPluginSummary>> ListPluginsAsync()
        {
            return await _operations.ListPluginsAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<List<AppServerPluginSummary>> ListPluginsAsync(CancellationToken cancellationToken)
        {
            return await _operations.ListPluginsAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerPluginInstalledSnapshot> GetInstalledPluginsAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _operations.GetInstalledPluginsAsync(cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerPluginDetails> ReadPluginAsync(string marketplacePath, string pluginName)
        {
            return await _operations.ReadPluginAsync(marketplacePath, pluginName).ConfigureAwait(false);
        }

        public async Task<AppServerPluginSkillReadResult> ReadPluginSkillAsync(
            string remoteMarketplaceName,
            string remotePluginId,
            string skillName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _operations.ReadPluginSkillAsync(remoteMarketplaceName, remotePluginId, skillName, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerMarketplaceUpgradeResult> UpgradeMarketplaceAsync(
            string marketplaceName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _operations.UpgradeMarketplaceAsync(marketplaceName, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerMarketplaceAddResult> AddMarketplaceAsync(
            string source,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await _operations.AddMarketplaceAsync(source, cancellationToken).ConfigureAwait(false);
        }

        public async Task RemoveMarketplaceAsync(
            string marketplaceName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            await _operations.RemoveMarketplaceAsync(marketplaceName, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerPluginInstallResult> InstallPluginAsync(string marketplacePath, string pluginName)
        {
            return await _operations.InstallPluginAsync(marketplacePath, pluginName).ConfigureAwait(false);
        }

        public async Task<AppServerPluginInstallResult> InstallRemotePluginAsync(string remoteMarketplaceName, string remotePluginId)
        {
            return await _operations.InstallRemotePluginAsync(remoteMarketplaceName, remotePluginId).ConfigureAwait(false);
        }

        public async Task UninstallPluginAsync(string pluginId)
        {
            await _operations.UninstallPluginAsync(pluginId).ConfigureAwait(false);
        }

        public async Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync()
        {
            return await _operations.ReadConfigRequirementsAsync().ConfigureAwait(false);
        }

        public async Task<ExternalAgentConfigDetectResult> DetectExternalAgentConfigAsync(
            IReadOnlyCollection<string> workingDirectories,
            bool includeHome,
            CancellationToken cancellationToken)
        {
            return await _operations.DetectExternalAgentConfigAsync(workingDirectories, includeHome, cancellationToken).ConfigureAwait(false);
        }

        public async Task ImportExternalAgentConfigAsync(
            IReadOnlyCollection<ExternalAgentConfigMigrationItem> migrationItems,
            CancellationToken cancellationToken)
        {
            await _operations.ImportExternalAgentConfigAsync(migrationItems, cancellationToken).ConfigureAwait(false);
        }

        public async Task<List<AppServerModelOption>> ListModelsAsync()
        {
            return await _operations.ListModelsAsync(CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<List<AppServerModelOption>> ListModelsAsync(CancellationToken cancellationToken)
        {
            return await _operations.ListModelsAsync(cancellationToken).ConfigureAwait(false);
        }

        public Task<List<AppServerThreadSummary>> ListThreadsAsync()
        {
            return _threadOperations.ListThreadsAsync(CancellationToken.None);
        }

        public Task<List<AppServerThreadSummary>> ListThreadsAsync(CancellationToken cancellationToken)
        {
            return _threadOperations.ListThreadsAsync(cancellationToken);
        }

        public async Task<List<string>> ListLoadedThreadIdsAsync(int? limit = null)
        {
            return await ListLoadedThreadIdsAsync(limit, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task<List<string>> ListLoadedThreadIdsAsync(int? limit, CancellationToken cancellationToken)
        {
            return await _threadOperations.ListLoadedThreadIdsAsync(limit, cancellationToken).ConfigureAwait(false);
        }

        internal static List<string> MergeLoadedThreadIdsForSync(
            IEnumerable<string> remoteThreadIds,
            IEnumerable<string> locallyKnownThreadIds,
            int? limit)
        {
            return AppServerThreadOperations.MergeLoadedThreadIdsForSync(remoteThreadIds, locallyKnownThreadIds, limit);
        }

        public Task<List<AppServerThreadSummary>> ListArchivedThreadsAsync(string workspacePath)
        {
            return _threadOperations.ListArchivedThreadsAsync(workspacePath);
        }

        public Task<AppServerConversationSummary> GetConversationSummaryAsync(
            AppServerConversationSummaryRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _threadOperations.GetConversationSummaryAsync(request, cancellationToken);
        }

        public Task<AppServerGitDiffToRemoteResult> GetGitDiffToRemoteAsync(
            string workspacePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _operations.GetGitDiffToRemoteAsync(workspacePath, cancellationToken);
        }

        internal static Dictionary<string, object> BuildThreadListParameters(
            bool archived,
            string mappedWorkspacePath,
            string cursor)
        {
            return AppServerThreadOperations.BuildThreadListParameters(archived, mappedWorkspacePath, cursor);
        }

        private Task TryEnrichLocalThreadSummariesWithConversationSummaryAsync(
            List<AppServerThreadSummary> summaries,
            ISet<string> appServerThreadIds,
            CancellationToken cancellationToken)
        {
            return _threadOperations.TryEnrichLocalThreadSummariesWithConversationSummaryAsync(
                summaries,
                appServerThreadIds,
                cancellationToken);
        }

        private Task<IReadOnlyList<string>> ResolveLocalCodexHomePathsForThreadScanAsync()
        {
            var config = _appConfigStore != null ? _appConfigStore.Load() : null;
            var hostPlatform = GetCurrentHostPlatform();
            var codexHomePaths = new List<string>();
            string officialCodexHomePath;
            if (!LocalSessionCatalog.TryResolveLocalCodexHomePathForThreadScan(
                hostPlatform,
                config,
                out officialCodexHomePath))
            {
                officialCodexHomePath = null;
            }

            AddDistinctLocalCodexHomePath(codexHomePaths, officialCodexHomePath);
            return Task.FromResult((IReadOnlyList<string>)codexHomePaths);
        }

        private static void AddDistinctLocalCodexHomePath(List<string> paths, string path)
        {
            if (paths == null || string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var normalizedPath = AppServerPathUtilities.NormalizeLocalPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (paths.Any(existing => string.Equals(
                AppServerPathUtilities.NormalizeLocalPath(existing)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                normalizedPath,
                StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            paths.Add(path);
        }

        public async Task<IReadOnlyList<string>> ListLocalCodexHomePathsForThreadScanAsync()
        {
            return await ResolveLocalCodexHomePathsForThreadScanAsync().ConfigureAwait(false);
        }

        public async Task<List<AppServerThreadSummary>> ListSubAgentThreadsAsync(string parentThreadId, CancellationToken cancellationToken, bool includeArchived = false)
        {
            EnsureConnected();
            var epoch = GetConnectionEpoch();
            var capabilities = ProtocolCapabilities;
            var result = capabilities != null && capabilities.SupportsModernAppServerProtocol
                ? await AppServerSubAgentOperations.ListAsync(parentThreadId, capabilities.SupportsSubagentThreadFilters,
                    SendRequestAsync, MapServerPathToClient, cancellationToken).ConfigureAwait(false)
                : await ListLocalThreadSummariesForThreadScanAsync(0).ConfigureAwait(false);
            if (includeArchived)
            {
                if (capabilities == null || !capabilities.SupportsModernAppServerProtocol)
                    throw new NotSupportedException("Complete archived subagent discovery is unavailable.");
                result.AddRange(await AppServerSubAgentOperations.ListAsync(parentThreadId, capabilities.SupportsSubagentThreadFilters,
                    SendRequestAsync, MapServerPathToClient, cancellationToken, true).ConfigureAwait(false));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (epoch != GetConnectionEpoch()) throw new OperationCanceledException();
            return result;
        }

        public async Task<List<AppServerThreadSummary>> ListLocalThreadSummariesForThreadScanAsync(int maxSummaries)
        {
            var threads = new List<AppServerThreadSummary>();
            var mergedThreadMap = new Dictionary<string, AppServerThreadSummary>(StringComparer.OrdinalIgnoreCase);
            foreach (var localCodexHomePath in await ResolveLocalCodexHomePathsForThreadScanAsync().ConfigureAwait(false))
            {
                foreach (var localThread in LocalSessionCatalog.EnumerateLocalThreadSummaries(
                    false,
                    null,
                    localCodexHomePath,
                    maxSummaries,
                    CancellationToken.None))
                {
                    LocalSessionCatalog.MergeThreadSummary(threads, mergedThreadMap, localThread);
                }
            }

            foreach (var runtimeThread in await ListRuntimeThreadSummariesForThreadScanAsync(maxSummaries, CancellationToken.None).ConfigureAwait(false))
            {
                LocalSessionCatalog.MergeThreadSummary(threads, mergedThreadMap, runtimeThread);
            }

            threads.Sort((left, right) => right.UpdatedAt.CompareTo(left.UpdatedAt));
            return threads;
        }

        private Task<List<AppServerThreadSummary>> ListRuntimeThreadSummariesForThreadScanAsync(
            int maxSummaries,
            CancellationToken cancellationToken)
        {
            return _runtimeThreadScanCatalog.ListThreadSummariesAsync(
                GetCurrentHostPlatform(),
                maxSummaries,
                cancellationToken);
        }

        public async Task<AppServerThreadDetails> ReadThreadAsync(string threadId)
        {
            return await ReadThreadAsync(threadId, CancellationToken.None).ConfigureAwait(false);
        }

        public Task<bool> ConfirmThreadMissingAsync(string threadId, string sessionPath)
        {
            return _threadOperations.ConfirmThreadMissingAsync(threadId, sessionPath, GetMetadataAsync);
        }

        public async Task<AppServerThreadDetails> ReadThreadAsync(string threadId, CancellationToken cancellationToken)
        {
            return await _threadOperations.ReadThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
        }

        public async Task ArchiveThreadAsync(string threadId)
        {
            await _threadOperations.ArchiveThreadAsync(threadId).ConfigureAwait(false);
        }

        public async Task DeleteThreadAsync(string threadId)
        {
            await _threadOperations.DeleteThreadAsync(threadId).ConfigureAwait(false);
        }

        public async Task UnarchiveThreadAsync(string threadId)
        {
            await _threadOperations.UnarchiveThreadAsync(threadId).ConfigureAwait(false);
        }

        public async Task<AppServerThreadSummary> ForkThreadAsync(string threadId)
        {
            return await _threadOperations.ForkThreadAsync(threadId).ConfigureAwait(false);
        }

        public async Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId)
        {
            return await _threadOperations.ForkThreadAsync(threadId, lastTurnId).ConfigureAwait(false);
        }

        public Task<AppServerThreadSummary> ForkThreadAsync(string threadId, string lastTurnId, string workspacePath, ApprovalMode? approvalMode, string modelId, string reasoningEffort)
        {
            return _threadOperations.ForkThreadAsync(threadId, lastTurnId, workspacePath, approvalMode, modelId, reasoningEffort);
        }

        public async Task CompactThreadAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
        {
            await _threadOperations.CompactThreadAsync(threadId, workspacePath, approvalMode, modelId).ConfigureAwait(false);
        }

        public Task<AppServerTurnSteerResult> TrySteerTurnAsync(
            string threadId,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            string clientUserMessageId = null)
        {
            return TrySteerTurnAsync(threadId, inputItems, clientUserMessageId, null);
        }

        public Task<AppServerTurnSteerResult> TrySteerTurnAsync(
            string threadId,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            string clientUserMessageId,
            string expectedTurnId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ThreadIdRequired", "Thread ID cannot be empty."), nameof(threadId));
            }

            if (inputItems == null)
            {
                throw new ArgumentNullException(nameof(inputItems));
            }

            string activeTurnId;
            lock (_syncRoot)
            {
                _activeTurnIds.TryGetValue(threadId, out activeTurnId);
            }

            if (string.IsNullOrWhiteSpace(activeTurnId))
            {
                return Task.FromResult(AppServerTurnSteerResult.NotAttempted(null, "no-active-turn"));
            }

            if (!string.IsNullOrWhiteSpace(expectedTurnId) &&
                !string.Equals(activeTurnId, expectedTurnId, StringComparison.Ordinal))
            {
                return Task.FromResult(AppServerTurnSteerResult.NotAttempted(expectedTurnId, "active-turn-changed"));
            }

            return TrySteerTurnCoreAsync(threadId, activeTurnId, inputItems, clientUserMessageId);
        }

        public Task ApproveGuardianDeniedActionAsync(
            AppServerGuardianDeniedActionApprovalRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return _threadOperations.ApproveGuardianDeniedActionAsync(request, cancellationToken);
        }

        private async Task<AppServerTurnSteerResult> TrySteerTurnCoreAsync(
            string threadId,
            string expectedTurnId,
            IReadOnlyCollection<AppServerUserInputItem> inputItems,
            string clientUserMessageId)
        {
            EnsureConnected();

            var serializedInputs = BuildUserInputs(inputItems);
            if (serializedInputs.Count == 0)
            {
                throw new InvalidOperationException(L("AppServerClient.Error.ValidInputRequired", "At least one valid input item is required."));
            }

            lock (_syncRoot)
            {
                string currentTurnId;
                if (!_activeTurnIds.TryGetValue(threadId, out currentTurnId) ||
                    !string.Equals(currentTurnId, expectedTurnId, StringComparison.Ordinal))
                {
                    return AppServerTurnSteerResult.NotAttempted(expectedTurnId, "active-turn-changed");
                }
            }

            var parameters = new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "expectedTurnId", expectedTurnId },
                { "input", serializedInputs.ToArray() }
            };
            AddOptionalString(parameters, "clientUserMessageId", clientUserMessageId);

            try
            {
                var result = await SendRequestAsync("turn/steer", parameters, LongRunningRequestTimeoutMs).ConfigureAwait(false);
                var turnId = AppServerJsonValueReader.GetString(result, "turnId");
                if (string.IsNullOrWhiteSpace(turnId))
                {
                    var turn = AppServerJsonValueReader.GetDictionary(result, "turn");
                    turnId = AppServerJsonValueReader.GetString(turn, "id");
                }

                if (!string.IsNullOrWhiteSpace(turnId))
                {
                    lock (_syncRoot)
                    {
                        _activeTurnIds[threadId] = turnId;
                    }
                }

                return AppServerTurnSteerResult.AcceptedResult(expectedTurnId, turnId);
            }
            catch (InvalidOperationException ex)
            {
                if (IsFallbackableTurnSteerException(ex))
                {
                    return AppServerTurnSteerResult.Fallback(expectedTurnId, ex.Message);
                }

                throw;
            }
        }

        private async Task SubmitCompactThreadForAuthenticationGateAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId)
        {
            EnsureConnected();

            if (string.IsNullOrWhiteSpace(threadId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ThreadIdRequired", "Thread ID cannot be empty."), nameof(threadId));
            }

            await EnsureThreadLoadedAsync(threadId, workspacePath, approvalMode).ConfigureAwait(false);
            await EnsureWslNetworkReadyForConnectedActionAsync("thread/compact/start").ConfigureAwait(false);

            try
            {
                await _protocolTransport.SendRequestWithoutWaitingAsync(
                    "thread/compact/start",
                    ToProtocolParameters(new Dictionary<string, object>
                    {
                        { "threadId", threadId }
                    }),
                    GetLifetimeToken()).ConfigureAwait(false);
            }
            catch (AppServerTransportWriteException ex)
            {
                var disconnectMessage = BuildConnectionLostMessage(ex.InnerException ?? ex);
                HandleAppServerConnectionLost(disconnectMessage, ex.ConnectionEpoch, null);
                throw new InvalidOperationException(disconnectMessage, ex);
            }
        }

        public async Task<AppServerReviewStartResult> StartReviewAsync(string threadId, string workspacePath, ApprovalMode? approvalMode, string modelId, AppServerReviewTarget target, bool runDetached, string reasoningEffort = null, string reviewModelId = null)
        {
            EnsureConnected();
            var epoch = GetConnectionEpoch();
            var toolContext = GetInternalToolContext(threadId);
            var operations = new AppServerReviewOperations((method, parameters, cancellationToken, timeout) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                lock (_syncRoot)
                {
                    if (epoch != GetConnectionEpoch()) throw new OperationCanceledException();
                    if (string.Equals(method, "review/start", StringComparison.Ordinal))
                    {
                        var reviewThreadId = AppServerJsonValueReader.GetString((IDictionary<string, object>)parameters, "threadId");
                        // thread/closed clears host state during review preparation. Restore
                        // it before review/start can emit an approval or an internal tool call.
                        if (approvalMode.HasValue) _threadApprovalModes[reviewThreadId] = approvalMode.Value;
                        else _threadApprovalModes.Remove(reviewThreadId);
                        if (toolContext != null) _internalToolContexts[reviewThreadId] = toolContext;
                    }
                }
                return SendRequestAsync(method, parameters, cancellationToken, timeout);
            }, ProtocolCapabilities?.SupportsThreadHistoryPagination == true);
            try
            {
                var result = await operations.StartAsync(threadId,
                    MapRequiredClientPathToServer(workspacePath, "workspace"), approvalMode,
                    modelId, reasoningEffort, reviewModelId, target, runDetached,
                    GetLifetimeToken()).ConfigureAwait(false);
                RememberLoadedThreadId(result.ReviewThreadId);
                // turn/started owns active state. Re-adding it from a late response can
                // resurrect a turn already cleared by turn/completed.
                result.WorkingDirectory = MapServerPathToClient(result.WorkingDirectory);
                if (result.CreatedThread != null) result.CreatedThread.WorkingDirectory = MapServerPathToClient(result.CreatedThread.WorkingDirectory);
                return result;
            }
            catch (AppServerReviewStartException ex)
            {
                ex.CreatedReview.WorkingDirectory = MapServerPathToClient(ex.CreatedReview.WorkingDirectory);
                ex.CreatedReview.CreatedThread.WorkingDirectory = MapServerPathToClient(ex.CreatedReview.CreatedThread.WorkingDirectory);
                throw;
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (!runDetached && toolContext != null && epoch == GetConnectionEpoch())
                    {
                        _internalToolContexts[threadId] = toolContext;
                    }
                }
            }
        }

        public async Task UnsubscribeThreadAsync(string threadId)
        {
            await _threadOperations.UnsubscribeThreadAsync(threadId).ConfigureAwait(false);
        }

        public Task DeleteThreadFileAsync(string sessionPath)
        {
            if (string.IsNullOrWhiteSpace(sessionPath) ||
                (AppServerPathUtilities.LooksLikeUnixAbsolutePath(sessionPath) &&
                 GetCurrentHostPlatform() == AppServerHostPlatform.Windows))
            {
                return _sessionFileStore.DeleteThreadFileAsync(sessionPath);
            }

            return DeleteThreadFileWithConfiguredRootsAsync(sessionPath);
        }

        private async Task DeleteThreadFileWithConfiguredRootsAsync(string sessionPath)
        {
            var allowedCodexHomePaths = await ResolveLocalCodexHomePathsForThreadScanAsync().ConfigureAwait(false);
            await _sessionFileStore.DeleteThreadFileAsync(sessionPath, allowedCodexHomePaths).ConfigureAwait(false);
        }

        private void ForgetThreadState(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            lock (_syncRoot)
            {
                _loadedThreadIds.Remove(threadId);
                _activeTurnIds.Remove(threadId);
                _threadApprovalModes.Remove(threadId);
                _internalToolContexts.Remove(threadId);
            }
        }

        private void RememberLoadedThreadId(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            lock (_syncRoot)
            {
                _loadedThreadIds.Add(threadId);
            }
        }

        private IEnumerable<string> GetLocallyKnownLoadedThreadIds()
        {
            lock (_syncRoot)
            {
                return _loadedThreadIds.ToList();
            }
        }

        private void RememberActiveTurnId(string threadId, string turnId)
        {
            if (string.IsNullOrWhiteSpace(threadId) || string.IsNullOrWhiteSpace(turnId))
            {
                return;
            }

            lock (_syncRoot)
            {
                _activeTurnIds[threadId] = turnId;
            }
        }

        private void ForgetActiveTurnId(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            lock (_syncRoot)
            {
                _activeTurnIds.Remove(threadId);
            }
        }

        public async Task SetThreadNameAsync(string threadId, string name)
        {
            await _threadOperations.SetThreadNameAsync(threadId, name).ConfigureAwait(false);
        }

        public async Task<AppServerThreadGoal> SetThreadGoalAsync(
            string threadId,
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId,
            string objective,
            long? tokenBudget = null)
        {
            return await _threadOperations.SetThreadGoalAsync(threadId, workspacePath, approvalMode, modelId, objective, tokenBudget).ConfigureAwait(false);
        }

        public async Task<AppServerThreadGoal> GetThreadGoalAsync(
            string threadId,
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId)
        {
            return await _threadOperations.GetThreadGoalAsync(threadId, workspacePath, approvalMode, modelId).ConfigureAwait(false);
        }

        public async Task ClearThreadGoalAsync(
            string threadId,
            string workspacePath,
            ApprovalMode? approvalMode,
            string modelId)
        {
            await _threadOperations.ClearThreadGoalAsync(threadId, workspacePath, approvalMode, modelId).ConfigureAwait(false);
        }

        public async Task UpdateThreadGitMetadataAsync(string threadId, AppServerThreadGitInfoUpdate gitInfo)
        {
            await _threadOperations.UpdateThreadGitMetadataAsync(threadId, gitInfo).ConfigureAwait(false);
        }

        private Task EnsureWslNetworkReadyForConnectedActionAsync(string operationName)
        {
            var policy = _pathMappingPolicy;
            if (policy == null || policy.HostPlatform != AppServerHostPlatform.Wsl)
            {
                return Task.CompletedTask;
            }

            return _runtimeStartupCoordinator.EnsureWslNetworkReadyAsync(
                _appConfigStore.Load(),
                null,
                WslNetworkActionProbeTimeoutMilliseconds,
                false,
                operationName);
        }

        private void InvalidateWslNetworkReadiness()
        {
            _runtimeStartupCoordinator.InvalidateWslNetworkReadiness();
        }

        private void ApplyRuntimeProcessStartResult(AppServerRuntimeProcessStartResult result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            _writeLock.Wait();
            try
            {
                lock (_syncRoot)
                {
                    _lifetimeCts = result.LifetimeCts;
                    _connectionLostHandled = false;
                    _pathMappingPolicy = result.PathMappingPolicy ?? AppServerPathMappingPolicy.ForHostPlatform(AppServerHostPlatform.Windows);
                    _commandLinePolicy = result.CommandLinePolicy ?? AppServerCommandLinePolicy.ForHostPlatform(AppServerHostPlatform.Windows, null);
                    _process = result.Process;
                    Interlocked.Increment(ref _connectionEpoch);
                    _inputWriter = result.InputWriter;
                }
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private long GetConnectionEpoch()
        {
            return Interlocked.Read(ref _connectionEpoch);
        }

        private void DisconnectForRuntimeChange()
        {
            IList<TaskCompletionSource<IDictionary<string, object>>> pendingRequests;
            lock (_syncRoot)
            {
                pendingRequests = _protocolTransport.DrainPendingRequests();
                _activeTurnIds.Clear();
                _authenticationLoginPending = false;
                _pendingRequestRegistry.Clear();
                _loadedThreadIds.Clear();
                _threadApprovalModes.Clear();
                _isConnected = false;
                _connectedRuntimeKey = null;
                _connectionLostHandled = true;
            }

            CleanupProcessResources();

            for (var i = 0; i < pendingRequests.Count; i++)
            {
                pendingRequests[i].TrySetException(new InvalidOperationException(L("AppServerClient.Error.RuntimeChanged", "Codex App Server runtime changed.")));
            }
        }

        private static string BuildRuntimeConnectionKey(
            AppConfigSnapshot appConfig,
            AppServerLaunchInfo launchInfo)
        {
            return AppServerRuntimeStartupCoordinator.BuildRuntimeConnectionKey(
                appConfig,
                launchInfo);
        }

        private static string BuildConnectionKeyArguments(AppServerLaunchInfo launchInfo)
        {
            return AppServerRuntimeStartupCoordinator.BuildConnectionKeyArguments(launchInfo);
        }

        private void StartBackgroundReaders()
        {
            Process process;
            TextReader standardOutput;
            TextReader standardError;
            CancellationToken cancellationToken;
            long connectionEpoch;
            lock (_syncRoot)
            {
                process = _process;
                standardOutput = process.StandardOutput;
                standardError = process.StandardError;
                cancellationToken = _lifetimeCts.Token;
                connectionEpoch = GetConnectionEpoch();
            }

            Task.Run(
                () => ReadStdOutLoopAsync(standardOutput, cancellationToken, connectionEpoch, process),
                cancellationToken);
            Task.Run(
                () => ReadStdErrLoopAsync(standardError, cancellationToken, connectionEpoch, process),
                cancellationToken);
        }

        private async Task ReadStdOutLoopAsync(
            TextReader standardOutput,
            CancellationToken cancellationToken,
            long connectionEpoch,
            Process process)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await standardOutput.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    if (!IsCurrentConnectionSource(connectionEpoch, process))
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    ProcessIncomingMessage(line, connectionEpoch);
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    HandleAppServerConnectionLost(
                        "Failed to read App Server output: " + ex.Message,
                        connectionEpoch,
                        process);
                }
            }
        }

        private async Task ReadStdErrLoopAsync(
            TextReader standardError,
            CancellationToken cancellationToken,
            long connectionEpoch,
            Process process)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var line = await standardError.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                    {
                        break;
                    }

                    if (!IsCurrentConnectionSource(connectionEpoch, process))
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var output = AppServerProtocolTextComposer.StripAnsiEscapeSequences(line);
                    Trace.TraceWarning("App Server stderr received. Characters={0}", output.Length);
                    OnEventReceived(new AppServerEventArgs(
                        "system_notice",
                        L("AppServerClient.SystemNotice.AppServerOutputPrefix", "App Server: ") + output));
                }
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested && IsCurrentConnectionSource(connectionEpoch, process))
                {
                    Trace.TraceError("Failed to read App Server stderr. ErrorType={0}", ex.GetType().FullName);
                    OnEventReceived(new AppServerEventArgs(
                        "system_notice",
                        L("AppServerClient.SystemNotice.ReadStderrFailed.Prefix", "Failed to read App Server stderr: ") + ex.Message));
                }
            }
        }

        private void ProcessIncomingMessage(string json, long connectionEpoch)
        {
            // Large history pages are parsed on this reader thread; record their cost for load diagnostics.
            var watch = json.Length >= AppServerHistoryLoadTrace.LargeMessageCharacters ? Stopwatch.StartNew() : null;
            IDictionary<string, object> message;
            if (!TryDeserializeIncomingMessage(json, out message))
            {
                return;
            }

            var parseMs = watch != null ? watch.ElapsedMilliseconds : 0L;
            ProcessIncomingMessage(message, connectionEpoch);
            if (watch != null)
            {
                Trace.TraceInformation(AppServerHistoryLoadTrace.BuildLargeMessageText(
                    Convert.ToString(AppServerIncomingMessageRouter.Classify(message), CultureInfo.InvariantCulture),
                    message != null && message.ContainsKey("id") ? Convert.ToString(message["id"], CultureInfo.InvariantCulture) : null,
                    AppServerJsonValueReader.GetString(message, "method"),
                    json.Length,
                    parseMs,
                    watch.ElapsedMilliseconds - parseMs));
            }
        }

        private void ProcessIncomingMessage(IDictionary<string, object> message, long connectionEpoch)
        {
            if (connectionEpoch != GetConnectionEpoch())
            {
                return;
            }

            switch (AppServerIncomingMessageRouter.Classify(message))
            {
                case AppServerIncomingMessageKind.ServerRequest:
                    _dynamicRequestCoordinator.HandleServerRequest(message, connectionEpoch);
                    break;
                case AppServerIncomingMessageKind.Notification:
                    if (string.Equals(AppServerJsonValueReader.GetString(message, "method"), "account/login/completed", StringComparison.Ordinal))
                    {
                        ObserveAuthenticationLoginCompletion(AppServerJsonValueReader.GetString(AppServerJsonValueReader.GetDictionary(message, "params"), "loginId"));
                    }
                    HandleNotification(message);
                    break;
                case AppServerIncomingMessageKind.Response:
                    HandleResponse(message);
                    break;
            }
        }

        private bool TryDeserializeIncomingMessage(string json, out IDictionary<string, object> message)
        {
            try
            {
                message = DeserializeDictionary(json);
                return true;
            }
            catch (ArgumentException ex)
            {
                ReportMalformedIncomingMessage(json, ex);
            }
            catch (InvalidOperationException ex)
            {
                ReportMalformedIncomingMessage(json, ex);
            }

            message = null;
            return false;
        }

        private void ReportMalformedIncomingMessage(string json, Exception ex)
        {
            Trace.TraceError(
                "Failed to parse App Server message. ErrorType={0}; Characters={1}",
                ex.GetType().FullName,
                json == null ? 0 : json.Length);
            OnEventReceived(new AppServerEventArgs(
                "system_notice",
                L("AppServerClient.SystemNotice.UnparsableMessageIgnored", "Received an unparsable App Server message and ignored it.")));
        }

        private void HandleResponse(IDictionary<string, object> message)
        {
            var requestId = GetRequestId(message);
            string errorMessage;
            if (!_protocolTransport.TryHandleResponse(message, out errorMessage))
            {
                Trace.TraceWarning("App Server response could not be handled. RequestId={0}", requestId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                Trace.TraceWarning(
                    "App Server response error. RequestId={0}; MessageCharacters={1}",
                    requestId,
                    errorMessage.Length);
            }
        }

        private void HandleServerRequest(IDictionary<string, object> message)
        {
            _dynamicRequestCoordinator.HandleServerRequest(message);
        }

        private void HandleNotification(IDictionary<string, object> message)
        {
            var method = AppServerJsonValueReader.GetString(message, "method");
            var parameters = AppServerJsonValueReader.GetDictionary(message, "params");
            if (HandleRealtimeVoiceNotification(method, parameters))
            {
                return;
            }

            AppServerEventDiagnostics.TraceNotification(method, parameters);
            HandleComputerUseNotification(method, parameters);
            _notificationEventProcessor.HandleNotification(method, parameters);
        }
        // Runs while the thread is not loaded, before thread/resume reads its session file.
        public Func<string, Task> ThreadResumePreparation { get; set; }

        private async Task PrepareThreadResumeAsync(string threadId)
        {
            var prepare = ThreadResumePreparation;
            if (prepare == null)
            {
                return;
            }

            try
            {
                await prepare(threadId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Thread resume preparation failed. ExceptionType={0}", ex.GetType().FullName);
            }
        }

        private async Task EnsureThreadLoadedAsync(string threadId, string workspacePath, ApprovalMode? approvalMode)
        {
            var shouldResume = false;
            lock (_syncRoot)
            {
                if (!_loadedThreadIds.Contains(threadId))
                {
                    shouldResume = true;
                }
            }

            if (!shouldResume)
            {
                return;
            }

            await PrepareThreadResumeAsync(threadId).ConfigureAwait(false);
            var serverWorkspacePath = MapRequiredClientPathToServer(workspacePath, "workspace");
            var parameters = new Dictionary<string, object>
            {
                { "threadId", threadId },
                { "cwd", serverWorkspacePath },
                { "persistExtendedHistory", false }
            };
            if (ProtocolCapabilities?.SupportsThreadHistoryPagination == true)
                parameters["excludeTurns"] = true;
            if (approvalMode.HasValue)
            {
                parameters["approvalPolicy"] = MapApprovalPolicy(approvalMode.Value);
                parameters["approvalsReviewer"] = AppServerThreadStartParameterBuilder.MapApprovalsReviewer(approvalMode.Value);
                parameters["sandbox"] = MapSandboxMode(approvalMode.Value);
            }
            AppServerThreadStartParameterBuilder.AddProjectTrustOverride(
                parameters,
                serverWorkspacePath,
                approvalMode);

            var result = await SendRequestAsync("thread/resume", parameters).ConfigureAwait(false);

            var thread = AppServerJsonValueReader.GetDictionary(result, "thread");
            var resumedThreadId = AppServerJsonValueReader.GetString(thread, "id");
            if (string.IsNullOrWhiteSpace(resumedThreadId))
            {
                throw new InvalidOperationException(L("AppServerClient.Error.ResumeThreadIdMissing", "Failed to restore thread: thread ID was not returned."));
            }

            lock (_syncRoot)
            {
                _loadedThreadIds.Add(resumedThreadId);
                if (approvalMode.HasValue)
                {
                    _threadApprovalModes[resumedThreadId] = approvalMode.Value;
                }
                else
                {
                    _threadApprovalModes.Remove(resumedThreadId);
                }
            }
        }

        private async Task RefreshLoadedThreadIdsAsync()
        {
            var loadedThreadIds = await ListLoadedThreadIdsAsync().ConfigureAwait(false);
            lock (_syncRoot)
            {
                _loadedThreadIds.Clear();
                foreach (var threadId in loadedThreadIds)
                {
                    if (!string.IsNullOrWhiteSpace(threadId))
                    {
                        _loadedThreadIds.Add(threadId);
                    }
                }
            }
        }

        private static IDictionary<string, object> ToProtocolParameters(object parameters)
        {
            var dictionary = parameters as IDictionary<string, object>;
            return dictionary ?? new Dictionary<string, object>();
        }

        private async Task<IDictionary<string, object>> SendRequestAsync(string method, object parameters, int timeoutMs = DefaultRequestTimeoutMs)
        {
            return await SendRequestAsync(method, parameters, GetLifetimeToken(), timeoutMs).ConfigureAwait(false);
        }

        // Disconnect cleanup nulls and disposes the lifetime source; read it once under the lock.
        private CancellationToken GetLifetimeToken()
        {
            lock (_syncRoot)
            {
                var lifetimeCts = _lifetimeCts;
                if (lifetimeCts == null)
                {
                    return CancellationToken.None;
                }

                try
                {
                    return lifetimeCts.Token;
                }
                catch (ObjectDisposedException)
                {
                    return new CancellationToken(true);
                }
            }
        }

        private async Task<IDictionary<string, object>> SendRequestAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken,
            int timeoutMs = DefaultRequestTimeoutMs)
        {
            return await SendComputerUseRequestAsync(method, ToProtocolParameters(parameters), cancellationToken, timeoutMs).ConfigureAwait(false);
        }

        private async Task<IDictionary<string, object>> SendRequestDirectAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken,
            int timeoutMs = DefaultRequestTimeoutMs,
            long? expectedConnectionEpoch = null)
        {
            // A managed MCP mutation reloads config while retaining its transition lease.
            // Only that owner's reload may enter; other writes and callers remain blocked.
            using (RequiresAuthenticationGate(method) ? RuntimeOperationGate.Shared.EnterOperation(
                allowTransitionOwner: string.Equals(method, "config/mcpServer/reload", StringComparison.Ordinal)) : null)
            {
                try
                {
                    if (string.Equals(method, "thread/resume", StringComparison.Ordinal))
                    {
                        var epoch = expectedConnectionEpoch ?? GetConnectionEpoch();
                        return await AppServerThreadResumeConfiguration.SendAsync(
                            (operation, values, token, budget) => _protocolTransport.SendRequestAsync(operation,
                                ToProtocolParameters(values), TimeSpan.FromMilliseconds(budget), token, epoch),
                            ToProtocolParameters(parameters), cancellationToken, timeoutMs, CreateRequestTimeoutException).ConfigureAwait(false);
                    }
                    return await _protocolTransport.SendRequestAsync(
                        method,
                        ToProtocolParameters(parameters),
                        TimeSpan.FromMilliseconds(timeoutMs),
                        cancellationToken,
                        expectedConnectionEpoch).ConfigureAwait(false);
                }
                catch (AppServerTransportWriteException ex)
                {
                    var disconnectMessage = BuildConnectionLostMessage(ex.InnerException ?? ex);
                    HandleAppServerConnectionLost(disconnectMessage, ex.ConnectionEpoch, null);
                    throw new InvalidOperationException(disconnectMessage, ex);
                }
            }
        }

        private void HandleAppServerConnectionLost(
            string message,
            long expectedConnectionEpoch,
            Process expectedProcess)
        {
            List<TaskCompletionSource<IDictionary<string, object>>> pendingRequests;
            List<KeyValuePair<string, string>> activeTurns;
            AppServerProcessResources resources;

            _writeLock.Wait();
            try
            {
                lock (_syncRoot)
                {
                    if (GetConnectionEpoch() != expectedConnectionEpoch ||
                        (expectedProcess != null && !ReferenceEquals(_process, expectedProcess)) ||
                        _connectionLostHandled)
                    {
                        return;
                    }

                    _connectionLostHandled = true;
                    _isConnected = false;
                    pendingRequests = _protocolTransport.DrainPendingRequests().ToList();
                    activeTurns = _activeTurnIds.ToList();
                    _activeTurnIds.Clear();
                    _authenticationLoginPending = false;
                    _pendingRequestRegistry.Clear();
                    _loadedThreadIds.Clear();
                    resources = TakeProcessResourcesUnsafe();
                }
            }
            finally
            {
                _writeLock.Release();
            }

            Trace.TraceWarning(
                "App Server connection lost. ReasonCharacters={0}",
                string.IsNullOrEmpty(message) ? 0 : message.Length);

            try
            {
                resources.LifetimeCts?.Cancel();
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Failed to cancel App Server lifecycle token: {0}", ex.Message);
            }

            CleanupProcessResources(resources);

            for (var i = 0; i < pendingRequests.Count; i++)
            {
                pendingRequests[i].TrySetException(new InvalidOperationException(message));
            }

            for (var i = 0; i < activeTurns.Count; i++)
            {
                OnEventReceived(new AppServerEventArgs("error", message, activeTurns[i].Key, activeTurns[i].Value));
            }

            OnEventReceived(new AppServerEventArgs("app_server_disconnected", message));
        }

        private static TimeoutException CreateRequestTimeoutException(string method, int timeoutMs)
        {
            var normalizedMethod = string.IsNullOrWhiteSpace(method) ? "unknown" : method.Trim();
            var timeoutSeconds = Math.Max(1, timeoutMs / 1000);
            return new TimeoutException(
                LF(
                    "AppServerClient.Error.RequestTimeout.Template",
                    "App Server request timed out: {0}, still no response after waiting about {1} seconds.",
                    normalizedMethod,
                    timeoutSeconds));
        }

        private static int BuildCommandRequestTimeoutMs(long commandTimeoutMs)
        {
            if (commandTimeoutMs <= 0)
            {
                return LongRunningRequestTimeoutMs;
            }

            var bufferedTimeout = commandTimeoutMs + CommandRequestTimeoutBufferMs;
            if (bufferedTimeout < DefaultRequestTimeoutMs)
            {
                bufferedTimeout = DefaultRequestTimeoutMs;
            }

            return bufferedTimeout > int.MaxValue ? int.MaxValue : (int)bufferedTimeout;
        }

        private void CleanupProcessResources()
        {
            AppServerProcessResources resources;
            _writeLock.Wait();
            try
            {
                lock (_syncRoot)
                {
                    resources = TakeProcessResourcesUnsafe();
                }
            }
            finally
            {
                _writeLock.Release();
            }

            CleanupProcessResources(resources);
        }

        private AppServerProcessResources TakeProcessResourcesUnsafe()
        {
            var resources = new AppServerProcessResources
            {
                Process = _process,
                InputWriter = _inputWriter,
                LifetimeCts = _lifetimeCts,
                ComputerUse = _computerUse
            };

            _process = null;
            _inputWriter = null;
            _lifetimeCts = null;
            _computerUse = null;
            _pathMappingPolicy = AppServerPathMappingPolicy.ForHostPlatform(AppServerHostPlatform.Windows);
            _commandLinePolicy = AppServerCommandLinePolicy.ForHostPlatform(AppServerHostPlatform.Windows, null);
            return resources;
        }

        private void CleanupProcessResources(AppServerProcessResources resources)
        {
            resources.ComputerUse?.Dispose();
            AppServerProcessLifecycle.CleanupResources(
                resources,
                processToUnbind => processToUnbind.Exited -= OnProcessExited);
        }

        private static string BuildConnectionLostMessage(Exception ex)
        {
            return ex == null || string.IsNullOrWhiteSpace(ex.Message)
                ? L("AppServerClient.Error.ConnectionLost", "Connection to Codex App Server was lost.")
                : L("AppServerClient.Error.ConnectionLostWithDetail.Prefix", "Connection to Codex App Server was lost: ") + ex.Message;
        }

        private Task SendNotificationAsync(string method)
        {
            return _protocolTransport.SendNotificationAsync(
                method,
                new Dictionary<string, object>(),
                CancellationToken.None);
        }

        private Task SendServerRequestResponseAsync(object requestId, object result, long expectedConnectionEpoch)
        {
            return _protocolTransport.SendServerRequestResponseAsync(
                requestId,
                ToProtocolParameters(result),
                expectedConnectionEpoch,
                CancellationToken.None);
        }

        private Task SendErrorResponseAsync(object requestId, int code, string message, long expectedConnectionEpoch)
        {
            return _protocolTransport.SendErrorResponseAsync(
                requestId,
                code,
                message,
                expectedConnectionEpoch,
                CancellationToken.None);
        }

        private AppServerEventArgs CreateInternalToolsRegisteredEvent(string threadId, AppServerThreadToolContext toolContext)
        {
            if (toolContext == null || toolContext.DynamicTools == null)
            {
                return null;
            }

            var toolNames = toolContext.DynamicTools
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Name))
                .Select(item => string.IsNullOrWhiteSpace(item.Namespace) ? item.Name : item.Namespace + "/" + item.Name)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(item => item, StringComparer.Ordinal)
                .ToArray();
            if (toolNames.Length == 0)
            {
                return null;
            }

            return new AppServerEventArgs(
                "internal_tools_registered",
                "Internal tools registered: " + string.Join(", ", toolNames),
                threadId: threadId);
        }

        private void ClearResolvedInteractionRequest(string requestId)
        {
            _dynamicRequestCoordinator.ClearResolvedInteractionRequest(requestId);
        }

        private void HandleCommandApprovalRequest(object rawRequestId, string requestId, IDictionary<string, object> parameters)
        {
            _dynamicRequestCoordinator.HandleCommandApprovalRequest(rawRequestId, requestId, parameters);
        }

        private ApprovalMode GetThreadApprovalMode(string threadId)
        {
            lock (_syncRoot)
            {
                ApprovalMode approvalMode;
                if (!string.IsNullOrWhiteSpace(threadId) &&
                    _threadApprovalModes.TryGetValue(threadId, out approvalMode))
                {
                    return approvalMode;
                }
            }

            return ApprovalMode.Auto;
        }

        private CustomApprovalPolicyEvaluation EvaluateCustomApproval(
            ApprovalMode approvalMode,
            AppServerPendingApprovalRequest pendingRequest)
        {
            if (pendingRequest == null)
            {
                return new CustomApprovalPolicyEvaluation();
            }

            var snapshot = _appConfigStore.Load() ?? new AppConfigSnapshot();
            return _customApprovalPolicyEvaluator.Evaluate(
                approvalMode,
                snapshot.CustomApprovalSettings,
                pendingRequest.Classification,
                pendingRequest.Kind,
                pendingRequest.CommandText);
        }

        private InternalToolExecutionContext GetInternalToolContext(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return null;
            }

            lock (_syncRoot)
            {
                InternalToolExecutionContext context;
                return _internalToolContexts.TryGetValue(threadId, out context) ? context : null;
            }
        }

        private static string MapApprovalPolicy(ApprovalMode approvalMode)
        {
            return "on-request";
        }

        private static string MapSandboxMode(ApprovalMode approvalMode)
        {
            switch (approvalMode)
            {
                case ApprovalMode.ReadOnly:
                case ApprovalMode.Custom:
                    return "read-only";
                case ApprovalMode.FullAccess:
                case ApprovalMode.FullAccessAutoApprove:
                    return "danger-full-access";
                default:
                    return "workspace-write";
            }
        }

        private string MapRequiredClientPathToServer(string path, string pathKind)
        {
            var policy = _pathMappingPolicy;
            return policy == null ? path : policy.MapRequiredClientPathToServer(path, pathKind);
        }

        private string MapClientOrServerPathToServer(string path)
        {
            var policy = _pathMappingPolicy;
            return policy == null ? path : policy.MapClientOrServerPathToServer(path);
        }

        private string MapServerPathToClient(string path)
        {
            var policy = _pathMappingPolicy;
            return policy == null ? path : policy.MapServerPathToClient(path);
        }

        private bool IsCurrentHostPlatform(AppServerHostPlatform hostPlatform)
        {
            return GetCurrentHostPlatform() == hostPlatform;
        }

        private AppServerHostPlatform GetCurrentHostPlatform()
        {
            var policy = _pathMappingPolicy;
            return policy != null ? policy.HostPlatform : AppServerHostPlatform.Windows;
        }

        private IDictionary<string, object> BuildSandboxPolicy(ApprovalMode approvalMode, string workspacePath)
        {
            switch (approvalMode)
            {
                case ApprovalMode.ReadOnly:
                case ApprovalMode.Custom:
                    return new Dictionary<string, object>
                    {
                        { "type", "readOnly" },
                        { "access", new Dictionary<string, object> { { "type", "fullAccess" } } },
                        { "networkAccess", false }
                    };
                case ApprovalMode.FullAccess:
                case ApprovalMode.FullAccessAutoApprove:
                    return new Dictionary<string, object> { { "type", "dangerFullAccess" } };
                default:
                    var serverWorkspacePath = MapRequiredClientPathToServer(workspacePath, "workspace");
                    return new Dictionary<string, object>
                    {
                        { "type", "workspaceWrite" },
                        { "writableRoots", new[] { serverWorkspacePath } },
                        { "readOnlyAccess", new Dictionary<string, object> { { "type", "fullAccess" } } },
                        { "networkAccess", false },
                        { "excludeTmpdirEnvVar", false },
                        { "excludeSlashTmp", false }
                    };
            }
        }

        private static string GetClientVersion()
        {
            var version = typeof(AppServerClient).Assembly.GetName().Version;
            return version == null ? "1.0.0" : version.ToString();
        }

        private static IDictionary<string, object> DeserializeDictionary(string json)
        {
            return AppServerJsonValue.DeserializeDictionary(json);
        }

        private static string AbbreviateForLog(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || maxLength <= 0 || text.Length <= maxLength)
            {
                return text ?? string.Empty;
            }

            return text.Substring(0, maxLength) + "...";
        }

        private string ResolveCurrentPluginSkillPath(string path, string operation, string name)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string resolvedPath;
            if (!PluginSkillPathResolver.TryResolveCurrentPath(path, out resolvedPath) ||
                string.IsNullOrWhiteSpace(resolvedPath) ||
                string.Equals(path, resolvedPath, StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            TraceSkillPluginPathResolution(operation, name, path, resolvedPath, "plugin-cache-version-changed");
            return resolvedPath;
        }

        private void TraceSkillPluginPathResolution(string operation, string name, string originalPath, string resolvedPath, string reason)
        {
            var config = LoadAppConfigForDiagnostics();
            Trace.TraceInformation(
                "Skill/plugin diagnostic. Operation=skill-path/resolve; RuntimeProfile={0}; HostPlatform={1}; RuntimeSource={2}; SourceOperation={3}; Name={4}; OriginalPath={5}; ResolvedPath={6}; OriginalPathExists={7}; ResolvedPathExists={8}; PathResolution={9}",
                BuildDiagnosticRuntimeProfileId(config),
                GetCurrentHostPlatform(),
                config == null ? string.Empty : config.CodexRuntimeSource,
                operation ?? string.Empty,
                AbbreviateForLog(name, 160),
                AbbreviateForLog(originalPath, 500),
                AbbreviateForLog(resolvedPath, 500),
                GetDiagnosticPathExistsText(originalPath),
                GetDiagnosticPathExistsText(resolvedPath),
                reason ?? string.Empty);
        }

        private void TraceSkillPluginInputPathDiagnostic(string inputType, string name, string originalPath, string resolvedPath, string serverPath)
        {
            var config = LoadAppConfigForDiagnostics();
            Trace.TraceInformation(
                "Skill/plugin diagnostic. Operation=turn/input; RuntimeProfile={0}; HostPlatform={1}; RuntimeSource={2}; InputType={3}; Name={4}; OriginalPath={5}; ResolvedPath={6}; ServerPath={7}; OriginalPathExists={8}; ResolvedPathExists={9}; ServerPathExists={10}",
                BuildDiagnosticRuntimeProfileId(config),
                GetCurrentHostPlatform(),
                config == null ? string.Empty : config.CodexRuntimeSource,
                inputType ?? string.Empty,
                AbbreviateForLog(name, 160),
                AbbreviateForLog(originalPath, 500),
                AbbreviateForLog(resolvedPath, 500),
                AbbreviateForLog(serverPath, 500),
                GetDiagnosticPathExistsText(originalPath),
                GetDiagnosticPathExistsText(resolvedPath),
                GetDiagnosticPathExistsText(serverPath));
        }

        private void TracePluginListDiagnostics(IReadOnlyCollection<AppServerPluginSummary> plugins)
        {
            var config = LoadAppConfigForDiagnostics();
            var pluginItems = plugins ?? new List<AppServerPluginSummary>();
            Trace.TraceInformation(
                "Skill/plugin diagnostic. Operation=plugin/list; RuntimeProfile={0}; HostPlatform={1}; RuntimeSource={2}; PluginCount={3}",
                BuildDiagnosticRuntimeProfileId(config),
                GetCurrentHostPlatform(),
                config == null ? string.Empty : config.CodexRuntimeSource,
                pluginItems.Count);

            foreach (var plugin in pluginItems.Where(item => item != null))
            {
                Trace.TraceInformation(
                    "Skill/plugin diagnostic. Operation=plugin/list; Item=plugin; RuntimeProfile={0}; PluginId={1}; Name={2}; DisplayName={3}; Marketplace={4}; MarketplacePath={5}; Installed={6}; Enabled={7}; SourceType={8}; SourcePath={9}; SourcePathExists={10}",
                    BuildDiagnosticRuntimeProfileId(config),
                    AbbreviateForLog(plugin.PluginId, 160),
                    AbbreviateForLog(plugin.Name, 160),
                    AbbreviateForLog(plugin.DisplayName, 160),
                    AbbreviateForLog(plugin.MarketplaceName, 160),
                    AbbreviateForLog(plugin.MarketplacePath, 500),
                    plugin.Installed,
                    plugin.Enabled,
                    AbbreviateForLog(plugin.SourceType, 80),
                    AbbreviateForLog(plugin.SourcePath, 500),
                    GetDiagnosticPathExistsText(plugin.SourcePath));
            }
        }

        private void TraceSkillsListDiagnostics(
            IReadOnlyCollection<string> workspacePaths,
            bool forceReload,
            IReadOnlyCollection<AppServerSkillsSnapshot> snapshots)
        {
            var config = LoadAppConfigForDiagnostics();
            var snapshotItems = snapshots ?? new List<AppServerSkillsSnapshot>();
            var skillCount = snapshotItems.Sum(item => item == null || item.Skills == null ? 0 : item.Skills.Count);
            var errorCount = snapshotItems.Sum(item => item == null || item.Errors == null ? 0 : item.Errors.Count);
            Trace.TraceInformation(
                "Skill/plugin diagnostic. Operation=skills/list; RuntimeProfile={0}; HostPlatform={1}; RuntimeSource={2}; ForceReload={3}; RequestedWorkspaces={4}; SnapshotCount={5}; SkillCount={6}; ErrorCount={7}",
                BuildDiagnosticRuntimeProfileId(config),
                GetCurrentHostPlatform(),
                config == null ? string.Empty : config.CodexRuntimeSource,
                forceReload,
                AbbreviateForLog(string.Join("|", (workspacePaths ?? new string[0]).Where(item => !string.IsNullOrWhiteSpace(item)).ToArray()), 1000),
                snapshotItems.Count,
                skillCount,
                errorCount);

            foreach (var snapshot in snapshotItems.Where(item => item != null))
            {
                var workspacePath = snapshot.WorkspacePath ?? string.Empty;
                foreach (var skill in (snapshot.Skills ?? new List<AppServerSkillInfo>()).Where(item => item != null))
                {
                    Trace.TraceInformation(
                        "Skill/plugin diagnostic. Operation=skills/list; Item=skill; RuntimeProfile={0}; Workspace={1}; Name={2}; DisplayName={3}; Scope={4}; Enabled={5}; Path={6}; PathExists={7}",
                        BuildDiagnosticRuntimeProfileId(config),
                        AbbreviateForLog(workspacePath, 500),
                        AbbreviateForLog(skill.Name, 160),
                        AbbreviateForLog(skill.DisplayName, 160),
                        AbbreviateForLog(skill.Scope, 80),
                        skill.Enabled,
                        AbbreviateForLog(skill.Path, 500),
                        GetDiagnosticPathExistsText(skill.Path));
                }

                foreach (var error in (snapshot.Errors ?? new List<AppServerSkillError>()).Where(item => item != null))
                {
                    Trace.TraceWarning(
                        "Skill/plugin diagnostic. Operation=skills/list; Item=error; RuntimeProfile={0}; Workspace={1}; Path={2}; PathExists={3}; Message={4}",
                        BuildDiagnosticRuntimeProfileId(config),
                        AbbreviateForLog(workspacePath, 500),
                        AbbreviateForLog(error.Path, 500),
                        GetDiagnosticPathExistsText(error.Path),
                        AbbreviateForLog(error.Message, 1000));
                }
            }
        }

        private AppConfigSnapshot LoadAppConfigForDiagnostics()
        {
            try
            {
                return _appConfigStore == null ? null : _appConfigStore.Load();
            }
            catch
            {
                return null;
            }
        }

        private string BuildDiagnosticRuntimeProfileId(AppConfigSnapshot config)
        {
            try
            {
                return new RuntimeProfileService().GetCurrentRuntimeProfileId(config);
            }
            catch
            {
                return RuntimeProfileService.WindowsRuntimeProfileId;
            }
        }

        private string BuildDiagnosticCodexHomeText(AppConfigSnapshot config)
        {
            return "target-owned";
        }

        private string GetDiagnosticPathExistsText(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return "empty";
            }

            var trimmedPath = path.Trim();
            var hostPlatform = GetCurrentHostPlatform();
            if (WorkspacePathUtilities.LooksLikeUnixAbsolutePath(trimmedPath))
            {
                if (hostPlatform == AppServerHostPlatform.RemoteMac)
                {
                    return "unknown-remote";
                }

                if (hostPlatform == AppServerHostPlatform.Wsl)
                {
                    return "unknown-wsl";
                }

                return "unknown-unix-path";
            }

            try
            {
                return (File.Exists(trimmedPath) || Directory.Exists(trimmedPath))
                    ? "true"
                    : "false";
            }
            catch (Exception ex)
            {
                return "error: " + AbbreviateForLog(ex.Message, 160);
            }
        }

        private static bool IsTransientRateLimitsReadException(Exception exception)
        {
            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
            {
                return false;
            }

            var message = exception.Message;
            return message.IndexOf("failed to fetch codex rate limits", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("error sending request", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("failed to lookup address information", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("dns error", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("try again", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsFallbackableTurnSteerException(Exception exception)
        {
            if (exception == null || string.IsNullOrWhiteSpace(exception.Message))
            {
                return false;
            }

            var message = exception.Message;
            return message.IndexOf("method not found", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("unknown method", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("not implemented", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("unsupported", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("cannot accept steering", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("does not accept steering", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("expectedturnid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("expected turn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("active turn changed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   message.IndexOf("precondition", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object GetRawRequestId(IDictionary<string, object> message)
        {
            object value;
            if (message != null && message.TryGetValue("id", out value) && value != null)
            {
                return value;
            }

            return null;
        }

        private static string GetRequestId(IDictionary<string, object> message)
        {
            return NormalizeRequestId(GetRawRequestId(message));
        }

        private static string NormalizeRequestId(object requestId)
        {
            return requestId == null ? string.Empty : Convert.ToString(requestId);
        }

        private static Dictionary<string, object> BuildCommandInputWriteParameters(string processId, string input)
        {
            if (string.IsNullOrWhiteSpace(processId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ProcessIdRequired", "Process ID cannot be empty."), nameof(processId));
            }

            return AppServerRequestParameterBuilder.BuildCommandInputWriteParameters(processId.Trim(), input);
        }

        private void EnsureConnected()
        {
            if (!_isConnected || _process == null || _process.HasExited)
            {
                throw new InvalidOperationException(L("AppServerClient.Error.NotConnected", "App Server is not connected."));
            }
        }

        internal static AppServerProtocolCapabilities BuildProtocolCapabilities(
            AppConfigSnapshot appConfig,
            AppServerLaunchInfo launchInfo,
            IDictionary<string, object> initializeResult)
        {
            return AppServerProtocolMetadataMapper.BuildProtocolCapabilities(appConfig, launchInfo, initializeResult);
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, L(key, fallback), args ?? Array.Empty<object>());
        }

        private static void AddOptionalString(IDictionary<string, object> target, string key, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            target[key] = value;
        }

        private bool IsCurrentConnectionSource(long expectedConnectionEpoch, Process expectedProcess)
        {
            lock (_syncRoot)
            {
                return GetConnectionEpoch() == expectedConnectionEpoch &&
                       ReferenceEquals(_process, expectedProcess);
            }
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            var process = sender as Process;
            if (process == null)
            {
                return;
            }

            HandleAppServerConnectionLost(
                L("AppServerClient.Error.AppServerExited", "Codex App Server exited."),
                GetConnectionEpoch(),
                process);
        }

        private void OnEventReceived(AppServerEventArgs args)
        {
            // Output for a caller-supplied receiver is delivered only to that receiver, matching local execution.
            if (TryDeliverCommandOutputToReceiver(args))
            {
                return;
            }

            if (args?.SubAgentActivity != null)
                RememberSubAgentParent(args.SubAgentActivity.AgentThreadId, args.ThreadId);
            if (args?.ThreadSummary != null)
                RememberSubAgentParent(args.ThreadSummary.ThreadId, args.ThreadSummary.ParentThreadId);
            if (ShouldRememberLoadedThreadIdFromEvent(args))
            {
                lock (_syncRoot)
                {
                    _loadedThreadIds.Add(args.ThreadId);
                }
            }

            AppServerEventDiagnostics.TraceMappedEvent(args);

            var handler = EventReceived;
            if (handler != null)
            {
                handler(this, args);
            }
        }

        internal static bool ShouldRememberLoadedThreadIdFromEvent(AppServerEventArgs args)
        {
            return args != null &&
                   !string.IsNullOrWhiteSpace(args.ThreadId) &&
                   !string.Equals(args.EventType, "thread_deleted", StringComparison.Ordinal) &&
                   !string.Equals(args.EventType, "thread_closed", StringComparison.Ordinal);
        }
    }

    internal static class TaskExtensions
    {
        public static void Forget(this Task task, string operationName = null)
        {
            if (task == null)
            {
                return;
            }

            task.ContinueWith(
                t =>
                {
                    var exception = t.Exception != null ? t.Exception.GetBaseException() : null;
                    if (string.IsNullOrWhiteSpace(operationName))
                    {
                        Trace.TraceError("Background task failed: {0}", exception ?? t.Exception);
                        return;
                    }

                    Trace.TraceError("Background task failed ({0}): {1}", operationName, exception ?? t.Exception);
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
