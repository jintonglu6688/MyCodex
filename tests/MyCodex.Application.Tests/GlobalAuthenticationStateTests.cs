using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    [SetUICulture("en-US")]
    public sealed class GlobalAuthenticationStateTests
    {
        [Test]
        public void ClientReadsLiveMetadataFromTheExistingProviderListRequest()
        {
            var response = Response();
            response["syncError"] = "credential_generation_conflict";
            var methods = new List<string>();
            var snapshot = Client(response, methods).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(snapshot.LiveState, Is.EqualTo("current"));
            Assert.That(snapshot.LiveAccountLabel, Is.EqualTo("Live B"));
            Assert.That(snapshot.SyncError, Is.EqualTo("credential_generation_conflict"));
            Assert.That(snapshot.CurrentProviderId, Is.EqualTo("b"));
            Assert.That(snapshot.Providers.Count, Is.EqualTo(2));
            Assert.That(methods, Is.EqualTo(new[] { "status", "gui/provider/list" }));
        }

        [Test]
        public void CaptureCoreUsesPostCaptureRouteAndLiveMetadata()
        {
            var response = Response();
            response["route"] = Route(false);
            var snapshot = Client(response, new List<string>(), true).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(snapshot.LiveState, Is.EqualTo("current"));
            Assert.That(snapshot.CurrentProviderId, Is.EqualTo("b"));
            Assert.That(snapshot.RouteRequired, Is.False, "pre-capture status still reported takeover");
            Assert.That(snapshot.RouterRunning, Is.False);
            Assert.That(snapshot.RouteInFlight, Is.Zero);
        }

        [TestCase(null)]
        [TestCase("future-state")]
        [TestCase("signed_out")]
        [TestCase("unavailable")]
        public void CaptureCoreDoesNotExposeStaleCurrentOnUnconfirmedState(string state)
        {
            var response = Response();
            response["route"] = Route(false);
            response["liveState"] = state;
            var snapshot = Client(response, new List<string>(), true).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(snapshot.CurrentProviderId, Is.Null);
            Assert.That(snapshot.CurrentProviderVersion, Is.Null);
            Assert.That(snapshot.LiveState, Is.EqualTo(state == "signed_out" ? state : "unavailable"));
        }

        [Test]
        public void CaptureCoreRequiresRouteFromTheSameListWhileOlderCoreRemainsCompatible()
        {
            var response = Response();
            response.Remove("route");
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                Client(response, new List<string>(), true).ReadAsync(CancellationToken.None)).Code, Is.EqualTo("invalid_response"));
            var old = Client(response, new List<string>(), false).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(old.LiveState, Is.Null);
            Assert.That(old.RouteRequired, Is.True);
            Assert.That(old.CurrentProviderId, Is.EqualTo("b"));
        }

        [Test]
        public void CaptureCoreDoesNotClaimCurrentWithoutAMatchingArchivedRow()
        {
            var response = Response();
            response["route"] = Route(false);
            response["currentProviderId"] = "missing";
            var snapshot = Client(response, new List<string>(), true).ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(snapshot.LiveState, Is.EqualTo("unavailable"));
            Assert.That(snapshot.CurrentProviderId, Is.Null);
            Assert.That(snapshot.LiveAccountLabel, Is.Null);
            Assert.That(snapshot.Providers.Count, Is.EqualTo(2));
        }

        [Test]
        public void NativeCaptureRefreshPreservesSelectionAndDisplaysErrorWithoutSpeaking()
        {
            var response = Response();
            response["route"] = Route(false);
            var reader = new Reader();
            using (var form = new AuthenticationCenterForm(Client(response, new List<string>(), true), reader))
            {
                var list = Initialize(form);
                response["currentProviderId"] = "a";
                response["currentProviderVersion"] = "v-a";
                response["liveAccountLabel"] = "Imported A";
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(list.Items[0].SubItems[4].Text, Is.EqualTo("Current"));
                Assert.That(((AuthenticationProvider)list.SelectedItems[0].Tag).Id, Is.EqualTo("b"));
                Assert.That(list.FocusedItem, Is.SameAs(list.SelectedItems[0]));
                response["liveState"] = "unavailable";
                response["syncError"] = "capture_incomplete";
                form.RefreshAsync().GetAwaiter().GetResult();
                Assert.That(Field<Label>(form, "_statusLabel").Text, Does.Contain("Automatic capture did not finish"));
                Assert.That(list.Items.Cast<ListViewItem>().All(item => item.SubItems[4].Text == "Saved"), Is.True);
                Assert.That(reader.SpeakCalls, Is.Zero);
            }
        }

        [Test]
        public void ExternalAccountSwitchUpdatesCurrentRowAndIdentityWithoutSelectingOrApplyingIt()
        {
            var response = Response();
            response["currentProviderId"] = "a";
            response["currentProviderVersion"] = "v-a";
            response["liveAccountLabel"] = "Live A";
            var methods = new List<string>();
            var reader = new Reader();
            var identityCalls = 0;
            using (var form = new AuthenticationCenterForm(Client(response, methods), reader,
                apply: (provider, token) => throw new InvalidOperationException("Refresh must not apply"),
                officialIdentity: () => { identityCalls++; return "Stale cached identity"; }))
            {
                var list = Initialize(form);
                response["currentProviderId"] = "b";
                response["currentProviderVersion"] = "v-b";
                response["liveAccountLabel"] = "Live B";
                form.RefreshAsync().GetAwaiter().GetResult();

                Assert.That(list.Items[0].SubItems[4].Text, Is.EqualTo("Saved"));
                Assert.That(list.Items[1].SubItems[4].Text, Is.EqualTo("Current"));
                Assert.That(((AuthenticationProvider)list.SelectedItems[0].Tag).Id, Is.EqualTo("a"));
                Assert.That(list.FocusedItem, Is.SameAs(list.SelectedItems[0]));
                Assert.That(Field<Label>(form, "_identityLabel").Text, Does.Contain("Live B"));
                Assert.That(identityCalls, Is.Zero);
                Assert.That(reader.SpeakCalls, Is.Zero);
                Assert.That(methods, Is.EqualTo(new[] { "status", "gui/provider/list", "status", "gui/provider/list" }));
            }
        }

        [TestCase("external", "could not be confirmed")]
        [TestCase("signed_out", "not signed in")]
        [TestCase("unavailable", "could not be confirmed")]
        [TestCase("future-state", "could not be confirmed")]
        public void UnconfirmedStateKeepsSavedRowsSelectionAndVisibleError(string state, string statusText)
        {
            var response = Response();
            var methods = new List<string>();
            var reader = new Reader();
            using (var form = new AuthenticationCenterForm(Client(response, methods), reader,
                officialIdentity: () => "Stale cached identity"))
            {
                var list = Initialize(form);
                response["liveState"] = state;
                response["liveAccountLabel"] = null;
                response["syncError"] = "credential_generation_conflict";
                // Even an inconsistent response must not revive a stale current row.
                response["currentProviderId"] = "a";
                form.RefreshAsync().GetAwaiter().GetResult();

                Assert.That(list.Items.Count, Is.EqualTo(2));
                Assert.That(list.Items.Cast<ListViewItem>().All(item => item.SubItems[4].Text == "Saved"), Is.True);
                Assert.That(((AuthenticationProvider)list.SelectedItems[0].Tag).Id, Is.EqualTo("b"));
                Assert.That(list.FocusedItem, Is.SameAs(list.SelectedItems[0]));
                Assert.That(Field<MenuItem>(form, "_editMenuItem").Enabled, Is.True);
                Assert.That(Field<Label>(form, "_statusLabel").Text, Does.Contain(statusText));
                if (state == "signed_out" || state == "unavailable")
                    Assert.That(Field<Label>(form, "_statusLabel").Text, Does.Contain("credentials"));
                Assert.That(Field<Label>(form, "_statusLabel").TabStop, Is.False);
                Assert.That(Field<Label>(form, "_identityLabel").Text, Does.Not.Contain("Stale cached identity"));
                Assert.That(reader.SpeakCalls, Is.Zero);
                Assert.That(methods.Count, Is.EqualTo(4));
            }
        }

        [TestCase("config_conflict", "changed while it was being read")]
        [TestCase("account_write_failed", "could not be saved")]
        [TestCase("unsupported_auth_source", "cannot be synchronized automatically")]
        [TestCase("account_identity_ambiguous", "credentials")]
        [TestCase("unknown-internal-detail", "could not be completed")]
        [TestCase("incomplete_account_credentials", "incomplete")]
        [TestCase("external_proxy_unavailable", "external proxy")]
        [TestCase("capture_incomplete", "Automatic capture did not finish")]
        [TestCase("invalid_live_config", "incomplete or invalid")]
        [TestCase("unsupported_api_format", "cannot be synchronized automatically")]
        [TestCase("credential_reference_unavailable", "environment variable")]
        public void SyncErrorsUseSafeVisibleText(string error, string expectedText)
        {
            var snapshot = new AuthenticationCenterSnapshot { LiveState = "unavailable", SyncError = error };
            var status = AuthenticationCenterUiText.LiveStatus(snapshot, "Ready");
            Assert.That(status, Does.Contain(expectedText));
            Assert.That(status, Does.Not.Contain(error));
        }

        private static ListView Initialize(AuthenticationCenterForm form)
        {
            var formHandle = form.Handle;
            var list = Field<ListView>(form, "_providersList");
            var listHandle = list.Handle;
            form.RefreshAsync().GetAwaiter().GetResult();
            return list;
        }

        private static AuthenticationCenterClient Client(IDictionary<string, object> response, List<string> methods,
            bool supportsCapture = true)
        {
            return new AuthenticationCenterClient("Test target", (method, values, token) =>
            {
                methods.Add(method);
                Assert.That(values, Is.Empty);
                if (method == "status")
                {
                    return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                    {
                        { "protocolVersion", 3 }, { "route", Route(true) },
                        { "capabilities", supportsCapture ? new[] { "globalAuthCapture" } : new string[0] }
                    });
                }
                Assert.That(method, Is.EqualTo("gui/provider/list"));
                return Task.FromResult(response);
            });
        }

        private static Dictionary<string, object> Route(bool active)
        {
            return new Dictionary<string, object>
            {
                { "running", active }, { "accepting", active }, { "takeover", active }, { "activeConnections", active ? 3 : 0 }
            };
        }

        private static Dictionary<string, object> Response()
        {
            return new Dictionary<string, object>
            {
                { "route", Route(false) }, { "liveState", "current" }, { "liveAccountLabel", "Live B" },
                { "currentProviderId", "b" }, { "currentProviderVersion", "v-b" },
                { "providers", new[] { Provider("a"), Provider("b") } }
            };
        }

        private static Dictionary<string, object> Provider(string id)
        {
            return new Dictionary<string, object>
            {
                { "id", id }, { "version", "v-" + id }, { "name", "Saved " + id },
                { "kind", "chatgpt" }, { "accountId", "account-" + id }, { "accountLabel", "Saved identity " + id }
            };
        }

        private static T Field<T>(object instance, string name)
        {
            return (T)instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
        }

        private sealed class Reader : IScreenReaderService
        {
            internal int SpeakCalls;
            public bool IsAvailable => true;
            public bool IsInitialized => true;
            public string LastError => null;
            public bool EnsureInitialized() => true;
            public bool IsReaderRunning() => true;
            public int GetReaderState() => 1;
            public bool Speak(string text, bool useSlave, bool append, bool allowBreak) { SpeakCalls++; return true; }
            public bool Stop(bool useSlave) => true;
            public void Dispose() { }
        }
    }
}
