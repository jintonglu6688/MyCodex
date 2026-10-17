using System;
using System.Collections.Generic;
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
