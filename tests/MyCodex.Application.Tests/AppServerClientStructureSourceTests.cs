using System;
using System.IO;
using MyCodex.Application.Tests.SourceGuards;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerClientStructureSourceTests
    {
        [Category(SourceGuardCategory.Placement)]
        [TestCase("AppServerJsonValueReader.cs", "internal static class AppServerJsonValueReader")]
        [TestCase("AppServerRequestParameterBuilder.cs", "internal static class AppServerRequestParameterBuilder")]
        [TestCase("AppServerPendingRequestRegistry.cs", "internal sealed class AppServerPendingRequestRegistry")]
        [TestCase("AppServerProtocolTransport.cs", "internal sealed class AppServerProtocolTransport")]
        [TestCase("AppServerIncomingMessageRouter.cs", "internal static class AppServerIncomingMessageRouter")]
        [TestCase("AppServerProcessLifecycle.cs", "internal static class AppServerProcessLifecycle")]
        [TestCase("AppServerEventDiagnostics.cs", "internal static class AppServerEventDiagnostics")]
        [TestCase("AppServerNotificationDispatcher.cs", "internal static class AppServerNotificationDispatcher")]
        public void AppServerClientHelpers_ExistWithExpectedTypes(string fileName, string expectedTypeDeclaration)
        {
            var path = RepositorySource.FindSourceFile(fileName);
            Assert.That(File.Exists(path), Is.True, fileName + " should exist.");
            Assert.That(File.ReadAllText(path), Does.Contain(expectedTypeDeclaration));
        }

        [Category(SourceGuardCategory.Placement)]
        [TestCase("AppServerNotificationEventProcessor.cs", "internal sealed class AppServerNotificationEventProcessor")]
        [TestCase("AppServerNotificationEventContext.cs", "internal sealed class AppServerNotificationEventContext")]
        [TestCase("AppServerProtocolTextComposer.cs", "internal static class AppServerProtocolTextComposer")]
        [TestCase("AppServerProtocolMetadataMapper.cs", "internal static class AppServerProtocolMetadataMapper")]
        [TestCase("AppServerClientOperations.cs", "internal sealed class AppServerClientOperations")]
        [TestCase("AppServerThreadOperations.cs", "internal sealed class AppServerThreadOperations")]
        [TestCase("AppServerDynamicRequestCoordinator.cs", "internal sealed class AppServerDynamicRequestCoordinator")]
        [TestCase("AppServerRuntimeStartupCoordinator.cs", "internal sealed class AppServerRuntimeStartupCoordinator")]
        public void AppServerClientSecondSlimdownCollaborators_ExistWithExpectedTypes(
            string fileName,
            string expectedTypeDeclaration)
        {
            var path = RepositorySource.FindSourceFile(fileName);
            Assert.That(File.Exists(path), Is.True, fileName + " should exist.");
            Assert.That(File.ReadAllText(path), Does.Contain(expectedTypeDeclaration));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_RemainsFacadeContract()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs") +
                RepositorySource.ReadSourceFile("AppServerClient.AuthenticationTransition.cs");
            Assert.That(source, Does.Contain("public partial class AppServerClient : IAppServerClient, IAppServerLocalThreadCatalogClient, IAppServerSubAgentClient, IDisposable"));
            Assert.That(source, Does.Contain("public event EventHandler<AppServerEventArgs> EventReceived"));
            Assert.That(source, Does.Contain("public Task ConnectAsync()"));
            Assert.That(source, Does.Contain("public async Task SendUserMessageAsync("));
            Assert.That(source, Does.Contain("public async Task<IReadOnlyList<string>> ListLocalCodexHomePathsForThreadScanAsync()"));
            Assert.That(source, Does.Contain("public async Task<List<AppServerThreadSummary>> ListLocalThreadSummariesForThreadScanAsync(int maxSummaries)"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_RemainsFacadeAfterSecondSlimdown()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs") +
                RepositorySource.ReadSourceFile("AppServerClient.AuthenticationTransition.cs");

            Assert.That(source, Does.Contain("public partial class AppServerClient : IAppServerClient, IAppServerLocalThreadCatalogClient, IAppServerSubAgentClient, IDisposable"));
            Assert.That(source, Does.Contain("private readonly AppServerProtocolTransport _protocolTransport"));
            Assert.That(source, Does.Contain("private readonly AppServerClientOperations _operations"));
            Assert.That(source, Does.Contain("private readonly AppServerThreadOperations _threadOperations"));
            Assert.That(source, Does.Contain("private readonly RuntimeThreadScanCatalog _runtimeThreadScanCatalog"));
            Assert.That(source, Does.Contain("private readonly AppServerDynamicRequestCoordinator _dynamicRequestCoordinator"));
            Assert.That(source, Does.Contain("private readonly AppServerPendingRequestRegistry _pendingRequestRegistry = new AppServerPendingRequestRegistry();"));
            Assert.That(source, Does.Contain("public event EventHandler<AppServerEventArgs> EventReceived"));
            Assert.That(source, Does.Contain("public Task ConnectAsync()"));
            Assert.That(source, Does.Contain("public async Task SendUserMessageAsync("));
            Assert.That(source, Does.Not.Contain("Dictionary<string, TaskCompletionSource<IDictionary<string, object>>> _pendingRequests"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesRequestOperationFamilies()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var operationsSource = RepositorySource.ReadSourceFile("AppServerClientOperations.cs");
            var threadOperationsSource = RepositorySource.ReadSourceFile("AppServerThreadOperations.cs");

            Assert.That(clientSource, Does.Contain("private readonly AppServerClientOperations _operations"));
            Assert.That(clientSource, Does.Contain("private readonly AppServerThreadOperations _threadOperations"));
            Assert.That(operationsSource, Does.Contain("internal delegate Task<IDictionary<string, object>> AppServerRequestInvoker"));
            Assert.That(operationsSource, Does.Contain("internal sealed class AppServerClientOperations"));
            Assert.That(threadOperationsSource, Does.Contain("internal sealed class AppServerThreadOperations"));
            Assert.That(operationsSource, Does.Contain("SendRequestAsync(\"plugin/list\""));
            Assert.That(threadOperationsSource, Does.Contain("SendRequestAsync(\"thread/list\""));
            Assert.That(clientSource, Does.Not.Contain("SendRequestAsync(\"plugin/list\""));
            Assert.That(clientSource, Does.Not.Contain("SendRequestAsync(\"thread/list\""));
            Assert.That(clientSource, Does.Contain("return await _operations.ListPluginsAsync(cancellationToken).ConfigureAwait(false);"));
            Assert.That(clientSource, Does.Contain("return _threadOperations.ListThreadsAsync(cancellationToken);"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerPluginSkillMapping_HasSingleInfrastructureMapperOwner()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var operationsSource = RepositorySource.ReadSourceFile("AppServerClientOperations.cs");
            var mapperSource = RepositorySource.ReadSourceFile("AppServerPluginSkillMapper.cs");

            Assert.That(File.Exists(RepositorySource.GetPath("src", "MyCodex.Infrastructure", "AppServer", "AppServerPluginSkillMapper.cs")), Is.True);
            Assert.That(mapperSource, Does.Contain("internal static class AppServerPluginSkillMapper"));
            Assert.That(mapperSource, Does.Contain("BuildSkillsSnapshots("));
            Assert.That(mapperSource, Does.Contain("BuildPluginSummaries("));
            Assert.That(mapperSource, Does.Contain("BuildPluginDetails("));
            Assert.That(mapperSource, Does.Contain("BuildPluginApps("));

            Assert.That(operationsSource, Does.Contain("AppServerPluginSkillMapper.BuildSkillsSnapshots("));
            Assert.That(operationsSource, Does.Contain("AppServerPluginSkillMapper.BuildPluginSummaries("));
            Assert.That(operationsSource, Does.Contain("AppServerPluginSkillMapper.BuildPluginDetails("));
            Assert.That(operationsSource, Does.Contain("AppServerPluginSkillMapper.BuildPluginApps("));

            Assert.That(clientSource, Does.Not.Contain("private List<AppServerSkillsSnapshot> BuildSkillsSnapshots("));
            Assert.That(clientSource, Does.Not.Contain("private static List<AppServerPluginSummary> BuildPluginSummaries("));
            Assert.That(clientSource, Does.Not.Contain("private static AppServerPluginSummary BuildPluginSummary("));
            Assert.That(clientSource, Does.Not.Contain("private AppServerPluginDetails BuildPluginDetails("));
            Assert.That(clientSource, Does.Not.Contain("private List<AppServerSkillInfo> BuildSkillInfos("));
            Assert.That(clientSource, Does.Not.Contain("private List<AppServerPluginSkillInfo> BuildPluginSkillInfos("));
            Assert.That(clientSource, Does.Not.Contain("private static List<AppServerPluginAppInfo> BuildPluginApps("));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesDynamicRequestCoordination()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var coordinatorSource = RepositorySource.ReadSourceFile("AppServerDynamicRequestCoordinator.cs");

            Assert.That(clientSource, Does.Contain("private readonly AppServerDynamicRequestCoordinator _dynamicRequestCoordinator"));
            Assert.That(coordinatorSource, Does.Contain("public void HandleServerRequest("));
            Assert.That(coordinatorSource, Does.Contain("public bool TryHandleServerRequest("));
            Assert.That(coordinatorSource, Does.Contain("public async Task ResolveApprovalRequestAsync("));
            Assert.That(coordinatorSource, Does.Contain("public async Task ResolveToolUserInputRequestAsync("));
            Assert.That(coordinatorSource, Does.Contain("private async Task<InternalToolResult> ExecuteDynamicToolCallAsync("));
            Assert.That(clientSource, Does.Contain("_dynamicRequestCoordinator.HandleServerRequest(message);"));
            Assert.That(clientSource, Does.Contain("_dynamicRequestCoordinator.ResolveApprovalRequestAsync(requestId, decision)"));
            Assert.That(clientSource, Does.Not.Contain("private async Task<InternalToolResult> ExecuteDynamicToolCallAsync("));
            Assert.That(clientSource, Does.Not.Contain("private void RegisterToolUserInputRequest("));
            Assert.That(clientSource, Does.Not.Contain("private void RegisterMcpElicitationRequest("));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesTransportRoutingAndLifecycleResponsibilities()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var startupCoordinatorSource = RepositorySource.ReadSourceFile("AppServerRuntimeStartupCoordinator.cs");

            Assert.That(source, Does.Contain("_protocolTransport"));
            Assert.That(source, Does.Contain("AppServerIncomingMessageRouter.Classify"));
            Assert.That(startupCoordinatorSource, Does.Contain("AppServerProcessLifecycle.BuildStartInfo"));
            Assert.That(source, Does.Contain("AppServerProcessLifecycle.CleanupResources"));
            Assert.That(
                source,
                Does.Not.Contain("private readonly Dictionary<string, TaskCompletionSource<IDictionary<string, object>>> _pendingRequests"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DoesNotOwnJsonRpcPendingRequestDictionary()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var transportSource = RepositorySource.ReadSourceFile("AppServerProtocolTransport.cs");

            Assert.That(clientSource, Does.Not.Contain("Dictionary<string, TaskCompletionSource<IDictionary<string, object>>>"));
            Assert.That(clientSource, Does.Not.Contain("_pendingRequests"));
            Assert.That(clientSource, Does.Contain("private readonly AppServerProtocolTransport _protocolTransport"));
            Assert.That(clientSource, Does.Contain("_protocolTransport.SendRequestAsync("));
            Assert.That(clientSource, Does.Contain("_protocolTransport.TryHandleResponse("));
            Assert.That(clientSource, Does.Contain("_protocolTransport.DrainPendingRequests()"));
            Assert.That(clientSource, Does.Contain("private readonly AppServerPendingRequestRegistry _pendingRequestRegistry = new AppServerPendingRequestRegistry();"));

            Assert.That(transportSource, Does.Contain("Dictionary<string, TaskCompletionSource<IDictionary<string, object>>> _pendingRequests"));
            Assert.That(transportSource, Does.Contain("public IList<TaskCompletionSource<IDictionary<string, object>>> DrainPendingRequests()"));
            Assert.That(transportSource, Does.Contain("public bool TryHandleResponse(IDictionary<string, object> message, out string errorMessage)"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesDiagnosticsWithoutAuthenticationMaterialization()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var operationsSource = RepositorySource.ReadSourceFile("AppServerClientOperations.cs");
            var startupCoordinatorSource = RepositorySource.ReadSourceFile("AppServerRuntimeStartupCoordinator.cs");

            Assert.That(source, Does.Contain("AppServerEventDiagnostics."));
            Assert.That(source, Does.Contain("AppServerEventDiagnostics.TraceNotification("));
            Assert.That(source, Does.Contain("AppServerEventDiagnostics.TraceMappedEvent("));
            Assert.That(operationsSource, Does.Contain("AppServerEventDiagnostics.TraceConfigWrite("));
            Assert.That(startupCoordinatorSource, Does.Not.Contain("AuthenticationMaterialization"));
            Assert.That(startupCoordinatorSource, Does.Not.Contain("Provider"));
            Assert.That(source, Does.Not.Contain("private static void TraceAppServerNotificationDiagnostic("));
            Assert.That(source, Does.Not.Contain("private static bool ShouldTraceAppServerNotification("));
            Assert.That(source, Does.Not.Contain("private static string BuildAppServerEventDiagnosticSummary("));
            Assert.That(source, Does.Not.Contain("private static bool ShouldWriteMaterializationToCurrentRuntime("));
            Assert.That(source, Does.Not.Contain("private static string ResolveModelConversionMaterializationBaseUrl("));
            Assert.That(source, Does.Not.Contain("private static string BuildAuthenticationMaterializationConnectionKey("));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void AppServerClient_DoesNotMaterializeRuntimeConfig()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var startupCoordinatorSource = RepositorySource.ReadSourceFile("AppServerRuntimeStartupCoordinator.cs");
            Assert.That(source, Does.Not.Contain("AppServerRuntimeConfigMaterializer"));
            Assert.That(startupCoordinatorSource, Does.Not.Contain("Materialization"));
            Assert.That(startupCoordinatorSource, Does.Not.Contain("Provider"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesRuntimeStartupCoordination()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var coordinatorSource = RepositorySource.ReadSourceFile("AppServerRuntimeStartupCoordinator.cs");

            Assert.That(clientSource, Does.Contain("private readonly AppServerRuntimeStartupCoordinator _runtimeStartupCoordinator"));
            Assert.That(coordinatorSource, Does.Contain("internal sealed class AppServerRuntimeStartupCoordinator"));
            Assert.That(coordinatorSource, Does.Contain("EnsureWslNetworkReadyAsync("));
            Assert.That(coordinatorSource, Does.Contain("public AppServerRuntimeProcessStartResult StartProcess("));
            Assert.That(coordinatorSource, Does.Contain("public static string BuildRuntimeConnectionKey("));
            Assert.That(coordinatorSource, Does.Not.Contain("AuthenticationEnvironment"));
            Assert.That(coordinatorSource, Does.Not.Contain("SourceMaterialization"));
            Assert.That(coordinatorSource, Does.Not.Contain("AuthenticationSharedSkills"));
            Assert.That(clientSource, Does.Contain("return AppServerRuntimeStartupCoordinator.BuildRuntimeConnectionKey("));
            Assert.That(clientSource, Does.Contain("_runtimeStartupCoordinator.EnsureWslNetworkReadyAsync("));
            Assert.That(clientSource, Does.Contain("_runtimeStartupCoordinator.StartProcess("));
            Assert.That(clientSource, Does.Not.Contain("private async Task EnsureWslNetworkReadyAsync("));
            Assert.That(clientSource, Does.Not.Contain("private void StartProcess(AppConfigSnapshot appConfig, AppServerLaunchInfo launchInfo)"));
            Assert.That(clientSource, Does.Not.Contain("private void BuildCurrentSourceMaterialization("));
            Assert.That(clientSource, Does.Not.Contain("private async Task<bool> TrySynchronizeCurrentAuthenticationSharedSkillsAsync("));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void WindowsStartup_UsesOnlyTheTargetEnvironmentAndOfficialCodexState()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var runtimeManagerSource = RepositorySource.ReadSourceFile("CodexRuntimeManager.cs");
            var connectCoreStart = source.IndexOf("private async Task ConnectCoreAsync()", StringComparison.Ordinal);
            var restartStart = source.IndexOf("public async Task RestartAsync()", StringComparison.Ordinal);
            var createThreadStart = source.IndexOf("public async Task<string> CreateThreadAsync", StringComparison.Ordinal);

            Assert.That(connectCoreStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(restartStart, Is.GreaterThan(connectCoreStart));
            Assert.That(createThreadStart, Is.GreaterThan(restartStart));

            var connectCore = source.Substring(connectCoreStart, restartStart - connectCoreStart);
            var launchInputEnd = connectCore.IndexOf("var runtimeKey =", StringComparison.Ordinal);
            Assert.That(launchInputEnd, Is.GreaterThanOrEqualTo(0));
            var launchInputs = connectCore.Substring(0, launchInputEnd);
            Assert.That(launchInputs, Does.Contain("_runtimeManager.ResolveLaunchInfo(appConfig)"));
            Assert.That(launchInputs, Does.Not.Contain("BuildAuthenticationEnvironmentVariablesAsync"));
            Assert.That(launchInputs, Does.Not.Contain("_currentAuthenticationModelCatalogJsonPath"));
            Assert.That(launchInputs, Does.Not.Contain("_lastAuthenticationMaterializationConnectionKey"));
            Assert.That(launchInputs, Does.Not.Contain("Profile"));
            Assert.That(launchInputs, Does.Not.Contain("Provider"));
            Assert.That(launchInputs, Does.Not.Contain("Relay"));
            Assert.That(launchInputs, Does.Not.Contain("Tunnel"));
            Assert.That(launchInputs, Does.Not.Contain("Credential"));
            Assert.That(launchInputs, Does.Not.Contain("CODEX_HOME"));
            Assert.That(launchInputs, Does.Not.Contain("SharedWindowsHome"));
            Assert.That(source, Does.Not.Contain("GetSharedCodexHomeDirectory"));
            Assert.That(source, Does.Not.Contain("GetAuthenticationProfileCodexHomeDirectory"));
            var restart = source.Substring(restartStart, createThreadStart - restartStart);
            Assert.That(restart, Does.Contain("await ConnectCoreAsync().ConfigureAwait(false);"));
            var buildLaunchInfoStart = runtimeManagerSource.IndexOf("private AppServerLaunchInfo BuildLaunchInfo(", StringComparison.Ordinal);
            var applyEnvironmentStart = runtimeManagerSource.IndexOf("private static void ApplyEnvironment(", buildLaunchInfoStart, StringComparison.Ordinal);

            Assert.That(buildLaunchInfoStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(applyEnvironmentStart, Is.GreaterThan(buildLaunchInfoStart));

            var windowsLaunchInfo = runtimeManagerSource.Substring(buildLaunchInfoStart, applyEnvironmentStart - buildLaunchInfoStart);
            Assert.That(windowsLaunchInfo, Does.Not.Contain("ApplyLocalWindowsHomeEnvironment"));
            Assert.That(windowsLaunchInfo, Does.Not.Contain("environmentVariables[\"HOME\"]"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void WslAndRemoteStartup_UseOnlyTheirTargetEnvironmentAndOfficialCodexState()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var runtimeManagerSource = RepositorySource.ReadSourceFile("CodexRuntimeManager.cs");
            var connectCoreStart = clientSource.IndexOf("private async Task ConnectCoreAsync()", StringComparison.Ordinal);
            var restartStart = clientSource.IndexOf("public async Task RestartAsync()", connectCoreStart, StringComparison.Ordinal);
            var connectCore = clientSource.Substring(connectCoreStart, restartStart - connectCoreStart);

            Assert.That(connectCore, Does.Not.Contain("AuthenticationMaterialization"));
            Assert.That(connectCore, Does.Contain("_runtimeManager.ResolveLaunchInfo(appConfig)"));
            Assert.That(connectCore, Does.Not.Contain("BuildAuthenticationEnvironmentVariablesAsync"));
            Assert.That(connectCore, Does.Not.Contain("_currentAuthenticationModelCatalogJsonPath"));
            Assert.That(runtimeManagerSource, Does.Not.Contain("ApplyWslCodexHomeEnvironment"));
            Assert.That(runtimeManagerSource, Does.Not.Contain("MYCODEX_WSL_CODEX_HOME"));
            Assert.That(runtimeManagerSource, Does.Not.Contain("MYCODEX_WSL_CODEX_HOME_STRATEGY"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AutomaticReconnect_OnlyRecoversTransportAndDoesNotRequestTargetReload()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var connectionLostStart = clientSource.IndexOf("private void HandleAppServerConnectionLost(", StringComparison.Ordinal);
            var timeoutFactoryStart = clientSource.IndexOf("private static TimeoutException CreateRequestTimeoutException", StringComparison.Ordinal);

            Assert.That(connectionLostStart, Is.GreaterThanOrEqualTo(0));
            Assert.That(timeoutFactoryStart, Is.GreaterThan(connectionLostStart));

            var connectionLost = clientSource.Substring(connectionLostStart, timeoutFactoryStart - connectionLostStart);
            Assert.That(connectionLost, Does.Not.Contain("RestartAsync"));
            Assert.That(connectionLost, Does.Not.Contain("ConnectCoreAsync"));
            Assert.That(connectionLost, Does.Not.Contain("BuildAuthenticationEnvironmentVariablesAsync"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_BackgroundReaders_AreBoundToCapturedConnectionStreams()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");

            Assert.That(source, Does.Contain("standardOutput = process.StandardOutput;"));
            Assert.That(source, Does.Contain("standardError = process.StandardError;"));
            Assert.That(source, Does.Contain("ReadStdOutLoopAsync(standardOutput, cancellationToken, connectionEpoch, process)"));
            Assert.That(source, Does.Contain("ReadStdErrLoopAsync(standardError, cancellationToken, connectionEpoch, process)"));
            Assert.That(source, Does.Not.Contain("_process.StandardOutput.ReadLineAsync()"));
            Assert.That(source, Does.Not.Contain("_process.StandardError.ReadLineAsync()"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_PersistentDiagnostics_DoNotLogRawProtocolOutput()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");

            Assert.That(source, Does.Contain("App Server stderr received. Characters={0}"));
            Assert.That(source, Does.Contain("Failed to parse App Server message. ErrorType={0}; Characters={1}"));
            Assert.That(source, Does.Not.Contain("App Server stderr: {0}"));
            Assert.That(source, Does.Not.Contain("Raw message: {1}"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_ConnectionReplacementAndLossClaim_UseTheSameLockOrder()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");

            Assert.That(source, Does.Contain("private void HandleAppServerConnectionLost("));
            Assert.That(source, Does.Contain("long expectedConnectionEpoch,"));
            Assert.That(source, Does.Contain("Process expectedProcess)"));
            Assert.That(source, Does.Contain("if (GetConnectionEpoch() != expectedConnectionEpoch"));
            Assert.That(source, Does.Contain("!ReferenceEquals(_process, expectedProcess)"));
            Assert.That(source, Does.Contain("resources = TakeProcessResourcesUnsafe();"));
            Assert.That(source, Does.Contain("AppServerProcessLifecycle.CleanupResources("));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_TransportWriteFailures_UseTheTransportSourceEpoch()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var transportSource = RepositorySource.ReadSourceFile("AppServerProtocolTransport.cs");

            Assert.That(clientSource, Does.Contain("HandleAppServerConnectionLost(disconnectMessage, ex.ConnectionEpoch, null);"));
            Assert.That(transportSource, Does.Contain("public long ConnectionEpoch { get; }"));
            Assert.That(transportSource, Does.Contain("ex, expectedConnectionEpoch)"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesNotificationClassification()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var processorSource = RepositorySource.ReadSourceFile("AppServerNotificationEventProcessor.cs");

            Assert.That(processorSource, Does.Contain("AppServerNotificationDispatcher."));
            Assert.That(processorSource, Does.Contain("AppServerNotificationDispatcher.Classify("));
            Assert.That(clientSource, Does.Not.Contain("AppServerNotificationDispatcher.Classify("));
            Assert.That(clientSource, Does.Not.Contain("private static bool IsLowNoiseCompatibilityNotification(string method)"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesNotificationEventProcessing()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var processorSource = RepositorySource.ReadSourceFile("AppServerNotificationEventProcessor.cs");
            var contextSource = RepositorySource.ReadSourceFile("AppServerNotificationEventContext.cs");

            Assert.That(clientSource, Does.Contain("private readonly AppServerNotificationEventProcessor _notificationEventProcessor"));
            Assert.That(clientSource, Does.Contain("_notificationEventProcessor.HandleNotification("));
            Assert.That(processorSource, Does.Contain("internal sealed class AppServerNotificationEventProcessor"));
            Assert.That(processorSource, Does.Contain("public void HandleNotification(string method, IDictionary<string, object> parameters)"));
            Assert.That(processorSource, Does.Contain("private void HandleItemCompleted("));
            Assert.That(clientSource, Does.Not.Contain("private void HandleItemCompleted("));
            Assert.That(clientSource, Does.Not.Contain("private void HandleTurnCompleted("));
            Assert.That(contextSource, Does.Contain("public Action<string> ForgetThreadState"));
            Assert.That(contextSource, Does.Contain("public Action<string> ClearResolvedInteractionRequest"));
            Assert.That(contextSource, Does.Not.Contain("Action<IDictionary<string, object>>"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_DelegatesProtocolTextAndMetadataHelpers()
        {
            var clientSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var textSource = RepositorySource.ReadSourceFile("AppServerProtocolTextComposer.cs");
            var metadataSource = RepositorySource.ReadSourceFile("AppServerProtocolMetadataMapper.cs");

            Assert.That(textSource, Does.Contain("internal static class AppServerProtocolTextComposer"));
            Assert.That(textSource, Does.Contain("internal static string BuildFileChangeSummaryText("));
            Assert.That(textSource, Does.Contain("internal static string StripAnsiEscapeSequences("));
            Assert.That(metadataSource, Does.Contain("internal static class AppServerProtocolMetadataMapper"));
            Assert.That(metadataSource, Does.Contain("internal static AppServerProtocolCapabilities BuildProtocolCapabilities("));
            Assert.That(clientSource, Does.Not.Contain("private static string BuildFileChangeSummaryText("));
            Assert.That(clientSource, Does.Not.Contain("private static string StripAnsiEscapeSequences("));
            Assert.That(clientSource, Does.Not.Contain("private static Version ParseRuntimeVersion("));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void SkillAndPluginPathDiagnostics_AreEmittedAtRuntimeBoundaries()
        {
            var appServerSource = RepositorySource.ReadSourceFile("AppServerClient.cs");
            var operationsSource = RepositorySource.ReadSourceFile("AppServerClientOperations.cs");
            Assert.That(appServerSource, Does.Contain("TraceSkillPluginInputPathDiagnostic(\"mention\""));
            Assert.That(appServerSource, Does.Contain("TraceSkillPluginInputPathDiagnostic(\"skill\""));
            Assert.That(operationsSource, Does.Contain("_traceSkillsListDiagnostics(workspacePaths, forceReload, snapshots)"));
            Assert.That(operationsSource, Does.Contain("_tracePluginListDiagnostics(plugins)"));
            Assert.That(operationsSource, Does.Not.Contain("SynchronizeCurrentAuthenticationSharedSkills"));
            Assert.That(operationsSource, Does.Not.Contain("RepairLocalPluginMarketManifests"));
            Assert.That(appServerSource, Does.Contain("Skill/plugin diagnostic. Operation=turn/input"));
            Assert.That(appServerSource, Does.Contain("Skill/plugin diagnostic. Operation=skills/list"));
            Assert.That(appServerSource, Does.Contain("Skill/plugin diagnostic. Operation=plugin/list"));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_ResolvesSkillListPathsBeforeDiagnostics()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");

            Assert.That(source, Does.Contain("ResolveCurrentPluginSkillPath"));
            Assert.That(source, Does.Contain("Skill/plugin diagnostic. Operation=skill-path/resolve"));
            Assert.That(source, Does.Contain("PathResolution="));
        }

        [Category(SourceGuardCategory.Placement)]
        [Test]
        public void AppServerClient_RefreshesSkillInventoryBeforeTurnStart()
        {
            var source = RepositorySource.ReadSourceFile("AppServerClient.cs");

            Assert.That(source, Does.Contain("RefreshSkillInventoryBeforeTurnStartAsync"));
            Assert.That(source, Does.Contain("await RefreshSkillInventoryBeforeTurnStartAsync(workspacePath).ConfigureAwait(false);"));
            Assert.That(source, Does.Contain("Skill/plugin diagnostic. Operation=skill-inventory/refresh"));
        }
    }
}
