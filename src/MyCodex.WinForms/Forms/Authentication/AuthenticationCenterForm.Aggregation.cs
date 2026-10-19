using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    // Aggregation for the current execution target: the listed providers' models appear in the
    // Codex model list, and the model chosen there decides which provider answers.
    public sealed partial class AuthenticationCenterForm
    {
        private readonly FlowLayoutPanel _aggregationRow;
        private readonly Label _aggregationLabel;
        private readonly Button _aggregationButton;
        private readonly Button _aggregationMemberButton;
        private readonly Button _aggregationCatalogButton;
        private readonly CheckBox _classicSubagentsCheckBox;
        private readonly MenuItem _aggregationMemberMenuItem;
        private readonly Func<Func<CancellationToken, Task<AuthenticationStackState>>, CancellationToken, Task<AuthenticationStackResult>> _changeStack;
        private bool _supportsAggregation;
        private AuthenticationStackState _stack;

        // Replaces the aggregation dialog in tests: returns the default and the other members, or null to cancel.
        internal Func<AuthenticationStackState, Tuple<string, IReadOnlyList<string>>> ChooseAggregation { get; set; }

        private bool AggregationOffered
        {
            get { return _supportsAggregation && _changeStack != null; }
        }

        private static CheckBox CreateClassicSubagentsCheckBox()
        {
            var caption = UiLocalization.Text("Aggregation.ClassicSubagents", "Classic sub-agent tools while aggregating (&K)");
            var checkBox = new CheckBox
            {
                Text = caption, AccessibleName = AuthenticationCenterUiText.Name(caption),
                AccessibleDescription = UiLocalization.Text("Aggregation.ClassicSubagents.Description", "Turn this on when a sub-agent on another provider cannot read its task. Tasks then pass as plain text, without follow-up and interrupt tools. Codex restarts to apply it."),
                AutoCheck = false, AutoSize = true, TabIndex = 3, UseMnemonic = true
            };
            UiTheme.ApplyCheckBox(checkBox);
            return checkBox;
        }

        private static string AggregationButtonText(bool active)
        {
            return active
                ? UiLocalization.Text("Aggregation.Button.Stop", "Stop aggregating (&M)")
                : UiLocalization.Text("Aggregation.Button.Start", "Aggregate &models…");
        }

        private static string MemberButtonText(bool member)
        {
            return member
                ? UiLocalization.Text("Aggregation.Button.Leave", "Remove from aggregation (&J)")
                : UiLocalization.Text("Aggregation.Button.Join", "Add to aggregation (&J)");
        }

        // Returns a note for the status line when the state could not be read.
        private async Task<string> ReadStackStateAsync()
        {
            _stack = null;
            if (!AggregationOffered) return null;
            try
            {
                _stack = await _client.ReadStackAsync(_lifetime.Token);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                return AuthenticationCenterUiText.Error(error);
            }
            return null;
        }

        private string AggregationColumnText(string providerId)
        {
            switch (AuthenticationStackPlanner.RoleOf(_stack, providerId))
            {
                case AuthenticationStackRole.Default:
                    return UiLocalization.Text("Aggregation.Role.Default", "Default");
                case AuthenticationStackRole.Member:
                    return UiLocalization.Text("Aggregation.Role.Member", "Added");
                default:
                    return string.Empty;
            }
        }

        private string AggregationStatusText()
        {
            if (_stack == null) return string.Empty;
            if (!_stack.Active)
            {
                return _stack.Members.Count == 0
                    ? UiLocalization.Text("Aggregation.Status.Off", "Aggregation is off.")
                    : string.Format(CultureInfo.CurrentCulture, UiLocalization.Text("Aggregation.Status.OffWithList", "Aggregation is off; {0} providers stay listed for next time."), _stack.Members.Count);
            }
            var text = string.Format(CultureInfo.CurrentCulture,
                UiLocalization.Text("Aggregation.Status.On", "Aggregating. Default: {0}; {1} more providers add {2} models."),
                ProviderName(_stack.DefaultProviderId), AuthenticationStackPlanner.PublishingProviders(_stack), AuthenticationStackPlanner.PublishedModels(_stack));
            var notice = NoticeText(_stack.Notice);
            return notice == null ? text : text + " " + notice;
        }

        private static string NoticeText(string notice)
        {
            switch (notice)
            {
                case "routeOwnsCatalog":
                    return UiLocalization.Text("Aggregation.Notice.RouteOwnsCatalog", "The default provider uses its own model catalog file, so the aggregated models are hidden. Select Use aggregated catalog.");
                case "officialModelsBundled":
                    return UiLocalization.Text("Aggregation.Notice.OfficialBundled", "The account's official model list is not available yet; the list bundled with Codex is used and may miss account-specific models.");
                case "officialModelsUnavailable":
                    return UiLocalization.Text("Aggregation.Notice.OfficialUnavailable", "The official model list is not available yet, so the aggregated models are not published. Refresh later.");
                case "officialModelsOutdated":
                    return UiLocalization.Text("Aggregation.Notice.OfficialOutdated", "This Codex version is too old for the official model list. Update Codex.");
                default:
                    return null;
            }
        }

        private string ProviderName(string providerId)
        {
            foreach (ListViewItem item in _providersList.Items)
            {
                if (item.Tag is AuthenticationProvider provider && provider.Id == providerId) return provider.Name;
            }
            return providerId;
        }

        private void UpdateAggregationControls()
        {
            var available = !_busy && _loaded && !_reconnectPending && _stack != null;
            var provider = SelectedProvider;
            var member = provider != null && AuthenticationStackPlanner.IsMember(_stack, provider.Id);
            _aggregationRow.Visible = AggregationOffered;
            _aggregationLabel.Text = AggregationStatusText();
            _aggregationButton.Text = AggregationButtonText(_stack != null && _stack.Active);
            _aggregationButton.AccessibleName = AuthenticationCenterUiText.Name(_aggregationButton.Text);
            _aggregationButton.Enabled = available;
            _aggregationMemberButton.Text = MemberButtonText(member);
            _aggregationMemberButton.AccessibleName = AuthenticationCenterUiText.Name(_aggregationMemberButton.Text);
            _aggregationMemberButton.Enabled = available && provider != null &&
                (member ? AuthenticationStackPlanner.CanLeave(_stack, provider.Id) : AuthenticationStackPlanner.CanJoin(_stack, provider.Id));
            _aggregationMemberMenuItem.Visible = AggregationOffered;
            _aggregationMemberMenuItem.Text = _aggregationMemberButton.Text;
            _aggregationMemberMenuItem.Enabled = _aggregationMemberButton.Enabled;
            _aggregationCatalogButton.Visible = _stack != null && _stack.Active && _stack.Notice == "routeOwnsCatalog";
            _aggregationCatalogButton.Enabled = available;
            _classicSubagentsCheckBox.Checked = _stack != null && _stack.ClassicSubagents;
            _classicSubagentsCheckBox.Enabled = available;
        }

        internal async Task ToggleAggregationAsync()
        {
            if (_busy || !_loaded || _reconnectPending || _stack == null || !AggregationOffered) return;
            if (_stack.Active)
            {
                await ChangeStackAsync(token => _client.ExitStackAsync(token), _aggregationButton,
                    state => UiLocalization.Text("Aggregation.Result.Stopped", "Aggregation stopped. Codex uses the default provider only; the list is kept."));
                return;
            }
            var choice = (ChooseAggregation ?? ShowAggregationDialog)(_stack);
            if (choice == null || string.IsNullOrEmpty(choice.Item1)) return;
            await ChangeStackAsync(token => _client.EnterStackAsync(choice.Item1, choice.Item2, token), _aggregationButton,
                state => UiLocalization.Text("Aggregation.Result.Started", "Aggregating. Choose a model marked with its provider in the model list."));
        }

        internal async Task ToggleSelectedMemberAsync()
        {
            var provider = SelectedProvider;
            if (_busy || !_loaded || _reconnectPending || _stack == null || provider == null || !_aggregationMemberButton.Enabled) return;
            var enable = !AuthenticationStackPlanner.IsMember(_stack, provider.Id);
            await ChangeStackAsync(token => _client.SetStackMemberAsync(provider.Id, enable, token), _aggregationMemberButton,
                state => enable
                    ? UiLocalization.Text("Aggregation.Result.Joined", "Provider added to aggregation.")
                    : UiLocalization.Text("Aggregation.Result.Left", "Provider removed from aggregation. Conversations that chose its models need another model."));
        }

        internal async Task AdoptAggregationCatalogAsync()
        {
            if (_busy || !_loaded || _reconnectPending || _stack == null || !_stack.Active) return;
            await ChangeStackAsync(token => _client.AdoptStackCatalogAsync(token), _aggregationButton,
                state => UiLocalization.Text("Aggregation.Result.CatalogAdopted", "The aggregated model catalog is in use."));
        }

        internal async Task ToggleClassicSubagentsAsync()
        {
            if (_busy || !_loaded || _reconnectPending || _stack == null) return;
            var enable = !_stack.ClassicSubagents;
            await ChangeStackAsync(token => _client.SetStackClassicSubagentsAsync(enable, token), _classicSubagentsCheckBox,
                state => enable
                    ? UiLocalization.Text("Aggregation.Result.ClassicOn", "Aggregated models use the classic sub-agent tools.")
                    : UiLocalization.Text("Aggregation.Result.ClassicOff", "Aggregated models use the current sub-agent tools."));
        }

        private async Task ChangeStackAsync(Func<CancellationToken, Task<AuthenticationStackState>> change, Control focus,
            Func<AuthenticationStackState, string> success)
        {
            _busy = true;
            UpdateActions();
            Announce(UiLocalization.Text("Aggregation.Status.Working", "Applying aggregation and restarting Codex…"));
            try
            {
                var result = await _changeStack(change, _lifetime.Token);
                if (IsDisposed) return;
                _stack = result.State;
                _reconnectPending |= !result.Reconnected;
                _busy = false;
                await RefreshAsync();
                if (!IsDisposed && _loaded)
                {
                    if (result.Reconnected) Announce(success(result.State));
                    else AnnounceError(UiLocalization.Text("AuthenticationCenter.Status.ReconnectFailed", "Global configuration is applied, but MyCodex could not reconnect. Retry the connection; the global change has not been rolled back."));
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                if (!IsDisposed)
                {
                    if (error is AuthenticationCenterException failure && failure.Code == "operation_unknown")
                        _reconnectPending = true;
                    AnnounceError(AuthenticationCenterUiText.Error(error));
                }
            }
            finally
            {
                _busy = false;
                if (!IsDisposed)
                {
                    UpdateActions();
                    if (focus.Visible && focus.Enabled) focus.Focus();
                    else _providersList.Focus();
                }
            }
        }

        private Tuple<string, IReadOnlyList<string>> ShowAggregationDialog(AuthenticationStackState state)
        {
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ListViewItem item in _providersList.Items)
            {
                if (item.Tag is AuthenticationProvider provider) names[provider.Id] = provider.Name;
            }
            using (var form = new AuthenticationAggregationForm(state, names, _currentProviderId))
            {
                return form.ShowDialog(this) == DialogResult.OK && form.DefaultProviderId != null
                    ? Tuple.Create(form.DefaultProviderId, form.MemberIds) : null;
            }
        }
    }
}
