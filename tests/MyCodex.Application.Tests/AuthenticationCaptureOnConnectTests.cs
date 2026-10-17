using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [SetUICulture("en-US")]
    public sealed class AuthenticationCaptureOnConnectTests
    {
        [TestCase("Windows")]
        [TestCase("WSL: test")]
        [TestCase("SSH: Mac")]
        [TestCase("SSH: Linux")]
        public async Task ReadyTargetCapturesOnceThroughItsExistingClient(string target)
        {
            var methods = new List<string>();
            var client = new AuthenticationCenterClient(target, (method, values, token) =>
            {
                methods.Add(method);
                return Task.FromResult<IDictionary<string, object>>(method == "status"
                    ? new Dictionary<string, object> { { "protocolVersion", 3 }, { "route", new Dictionary<string, object>() } }
                    : new Dictionary<string, object> { { "providers", new object[0] } });
            });
            await AuthenticationCaptureOnConnect.RunAsync(client, () => true,
                text => Assert.Fail("Successful capture must be silent"), CancellationToken.None);
            Assert.That(methods, Is.EqualTo(new[] { "status", "gui/provider/list" }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task SlowOrMissingBackendDoesNotBlockConnectionAndLateErrorStaysOnItsTarget(bool stillCurrent)
        {
            var response = new TaskCompletionSource<IDictionary<string, object>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new AuthenticationCenterClient("target", (method, values, token) =>
            {
                entered.TrySetResult(true);
                return response.Task;
            });
            var status = new List<string>();
            var capture = AuthenticationCaptureOnConnect.RunAsync(client, () => stillCurrent, status.Add, CancellationToken.None);
            await entered.Task;
            Assert.That(capture.IsCompleted, Is.False);
            response.SetException(new AuthenticationCenterException("backend_not_found"));
            await capture;
            Assert.That(status.Count, Is.EqualTo(stillCurrent ? 1 : 0));
            if (stillCurrent) Assert.That(status[0], Does.Not.Contain("backend_not_found"));
        }

        [Test]
        public async Task CancellationDoesNotPublishOrSendCapture()
        {
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                var client = new AuthenticationCenterClient("target", (method, values, token) =>
                    throw new AssertionException("No request expected"));
                await AuthenticationCaptureOnConnect.RunAsync(client, () => true,
                    text => Assert.Fail("No status expected"), cancellation.Token);
            }
        }
    }
}
