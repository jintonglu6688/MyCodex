using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerAuthenticationOperationsTests
    {
        [Test]
        public async Task AuthenticationState_UsesOnlyOfficialAccountReadObservation()
        {
            var requestedMethods = new List<string>();
            object requestedParameters = null;
            var operations = new AppServerClientOperations(
                () => { },
                () => null,
                (method, parameters, cancellationToken, timeoutMs) =>
                {
                    requestedMethods.Add(method);
                    requestedParameters = parameters;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        {
                            "account",
                            new Dictionary<string, object>
                            {
                                { "type", "chatgpt" },
                                { "email", "reader@example.test" },
                                { "planType", "plus" }
                            }
                        },
                        { "requiresOpenaiAuth", true }
                    });
                },
                (path, name) => path,
                path => path,
                path => path,
                (source, skillPath, skillId) => skillPath,
                (workspaceRoots, forceReload, snapshots) => { },
                plugins => { });

            var result = await operations.GetAuthenticationStateAsync(true);

            Assert.That(requestedMethods, Is.EqualTo(new[] { "account/read" }));
            var parameters = requestedParameters as IDictionary<string, object>;
            Assert.That(parameters, Is.Not.Null);
            Assert.That(parameters["refreshToken"], Is.EqualTo(true));
            Assert.That(result.HasAccount, Is.True);
            Assert.That(result.IsAuthenticated, Is.True);
            Assert.That(result.RequiresOpenAiAuth, Is.True);
            Assert.That(result.AuthMode, Is.EqualTo("chatgpt"));
            Assert.That(result.AccountType, Is.EqualTo("chatgpt"));
            Assert.That(result.Email, Is.EqualTo("reader@example.test"));
            Assert.That(result.PlanType, Is.EqualTo("plus"));
        }

        [Test]
        public async Task DeviceCodeLogin_UsesOfficialProtocolAndMapsInstructions()
        {
            string requestedMethod = null;
            object requestedParameters = null;
            var operations = new AppServerClientOperations(
                () => { },
                () => null,
                (method, parameters, cancellationToken, timeoutMs) =>
                {
                    requestedMethod = method;
                    requestedParameters = parameters;
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "type", "chatgptDeviceCode" },
                        { "loginId", "login-device-1" },
                        { "verificationUrl", "https://auth.openai.example.test/codex/device" },
                        { "userCode", "ABCD-1234" }
                    });
                },
                (path, name) => path,
                path => path,
                path => path,
                (source, skillPath, skillId) => skillPath,
                (workspaceRoots, forceReload, snapshots) => { },
                plugins => { });

            var result = await operations.StartChatGptDeviceCodeLoginAsync();

            Assert.That(requestedMethod, Is.EqualTo("account/login/start"));
            var parameters = requestedParameters as IDictionary<string, object>;
            Assert.That(parameters, Is.Not.Null);
            Assert.That(parameters["type"], Is.EqualTo("chatgptDeviceCode"));
            Assert.That(result.LoginType, Is.EqualTo("chatgptDeviceCode"));
            Assert.That(result.LoginId, Is.EqualTo("login-device-1"));
            Assert.That(result.VerificationUrl, Is.EqualTo("https://auth.openai.example.test/codex/device"));
            Assert.That(result.UserCode, Is.EqualTo("ABCD-1234"));
        }
    }
}
