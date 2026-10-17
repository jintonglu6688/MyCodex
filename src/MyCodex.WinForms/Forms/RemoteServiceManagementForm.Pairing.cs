using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public sealed partial class RemoteServiceManagementForm
    {
        private readonly IScreenReaderService _screenReaderService;
        private readonly ISecureRemotePairingWorkflow _pairingWorkflow;
        private readonly System.Windows.Forms.Timer _pairingPollTimer;
        private readonly Dictionary<string, VerifiedPairingClaim> _verifiedPairingClaims =
            new Dictionary<string, VerifiedPairingClaim>(StringComparer.Ordinal);
        private ActivePairingInvite _activePairingInvite;
        private DateTime _pairingPollingExpiresAtUtc;
        private bool _isPollingPairingClaims;

        internal Func<VerifiedPairingClaim, bool?> PairingConfirmationForTests { get; set; }

        internal string PendingClaimSummaryForTests
        {
            get
            {
                return string.Join(
                    " ",
                    _devicesListView.Items.Cast<ListViewItem>()
                        .SelectMany(item => item.SubItems.Cast<ListViewItem.ListViewSubItem>())
                        .Select(item => item.Text)
                        .ToArray());
            }
        }

        internal int PairingPollingIntervalForTests
        {
            get { return _pairingPollTimer.Interval; }
        }

        internal bool PairingPollingEnabledForTests
        {
            get { return _pairingPollTimer.Enabled; }
        }

        internal Task PollPairingClaimsForTests()
        {
            return PollPairingClaimsAsync();
        }

        internal Task ReviewSelectedPairingClaimForTests()
        {
            return ReviewSelectedPairingClaimAsync();
        }

        private async Task PollPairingClaimsAsync()
        {
            if (_activePairingInvite == null ||
                _isPollingPairingClaims ||
                _lifetimeCancellation.IsCancellationRequested)
            {
                return;
            }

            if (DateTime.UtcNow >= _pairingPollingExpiresAtUtc)
            {
                StopPairingPolling();
                return;
            }

            _isPollingPairingClaims = true;
            try
            {
                var claims = await _relayClient.ListPairingClaimsAsync(
                    BuildClientConfig(),
                    _activePairingInvite.Package.HostId,
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                var changed = false;
                var newClaims = new List<VerifiedPairingClaim>();
                var pendingClaims = claims.Where(item =>
                    string.Equals(item.Status, "pending", StringComparison.Ordinal)).ToList();
                var pendingIds = new HashSet<string>(
                    pendingClaims.Select(item => item.ClaimId),
                    StringComparer.Ordinal);
                foreach (var staleClaimId in _verifiedPairingClaims.Keys
                    .Where(item => !pendingIds.Contains(item))
                    .ToArray())
                {
                    _verifiedPairingClaims.Remove(staleClaimId);
                    changed = true;
                }

                foreach (var opaqueClaim in pendingClaims)
                {
                    if (_verifiedPairingClaims.ContainsKey(opaqueClaim.ClaimId))
                    {
                        continue;
                    }

                    VerifiedPairingClaim verified;
                    try
                    {
                        verified = _pairingWorkflow.VerifyClaim(_activePairingInvite, opaqueClaim);
                    }
                    catch (InvalidOperationException)
                    {
                        continue;
                    }

                    _verifiedPairingClaims.Add(verified.Claim.ClaimId, verified);
                    newClaims.Add(verified);
                    changed = true;
                }

                if (changed)
                {
                    BindPendingPairingClaims();
                    foreach (var newClaim in newClaims.OrderBy(item => item.Claim.CreatedAt))
                    {
                        AnnouncePairingClaim(newClaim);
                        var handled = await ReviewPairingClaimAsync(newClaim).ConfigureAwait(true);
                        if (!handled || _activePairingInvite == null)
                        {
                            break;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception)
            {
                var text = UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.PairingRefreshFailed",
                    "Pairing requests could not be refreshed.");
                if (!string.Equals(_serverStateLabel.Text, text, StringComparison.Ordinal))
                {
                    _serverStateLabel.Text = text;
                }
            }
            finally
            {
                _isPollingPairingClaims = false;
                RefreshButtons();
            }
        }

        private Task ReviewSelectedPairingClaimAsync()
        {
            var verified = GetSelectedPairingClaim();
            return verified == null || _isBusy
                ? Task.CompletedTask
                : ReviewPairingClaimAsync(verified);
        }

        private async Task<bool> ReviewPairingClaimAsync(VerifiedPairingClaim verified)
        {
            SelectPairingClaim(verified);
            var confirmation = PairingConfirmationForTests != null
                ? PairingConfirmationForTests(verified)
                : ShowPairingConfirmation(verified);
            if (!confirmation.HasValue)
            {
                FocusControl(_devicesListView);
                return false;
            }

            if (!confirmation.Value)
            {
                await RejectPairingClaimAsync(verified).ConfigureAwait(true);
                return true;
            }

            try
            {
                SetBusy(true);
                await _pairingWorkflow.ApprovePairingClaimAsync(
                    BuildClientConfig(),
                    verified,
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                await EnsureHostRealtimeConnectionAsync().ConfigureAwait(true);
                _verifiedPairingClaims.Remove(verified.Claim.ClaimId);
                StopPairingPolling();
                var devices = await _relayClient.ListDevicesAsync(
                    BuildClientConfig(),
                    verified.Claim.HostId,
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                BindDevices(devices);
                AnnounceImportant(UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.PairingApproved",
                    "Pairing approved."));
                FocusControl(_devicesListView.Items.Count > 0
                    ? (Control)_devicesListView
                    : _refreshDevicesButton);
                return true;
            }
            catch (Exception ex)
            {
                ShowError(
                    UiLocalization.Text(
                        "RemoteServiceManagementForm.Status.PairingApprovalFailed",
                        "Pairing could not be approved.") +
                    " " +
                    RelaySecretRedactor.Redact(ex.Message));
                FocusControl(_devicesListView);
                return true;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SelectPairingClaim(VerifiedPairingClaim verified)
        {
            var item = _devicesListView.Items.Cast<ListViewItem>()
                .FirstOrDefault(value =>
                    value.Tag is VerifiedPairingClaim claim &&
                    string.Equals(
                        claim.Claim.ClaimId,
                        verified.Claim.ClaimId,
                        StringComparison.Ordinal));
            if (item == null)
            {
                return;
            }

            item.Selected = true;
            item.Focused = true;
        }

        private async Task RejectPairingClaimAsync(VerifiedPairingClaim verified)
        {
            try
            {
                SetBusy(true);
                await _relayClient.RejectPairingClaimAsync(
                    BuildClientConfig(),
                    verified.Claim.HostId,
                    verified.Claim.ClaimId,
                    verified.Claim.DeviceId,
                    _lifetimeCancellation.Token).ConfigureAwait(true);
                _verifiedPairingClaims.Remove(verified.Claim.ClaimId);
                BindPendingPairingClaims();
                AnnounceImportant(UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.PairingRejected",
                    "Pairing rejected."));
                FocusControl(_devicesListView.Items.Count > 0
                    ? (Control)_devicesListView
                    : _refreshDevicesButton);
            }
            catch (Exception ex)
            {
                ShowError(
                    UiLocalization.Text(
                        "RemoteServiceManagementForm.Status.PairingRejectionFailed",
                        "Pairing could not be rejected.") +
                    " " +
                    RelaySecretRedactor.Redact(ex.Message));
                FocusControl(_devicesListView);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool? ShowPairingConfirmation(VerifiedPairingClaim verified)
        {
            using (var form = new RemotePairingConfirmationForm(
                _activePairingInvite.Package.HostName,
                verified))
            {
                var result = form.ShowDialog(this);
                if (result == DialogResult.OK)
                {
                    return true;
                }

                return result == DialogResult.No ? (bool?)false : null;
            }
        }

        private void BindPendingPairingClaims()
        {
            var selectedClaimId = GetSelectedPairingClaim()?.Claim.ClaimId;
            var deviceItems = _devicesListView.Items.Cast<ListViewItem>()
                .Where(item => item.Tag is RelayDeviceSummary)
                .ToArray();
            _devicesListView.BeginUpdate();
            try
            {
                _devicesListView.Items.Clear();
                _devicesListView.Items.AddRange(deviceItems);
                foreach (var verified in _verifiedPairingClaims.Values.OrderBy(item => item.Claim.CreatedAt))
                {
                    var item = new ListViewItem(verified.Claim.DeviceName ?? verified.Claim.DeviceId);
                    item.SubItems.Add(_activePairingInvite.Package.HostName ?? _activePairingInvite.Package.HostId);
                    item.SubItems.Add(verified.Claim.Platform ?? string.Empty);
                    item.SubItems.Add(FormatDigitReadableCode(verified.ShortCode));
                    item.SubItems.Add(UiLocalization.Text(
                        "RemoteServiceManagementForm.PairingStatus.Pending",
                        "Pending"));
                    item.Tag = verified;
                    _devicesListView.Items.Add(item);
                    if (string.Equals(verified.Claim.ClaimId, selectedClaimId, StringComparison.Ordinal))
                    {
                        item.Selected = true;
                    }
                }
            }
            finally
            {
                _devicesListView.EndUpdate();
            }

            RefreshButtons();
        }

        private VerifiedPairingClaim GetSelectedPairingClaim()
        {
            return _devicesListView.SelectedItems.Count == 1
                ? _devicesListView.SelectedItems[0].Tag as VerifiedPairingClaim
                : null;
        }

        private void AnnouncePairingClaim(VerifiedPairingClaim verified)
        {
            AnnounceImportant(string.Format(
                UiLocalization.Text(
                    "RemoteServiceManagementForm.Status.NewPairingClaim.Template",
                    "Pairing request from {0}. Verification code {1}."),
                verified.Claim.DeviceName ?? verified.Claim.DeviceId,
                FormatDigitReadableCode(verified.ShortCode)));
        }

        private void AnnounceImportant(string text)
        {
            _serverStateLabel.Text = text;
            if (_screenReaderService != null)
            {
                _screenReaderService.Speak(text, false, false, true);
            }
        }

        private void StopPairingPolling()
        {
            _pairingPollTimer.Stop();
            _activePairingInvite = null;
            _isPollingPairingClaims = false;
            _verifiedPairingClaims.Clear();
            ClearPendingPairingRows();
        }

        private void ClearPendingPairingRows()
        {
            foreach (var item in _devicesListView.Items.Cast<ListViewItem>()
                .Where(value => value.Tag is VerifiedPairingClaim)
                .ToArray())
            {
                _devicesListView.Items.Remove(item);
            }

            RefreshButtons();
        }

        private static DateTime UnixMillisecondsToUtc(long value)
        {
            if (value <= 0)
            {
                throw new InvalidOperationException("The pairing expiration is invalid.");
            }

            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddMilliseconds(value);
        }

        private static string FormatDigitReadableCode(string value)
        {
            return string.Join(
                " ",
                (value ?? string.Empty).Select(character => character.ToString()).ToArray());
        }
    }
}
