using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [NonParallelizable]
    public sealed class AuthenticationCenterDiagnosticsTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task CaptureAndSaveFailuresRequestCredentialDiagnosticsWithoutReplacingOriginalResult(bool save)
        {
            var failure = new AuthenticationCenterException("credential_reference_unavailable");
            var client = new AuthenticationCenterClient("Windows", (method, values, token) =>
            {
                if (method == "gui/provider/save") throw failure;
                return Task.FromResult<IDictionary<string, object>>(method == "status" ? new Dictionary<string, object>
                {
                    { "protocolVersion", 3 }, { "capabilities", new[] { "globalAuthCapture" } }
                } : new Dictionary<string, object>
                {
                    { "providers", new object[0] }, { "route", new Dictionary<string, object>() },
                    { "liveState", "unavailable" }, { "syncError", "credential_reference_unavailable" }
                });
            }, true);
            string trigger = null;
            client.CredentialDiagnostics = stage => { trigger = stage; throw new InvalidOperationException("synthetic-secret"); };
            if (save)
                Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => client.SaveAsync(new AuthenticationProviderEdit(), CancellationToken.None)), Is.SameAs(failure));
            else
                Assert.That((await client.ReadAsync(CancellationToken.None)).SyncError, Is.EqualTo(failure.Code));
            Assert.That(trigger, Is.EqualTo(save ? "provider/save" : "provider/list"));
        }

        [Test]
        public async Task UnresponsiveDiagnosticConfigReadIsBoundedAndDoesNotHideTheSaveFailure()
        {
            var pending = new TaskCompletionSource<AppServerConfigSnapshot>();
            var client = new AuthenticationCenterClient("Windows", (method, values, token) => throw new AuthenticationCenterException("credential_reference_unavailable"), true);
            client.CredentialDiagnostics = trigger => AuthenticationCenterClient.CollectWindowsCredentialDiagnosticsAsync(trigger, token => pending.Task, null, null);
            var save = client.SaveAsync(new AuthenticationProviderEdit(), CancellationToken.None);
            Assert.That(await Task.WhenAny(save, Task.Delay(7000)), Is.SameAs(save));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(async () => await save).Code, Is.EqualTo("credential_reference_unavailable"));
            pending.SetException(new InvalidOperationException("synthetic-secret"));
        }

        [TestCase("Windows", true, "Windows")]
        [TestCase("WSL: private-target", false, "WSL")]
        [TestCase("SSH: private-target", false, "SSH")]
        public void SaveFailureLogsSafeContextAndPreservesTheError(string target, bool local, string targetKind)
        {
            var failure = new AuthenticationCenterException("credential_reference_unavailable");
            var client = new AuthenticationCenterClient(target, (method, parameters, token) => throw failure, local);
            using (var output = new StringWriter())
            using (var listener = new TextWriterTraceListener(output))
            {
                Trace.Listeners.Add(listener);
                try
                {
                    var error = Assert.ThrowsAsync<AuthenticationCenterException>(() => client.SaveAsync(new AuthenticationProviderEdit
                    {
                        Name = "private-name", PresetId = "private-preset", Kind = "responses",
                        BaseUrl = "https://private-endpoint.invalid/v1", Model = "private-model", ApiKey = "synthetic-secret",
                        ConfigToml = "private-config", Advanced = new Dictionary<string, object> { { "envKey", "PRIVATE_ENV_NAME" } }
                    }, CancellationToken.None));
                    Assert.That(error, Is.SameAs(failure));
                    listener.Flush();
                    var log = output.ToString();
                    Assert.That(log, Does.Contain("[AuthenticationCenter] Provider save started."));
                    Assert.That(log, Does.Contain("TargetKind=" + targetKind).And.Contain("Mode=add").And.Contain("Kind=responses"));
                    Assert.That(log, Does.Contain("ApiKeyProvided=True").And.Contain("CredentialReferenceConfigured=True"));
                    Assert.That(log, Does.Contain("Method=provider/save").And.Contain("ErrorCode=credential_reference_unavailable"));
                    foreach (var secret in new[] { "private-", "synthetic-secret", "PRIVATE_ENV_NAME", "private-config" })
                        Assert.That(log, Does.Not.Contain(secret));
                }
                finally { Trace.Listeners.Remove(listener); }
            }
        }

        [Test]
        public void UnexpectedFailureDoesNotLogMessagesOrUntrustedErrorCodes()
        {
            using (var output = new StringWriter())
            using (var listener = new TextWriterTraceListener(output))
            {
                Trace.Listeners.Add(listener);
                try
                {
                    foreach (var failure in new Exception[]
                    {
                        new InvalidOperationException("synthetic-secret"),
                        new AuthenticationCenterException("bad\nsynthetic-secret")
                    })
                    {
                        var client = new AuthenticationCenterClient("private-target", (method, values, token) => throw failure);
                        Assert.CatchAsync<Exception>(() => client.ReadPresetsAsync(CancellationToken.None));
                    }
                    listener.Flush();
                    Assert.That(output.ToString(), Does.Contain("Method=preset/list").And.Contain("ErrorCode=unrecognized"));
                    Assert.That(output.ToString(), Does.Not.Contain("synthetic-secret").And.Not.Contain("private-target"));
                }
                finally { Trace.Listeners.Remove(listener); }
            }
        }

        [Test]
        public async Task UnavailableCaptureIsLoggedEvenWhenListRequestSucceeds()
        {
            var client = new AuthenticationCenterClient("Windows", (method, values, token) => Task.FromResult<IDictionary<string, object>>(
                method == "status" ? new Dictionary<string, object>
                {
                    { "protocolVersion", 3 }, { "hostVersion", "0.3.4" }, { "capabilities", new[] { "globalAuthCapture" } }
                } : new Dictionary<string, object>
                {
                    { "providers", new object[0] }, { "route", new Dictionary<string, object>() },
                    { "liveState", "unavailable" }, { "syncError", "credential_reference_unavailable" }
                }), true);
            using (var output = new StringWriter())
            using (var listener = new TextWriterTraceListener(output))
            {
                Trace.Listeners.Add(listener);
                try
                {
                    var snapshot = await client.ReadAsync(CancellationToken.None);
                    Assert.That(snapshot.SyncError, Is.EqualTo("credential_reference_unavailable"));
                    listener.Flush();
                    Assert.That(output.ToString(), Does.Contain("[AuthenticationCenter] Global capture unavailable."));
                    Assert.That(output.ToString(), Does.Contain("HostVersion=0.3.4").And.Contain("LiveState=unavailable")
                        .And.Contain("Providers=0").And.Contain("ErrorCode=credential_reference_unavailable"));
                }
                finally { Trace.Listeners.Remove(listener); }
            }
        }
    }
}
