using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerClientOperationsRequestTests
    {
        [Test]
        public async Task DetectExternalAgentConfigAsync_SendsMappedRequestWithCallerCancellation()
        {
            string requestedMethod = null;
            object requestedParameters = null;
            string mappedPathName = null;
            CancellationToken requestedCancellationToken = default(CancellationToken);
            var requestedTimeoutMs = 0;
            using (var cancellationTokenSource = new CancellationTokenSource())
            {
                var operations = CreateOperations(
                    (method, parameters, cancellationToken, timeoutMs) =>
                    {
                        requestedMethod = method;
                        requestedParameters = parameters;
                        requestedCancellationToken = cancellationToken;
                        requestedTimeoutMs = timeoutMs;
                        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
                    },
                    (path, name) =>
                    {
                        mappedPathName = name;
                        return "server:" + path;
                    });

                await operations.DetectExternalAgentConfigAsync(
                    new[] { @"E:\Repo", @"E:\Repo", " " },
                    true,
                    cancellationTokenSource.Token);

                var parameters = requestedParameters as IDictionary<string, object>;
                Assert.That(requestedMethod, Is.EqualTo("externalAgentConfig/detect"));
                Assert.That(parameters, Is.Not.Null);
                Assert.That(parameters["includeHome"], Is.EqualTo(true));
                Assert.That((string[])parameters["cwds"], Is.EqualTo(new[] { @"server:E:\Repo" }));
                Assert.That(mappedPathName, Is.EqualTo("workspace"));
                Assert.That(requestedCancellationToken, Is.EqualTo(cancellationTokenSource.Token));
                Assert.That(requestedTimeoutMs, Is.EqualTo(60000));
            }
        }

        [Test]
        public async Task StartWindowsSandboxSetupAsync_SendsMappedLongRunningRequest()
        {
            string requestedMethod = null;
            object requestedParameters = null;
            string mappedPathName = null;
            CancellationToken requestedCancellationToken = default(CancellationToken);
            var requestedTimeoutMs = 0;
            var operations = CreateOperations(
                (method, parameters, cancellationToken, timeoutMs) =>
                {
                    requestedMethod = method;
                    requestedParameters = parameters;
                    requestedCancellationToken = cancellationToken;
                    requestedTimeoutMs = timeoutMs;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "started", true }
                    });
                },
                (path, name) =>
                {
                    mappedPathName = name;
                    return "server:" + path;
                });

            var result = await operations.StartWindowsSandboxSetupAsync(
                AppServerWindowsSandboxSetupMode.Elevated,
                @"E:\Repo");

            var parameters = requestedParameters as IDictionary<string, object>;
            Assert.That(requestedMethod, Is.EqualTo("windowsSandbox/setupStart"));
            Assert.That(parameters, Is.Not.Null);
            Assert.That(parameters["mode"], Is.EqualTo("elevated"));
            Assert.That(parameters["cwd"], Is.EqualTo(@"server:E:\Repo"));
            Assert.That(mappedPathName, Is.EqualTo("workspace"));
            Assert.That(requestedCancellationToken, Is.EqualTo(CancellationToken.None));
            Assert.That(requestedTimeoutMs, Is.EqualTo(300000));
            Assert.That(result.Started, Is.True);
        }

        [Test]
        public async Task ListHooksAsync_SendsMappedCwdAndMapsHookMetadata()
        {
            object requestedParameters = null;
            var operations = CreateOperations(
                (method, parameters, cancellationToken, timeoutMs) =>
                {
                    Assert.That(method, Is.EqualTo("hooks/list"));
                    requestedParameters = parameters;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        {
                            "data", new object[]
                            {
                                new Dictionary<string, object>
                                {
                                    { "cwd", "/repo" },
                                    { "warnings", new object[] { "bad matcher" } },
                                    { "errors", new object[] { new Dictionary<string, object> { { "path", "/repo/hooks.json" }, { "message", "parse" } } } },
                                    {
                                        "hooks", new object[]
                                        {
                                            new Dictionary<string, object>
                                            {
                                                { "key", "k" }, { "eventName", "preToolUse" }, { "handlerType", "mcpTool" },
                                                { "server", "s" }, { "tool", "t" }, { "sourcePath", "/repo/hooks.json" },
                                                { "enabled", true }, { "isManaged", false }, { "timeoutSec", 30 },
                                                { "currentHash", "sha256:x" }, { "trustStatus", "modified" }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    });
                },
                (path, name) => "/repo");

            var result = await operations.ListHooksAsync(@"E:\Repo", null);

            Assert.That((string[])((IDictionary<string, object>)requestedParameters)["cwds"], Is.EqualTo(new[] { "/repo" }));
            var hook = result.Hooks.Single();
            Assert.That(hook.McpServer + "/" + hook.McpTool, Is.EqualTo("s/t"));
            Assert.That(hook.TimeoutSec, Is.EqualTo(30));
            Assert.That(hook.TrustStatus, Is.EqualTo("modified"));
            Assert.That(result.Warnings, Is.EqualTo(new[] { "bad matcher" }));
            Assert.That(result.Errors, Is.EqualTo(new[] { "/repo/hooks.json: parse" }));
        }

        [Test]
        public async Task WriteHookStateAsync_SendsVersionedReloadingBatchAndReadsStatus()
        {
            IDictionary<string, object> requestedParameters = null;
            var operations = CreateOperations(
                (method, parameters, cancellationToken, timeoutMs) =>
                {
                    Assert.That(method, Is.EqualTo("config/batchWrite"));
                    requestedParameters = (IDictionary<string, object>)parameters;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "status", "okOverridden" }, { "version", "v2" }
                    });
                },
                (path, name) => path);

            var result = await operations.WriteHookStateAsync(
                new[] { AppServerConfigWriteEdit.CreateReplace("hooks.state.\"k\".trusted_hash", null) },
                "v1");

            Assert.That(requestedParameters["expectedVersion"], Is.EqualTo("v1"));
            Assert.That(requestedParameters["reloadUserConfig"], Is.EqualTo(true));
            Assert.That(result.IsOverridden, Is.True);
            Assert.That(result.Version, Is.EqualTo("v2"));
        }

        private static AppServerClientOperations CreateOperations(
            AppServerRequestInvoker sendRequestAsync,
            Func<string, string, string> mapRequiredClientPathToServer)
        {
            return new AppServerClientOperations(
                () => { },
                () => null,
                sendRequestAsync,
                mapRequiredClientPathToServer,
                path => path,
                path => path,
                (source, skillPath, skillId) => skillPath,
                (workspaceRoots, forceReload, snapshots) => { },
                plugins => { });
        }
    }
}
