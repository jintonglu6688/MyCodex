using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using MyCodex.WinForms.Controls;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationLoginControlTests
    {
        [TestCase(true, "_useSaved")]
        [TestCase(true, "_device")]
        [TestCase(false, "_useSaved")]
        public void BackgroundPollingPreservesFocusedAction(bool browser, string buttonName)
        {
            var backend = new Backend { BrowserAvailable = browser };
            using (var form = new Form { ShowInTaskbar = false })
            using (var control = new AuthenticationLoginControl(backend.Client(browser), null, null, code => { }, url => { }))
            {
                form.Controls.Add(control);
                form.Show();
                control.StartLoginAsync().GetAwaiter().GetResult();
                Field<System.Windows.Forms.Timer>(control, "_timer").Stop();
                var button = Field<Button>(control, buttonName);
                Assert.That(button.Focus(), Is.True);
                backend.PendingPoll = new TaskCompletionSource<IDictionary<string, object>>();
                var poll = control.PollLoginAsync();
                try
                {
                    Assert.That(button.Focused, Is.True, "Background polling must not move keyboard focus.");
                    Assert.That(button.Enabled, Is.True);
                    Assert.That(control.IsBusy, Is.False, "Reading login status is not a user operation.");
                    control.PollLoginAsync().GetAwaiter().GetResult();
                    Assert.That(backend.Polls, Is.EqualTo(1), "Only one status request may be in flight.");
                }
                finally
                {
                    backend.PendingPoll.SetResult(backend.Login("pending"));
                    Pump(poll);
                }
                Assert.That(button.Focused, Is.True, "A pending reply must also preserve focus.");
            }
        }

        [Test]
        public void LocalBrowserLoginOpensOnceWithoutClipboardAndCanFallBackToDevice()
        {
            var backend = new Backend { BrowserAvailable = true };
            var actions = new List<string>();
            using (var control = new AuthenticationLoginControl(backend.Client(true), null, null,
                code => actions.Add("copy:" + code), url => actions.Add("open:" + url)))
            {
                control.StartLoginAsync().GetAwaiter().GetResult();
                control.PollLoginAsync().GetAwaiter().GetResult();
                Assert.That(backend.Method, Is.EqualTo("browser"));
                Assert.That(actions, Is.EqualTo(new[] { "open:" + Backend.BrowserUri }));
                Assert.That(Field<TextBox>(control, "_code").TabStop, Is.False);
                Assert.That(Field<Button>(control, "_device").AccessibleName, Is.Not.Empty);
                Assert.That(Field<TextBox>(control, "_status").TabStop, Is.False);
                control.StartDeviceFallbackAsync().GetAwaiter().GetResult();
                Assert.That(backend.Cancels, Is.EqualTo(1));
                Assert.That(backend.Starts, Is.EqualTo(2));
                Assert.That(backend.Method, Is.EqualTo("device"));
                Assert.That(actions, Is.EqualTo(new[] { "open:" + Backend.BrowserUri,
                    "copy:TEST-CODE", "open:https://auth.openai.com/codex/device" }));
                Assert.That(Field<TextBox>(control, "_code").TabStop, Is.True);
            }
        }

        [TestCase(false, true)]
        [TestCase(true, false)]
        public void RemoteTargetsAndOlderBackendsKeepDeviceLogin(bool local, bool available)
        {
            var backend = new Backend { BrowserAvailable = available };
            using (var control = new AuthenticationLoginControl(backend.Client(local), null, null, code => { }, url => { }))
            {
                control.StartLoginAsync().GetAwaiter().GetResult();
                Assert.That(backend.Method, Is.EqualTo("device"));
            }
            if (!local)
                Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() => backend.Client().StartBrowserLoginAsync(
                    null, Guid.NewGuid().ToString(), CancellationToken.None)).Code, Is.EqualTo("unsupported_target"));
        }

        [TestCase("http://auth.openai.com/oauth/authorize?state=test")]
        [TestCase("https://auth.openai.com.evil.invalid/oauth/authorize?state=test")]
        [TestCase("https://auth.openai.com:8443/oauth/authorize?state=test")]
        [TestCase("https://user@auth.openai.com/oauth/authorize?state=test")]
        [TestCase("https://auth.openai.com/oauth/authorize?state=test#fragment")]
        [TestCase("https://auth.openai.com/other?state=test")]
        public void BrowserLoginRejectsUnexpectedAuthorizationUrl(string url)
        {
            var backend = new Backend { Method = "browser", LoginId = Guid.NewGuid().ToString(), VerificationUri = url };
            Assert.That(Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                backend.Client(true).ReadDeviceLoginAsync(backend.LoginId, CancellationToken.None)).Code, Is.EqualTo("invalid_response"));
        }

        [Test]
        public void RestoredBrowserLoginDoesNotReopenAndCompletedLoginMayOmitAuthorizationUrl()
        {
            var backend = new Backend { BrowserAvailable = true, RestoreActive = true, Method = "browser",
                VerificationUri = Backend.BrowserUri, LoginId = Guid.NewGuid().ToString() };
            var actions = 0;
            using (var control = new AuthenticationLoginControl(backend.Client(true), null, null, code => actions++, url => actions++))
            {
                control.RestoreAsync().GetAwaiter().GetResult();
                Assert.That(control.IsLoginActive, Is.True);
                Assert.That(actions, Is.Zero);
                backend.Status = "completed";
                backend.VerificationUri = "";
                control.PollLoginAsync().GetAwaiter().GetResult();
                Assert.That(control.AccountId, Is.EqualTo("archived-id"));
                Assert.That(control.IsLoginActive, Is.False);
                Assert.That(Field<Button>(control, "_open").Enabled, Is.False);
            }
        }

        [Test]
        public void LostStartReplyRetainsKnownIdForCancellationAndHidesPayloads()
        {
            var backend = new Backend { LoseStartReply = true };
            using (var control = new AuthenticationLoginControl(backend.Client(), null, null, code => { }, url => { }))
            {
                control.StartLoginAsync().GetAwaiter().GetResult();
                Assert.That(Guid.TryParse(backend.LoginId, out _), Is.True);
                Assert.That(control.IsLoginActive, Is.True);
                Assert.That(Field<TextBox>(control, "_status").Text, Does.Not.Contain("synthetic-sensitive-payload"));
                control.CancelLoginAsync().GetAwaiter().GetResult();
                Assert.That(backend.Cancels, Is.EqualTo(1));
                Assert.That(backend.Status, Is.EqualTo("cancelled"));
            }
        }

        [Test]
        public void ClientRejectsUnexpectedVerificationWebsite()
        {
            var backend = new Backend { LoginId = Guid.NewGuid().ToString(), VerificationUri = "https://untrusted.example.test/" };
            var error = Assert.ThrowsAsync<AuthenticationCenterException>(() =>
                backend.Client().ReadDeviceLoginAsync(backend.LoginId, CancellationToken.None));
            Assert.That(error.Code, Is.EqualTo("invalid_response"));
        }

        [Test]
        public void ExplicitStartCopiesBeforeOpeningOnceAndCompletionExposesArchivedAccount()
        {
            var backend = new Backend();
            var actions = new List<string>();
            using (var control = new AuthenticationLoginControl(backend.Client(), null, null,
                code => actions.Add("copy:" + code), url => actions.Add("open:" + url)))
            {
                Assert.That(backend.Starts, Is.Zero);
                foreach (var name in new[] { "_start", "_copy", "_open", "_cancel" })
                    Assert.That(Field<Button>(control, name).AccessibleName, Is.Not.Empty);
                Assert.That(Field<TextBox>(control, "_code").ReadOnly, Is.True);
                control.StartLoginAsync().GetAwaiter().GetResult();
                control.PollLoginAsync().GetAwaiter().GetResult();
                Assert.That(actions, Is.EqualTo(new[] { "copy:TEST-CODE", "open:https://auth.openai.com/codex/device" }));
                backend.Status = "completed";
                control.PollLoginAsync().GetAwaiter().GetResult();
                Assert.That(control.AccountId, Is.EqualTo("archived-id"));
                Assert.That(control.IsLoginActive, Is.False);
                Assert.That(Field<TextBox>(control, "_code").Text, Is.Empty);
                Assert.That(Field<TextBox>(control, "_status").Text, Does.Contain("Test account"));
            }
        }

        [Test]
        public void BrowserFailureKeepsManualActionsEnabledAndIsNotAutomaticallyRepeated()
        {
            var backend = new Backend();
            var opens = 0;
            using (var control = new AuthenticationLoginControl(backend.Client(), null, null, code => { }, url =>
                { opens++; throw new Exception("secret-payload"); }))
            {
                control.StartLoginAsync().GetAwaiter().GetResult();
                control.PollLoginAsync().GetAwaiter().GetResult();
                Assert.That(opens, Is.EqualTo(1));
                Assert.That(Field<Button>(control, "_open").Enabled, Is.True);
                Assert.That(Field<Button>(control, "_copy").Enabled, Is.True);
                Assert.That(Field<TextBox>(control, "_status").Text, Does.Not.Contain("secret-payload"));
                control.CancelLoginAsync().GetAwaiter().GetResult();
                Assert.That(Field<TextBox>(control, "_code").Text, Is.Empty);
            }
        }

        [Test]
        public void ClipboardFailureDoesNotOpenBrowserAndExistingAccountOnlyReauthenticatesExplicitly()
        {
            var backend = new Backend();
            var opens = 0;
            using (var control = new AuthenticationLoginControl(backend.Client(), null, "existing-id",
                code => { throw new InvalidOperationException(); }, url => opens++))
            {
                Assert.That(backend.Starts, Is.Zero);
                Assert.That(control.AccountId, Is.EqualTo("existing-id"));
                control.StartLoginAsync().GetAwaiter().GetResult();
                Assert.That(backend.Target, Is.EqualTo("existing-id"));
                Assert.That(opens, Is.Zero);
                Assert.That(Field<TextBox>(control, "_code").Text, Is.EqualTo("TEST-CODE"));
            }
        }

        [Test]
        public void DisposeDuringStartIgnoresLateCodeAndCancelsKnownJob()
        {
            var backend = new Backend { PendingStart = new TaskCompletionSource<IDictionary<string, object>>() };
            var actions = 0;
            var control = new AuthenticationLoginControl(backend.Client(), null, null, code => actions++, url => actions++);
            var start = control.StartLoginAsync();
            control.Dispose();
            backend.PendingStart.SetResult(backend.Login("pending"));
            Pump(start);
            Assert.That(actions, Is.Zero);
            Assert.That(backend.Cancels, Is.GreaterThanOrEqualTo(1));
            Assert.That(control.AccountId, Is.Null);
        }

        [Test]
        public void CancelBeforeStartReplyCancelsTheLateJobAgain()
        {
            var backend = new Backend { PendingStart = new TaskCompletionSource<IDictionary<string, object>>() };
            var actions = 0;
            using (var control = new AuthenticationLoginControl(backend.Client(), null, null, code => actions++, url => actions++))
            {
                var start = control.StartLoginAsync();
                control.CancelLoginAsync().GetAwaiter().GetResult();
                backend.PendingStart.SetResult(backend.Login("pending"));
                Pump(start);
                Assert.That(actions, Is.Zero);
                Assert.That(backend.Cancels, Is.EqualTo(2));
                Assert.That(control.IsLoginActive, Is.False);
            }
        }

        [Test]
        public void FailedRestoreDoesNotStartAnUntrackedSecondLogin()
        {
            var backend = new Backend { FailRestore = true };
            using (var control = new AuthenticationLoginControl(backend.Client(), null, null, code => { }, url => { }))
            {
                control.StartLoginAsync().GetAwaiter().GetResult();
                Assert.That(backend.Starts, Is.Zero);
            }
        }

        [Test]
        public void ReopeningDoesNotAdoptOrCancelAnotherAccountsLogin()
        {
            var backend = new Backend { RestoreActive = true, Target = "another-id", LoginId = Guid.NewGuid().ToString() };
            using (var control = new AuthenticationLoginControl(backend.Client(), null, "existing-id", code => { }, url => { }))
            {
                control.StartLoginAsync().GetAwaiter().GetResult();
                Assert.That(control.IsLoginActive, Is.False);
                Assert.That(control.AccountId, Is.EqualTo("existing-id"));
                Assert.That(backend.Starts, Is.Zero);
            }
            Assert.That(backend.Cancels, Is.Zero);
        }

        [Test]
        public void ReopeningRestoresPendingLoginWithoutAnotherStartOrBrowserAction()
        {
            var backend = new Backend { RestoreActive = true, LoginId = Guid.NewGuid().ToString() };
            var actions = 0;
            using (var control = new AuthenticationLoginControl(backend.Client(), null, null, code => actions++, url => actions++))
            {
                control.RestoreAsync().GetAwaiter().GetResult();
                control.StartLoginAsync().GetAwaiter().GetResult();
                control.PollLoginAsync().GetAwaiter().GetResult();
                Assert.That(backend.Starts, Is.Zero);
                Assert.That(actions, Is.Zero);
                Assert.That(control.IsLoginActive, Is.True);
                Assert.That(Field<TextBox>(control, "_code").Text, Is.EqualTo("TEST-CODE"));
                Assert.That(Field<TextBox>(control, "_status").TabStop, Is.False);
                Assert.That(Field<TextBox>(control, "_status").ReadOnly, Is.True);
            }
        }

        [Test]
        public void CancellingWhilePollingPreventsLateCompletionFromChangingBinding()
        {
            var backend = new Backend();
            var opens = 0;
            using (var form = new Form { ShowInTaskbar = false })
            using (var control = new AuthenticationLoginControl(backend.Client(), null, null, code => { }, url => opens++))
            {
                form.Controls.Add(control);
                form.Show();
                control.StartLoginAsync().GetAwaiter().GetResult();
                backend.PendingPoll = new TaskCompletionSource<IDictionary<string, object>>();
                var poll = control.PollLoginAsync();
                Assert.That(Field<Button>(control, "_open").Enabled, Is.True);
                Assert.That(Field<Button>(control, "_copy").Enabled, Is.True);
                Field<Button>(control, "_open").PerformClick();
                Assert.That(opens, Is.EqualTo(2), "Manual browser opening remains usable during a pending poll.");
                control.CancelLoginAsync().GetAwaiter().GetResult();
                backend.PendingPoll.SetResult(backend.Login("completed"));
                Pump(poll);
                Assert.That(control.AccountId, Is.Null);
                Assert.That(control.IsLoginActive, Is.False);
            }
        }

        private static T Field<T>(object value, string name) where T : class
        {
            return (T)value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
        }

        private static void Pump(Task task)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted && DateTime.UtcNow < deadline)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.That(task.IsCompleted, Is.True);
            task.GetAwaiter().GetResult();
        }

        private sealed class Backend
        {
            internal int Starts;
            internal int Cancels;
            internal int Polls;
            internal string Target;
            internal string LoginId;
            internal string Status = "pending";
            internal TaskCompletionSource<IDictionary<string, object>> PendingStart;
            internal TaskCompletionSource<IDictionary<string, object>> PendingPoll;
            internal bool RestoreActive;
            internal bool FailRestore;
            internal bool LoseStartReply;
            internal bool BrowserAvailable;
            internal string Method = "device";
            internal const string BrowserUri = "https://auth.openai.com/oauth/authorize?state=synthetic-state";
            internal string VerificationUri = "https://auth.openai.com/codex/device";

            internal AuthenticationCenterClient Client(bool localWindows = false)
            {
                return new AuthenticationCenterClient("test", (method, values, token) =>
                {
                    switch (method)
                    {
                        case "gui/account/list":
                            if (FailRestore) throw new InvalidOperationException();
                            return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>
                            { { "accounts", new object[] { new Dictionary<string, object> { { "id", "existing-id" }, { "label", "Existing" } } } },
                                { "browserLoginAvailable", BrowserAvailable },
                                { "activeLogin", RestoreActive && Status == "pending" ? Login(Status) : null } });
                        case "gui/account/login/start":
                            Starts++;
                            Status = "pending";
                            Method = values.TryGetValue("method", out var loginMethod) ? (string)loginMethod : "device";
                            VerificationUri = Method == "browser" ? BrowserUri : "https://auth.openai.com/codex/device";
                            LoginId = (string)values["loginId"];
                            Target = values.TryGetValue("targetAccountId", out var target) ? target as string : null;
                            if (LoseStartReply) throw new Exception("synthetic-sensitive-payload");
                            if (PendingStart != null) return PendingStart.Task;
                            break;
                        case "gui/account/login/get":
                            Polls++;
                            if (PendingPoll != null) return PendingPoll.Task;
                            break;
                        case "gui/account/login/cancel":
                            Assert.That(values["loginId"], Is.EqualTo(LoginId));
                            Cancels++; Status = "cancelled"; break;
                        default: Assert.Fail("Login must not save or apply provider configuration: " + method); break;
                    }
                    return Task.FromResult(Login(Status));
                }, localWindows);
            }

            internal IDictionary<string, object> Login(string status)
            {
                return new Dictionary<string, object>
                {
                    { "loginId", LoginId }, { "status", status }, { "method", Method },
                    { "userCode", Method == "browser" ? "" : "TEST-CODE" }, { "targetAccountId", Target },
                    { "verificationUri", VerificationUri },
                    { "account", status == "completed" ? new Dictionary<string, object> { { "id", "archived-id" }, { "label", "Test account" } } : null }
                };
            }
        }
    }
}
