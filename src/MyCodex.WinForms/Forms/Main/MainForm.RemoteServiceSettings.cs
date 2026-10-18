using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.Interfaces;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly RemotePendingInteractionState _pendingRemoteInteractions = new RemotePendingInteractionState();
        private bool _remoteSettingsRefreshBusy;

        private void PrepareOwnedRemoteDisplayName(IRemoteServiceConfigStore store, RemoteServiceConfigSnapshot snapshot)
        {
            if (_targetCatalog == null || _fixedTarget == null) return;
            var name = TargetRemoteDisplayNameComposer.Compose(Environment.MachineName, _targetCatalog.GetTarget(_fixedTarget.Id));
            if (string.Equals(snapshot.ThisPcName, name, StringComparison.Ordinal)) return;
            snapshot.ThisPcName = name;
            store.Save(snapshot);
        }

        private async Task RefreshSharedRemoteServiceAsync()
        {
            if (_remoteSettingsRefreshBusy || !_targetStartupComplete || _isExitRequested || IsTargetExitPreparing || IsDisposed) return;
            _remoteSettingsRefreshBusy = true;
            try
            {
                await Task.Run(() => _remoteHostServiceController.RefreshConfigurationAsync(CancellationToken.None));
            }
            catch (Exception ex)
            {
                if (!_isExitRequested && !IsDisposed)
                    SetHighPriorityStatus(UiLocalization.Text("SharedRemoteService.Status.RefreshFailed", "Phone connection settings could not be applied; retry from Connection Settings in Phone Connection.") + " (" + ex.GetType().Name + ")");
            }
            finally { _remoteSettingsRefreshBusy = false; }
        }

        private void ShowSharedRemoteServiceSettings(IWin32Window owner)
        {
            var store = new SharedRemoteServiceSettingsStore(_appConfigService, _targetCatalog, _targetChannel);
            RemoteServiceSettingsForm editor = null;
            using (editor = new RemoteServiceSettingsForm(store, new RelayHostClient(), null, async snapshot =>
            {
                var usage = await Task.Run(store.GetUsage);
                var names = usage.TargetNames.Count == 0
                    ? UiLocalization.Text("SharedRemoteService.Targets.None", "No running targets are using phone connections.")
                    : string.Join(Environment.NewLine, usage.TargetNames.Select(name => "- " + name));
                var message = UiLocalization.Text("SharedRemoteService.Message.Apply",
                    "Apply phone connection settings to all targets? Phone connections may reconnect; desktop tasks continue. Affected targets:") +
                    Environment.NewLine + names;
                if (store.PairingIdentityChanged(snapshot))
                    message += Environment.NewLine + UiLocalization.Text("SharedRemoteService.Message.PairingImpact",
                        "The service security identity or certificate will change. Existing devices may need to pair again.");
                if (MessageBox.Show(editor, message, editor.Text, MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.OK) return false;
                await Task.Run(() => store.SaveConfirmed(snapshot, usage));
                await Task.Run(() => _remoteHostServiceController.RefreshConfigurationAsync(CancellationToken.None));
                editor.SetSharedServiceStatus(await ReadSharedRemoteServiceStatusAsync(snapshot), whileBusy: true);
                return true;
            }, firewallOwner =>
            {
                using (var firewall = new EmbeddedRelayFirewallForm(new EmbeddedRelayFirewallService()))
                    firewall.ShowDialog(firewallOwner);
            }))
            {
                editor.Text = UiLocalization.Text("SharedRemoteService.Title", "Phone Connection Settings") +
                    " - " + GetCurrentRuntimeDisplayName();
                editor.Shown += async (s, e) =>
                {
                    try { editor.SetSharedServiceStatus(await ReadSharedRemoteServiceStatusAsync(store.Load())); }
                    catch (Exception ex) { editor.SetSharedServiceStatus(RelaySecretRedactor.Redact(ex.Message)); }
                };
                editor.ShowDialog(owner);
            }
        }

        private async Task<string> ReadSharedRemoteServiceStatusAsync(RemoteServiceConfigSnapshot snapshot)
        {
            if (!RemoteServiceConfigPlanner.IsEmbeddedMode(snapshot.RemoteServiceMode))
                return UiLocalization.Text("SharedRemoteService.Status.External", "Public service selected. Check each target's connection under Phone Connection.");
            var status = await Task.Run(() => _embeddedRelayService.GetStatus(snapshot));
            var text = status.State == EmbeddedRelayRunState.Running
                ? UiLocalization.Text("SharedRemoteService.Status.Running", "The built-in service is running.")
                : status.State == EmbeddedRelayRunState.Failed
                    ? UiLocalization.Text("SharedRemoteService.Status.Failed", "The built-in service failed. Apply again to retry.")
                    : UiLocalization.Text("SharedRemoteService.Status.Stopped", "The built-in service is stopped. Enable a target's phone connection to start it.");
            return text + " " + RelaySecretRedactor.Redact(status.Message);
        }
    }
}
