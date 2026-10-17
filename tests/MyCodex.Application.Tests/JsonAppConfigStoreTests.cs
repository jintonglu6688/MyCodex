using System;
using System.IO;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Constants;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class JsonAppConfigStoreTests
    {
        [TestCase(SessionListDisplayOrder.AgentsTimeTitle, SessionListDisplayOrder.AgentsTimeTitle)]
        [TestCase(SessionListDisplayOrder.TitleTimeAgents, SessionListDisplayOrder.TitleTimeAgents)]
        [TestCase(SessionListDisplayOrder.TitleAgentsTime, SessionListDisplayOrder.TitleAgentsTime)]
        [TestCase(" TITLE-TIME-AGENTS ", SessionListDisplayOrder.TitleTimeAgents)]
        [TestCase("unknown", SessionListDisplayOrder.AgentsTimeTitle)]
        [TestCase("", SessionListDisplayOrder.AgentsTimeTitle)]
        [TestCase(null, SessionListDisplayOrder.AgentsTimeTitle)]
        public void SaveAndLoad_NormalizesSessionListDisplayOrder(string order, string expected)
        {
            var rootPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "JsonAppConfigStoreTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new JsonAppConfigStore(new AppConfigService(rootPath));
                store.Save(new AppConfigSnapshot { SessionListDisplayOrder = order });
                Assert.That(store.Load().SessionListDisplayOrder, Is.EqualTo(expected));
            }
            finally
            {
                if (Directory.Exists(rootPath)) Directory.Delete(rootPath, true);
            }
        }

        [Test]
        public void Load_LegacyConfigDefaultsOrderAndTargetsKeepSeparatePreferences()
        {
            var rootPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "JsonAppConfigStoreTests-" + Guid.NewGuid().ToString("N"));
            try
            {
                var localConfig = new AppConfigService(Path.Combine(rootPath, "windows"));
                localConfig.EnsureConfigDirectory();
                File.WriteAllText(localConfig.GetConfigFilePath(), "{}");
                var localStore = new JsonAppConfigStore(localConfig);
                var remoteStore = new JsonAppConfigStore(new AppConfigService(Path.Combine(rootPath, "remote")));
                Assert.That(localStore.Load().SessionListDisplayOrder, Is.EqualTo(SessionListDisplayOrder.AgentsTimeTitle));

                localStore.Save(new AppConfigSnapshot { SessionListDisplayOrder = SessionListDisplayOrder.TitleTimeAgents });
                remoteStore.Save(new AppConfigSnapshot { SessionListDisplayOrder = SessionListDisplayOrder.TitleAgentsTime });
                Assert.That(localStore.Load().SessionListDisplayOrder, Is.EqualTo(SessionListDisplayOrder.TitleTimeAgents));
                Assert.That(remoteStore.Load().SessionListDisplayOrder, Is.EqualTo(SessionListDisplayOrder.TitleAgentsTime));
            }
            finally
            {
                if (Directory.Exists(rootPath)) Directory.Delete(rootPath, true);
            }
        }

        [TestCase(CodexRuntimeSources.Bundled, CodexRuntimeSources.Bundled)]
        [TestCase("sYsTeM", CodexRuntimeSources.System)]
        [TestCase("wSl", CodexRuntimeSources.Wsl)]
        [TestCase(CodexRuntimeSources.RemoteMacSsh, CodexRuntimeSources.RemoteMacSsh)]
        [TestCase("rEmOtEmAcSsH", CodexRuntimeSources.RemoteMacSsh)]
        [TestCase(CodexRuntimeSources.RemoteUnixSsh, CodexRuntimeSources.RemoteUnixSsh)]
        [TestCase("rEmOtEuNiXsSh", CodexRuntimeSources.RemoteUnixSsh)]
        [TestCase("unknown", CodexRuntimeSources.Bundled)]
        public void SaveAndLoad_CanonicalizesRuntimeSourceForPersistence(string source, string expected)
        {
            var rootPath = Path.Combine(
                TestContext.CurrentContext.WorkDirectory,
                "JsonAppConfigStoreTests-" + Guid.NewGuid().ToString("N"));

            try
            {
                var store = new JsonAppConfigStore(new AppConfigService(rootPath));

                store.Save(new AppConfigSnapshot { CodexRuntimeSource = source });

                Assert.That(store.Load().CodexRuntimeSource, Is.EqualTo(expected));
            }
            finally
            {
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
            }
        }
    }
}
