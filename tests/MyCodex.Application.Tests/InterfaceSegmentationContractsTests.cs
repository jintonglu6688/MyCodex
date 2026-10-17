using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class InterfaceSegmentationContractsTests
    {
        [Test]
        public void GitRepositoryImplementations_ExposeFocusedCapabilityInterfaces()
        {
            var local = new GitRepositoryService(new MyCodex.Infrastructure.Git.GitCommandRunner());
            var wsl = UnixGitRepositoryServiceFactory.CreateForWsl(new MyCodex.Domain.Entities.WslAppServerConfigSnapshot());
            var remoteMac = UnixGitRepositoryServiceFactory.CreateForRemoteMac(new MyCodex.Domain.Entities.RemoteMacSshConfigSnapshot());

            Assert.That(local, Is.InstanceOf<IGitStatusService>());
            Assert.That(local, Is.InstanceOf<IGitDiffReader>());
            Assert.That(local, Is.InstanceOf<IGitCommitHistoryService>());
            Assert.That(local, Is.InstanceOf<IGitBranchService>());
            Assert.That(local, Is.InstanceOf<IGitRemoteService>());
            Assert.That(local, Is.InstanceOf<IGitStashService>());
            Assert.That(local, Is.InstanceOf<IGitWorktreeService>());
            Assert.That(local, Is.InstanceOf<IGitRepositoryMaintenanceService>());

            Assert.That(wsl, Is.InstanceOf<IGitStatusService>());
            Assert.That(wsl, Is.InstanceOf<IGitDiffReader>());
            Assert.That(wsl, Is.InstanceOf<IGitCommitHistoryService>());
            Assert.That(wsl, Is.InstanceOf<IGitBranchService>());
            Assert.That(wsl, Is.InstanceOf<IGitRemoteService>());
            Assert.That(wsl, Is.InstanceOf<IGitStashService>());
            Assert.That(wsl, Is.InstanceOf<IGitWorktreeService>());
            Assert.That(wsl, Is.InstanceOf<IGitRepositoryMaintenanceService>());

            Assert.That(remoteMac, Is.InstanceOf<IGitStatusService>());
            Assert.That(remoteMac, Is.InstanceOf<IGitDiffReader>());
            Assert.That(remoteMac, Is.InstanceOf<IGitCommitHistoryService>());
            Assert.That(remoteMac, Is.InstanceOf<IGitBranchService>());
            Assert.That(remoteMac, Is.InstanceOf<IGitRemoteService>());
            Assert.That(remoteMac, Is.InstanceOf<IGitStashService>());
            Assert.That(remoteMac, Is.InstanceOf<IGitWorktreeService>());
            Assert.That(remoteMac, Is.InstanceOf<IGitRepositoryMaintenanceService>());
        }

        [Test]
        public void AppServerClientInterface_ComposesFocusedCapabilityInterfaces()
        {
            Assert.That(typeof(IAppServerConnectionClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerThreadClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerAuthenticationClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerConfigClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerExtensionClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerMcpClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerSkillClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerPluginClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerInteractionClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            Assert.That(typeof(IAppServerCommandClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
        }

        [Test]
        public void AppServerFocusedInterfaces_ExposeCancellationAwareReadOperations()
        {
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.ListThreadsAsync),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.ListLoadedThreadIdsAsync),
                typeof(int?),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.ReadThreadAsync),
                typeof(string),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.DeleteThreadAsync),
                typeof(string));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.GetConversationSummaryAsync),
                typeof(MyCodex.Application.DTOs.AppServerConversationSummaryRequest),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.TrySteerTurnAsync),
                typeof(string),
                typeof(IReadOnlyCollection<MyCodex.Application.DTOs.AppServerUserInputItem>),
                typeof(string));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.TrySteerTurnAsync),
                typeof(string),
                typeof(IReadOnlyCollection<MyCodex.Application.DTOs.AppServerUserInputItem>),
                typeof(string),
                typeof(string));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.InjectThreadItemsAsync),
                typeof(string),
                typeof(IReadOnlyCollection<IDictionary<string, object>>),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerThreadClient>(
                nameof(IAppServerThreadClient.ApproveGuardianDeniedActionAsync),
                typeof(AppServerGuardianDeniedActionApprovalRequest),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerAuthenticationClient>(
                nameof(IAppServerAuthenticationClient.CancelLoginAsync),
                typeof(string));
            AssertHasMethod<IAppServerAuthenticationClient>(
                nameof(IAppServerAuthenticationClient.StartChatGptDeviceCodeLoginAsync));
            AssertHasMethod<IAppServerAuthenticationClient>(
                nameof(IAppServerAuthenticationClient.GetAccountTokenUsageAsync),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerAuthenticationClient>(
                nameof(IAppServerAuthenticationClient.ConsumeRateLimitResetCreditAsync),
                typeof(string),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerConfigClient>(
                nameof(IAppServerConfigClient.ReadConfigAsync),
                typeof(string),
                typeof(bool),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerConfigClient>(
                nameof(IAppServerConfigClient.ListModelsAsync),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerConfigClient>(
                nameof(IAppServerConfigClient.ListPermissionProfilesAsync),
                typeof(string),
                typeof(int?),
                typeof(string),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerCommandClient>(
                nameof(IAppServerCommandClient.ResizeCommandAsync),
                typeof(string),
                typeof(AppServerCommandTerminalSize),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerPluginClient>(
                nameof(IAppServerPluginClient.ListPluginsAsync),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerMcpClient>(
                nameof(IAppServerMcpClient.ReadMcpResourceAsync),
                typeof(string),
                typeof(string),
                typeof(string),
                typeof(CancellationToken));
            AssertHasMethod<IAppServerMcpClient>(
                nameof(IAppServerMcpClient.CallMcpToolAsync),
                typeof(AppServerMcpToolCallRequest),
                typeof(CancellationToken));
            Assert.That(typeof(IAppServerGitDiffClient).IsAssignableFrom(typeof(IAppServerClient)), Is.True);
            AssertHasMethod<IAppServerGitDiffClient>(
                nameof(IAppServerGitDiffClient.GetGitDiffToRemoteAsync),
                typeof(string),
                typeof(CancellationToken));
        }

        [Test]
        public void AppServerExtensionClient_IsCompatibilityAggregationOnly()
        {
            Assert.That(typeof(IAppServerMcpClient).IsAssignableFrom(typeof(IAppServerExtensionClient)), Is.True);
            Assert.That(typeof(IAppServerSkillClient).IsAssignableFrom(typeof(IAppServerExtensionClient)), Is.True);
            Assert.That(typeof(IAppServerPluginClient).IsAssignableFrom(typeof(IAppServerExtensionClient)), Is.True);
            Assert.That(
                typeof(IAppServerExtensionClient).GetMethods(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly),
                Is.Empty);
        }

        [Test]
        public void AppServerSinglePurposeConsumers_StoreFocusedCapabilities()
        {
            AssertFieldType<McpDiagnosticsForm>("_appServerClient", typeof(IAppServerMcpClient));
            AssertFieldType<McpToolTestForm>("_appServerClient", typeof(IAppServerMcpClient));
            AssertFieldType<PluginDetailsForm>("_appServerClient", typeof(IAppServerPluginClient));

            AssertFieldType<McpManagementForm>("_connectionClient", typeof(IAppServerConnectionClient));
            AssertFieldType<McpManagementForm>("_configClient", typeof(IAppServerConfigClient));
            AssertFieldType<McpManagementForm>("_mcpClient", typeof(IAppServerMcpClient));

            AssertFieldType<SkillsManagementForm>("_connectionClient", typeof(IAppServerConnectionClient));
            AssertFieldType<SkillsManagementForm>("_skillClient", typeof(IAppServerSkillClient));
            AssertFieldType<SkillsManagementForm>("_pluginClient", typeof(IAppServerPluginClient));

            AssertFieldType<ArchivedThreadRestoreForm>("_connectionClient", typeof(IAppServerConnectionClient));
            AssertFieldType<ArchivedThreadRestoreForm>("_threadClient", typeof(IAppServerThreadClient));
            AssertFieldType<ThreadImportForm>("_connectionClient", typeof(IAppServerConnectionClient));
            AssertFieldType<ThreadImportForm>("_threadClient", typeof(IAppServerThreadClient));

            AssertFieldType<AppServerCodingRemoteAttachmentCatalogOperator>(
                "_pluginClient",
                typeof(IAppServerPluginClient));
            AssertFieldType<AppServerCodingRemoteAttachmentCatalogOperator>(
                "_skillClient",
                typeof(IAppServerSkillClient));
        }

        [Test]
        public void GitForms_ConstructorsUseFocusedRepositoryInterfaces()
        {
            Assert.That(
                typeof(GitRemoteManagementForm).GetConstructor(new[] { typeof(string), typeof(IGitRemoteService) }),
                Is.Not.Null);
            Assert.That(
                typeof(GitRepositoryDiagnosticsForm).GetConstructor(new[] { typeof(string), typeof(IGitRepositoryMaintenanceService) }),
                Is.Not.Null);
            Assert.That(
                typeof(GitConflictDetailsForm).GetConstructor(new[] { typeof(string), typeof(IGitDiffReader), typeof(MyCodex.Application.DTOs.GitChangedFileEntry) }),
                Is.Not.Null);
            Assert.That(
                typeof(GitStashManagementForm).GetConstructor(new[] { typeof(string), typeof(IGitStashService), typeof(IScreenReaderService) }),
                Is.Not.Null);
        }

        private static void AssertHasMethod<T>(string methodName, params System.Type[] parameterTypes)
        {
            Assert.That(typeof(T).GetMethod(methodName, parameterTypes), Is.Not.Null);
        }

        private static void AssertFieldType<T>(string fieldName, System.Type expectedType)
        {
            var field = typeof(T).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, typeof(T).Name + "." + fieldName);
            Assert.That(field.FieldType, Is.EqualTo(expectedType), typeof(T).Name + "." + fieldName);
        }
    }
}
