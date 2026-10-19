using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationApplyCoordinatorTests
    {
        [Test]
        public void GateExcludesTransitionsAndOperationsAndReleaseIsIdempotent()
        {
            var gate = new RuntimeOperationGate();
            var operation = gate.EnterOperation();
            Assert.That(gate.TryEnterTransition(), Is.Null);
            operation.Dispose();
            operation.Dispose();
            using (gate.TryEnterTransition())
            {
                Assert.That(gate.TryEnterTransition(), Is.Null);
                Assert.That(Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation()).Code, Is.EqualTo("runtime_busy"));
            }
            using (gate.EnterOperation())
            {
                Assert.That(gate.TryEnterTransition(), Is.Null);
            }
        }

        [Test]
        public void BusyDoesNotPreflightOrWrite()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) => throw new AssertionException("Backend must not be called."));
            var coordinator = new AuthenticationApplyCoordinator(client, () => null, () => throw new AssertionException("Must not restart."));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.ApplyAsync(Provider(), CancellationToken.None)).Code, Is.EqualTo("runtime_busy"));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task BusySaveStoresOnlyProvidersNotInUseWithoutReconnecting(bool allowWhileBusy, bool unexpectedGlobalWrite)
        {
            var gate = new RuntimeOperationGate();
            var saves = new List<IDictionary<string, object>>();
            var reconnects = 0;
            var authenticationReads = 0;
            var client = new AuthenticationCenterClient("Test", (method, values, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/provider/save"));
                saves.Add(values);
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "id", "b" }, { "version", "v2" }, { "kind", "responses" }, { "globalApplied", unexpectedGlobalWrite && saves.Count == 1 }
                });
            });
            var coordinator = new AuthenticationApplyCoordinator(client, gate.TryEnterTransition, () => { reconnects++; return Task.FromResult(true); },
                () => { authenticationReads++; return Task.FromResult(new AppServerAuthenticationState()); });
            var edit = new AuthenticationProviderEdit { Id = "b", Name = "B", Kind = "responses", BaseUrl = "https://b.example.test/v1", Model = "m", AllowWhileBusy = allowWhileBusy };
            using (gate.EnterOperation())
            {
                if (!allowWhileBusy)
                {
                    Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.SaveAsync(edit, CancellationToken.None)).Code, Is.EqualTo("runtime_busy"));
                    Assert.That(saves, Is.Empty);
                    return;
                }
                var busy = await coordinator.SaveAsync(edit, CancellationToken.None);
                Assert.That(saves[0]["inactiveOnly"], Is.EqualTo(true), "The backend must refuse anything that changes the provider in use.");
                Assert.That(busy.Reconnected, Is.EqualTo(!unexpectedGlobalWrite), "A global write that cannot reconnect is reported, not hidden.");
                Assert.That(reconnects, Is.Zero, "Running work must not be interrupted.");
                Assert.That(authenticationReads, Is.Zero);
            }
            var idle = await coordinator.SaveAsync(edit, CancellationToken.None);
            Assert.That(saves[1].ContainsKey("inactiveOnly"), Is.False, "Without running work the usual save applies.");
            Assert.That(idle.Reconnected, Is.True);
            Assert.That(authenticationReads, Is.EqualTo(1));
        }

        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(false, false)]
        public async Task ApplyQueriesLostReplyAndKeepsGlobalSuccessWhenReconnectFails(bool loseReply, bool reconnectWorks)
        {
            var gate = new RuntimeOperationGate();
            var methods = new List<string>();
            string operationId = null;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                methods.Add(method);
                Assert.That(gate.TryEnterTransition(), Is.Null);
                Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation());
                if (method == "gui/provider/preflight")
                {
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "providerId", "a" }, { "version", "v1" }, { "globalApplied", false }, { "fingerprint", "fingerprint" }
                    });
                }
                if (method == "gui/provider/apply")
                {
                    Assert.That(parameters["expectedFingerprint"], Is.EqualTo("fingerprint"));
                    operationId = (string)parameters["operationId"];
                    if (loseReply)
                    {
                        throw new AuthenticationCenterException("backend_unavailable");
                    }
                }
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "operationId", operationId }, { "providerId", "a" }, { "status", "applied" }
                });
            });
            var restarts = 0;
            var coordinator = new AuthenticationApplyCoordinator(client, gate.TryEnterTransition, () =>
            {
                restarts++;
                Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation());
                if (!reconnectWorks)
                {
                    throw new Exception("Synthetic reconnect failure");
                }
                return Task.FromResult(true);
            });
            var result = await coordinator.ApplyAsync(Provider(), CancellationToken.None);
            Assert.That(result.GlobalApplied, Is.True);
            Assert.That(result.Reconnected, Is.EqualTo(reconnectWorks));
            Assert.That(result.OperationId, Is.EqualTo(operationId));
            Assert.That(restarts, Is.EqualTo(1));
            Assert.That(methods, Is.EqualTo(loseReply
                ? new[] { "gui/provider/preflight", "gui/provider/apply", "gui/operation/get" }
                : new[] { "gui/provider/preflight", "gui/provider/apply" }));
            using (var available = gate.TryEnterTransition())
            {
                Assert.That(available, Is.Not.Null);
            }
        }

        [Test]
        public void ExternalAuthenticationStopsBeforePreflightAndReleasesTransition()
        {
            var gate = new RuntimeOperationGate();
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) => throw new AssertionException("Backend must not be called."));
            var coordinator = new AuthenticationApplyCoordinator(client, gate.TryEnterTransition,
                () => throw new AssertionException("Must not restart."), () =>
                {
                    Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation());
                    return Task.FromResult(new AppServerAuthenticationState { HasAccount = true, AccountType = "chatgptAuthTokens" });
                });
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.ApplyAsync(Provider(), CancellationToken.None)).Code,
                Is.EqualTo("auth_backend_unsupported"));
            using (var lease = gate.TryEnterTransition())
            {
                Assert.That(lease, Is.Not.Null);
            }
        }

        [Test]
        public void EffectiveKeyringConfigurationStopsBeforeGlobalWrite()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) => throw new AssertionException("Backend must not be called."));
            var coordinator = new AuthenticationApplyCoordinator(client, new RuntimeOperationGate().TryEnterTransition,
                () => throw new AssertionException("Must not restart."),
                () => Task.FromResult(new AppServerAuthenticationState { AccountType = "chatgpt" }),
                () => Task.FromResult(new AppServerConfigSnapshot { AuthCredentialsStore = "keyring" }));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.ApplyAsync(Provider(), CancellationToken.None)).Code,
                Is.EqualTo("auth_backend_unsupported"));
        }

        [TestCase("config_conflict")]
        [TestCase("config_backup_failed")]
        [TestCase("credential_recovery_confirmation_required")]
        public void RefusedWriteDoesNotReconnectOrRetryMutation(string code)
        {
            var gate = new RuntimeOperationGate();
            var writes = 0;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                if (method == "gui/provider/preflight")
                {
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "providerId", "a" }, { "version", "v1" }, { "globalApplied", false }, { "fingerprint", "old" }
                    });
                }
                if (method == "gui/provider/apply")
                {
                    writes++;
                    throw new AuthenticationCenterException(code);
                }
                throw new AuthenticationCenterException("operation_not_found");
            });
            var coordinator = new AuthenticationApplyCoordinator(client, gate.TryEnterTransition, () => throw new AssertionException("Must not restart."));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.ApplyAsync(Provider(), CancellationToken.None)).Code, Is.EqualTo(code));
            Assert.That(writes, Is.EqualTo(1));
            using (var available = gate.TryEnterTransition())
            {
                Assert.That(available, Is.Not.Null);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ExplicitEnableRecoversWithoutAnotherConfirmationAndHonorsCancellation(bool cancel)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                var methods = new List<string>();
                var gate = new RuntimeOperationGate();
                var reconnected = false;
                var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
                {
                    methods.Add(method);
                    Assert.That(gate.TryEnterTransition(), Is.Null);
                    Assert.Throws<AuthenticationCenterException>(() => gate.EnterOperation());
                    if (method == "gui/provider/preflight")
                    {
                        if (cancel) cancellation.Cancel();
                        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                        {
                            { "providerId", "a" }, { "version", "v1" }, { "fingerprint", "unchanged" }, { "recoveryRequired", true }
                        });
                    }
                    Assert.That(method, Is.EqualTo("gui/provider/apply"));
                    Assert.That(parameters["confirmCredentialRecovery"], Is.True);
                    Assert.That(parameters["expectedFingerprint"], Is.EqualTo("unchanged"));
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "providerId", "a" }, { "operationId", parameters["operationId"] }, { "status", "applied" }
                    });
                });
                var coordinator = new AuthenticationApplyCoordinator(client, gate.TryEnterTransition,
                    () => { reconnected = true; return Task.FromResult(true); });
                if (cancel)
                    Assert.ThrowsAsync<OperationCanceledException>(() => coordinator.ApplyAsync(Provider(), cancellation.Token));
                else
                    Assert.That(coordinator.ApplyAsync(Provider(), cancellation.Token).GetAwaiter().GetResult().GlobalApplied, Is.True);
                Assert.That(reconnected, Is.EqualTo(!cancel));
                Assert.That(methods.Count, Is.EqualTo(cancel ? 1 : 2));
                using (var lease = gate.TryEnterTransition()) Assert.That(lease, Is.Not.Null);
            }
        }

        [Test]
        public async Task RecoveryPreflightPlanCanBeAppliedWithoutSeparateConfirmationState()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/provider/apply"));
                Assert.That(parameters["confirmCredentialRecovery"], Is.True);
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "providerId", "a" }, { "operationId", parameters["operationId"] }, { "status", "applied" }
                });
            });
            var result = await client.ApplyAsync(new AuthenticationApplyPlan { ProviderId = "a", RecoveryRequired = true }, "operation", CancellationToken.None);
            Assert.That(result.Status, Is.EqualTo("applied"));
        }

        private static AuthenticationProvider Provider()
        {
            return new AuthenticationProvider { Id = "a", Version = "v1", Kind = "responses" };
        }
    }
}
