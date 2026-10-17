using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Controls;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationNativeWorkflowTests
    {
        [TestCase(false, true)]
        [TestCase(true, true)]
        [TestCase(true, false)]
        public async Task SaveHoldsRuntimeLeaseAndReconnectsOnlyWhenGlobalWasWritten(bool applied, bool reconnects)
        {
            var gate = new RuntimeOperationGate();
            var calls = 0;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/provider/save"));
                Assert.That(gate.TryEnterTransition(), Is.Null);
                Assert.That(parameters.ContainsKey("commonConfigEnabled"), Is.False);
                calls++;
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "id", "test" }, { "version", "v2" }, { "globalApplied", applied }
                });
            });
            var restarts = 0;
            var coordinator = new AuthenticationApplyCoordinator(client, gate.TryEnterTransition, () =>
            {
                Assert.That(gate.TryEnterTransition(), Is.Null);
                restarts++;
                return Task.FromResult(reconnects);
            });
            var saved = await coordinator.SaveAsync(new AuthenticationProviderEdit { Id = "test" }, CancellationToken.None);
            Assert.That(saved.Provider.GlobalApplied, Is.EqualTo(applied));
            Assert.That(saved.Reconnected, Is.EqualTo(!applied || reconnects));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(restarts, Is.EqualTo(applied ? 1 : 0));
            using (var lease = gate.TryEnterTransition()) Assert.That(lease, Is.Not.Null);
        }

        [Test]
        public void BusyNativeSaveCannotWriteOrRestart()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) => throw new AssertionException("Must not write."));
            var coordinator = new AuthenticationApplyCoordinator(client, () => null, () => throw new AssertionException("Must not restart."));
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.SaveAsync(new AuthenticationProviderEdit(), CancellationToken.None)).Code,
                Is.EqualTo("runtime_busy"));
        }

        [TestCase("backend_unavailable")]
        [TestCase("ipc_timeout")]
        [TestCase("save_outcome_unknown")]
        [Apartment(ApartmentState.STA)]
        public void LostSaveReplyCannotBeInferredFromAListOrResubmittedInTheEditor(string errorCode)
        {
            var writes = 0;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/provider/save"), "No old list-based inference or automatic retry.");
                writes++;
                throw new AuthenticationCenterException(errorCode);
            });
            var coordinator = new AuthenticationApplyCoordinator(client, new RuntimeOperationGate().TryEnterTransition,
                () => throw new AssertionException("Unknown write must remain visible until explicit recovery."));
            using (var editor = new AuthenticationProviderEditForm(client,
                template: new AuthenticationProviderPreset { Id = "custom", Name = "Test", Model = "test", Kind = "responses" },
                save: coordinator.SaveAsync))
            {
                editor.SaveAsync().GetAwaiter().GetResult();
                Assert.That(editor.SaveOutcomeUnknown, Is.True);
                Assert.That(editor.SavedProvider, Is.Null);
                Assert.That(editor.DialogResult, Is.Not.EqualTo(DialogResult.OK));
                Assert.That(Field<Button>(editor, "_saveButton").Enabled, Is.False);
                editor.SaveAsync().GetAwaiter().GetResult();
                Assert.That(writes, Is.EqualTo(1));
            }
        }

        [Test]
        public async Task EditingCurrentProviderUsesFreshNativeSnapshot()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/provider/get"));
                Assert.That(parameters["providerId"], Is.EqualTo("test"));
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "id", "test" }, { "version", "fresh" }, { "model", "externally-edited" }
                });
            });
            var fresh = await client.ReadForEditAsync(new AuthenticationProvider { Id = "test", Version = "old", Model = "old" }, CancellationToken.None);
            Assert.That(fresh.Model, Is.EqualTo("externally-edited"));
            Assert.That(fresh.Version, Is.EqualTo("fresh"));
        }

        [TestCase(null, null, 1)]
        [TestCase("route_requests_active", "route_requests_active", 0)]
        [TestCase("account_remove_failed", "operation_unknown", 0)]
        [TestCase("ipc_timeout", "operation_unknown", 0)]
        public async Task AccountRemovalDistinguishesRejectedAndUncertainWrites(string backendError, string expectedError, int expectedReconnects)
        {
            var requests = 0;
            var restarts = 0;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/account/delete"));
                requests++;
                if (backendError != null) throw new AuthenticationCenterException(backendError);
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
            });
            var coordinator = new AuthenticationApplyCoordinator(client, new RuntimeOperationGate().TryEnterTransition, () => { restarts++; return Task.FromResult(true); });
            if (expectedError == null) Assert.That(await coordinator.DeleteAccountAsync("test", CancellationToken.None), Is.True);
            else Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => coordinator.DeleteAccountAsync("test", CancellationToken.None)).Code, Is.EqualTo(expectedError));
            Assert.That(requests, Is.EqualTo(1));
            Assert.That(restarts, Is.EqualTo(expectedReconnects));
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public async Task RemovedNativeAccountCanStartANewSignIn()
        {
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/account/list"));
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object> { { "accounts", new object[0] } });
            });
            using (var login = new AuthenticationLoginControl(client, accountId: "removed"))
            {
                await login.RestoreAsync();
                Assert.That(login.AccountId, Is.Null, "A deleted ID must not be sent as a reauthentication target.");
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void AccountManagerHasNativeKeyboardControlsAndDoesNotMutateOnSelection()
        {
            var deletes = 0;
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) =>
            {
                Assert.That(method, Is.EqualTo("gui/account/list"));
                return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                {
                    { "accounts", new object[] { new Dictionary<string, object> { { "id", "test" }, { "label", "Test account" } } } }
                });
            });
            using (var form = new AuthenticationAccountsForm(client, (id, token) => { deletes++; return Task.FromResult(true); }))
            {
                var handle = form.Handle;
                form.RefreshAsync().GetAwaiter().GetResult();
                var accounts = Field<ListView>(form, "_accounts");
                Assert.That(accounts.TabIndex, Is.Zero);
                Assert.That(accounts.TabStop, Is.True);
                Assert.That(accounts.AccessibleName, Is.Not.Empty);
                Assert.That(accounts.ContextMenu, Is.TypeOf<ContextMenu>());
                Assert.That(accounts.Items.Count, Is.EqualTo(1));
                accounts.Items[0].Selected = true;
                Assert.That(deletes, Is.Zero);
                Assert.That(Field<Button>(form, "_deleteButton").AccessibleName, Is.Not.Empty);
                Assert.That(Field<Button>(form, "_refresh").TabStop, Is.False);
                Assert.That(form.MinimumSize.Width, Is.GreaterThan(0));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void AccountRefreshRestoresFocusAndSelectionAfterDelayedReply()
        {
            var pending = new TaskCompletionSource<IDictionary<string, object>>();
            var delayed = false;
            IDictionary<string, object> reply = new Dictionary<string, object>
            {
                { "accounts", new object[]
                    {
                        new Dictionary<string, object> { { "id", "first" }, { "label", "First" } },
                        new Dictionary<string, object> { { "id", "second" }, { "label", "Second" } }
                    }
                }
            };
            var client = new AuthenticationCenterClient("Test", (method, parameters, token) => delayed ? pending.Task : Task.FromResult(reply));
            using (var form = new AuthenticationAccountsForm(client, null))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var accounts = Field<ListView>(form, "_accounts");
                accounts.Items[0].Selected = false;
                accounts.Items[1].Selected = true;
                accounts.Focus();
                Assert.That(accounts.ContainsFocus, Is.True);
                delayed = true;
                var refresh = form.RefreshAsync();
                Assert.That(refresh.IsCompleted, Is.False);
                pending.SetResult(reply);
                var timer = Stopwatch.StartNew();
                while (!refresh.IsCompleted && timer.ElapsedMilliseconds < 2000) System.Windows.Forms.Application.DoEvents();
                Assert.That(refresh.IsCompleted, Is.True);
                refresh.GetAwaiter().GetResult();
                Assert.That(accounts.ContainsFocus, Is.True);
                Assert.That(((AuthenticationAccount)accounts.SelectedItems[0].Tag).Id, Is.EqualTo("second"));
            }
        }

        private static T Field<T>(object owner, string name)
        {
            return (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
        }
    }
}
