using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class UserCenterFormRateLimitDisplayTests
    {
        [Test]
        [Apartment(ApartmentState.STA)]
        public void ShowingUserCenterObservesAccountWithoutForcingTokenRefresh()
        {
            var client = new RecordingAuthenticationClient();
            using (var form = new UserCenterForm(client, client, CreateChatGptState()))
            {
                form.Show();
                var deadline = DateTime.UtcNow.AddSeconds(2);
                while (client.RefreshTokenValues.Count == 0 && DateTime.UtcNow < deadline)
                {
                    System.Windows.Forms.Application.DoEvents();
                    Thread.Sleep(10);
                }

                Assert.That(client.RefreshTokenValues, Is.EqualTo(new[] { false }));
                var details = (ListBox)typeof(UserCenterForm)
                    .GetField("_detailsListBox", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(form);
                Assert.That(details.Items.Cast<object>(), Has.Some.Contains("reader@example.test"));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void RefreshingUserCenterDoesNotSpeakProgressOrCompletion()
        {
            var client = new RecordingAuthenticationClient();
            var screenReader = new RecordingScreenReaderService();
            using (var form = new UserCenterForm(client, client, CreateChatGptState(), screenReader))
            {
                form.Show();
                var refreshButton = GetField<Button>(form, "_refreshButton");
                WaitUntil(() => client.RefreshTokenValues.Count > 0 && refreshButton.Enabled);

                Assert.That(screenReader.SpokenTexts, Is.Empty);
            }
        }

        [Test]
        public void BuildDetailLines_OrdersCodexRateLimitBeforeModelSpecificLimits()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
            var snapshot = new AppServerRateLimitsSnapshot();
            snapshot.Buckets.Add(new AppServerRateLimitBucket
            {
                LimitId = "model:gpt-5",
                LimitName = "GPT-5",
                Primary = CreateWindow(14M, 300)
            });
            snapshot.Buckets.Add(new AppServerRateLimitBucket
            {
                LimitId = "codex",
                LimitName = "Codex",
                Primary = CreateWindow(23M, 300)
            });

            var lines = BuildDetailLines(snapshot, null);

            var codexIndex = lines.FindIndex(line => line.Contains("77%"));
            var modelIndex = lines.FindIndex(line => line.Contains("86%"));
            Assert.That(codexIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(modelIndex, Is.GreaterThan(codexIndex));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [TestCase("62140.9876500000")]
        [TestCase("62140.0000000000")]
        [SetCulture("zh-CN")]
        [SetUICulture("zh-CN")]
        public void BuildDetailLines_ShowsCreditBalanceAlongsideBothQuotaWindows(string balance)
        {
            var snapshot = new AppServerRateLimitsSnapshot();
            snapshot.Buckets.Add(new AppServerRateLimitBucket
            {
                LimitId = "codex",
                Primary = CreateWindow(23M, 300),
                Secondary = CreateWindow(14M, 10080),
                Credits = new AppServerCreditsInfo { HasCredits = true, Balance = balance }
            });

            var lines = BuildDetailLines(snapshot, null);

            var primaryIndex = lines.FindIndex(line => line.Contains("77%"));
            var secondaryIndex = lines.FindIndex(line => line.Contains("86%"));
            var creditIndex = lines.FindIndex(line => line == "积分：62140");
            Assert.That(primaryIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(secondaryIndex, Is.GreaterThan(primaryIndex));
            Assert.That(creditIndex, Is.GreaterThan(secondaryIndex));
        }

        [Test]
        public void BuildDetailLines_AppendsTokenUsageAfterQuotaLines()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            var originalUiCulture = CultureInfo.CurrentUICulture;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            try
            {
            var snapshot = new AppServerRateLimitsSnapshot();
            snapshot.Buckets.Add(new AppServerRateLimitBucket
            {
                LimitName = "Codex",
                Primary = CreateWindow(14M, 300)
            });
            var usage = new AppServerAccountTokenUsageSnapshot { LifetimeTokens = 230000000L };

            var lines = BuildDetailLines(snapshot, usage);

            Assert.That(lines.FindIndex(line => line.StartsWith("Lifetime tokens", StringComparison.Ordinal)), Is.GreaterThan(0));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
                CultureInfo.CurrentUICulture = originalUiCulture;
            }
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        [Apartment(ApartmentState.STA)]
        public void UserCenterForm_OffersAccessibleManualQuotaResetControls()
        {
            var source = RepositorySource.ReadSourceFile("UserCenterForm.cs");

            Assert.That(source, Does.Contain("TokenCountFormatter.Format(value)"));
            using (var form = new UserCenterForm(
                       new RecordingAuthenticationClient(),
                       new RecordingAuthenticationClient(),
                       CreateChatGptState()))
            {
                var creditSelector = GetField<ComboBox>(form, "_resetCreditComboBox");
                var resetButton = GetField<Button>(form, "_resetQuotaButton");
                var closeButton = GetField<Button>(form, "_closeButton");

                Assert.That(creditSelector.DropDownStyle, Is.EqualTo(ComboBoxStyle.DropDownList));
                Assert.That(creditSelector.AccessibleName, Is.Not.Empty);
                Assert.That(creditSelector.TabIndex, Is.EqualTo(2));
                Assert.That(resetButton.AccessibleName, Is.Not.Empty);
                Assert.That(resetButton.UseMnemonic, Is.True);
                Assert.That(resetButton.Text, Does.Contain("&"));
                Assert.That(resetButton.TabIndex, Is.EqualTo(3));
                Assert.That(closeButton.TabIndex, Is.EqualTo(4));
            }
        }

        [Test]
        [Apartment(ApartmentState.STA)]
        public void ConfirmedManualResetConsumesTheSelectedOfficialResetCredit()
        {
            var client = new RecordingAuthenticationClient
            {
                RateLimits = CreateRateLimitsWithResetCredits()
            };
            var screenReader = new RecordingScreenReaderService();
            using (var form = new ConfirmingUserCenterForm(client, screenReader))
            {
                form.Show();
                var resetButton = GetField<Button>(form, "_resetQuotaButton");
                WaitUntil(() => resetButton.Enabled);
                var creditSelector = GetField<ComboBox>(form, "_resetCreditComboBox");
                creditSelector.SelectedIndex = 2;
                var spokenBeforeReset = screenReader.SpokenTexts.Count;

                resetButton.PerformClick();

                WaitUntil(() => client.ConsumedCreditId != null);
                Assert.That(form.ConfirmedCreditId, Is.EqualTo("credit-2"));
                Assert.That(client.ConsumedCreditId, Is.EqualTo("credit-2"));
                Assert.That(Guid.TryParse(client.ConsumedIdempotencyKey, out _), Is.True);
                var finalStatus = GetField<Label>(form, "_statusLabel").Text;
                Assert.That(finalStatus, Is.Not.Empty);
                Assert.That(screenReader.SpokenTexts.Count, Is.GreaterThanOrEqualTo(spokenBeforeReset + 2));
                Assert.That(screenReader.SpokenTexts.Last(), Is.EqualTo(finalStatus));
            }
        }

        private static AppServerRateLimitWindowInfo CreateWindow(decimal usedPercent, int durationMinutes)
        {
            return new AppServerRateLimitWindowInfo
            {
                UsedPercent = usedPercent,
                WindowDurationMinutes = durationMinutes,
                ResetsAtLocalTime = new DateTime(2026, 6, 5, 18, 3, 0)
            };
        }

        private static AppServerAuthenticationState CreateChatGptState()
        {
            return new AppServerAuthenticationState
            {
                HasAccount = true,
                IsAuthenticated = true,
                RequiresOpenAiAuth = true,
                AuthMode = "chatgpt",
                AccountType = "chatgpt",
                Email = "reader@example.test",
                PlanType = "plus"
            };
        }

        private static AppServerRateLimitsSnapshot CreateRateLimitsWithResetCredits()
        {
            var snapshot = new AppServerRateLimitsSnapshot
            {
                RateLimitResetCreditsAvailableCount = 2,
                ResetCredits = new List<AppServerRateLimitResetCredit>()
            };
            snapshot.ResetCredits.Add(new AppServerRateLimitResetCredit
            {
                Id = "credit-1",
                Status = "available",
                ExpiresAtLocalTime = new DateTime(2026, 8, 21, 12, 0, 0)
            });
            snapshot.ResetCredits.Add(new AppServerRateLimitResetCredit
            {
                Id = "credit-2",
                Status = "available",
                ExpiresAtLocalTime = new DateTime(2026, 8, 22, 12, 0, 0)
            });
            return snapshot;
        }

        private static void WaitUntil(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(2);
            while (!condition() && DateTime.UtcNow < deadline)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(10);
            }

            Assert.That(condition(), Is.True);
        }

        private static TControl GetField<TControl>(UserCenterForm form, string fieldName)
            where TControl : Control
        {
            var field = typeof(UserCenterForm).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (TControl)field.GetValue(form);
        }

        private sealed class RecordingAuthenticationClient : IAppServerAuthenticationClient, IAppServerConnectionClient
        {
            public event EventHandler<AppServerEventArgs> EventReceived
            {
                add { }
                remove { }
            }

            public AppServerProtocolCapabilities ProtocolCapabilities { get; } = new AppServerProtocolCapabilities();

            public List<bool> RefreshTokenValues { get; } = new List<bool>();

            public AppServerRateLimitsSnapshot RateLimits { get; set; } = new AppServerRateLimitsSnapshot();

            public string ConsumedCreditId { get; private set; }

            public string ConsumedIdempotencyKey { get; private set; }

            public Task ConnectAsync()
            {
                return Task.CompletedTask;
            }

            public Task RestartAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
            {
                RefreshTokenValues.Add(refreshToken);
                return Task.FromResult(refreshToken ? new AppServerAuthenticationState() : CreateChatGptState());
            }

            public Task<AppServerRateLimitsSnapshot> GetRateLimitsAsync()
            {
                return Task.FromResult(RateLimits);
            }

            public Task<AppServerAccountTokenUsageSnapshot> GetAccountTokenUsageAsync(
                CancellationToken cancellationToken = default(CancellationToken))
            {
                return Task.FromResult(new AppServerAccountTokenUsageSnapshot());
            }

            public Task<AppServerLoginResult> StartChatGptLoginAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerLoginResult> StartChatGptDeviceCodeLoginAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerLoginResult> StartOpenAiApiKeyLoginAsync(string apiKey)
            {
                throw new NotSupportedException();
            }

            public Task<AppServerLoginCancelResult> CancelLoginAsync(string loginId)
            {
                throw new NotSupportedException();
            }

            public Task LogoutAsync()
            {
                throw new NotSupportedException();
            }

            public Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
                string idempotencyKey,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                throw new NotSupportedException();
            }

            public Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
                string idempotencyKey,
                string creditId,
                CancellationToken cancellationToken = default(CancellationToken))
            {
                ConsumedIdempotencyKey = idempotencyKey;
                ConsumedCreditId = creditId;
                return Task.FromResult(new AppServerRateLimitResetConsumeResult { Outcome = "reset" });
            }
        }

        private sealed class ConfirmingUserCenterForm : UserCenterForm
        {
            public ConfirmingUserCenterForm(
                RecordingAuthenticationClient client,
                IScreenReaderService screenReaderService)
                : base(client, client, CreateChatGptState(), screenReaderService)
            {
            }

            public string ConfirmedCreditId { get; private set; }

            protected override bool ConfirmResetQuota(AppServerRateLimitResetCredit selectedCredit)
            {
                ConfirmedCreditId = selectedCredit == null ? null : selectedCredit.Id;
                return true;
            }
        }

        private sealed class RecordingScreenReaderService : IScreenReaderService
        {
            public bool IsAvailable => true;

            public bool IsInitialized => true;

            public string LastError => null;

            public List<string> SpokenTexts { get; } = new List<string>();

            public bool EnsureInitialized()
            {
                return true;
            }

            public bool IsReaderRunning()
            {
                return true;
            }

            public int GetReaderState()
            {
                return 1;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                SpokenTexts.Add(text);
                return true;
            }

            public bool Stop(bool useSlave)
            {
                return true;
            }

            public void Dispose()
            {
            }
        }

        private static List<string> BuildDetailLines(
            AppServerRateLimitsSnapshot snapshot,
            AppServerAccountTokenUsageSnapshot usage)
        {
            var method = typeof(UserCenterForm).GetMethod(
                "BuildDetailLines",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(AppServerAuthenticationState),
                    typeof(AppServerRateLimitsSnapshot),
                    typeof(AppServerAccountTokenUsageSnapshot),
                    typeof(DateTime)
                },
                null);
            Assert.That(method, Is.Not.Null);
            var state = new AppServerAuthenticationState
            {
                IsAuthenticated = true,
                AuthMode = "chatgpt",
                AccountType = "ChatGPT account",
                Email = "demo@example.com",
                PlanType = "pro"
            };
            return (List<string>)method.Invoke(
                null,
                new object[] { state, snapshot, usage, new DateTime(2026, 6, 5, 17, 0, 0) });
        }
    }
}
