using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public partial class UserCenterForm : Form
    {
        private readonly IAppServerAuthenticationClient _authenticationClient;
        private readonly IAppServerConnectionClient _connectionClient;
        private readonly IScreenReaderService _screenReaderService;
        private readonly ListBox _detailsListBox;
        private readonly Label _statusLabel;
        private readonly Button _refreshButton;
        private readonly ComboBox _resetCreditComboBox;
        private readonly Button _resetQuotaButton;
        private readonly Button _closeButton;

        private AppServerAuthenticationState _authenticationState;
        private AppServerRateLimitsSnapshot _rateLimitsSnapshot;
        private AppServerAccountTokenUsageSnapshot _tokenUsageSnapshot;
        private bool _isRefreshing;
        private bool _isResettingQuota;

        public UserCenterForm(
            IAppServerClient appServerClient,
            AppServerAuthenticationState initialAuthenticationState,
            IScreenReaderService screenReaderService = null)
            : this(appServerClient, appServerClient, initialAuthenticationState, screenReaderService)
        {
        }

        internal UserCenterForm(
            IAppServerAuthenticationClient authenticationClient,
            IAppServerConnectionClient connectionClient,
            AppServerAuthenticationState initialAuthenticationState)
            : this(authenticationClient, connectionClient, initialAuthenticationState, null)
        {
        }

        internal UserCenterForm(
            IAppServerAuthenticationClient authenticationClient,
            IAppServerConnectionClient connectionClient,
            AppServerAuthenticationState initialAuthenticationState,
            IScreenReaderService screenReaderService)
        {
            _authenticationClient = authenticationClient ?? throw new ArgumentNullException(nameof(authenticationClient));
            _connectionClient = connectionClient ?? throw new ArgumentNullException(nameof(connectionClient));
            _screenReaderService = screenReaderService;
            _authenticationState = initialAuthenticationState ?? new AppServerAuthenticationState();
            _rateLimitsSnapshot = new AppServerRateLimitsSnapshot();
            _tokenUsageSnapshot = null;

            Text = UiLocalization.Text("UserCenterForm.Title", "User Center");
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 520);
            Size = new Size(860, 620);
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _detailsListBox = CreateDetailsListBox();
            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = UiTheme.CreateUiFont(UiTheme.BodyFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                TabStop = false
            };
            _refreshButton = new Button
            {
                Text = UiLocalization.Text("UserCenterForm.Button.Refresh", "&Refresh"),
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.Refresh", "Refresh"),
                Width = 96,
                Height = 32,
                UseMnemonic = true,
                TabIndex = 1
            };
            UiTheme.ApplyButton(_refreshButton, UiGlyph.Refresh);
            _resetCreditComboBox = CreateResetCreditComboBox();
            _resetQuotaButton = new Button
            {
                Text = UiLocalization.Text("UserCenterForm.ResetQuota.Unavailable", "Reset &quota (unavailable)"),
                AccessibleName = UiLocalization.Text("UserCenterForm.Action.ResetQuota", "Reset quota"),
                Width = 168,
                Height = 32,
                Enabled = false,
                UseMnemonic = true,
                TabIndex = 3
            };
            UiTheme.ApplyButton(_resetQuotaButton, UiGlyph.Refresh);
            _closeButton = new Button
            {
                Text = UiLocalization.Text("UserCenterForm.Button.Close", "&Close"),
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.Close", "Close"),
                Width = 96,
                Height = 32,
                DialogResult = DialogResult.Cancel,
                UseMnemonic = true,
                TabIndex = 4
            };
            UiTheme.ApplyButton(_closeButton, UiGlyph.Cancel);
            FormInteractionConventions.ConfigureRefreshButton(_refreshButton);

            _refreshButton.Click += OnRefreshButtonClick;
            _resetQuotaButton.Click += OnResetQuotaButtonClick;
            CancelButton = _closeButton;
            KeyDown += UserCenterForm_KeyDown;

            BuildLayout();
            RefreshDetailsList();
            RefreshResetCreditSelector();
            RefreshResetQuotaButton();
            Shown += OnShown;
        }

        private async void OnShown(object sender, EventArgs e)
        {
            await RefreshDataAsync();
            this.BeginInvokeSafe(FocusDetailsList);
        }

        private void BuildLayout()
        {
            var mainPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(UiTheme.OuterPadding),
                TabStop = false
            };

            var titleLabel = new Label
            {
                Text = UiLocalization.Text("UserCenterForm.Header", "User Information"),
                Dock = DockStyle.Top,
                Height = 32,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = UiTheme.WindowBackColor,
                Padding = new Padding(0, 12, 0, 0),
                TabStop = false
            };

            var buttonsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 680,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = UiTheme.WindowBackColor,
                TabStop = false
            };

            buttonsPanel.Controls.Add(_refreshButton);
            buttonsPanel.Controls.Add(_resetCreditComboBox);
            buttonsPanel.Controls.Add(_resetQuotaButton);
            buttonsPanel.Controls.Add(_closeButton);
            footerPanel.Controls.Add(buttonsPanel);
            footerPanel.Controls.Add(_statusLabel);

            mainPanel.Controls.Add(_detailsListBox);
            mainPanel.Controls.Add(footerPanel);
            mainPanel.Controls.Add(titleLabel);

            Controls.Add(mainPanel);
        }

        private async void OnRefreshButtonClick(object sender, EventArgs e)
        {
            await RefreshDataAsync();
        }

        private async void OnResetQuotaButtonClick(object sender, EventArgs e)
        {
            await ResetQuotaAsync();
        }

        private async void UserCenterForm_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F5 && !_isRefreshing && !_isResettingQuota)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                await RefreshDataAsync();
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private async System.Threading.Tasks.Task RefreshDataAsync()
        {
            if (_isRefreshing || _isResettingQuota)
            {
                return;
            }

            var actionName = UiLocalization.Text("UserCenterForm.Action.RefreshUserInfo", "Refresh user info");
            if (!await EnsureAppServerAvailableForActionAsync(actionName))
            {
                return;
            }

            _isRefreshing = true;
            RefreshActionButtons();
            SetStatus(UiLocalization.Text("UserCenterForm.Status.Refreshing", "Refreshing..."));

            try
            {
                _authenticationState = await _authenticationClient.GetAuthenticationStateAsync(false);
                if (!IsChatGptAuthActive(_authenticationState))
                {
                    _rateLimitsSnapshot = new AppServerRateLimitsSnapshot();
                    _tokenUsageSnapshot = null;
                    RefreshDetailsList();
                    SetStatusAndSpeak(UiLocalization.Text(
                        "UserCenterForm.Status.UnavailableForApi",
                        "User Center is only available when signed in with a ChatGPT account."));
                    return;
                }

                try
                {
                    _rateLimitsSnapshot = await _authenticationClient.GetRateLimitsAsync();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Failed to refresh user center rate limits: {0}", ex.Message);
                    _rateLimitsSnapshot = new AppServerRateLimitsSnapshot();
                    _tokenUsageSnapshot = null;
                    RefreshDetailsList();
                    SetStatusAndSpeak(UiLocalization.Text(
                        "UserCenterForm.Status.RateLimitsUnavailable",
                        "User info refreshed. Quota information is temporarily unavailable."));
                    return;
                }

                try
                {
                    _tokenUsageSnapshot = await _authenticationClient.GetAccountTokenUsageAsync();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Failed to refresh user center token usage: {0}", ex.Message);
                    _tokenUsageSnapshot = null;
                }

                RefreshDetailsList();
                SetStatus(UiLocalization.Text("UserCenterForm.Status.RefreshCompleted", "Refresh completed."));
            }
            catch (Exception ex)
            {
                SetStatusAndSpeak(UiLocalization.Text("UserCenterForm.Status.RefreshFailed", "Refresh failed."));
                MessageBox.Show(
                    this,
                    ex.Message,
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                _isRefreshing = false;
                RefreshResetCreditSelector();
                RefreshActionButtons();
                this.BeginInvokeSafe(FocusDetailsList);
            }
        }

        private void RefreshDetailsList()
        {
            var selectedIndex = _detailsListBox.SelectedIndex;
            var lines = BuildDetailLines(
                _authenticationState,
                _rateLimitsSnapshot,
                _tokenUsageSnapshot,
                DateTime.Now);

            _detailsListBox.BeginUpdate();
            _detailsListBox.Items.Clear();
            foreach (var line in lines)
            {
                _detailsListBox.Items.Add(line);
            }

            if (_detailsListBox.Items.Count > 0)
            {
                if (selectedIndex >= 0 && selectedIndex < _detailsListBox.Items.Count)
                {
                    _detailsListBox.SelectedIndex = selectedIndex;
                }
                else
                {
                    _detailsListBox.SelectedIndex = 0;
                }
            }

            _detailsListBox.EndUpdate();
        }

        private void RefreshActionButtons()
        {
            _refreshButton.Enabled = !_isRefreshing && !_isResettingQuota;
            RefreshResetQuotaButton();
        }

        private void FocusDetailsList()
        {
            if (_detailsListBox.Items.Count > 0 && _detailsListBox.SelectedIndex < 0)
            {
                _detailsListBox.SelectedIndex = 0;
            }

            _detailsListBox.Focus();
        }

        private void SetStatus(string text)
        {
            _statusLabel.Text = text;
        }

        private void SetStatusAndSpeak(string text)
        {
            SetStatus(text);
            _screenReaderService?.Speak(text, true, false, true);
        }

        private async System.Threading.Tasks.Task<bool> EnsureAppServerAvailableForActionAsync(string actionName)
        {
            try
            {
                await _connectionClient.ConnectAsync();
                return true;
            }
            catch (Exception ex)
            {
                SetStatusAndSpeak(string.Format(
                    UiLocalization.Text(
                        "UserCenterForm.Status.ActionFailedDisconnected",
                        "{0} failed because App Server is not connected."),
                    actionName));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static List<string> BuildDetailLines(
            AppServerAuthenticationState authenticationState,
            AppServerRateLimitsSnapshot rateLimitsSnapshot,
            AppServerAccountTokenUsageSnapshot tokenUsageSnapshot)
        {
            return BuildDetailLines(authenticationState, rateLimitsSnapshot, tokenUsageSnapshot, DateTime.Now);
        }

        private static List<string> BuildDetailLines(
            AppServerAuthenticationState authenticationState,
            AppServerRateLimitsSnapshot rateLimitsSnapshot,
            AppServerAccountTokenUsageSnapshot tokenUsageSnapshot,
            DateTime now)
        {
            var lines = new List<string>();
            var state = authenticationState ?? new AppServerAuthenticationState();
            var isChatGptAuthActive = IsChatGptAuthActive(state);

            lines.Add(string.Format(
                UiLocalization.Text("UserCenterForm.Details.Email", "Email: {0}"),
                GetDisplayText(state.Email, UiLocalization.Text("UserCenterForm.Value.NotProvided", "(Not provided)"))));
            lines.Add(string.Format(
                UiLocalization.Text("UserCenterForm.Details.PlanType", "Plan: {0}"),
                GetDisplayText(GetPlanTypeForDisplay(state, rateLimitsSnapshot), UiLocalization.Text("UserCenterForm.Value.Unknown", "(Unknown)"))));

            var hasRateLimitLine = false;
            if (isChatGptAuthActive && rateLimitsSnapshot != null)
            {
                foreach (var bucket in BuildOrderedRateLimitBuckets(rateLimitsSnapshot.Buckets))
                {
                    foreach (var line in BuildRateLimitLines(bucket, now))
                    {
                        hasRateLimitLine = true;
                        lines.Add(line);
                    }
                }

                var countBeforeResetCreditExpiryLines = lines.Count;
                AddResetCreditExpiryLines(lines, rateLimitsSnapshot, now);
                hasRateLimitLine = hasRateLimitLine || lines.Count > countBeforeResetCreditExpiryLines;
            }

            if (!hasRateLimitLine)
            {
                lines.Add(UiLocalization.Text(
                    isChatGptAuthActive
                        ? "UserCenterForm.Details.NoRateLimitInfo"
                        : "UserCenterForm.Details.UnavailableForApi",
                    isChatGptAuthActive
                        ? "(No quota information yet)"
                        : "Current authentication is an API profile. User Center is only available for ChatGPT accounts."));
            }

            if (isChatGptAuthActive)
            {
                AddTokenUsageLines(lines, tokenUsageSnapshot);
            }

            return lines;
        }

        private static void AddTokenUsageLines(List<string> lines, AppServerAccountTokenUsageSnapshot usage)
        {
            if (lines == null || !HasTokenUsageData(usage))
            {
                return;
            }

            if (usage.LifetimeTokens.HasValue)
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.LifetimeTokens", "Lifetime tokens: {0}"),
                    FormatTokenCount(usage.LifetimeTokens.Value)));
            }

            if (usage.PeakDailyTokens.HasValue)
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.PeakDailyTokens", "Peak daily tokens: {0}"),
                    FormatTokenCount(usage.PeakDailyTokens.Value)));
            }

            if (usage.CurrentStreakDays.HasValue || usage.LongestStreakDays.HasValue)
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.UsageStreak", "Usage streak: current {0} days, longest {1} days"),
                    FormatNullableNumber(usage.CurrentStreakDays),
                    FormatNullableNumber(usage.LongestStreakDays)));
            }

            foreach (var bucket in GetRecentDailyBuckets(usage, 7))
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.DailyTokenUsage", "{0}: {1}"),
                    FormatUsageDate(bucket.StartDate),
                    FormatTokenCount(bucket.Tokens)));
            }
        }

        private static bool HasTokenUsageData(AppServerAccountTokenUsageSnapshot usage)
        {
            return usage != null &&
                   (usage.LifetimeTokens.HasValue ||
                    usage.PeakDailyTokens.HasValue ||
                    usage.CurrentStreakDays.HasValue ||
                    usage.LongestStreakDays.HasValue ||
                    (usage.DailyBuckets != null && usage.DailyBuckets.Count > 0));
        }

        private static IEnumerable<AppServerAccountTokenUsageDailyBucket> GetRecentDailyBuckets(
            AppServerAccountTokenUsageSnapshot usage,
            int limit)
        {
            var buckets = new List<AppServerAccountTokenUsageDailyBucket>();
            if (usage != null && usage.DailyBuckets != null)
            {
                foreach (var bucket in usage.DailyBuckets)
                {
                    if (bucket != null && !string.IsNullOrWhiteSpace(bucket.StartDate))
                    {
                        buckets.Add(bucket);
                    }
                }
            }

            buckets.Sort((left, right) => string.Compare(right.StartDate, left.StartDate, StringComparison.Ordinal));
            for (var i = 0; i < buckets.Count && i < limit; i++)
            {
                yield return buckets[i];
            }
        }

        private static string FormatTokenCount(long value)
        {
            return TokenCountFormatter.Format(value);
        }

        private static string FormatNullableNumber(long? value)
        {
            return value.HasValue
                ? value.Value.ToString("N0", CultureInfo.CurrentCulture)
                : UiLocalization.Text("UserCenterForm.Value.Unknown", "(Unknown)");
        }

        private static List<AppServerRateLimitBucket> BuildOrderedRateLimitBuckets(IEnumerable<AppServerRateLimitBucket> buckets)
        {
            var result = new List<AppServerRateLimitBucket>();
            foreach (var bucket in buckets ?? new AppServerRateLimitBucket[0])
            {
                if (bucket != null)
                {
                    result.Add(bucket);
                }
            }

            result.Sort(CompareRateLimitBucketsForDisplay);
            return result;
        }

        private static int CompareRateLimitBucketsForDisplay(AppServerRateLimitBucket left, AppServerRateLimitBucket right)
        {
            var leftRank = GetRateLimitBucketSortRank(left);
            var rightRank = GetRateLimitBucketSortRank(right);
            if (leftRank != rightRank)
            {
                return leftRank.CompareTo(rightRank);
            }

            var leftLabel = GetRateLimitBucketLabel(left);
            var rightLabel = GetRateLimitBucketLabel(right);
            var labelCompare = string.Compare(leftLabel, rightLabel, StringComparison.OrdinalIgnoreCase);
            if (labelCompare != 0)
            {
                return labelCompare;
            }

            return string.Compare(left != null ? left.LimitId : null, right != null ? right.LimitId : null, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetRateLimitBucketSortRank(AppServerRateLimitBucket bucket)
        {
            var text = BuildRateLimitBucketSearchText(bucket);
            if (IsSparkRateLimitBucketText(text))
            {
                return 1;
            }

            if (IsPrimaryCodexRateLimitBucketText(text))
            {
                return 0;
            }

            if (text.IndexOf("fast", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("quick", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("5.3", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("快速", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }

            if (text.IndexOf("general", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("default", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("common", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("shared", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("通用", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("gpt-5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                text.IndexOf("gpt 5", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 3;
            }

            return 4;
        }

        private static string BuildRateLimitBucketSearchText(AppServerRateLimitBucket bucket)
        {
            if (bucket == null)
            {
                return string.Empty;
            }

            return string.Join(
                " ",
                new[]
                {
                    bucket.LimitName ?? string.Empty,
                    bucket.LimitId ?? string.Empty
                });
        }

        private static bool IsSparkRateLimitBucketText(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   (text.IndexOf("spark", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("5.3", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsPrimaryCodexRateLimitBucketText(string text)
        {
            return !string.IsNullOrWhiteSpace(text) &&
                   (text.IndexOf("codex", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("all model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("all-model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("all_model", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    text.IndexOf("全模型", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsChatGptAuthActive(AppServerAuthenticationState state)
        {
            return state != null &&
                   state.IsAuthenticated &&
                   !string.Equals(state.AuthMode, "apikey", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetPlanTypeForDisplay(AppServerAuthenticationState state, AppServerRateLimitsSnapshot rateLimitsSnapshot)
        {
            var planType = state != null ? state.PlanType : null;
            if (!string.IsNullOrWhiteSpace(planType))
            {
                return planType;
            }

            if (rateLimitsSnapshot != null)
            {
                foreach (var bucket in rateLimitsSnapshot.Buckets)
                {
                    if (!string.IsNullOrWhiteSpace(bucket.PlanType))
                    {
                        return bucket.PlanType;
                    }
                }
            }

            return null;
        }

        private static List<string> BuildRateLimitLines(AppServerRateLimitBucket bucket, DateTime now)
        {
            var lines = new List<string>();
            if (bucket == null)
            {
                return lines;
            }

            if (bucket.Primary != null)
            {
                lines.Add(FormatWindowSentence(bucket.Primary, true, GetRateLimitBucketDisplayPrefix(bucket), now));
            }

            if (bucket.Secondary != null)
            {
                lines.Add(FormatWindowSentence(bucket.Secondary, false, GetRateLimitBucketDisplayPrefix(bucket), now));
            }

            if (bucket.Credits != null)
            {
                lines.Add(string.Format(
                    UiLocalization.Text("UserCenterForm.Details.Credits", "Credits: {0}"),
                    FormatCredits(bucket.Credits)));
            }

            return lines;
        }

        private static string FormatWindowSentence(
            AppServerRateLimitWindowInfo window,
            bool isPrimary,
            string prefix,
            DateTime now)
        {
            if (window == null)
            {
                return UiLocalization.Text("UserCenterForm.Details.NoCreditsInfo", "(No quota information)");
            }

            var remainingPercent = 100M - window.UsedPercent;
            if (remainingPercent < 0M)
            {
                remainingPercent = 0M;
            }

            if (remainingPercent > 100M)
            {
                remainingPercent = 100M;
            }

            var label = GetWindowLabel(window.WindowDurationMinutes, isPrimary);
            var resetText = FormatResetTimeWithCountdown(window.ResetsAtLocalTime, now);
            var sentence = string.Format(
                UiLocalization.Text(
                    "UserCenterForm.Details.WindowSentence",
                    "{0} remaining {1:0.##}%, resets {2}"),
                label,
                remainingPercent,
                resetText);
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return sentence;
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Details.WindowSentenceWithPrefix", "{0}: {1}"),
                prefix,
                sentence);
        }

        private static string FormatCredits(AppServerCreditsInfo credits)
        {
            if (credits == null)
            {
                return UiLocalization.Text("UserCenterForm.Details.NoCreditsInfo", "(No quota information)");
            }

            if (credits.Unlimited)
            {
                return UiLocalization.Text("UserCenterForm.Value.Unlimited", "Unlimited");
            }

            if (!credits.HasCredits)
            {
                return UiLocalization.Text("UserCenterForm.Value.Unavailable", "Unavailable");
            }

            if (string.IsNullOrWhiteSpace(credits.Balance))
            {
                return UiLocalization.Text("UserCenterForm.Value.Available", "Available");
            }

            return decimal.TryParse(credits.Balance, NumberStyles.Number, CultureInfo.InvariantCulture, out var balance)
                ? decimal.Truncate(balance).ToString("0", CultureInfo.CurrentCulture)
                : credits.Balance;
        }

        private static string GetRateLimitBucketLabel(AppServerRateLimitBucket bucket)
        {
            if (bucket == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(bucket.LimitName))
            {
                return bucket.LimitName.Trim();
            }

            return BuildLimitIdDisplayName(bucket.LimitId);
        }

        private static string BuildLimitIdDisplayName(string limitId)
        {
            if (string.IsNullOrWhiteSpace(limitId))
            {
                return string.Empty;
            }

            var value = limitId.Trim();
            var colonIndex = value.LastIndexOf(':');
            if (colonIndex >= 0 && colonIndex < value.Length - 1)
            {
                value = value.Substring(colonIndex + 1);
            }

            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return value
                .Replace('_', ' ');
        }

        private static string GetRateLimitBucketDisplayPrefix(AppServerRateLimitBucket bucket)
        {
            var text = BuildRateLimitBucketSearchText(bucket);
            if (IsSparkRateLimitBucketText(text))
            {
                return UiLocalization.Text("UserCenterForm.RateLimitBucket.Spark", "Spark");
            }

            return string.Empty;
        }

        private static ListBox CreateDetailsListBox()
        {
            var listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.UserInfo", "User Information"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                IntegralHeight = false,
                ItemHeight = 28,
                TabIndex = 0
            };
            UiTheme.ApplyListBox(listBox);
            return listBox;
        }

        private static ComboBox CreateResetCreditComboBox()
        {
            var comboBox = new ComboBox
            {
                AccessibleName = UiLocalization.Text("UserCenterForm.Accessible.ResetCredit", "Reset credit"),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 320,
                TabIndex = 2
            };
            UiTheme.ApplyComboBox(comboBox);
            return comboBox;
        }

        private static string GetWindowLabel(int? durationMinutes, bool isPrimary)
        {
            if (!durationMinutes.HasValue || durationMinutes.Value <= 0)
            {
                return isPrimary
                    ? UiLocalization.Text("UserCenterForm.Window.PrimaryCycle", "Current cycle")
                    : UiLocalization.Text("UserCenterForm.Window.SecondaryCycle", "Additional cycle");
            }

            if (durationMinutes.Value == 60)
            {
                return UiLocalization.Text("UserCenterForm.Window.OneHour", "1 hour");
            }

            if (durationMinutes.Value == 300)
            {
                return UiLocalization.Text("UserCenterForm.Window.FiveHours", "5 hours");
            }

            if (durationMinutes.Value == 1440)
            {
                return UiLocalization.Text("UserCenterForm.Window.Daily", "Daily");
            }

            if (durationMinutes.Value == 10080)
            {
                return UiLocalization.Text("UserCenterForm.Window.Weekly", "Weekly");
            }

            if (durationMinutes.Value % 1440 == 0)
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Window.Days", "{0} days"),
                    durationMinutes.Value / 1440);
            }

            if (durationMinutes.Value % 60 == 0)
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Window.Hours", "{0} hours"),
                    durationMinutes.Value / 60);
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Window.Minutes", "{0} minutes"),
                durationMinutes.Value);
        }

        private static string FormatResetTime(DateTime? time)
        {
            if (!time.HasValue)
            {
                return UiLocalization.Text("UserCenterForm.Time.Unknown", "Unknown time");
            }

            var value = time.Value;
            if (value.Minute == 0)
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Time.MonthDayHour", "{0}/{1} {2}:00"),
                    value.Month,
                    value.Day,
                    value.Hour);
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Time.MonthDayHourMinute", "{0}/{1} {2}:{3:00}"),
                value.Month,
                value.Day,
                value.Hour,
                value.Minute);
        }

        private static string FormatResetTimeWithCountdown(DateTime? time, DateTime now)
        {
            var timeText = FormatResetTime(time);
            if (!time.HasValue)
            {
                return timeText;
            }

            var countdown = FormatCountdown(time.Value - now);
            if (string.IsNullOrWhiteSpace(countdown))
            {
                return timeText;
            }

            return string.Format(
                UiLocalization.Text("UserCenterForm.Time.WithCountdown", "{0}, {1} later"),
                timeText,
                countdown);
        }

        private static string FormatCountdown(TimeSpan remaining)
        {
            if (remaining <= TimeSpan.Zero)
            {
                return UiLocalization.Text("UserCenterForm.Time.Expired", "expired");
            }

            var totalMinutes = (int)Math.Ceiling(remaining.TotalMinutes);
            var days = totalMinutes / (24 * 60);
            totalMinutes %= 24 * 60;
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            var parts = new List<string>();
            if (days > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("UserCenterForm.Time.CountdownDays", "{0} days"), days));
            }

            if (hours > 0)
            {
                parts.Add(string.Format(UiLocalization.Text("UserCenterForm.Time.CountdownHours", "{0} hours"), hours));
            }

            if (minutes > 0 || parts.Count == 0)
            {
                parts.Add(string.Format(UiLocalization.Text("UserCenterForm.Time.CountdownMinutes", "{0} minutes"), minutes));
            }

            return string.Concat(parts.ToArray());
        }

        private static void AddResetCreditExpiryLines(List<string> lines, AppServerRateLimitsSnapshot snapshot, DateTime now)
        {
            if (lines == null || snapshot == null || snapshot.ResetCredits == null)
            {
                return;
            }

            var creditNumber = 0;
            foreach (var credit in snapshot.ResetCredits)
            {
                if (credit == null ||
                    !string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase) ||
                    !credit.ExpiresAtLocalTime.HasValue)
                {
                    continue;
                }

                creditNumber++;
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    UiLocalization.Text("UserCenterForm.Details.ResetCreditExpires", "Reset credit {0} {1} expires"),
                    creditNumber,
                    FormatResetTimeWithCountdown(credit.ExpiresAtLocalTime, now)));
            }
        }

        private static string FormatUsageDate(string dateText)
        {
            DateTime value;
            if (!string.IsNullOrWhiteSpace(dateText) &&
                DateTime.TryParseExact(
                    dateText.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out value))
            {
                return string.Format(
                    UiLocalization.Text("UserCenterForm.Time.MonthDay", "{0}/{1}"),
                    value.Month,
                    value.Day);
            }

            return GetDisplayText(dateText, UiLocalization.Text("UserCenterForm.Time.Unknown", "Unknown time"));
        }

        private static string GetDisplayText(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }

    }
}
