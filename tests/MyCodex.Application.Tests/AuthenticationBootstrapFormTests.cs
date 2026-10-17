using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationBootstrapFormTests
    {
        [Test]
        public void ApiKeySubmission_UsesOfficialLoginClearsInputAndChecksAccountState()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
            var client = new FakeAuthenticationClient();
            var screenReader = new FakeScreenReaderService();
            using (var form = new AuthenticationBootstrapForm(client, screenReader))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var apiKeyTextBox = FindControl<TextBox>(form, "OpenAI API key");
                var apiKeyButton = FindControl<Button>(form, "Use API key");

                apiKeyTextBox.Text = "synthetic-api-key";
                apiKeyButton.PerformClick();
                System.Windows.Forms.Application.DoEvents();

                Assert.That(client.ApiKey, Is.EqualTo("synthetic-api-key"));
                Assert.That(apiKeyTextBox.Text, Is.Empty);
                Assert.That(client.AuthenticationStateReadCount, Is.EqualTo(1));
                Assert.That(screenReader.Messages, Is.Empty);
            }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void ChatGptLogin_OpensTheOfficialAuthenticationUrl()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
                const string authenticationUrl = "https://chatgpt.example.test/auth?flow=synthetic";
                var client = new FakeAuthenticationClient
                {
                    ChatGptAuthUrl = authenticationUrl
                };
                string openedUrl = null;
                using (var form = new AuthenticationBootstrapForm(
                    client,
                    new FakeScreenReaderService(),
                    url => openedUrl = url))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    var chatGptButton = FindControl<Button>(form, "Sign in with ChatGPT");

                    chatGptButton.PerformClick();
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(openedUrl, Is.EqualTo(authenticationUrl));
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void ChatGptLogin_WhenTheBrowserCannotOpen_ShowsFailureWithoutSpeakingOrLosingCancel()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
                var client = new FakeAuthenticationClient
                {
                    ChatGptAuthUrl = "https://chatgpt.example.test/auth?flow=launch-failure"
                };
                var screenReader = new FakeScreenReaderService();
                using (var form = new AuthenticationBootstrapForm(
                    client,
                    screenReader,
                    url => throw new InvalidOperationException("Synthetic browser failure")))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    var chatGptButton = FindControl<Button>(form, "Sign in with ChatGPT");
                    var cancelButton = FindControl<Button>(form, "Cancel sign-in");
                    var statusLabel = FindControl<Label>(form, "Sign-in status");

                    chatGptButton.PerformClick();
                    System.Windows.Forms.Application.DoEvents();

                    Assert.That(statusLabel.Text, Is.EqualTo(
                        "The sign-in page could not be opened. Check your default browser, then retry or cancel sign-in."));
                    Assert.That(screenReader.Messages, Is.Empty);
                    Assert.That(chatGptButton.Enabled, Is.True);
                    Assert.That(cancelButton.Enabled, Is.True);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void ChatGptLogin_CanBeCanceledAndReturnsFocusToTheOfficialLoginAction()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
            var client = new FakeAuthenticationClient
            {
                ChatGptAuthUrl = "https://chatgpt.example.test/auth?flow=cancel"
            };
            using (var form = new AuthenticationBootstrapForm(client, new FakeScreenReaderService(), url => { }))
            {
                form.Show();
                System.Windows.Forms.Application.DoEvents();
                var chatGptButton = FindControl<Button>(form, "Sign in with ChatGPT");
                var cancelButton = FindControl<Button>(form, "Cancel sign-in");

                chatGptButton.PerformClick();
                System.Windows.Forms.Application.DoEvents();
                Assert.That(cancelButton.Enabled, Is.True);

                cancelButton.PerformClick();
                System.Windows.Forms.Application.DoEvents();

                Assert.That(client.CanceledLoginId, Is.EqualTo("login-1"));
                Assert.That(chatGptButton.Focused || chatGptButton.ContainsFocus, Is.True);
            }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Test]
        public void DeviceCodeLogin_UsesOfficialFlowAndShowsCopyableAccessibleInstructions()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
                const string verificationUrl = "https://auth.openai.example.test/codex/device";
                const string userCode = "ABCD-1234";
                var client = new FakeAuthenticationClient
                {
                    DeviceCodeVerificationUrl = verificationUrl,
                    DeviceCodeUserCode = userCode
                };
                var screenReader = new FakeScreenReaderService();
                string openedUrl = null;
                using (var form = new AuthenticationBootstrapForm(
                    client,
                    screenReader,
                    url => openedUrl = url))
                {
                    form.Show();
                    System.Windows.Forms.Application.DoEvents();
                    var deviceCodeButton = FindControl<Button>(form, "Sign in with device code");

                    deviceCodeButton.PerformClick();
                    System.Windows.Forms.Application.DoEvents();

                    var deviceCodeTextBox = FindControl<TextBox>(form, "Device code");
                    Assert.That(client.DeviceCodeLoginStartCount, Is.EqualTo(1));
                    Assert.That(openedUrl, Is.EqualTo(verificationUrl));
                    Assert.That(deviceCodeTextBox.Text, Is.EqualTo(userCode));
                    Assert.That(deviceCodeTextBox.ReadOnly, Is.True);
                    Assert.That(screenReader.Messages, Is.Empty);
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        private static TControl FindControl<TControl>(Control parent, string accessibleName)
            where TControl : Control
        {
            var control = parent.Controls.Cast<Control>()
                .SelectMany(Flatten)
                .OfType<TControl>()
                .SingleOrDefault(candidate => string.Equals(candidate.AccessibleName, accessibleName, StringComparison.Ordinal));
            Assert.That(control, Is.Not.Null, accessibleName);
            return control;
        }

        private static IEnumerable<Control> Flatten(Control control)
        {
            yield return control;
            foreach (Control child in control.Controls)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }

        private sealed class FakeAuthenticationClient : IAppServerAuthenticationClient
        {
            public string ApiKey { get; private set; }

            public string CanceledLoginId { get; private set; }

            public int AuthenticationStateReadCount { get; private set; }

            public string ChatGptAuthUrl { get; set; }

            public string DeviceCodeVerificationUrl { get; set; }

            public string DeviceCodeUserCode { get; set; }

            public int DeviceCodeLoginStartCount { get; private set; }

            public Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
            {
                AuthenticationStateReadCount++;
                return Task.FromResult(new AppServerAuthenticationState
                {
                    RequiresOpenAiAuth = true
                });
            }

            public Task<AppServerLoginResult> StartChatGptLoginAsync()
            {
                return Task.FromResult(new AppServerLoginResult
                {
                    LoginId = "login-1",
                    AuthUrl = ChatGptAuthUrl
                });
            }

            public Task<AppServerLoginResult> StartChatGptDeviceCodeLoginAsync()
            {
                DeviceCodeLoginStartCount++;
                return Task.FromResult(new AppServerLoginResult
                {
                    LoginId = "device-login-1",
                    VerificationUrl = DeviceCodeVerificationUrl,
                    UserCode = DeviceCodeUserCode
                });
            }

            public Task<AppServerLoginResult> StartOpenAiApiKeyLoginAsync(string apiKey)
            {
                ApiKey = apiKey;
                return Task.FromResult(new AppServerLoginResult { LoginId = "login-1" });
            }

            public Task<AppServerLoginCancelResult> CancelLoginAsync(string loginId)
            {
                CanceledLoginId = loginId;
                return Task.FromResult(new AppServerLoginCancelResult { Status = "canceled" });
            }

            public Task LogoutAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerRateLimitsSnapshot> GetRateLimitsAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerAccountTokenUsageSnapshot> GetAccountTokenUsageAsync(CancellationToken cancellationToken = default(CancellationToken))
            {
                throw new NotSupportedException();
            }

            public Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(string idempotencyKey, CancellationToken cancellationToken = default(CancellationToken))
            {
                throw new NotSupportedException();
            }

            public Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(string idempotencyKey, string creditId, CancellationToken cancellationToken = default(CancellationToken))
            {
                throw new NotSupportedException();
            }
        }

        private sealed class FakeScreenReaderService : IScreenReaderService
        {
            public List<string> Messages { get; } = new List<string>();
            public bool IsAvailable { get { return true; } }
            public bool IsInitialized { get { return true; } }
            public string LastError { get { return null; } }
            public void Dispose() { }
            public bool EnsureInitialized() { return true; }
            public bool IsReaderRunning() { return true; }
            public int GetReaderState() { return 1; }
            public bool Speak(string text, bool useSlave, bool append, bool allowBreak) { Messages.Add(text); return true; }
            public bool Stop(bool useSlave) { return true; }
        }
    }
}
