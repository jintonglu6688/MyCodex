using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Instances;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private BundledRuntimeUpdateCoordinator _bundledRuntimeUpdates;
        private int _bundledRuntimePreparationCount;
        private CodexRuntimeUpdateResult _installedBundledRuntimeUpdate;

        private void CleanupConnectedBundledRuntimeUpdate()
        {
            var installed = _installedBundledRuntimeUpdate;
            _installedBundledRuntimeUpdate = null;
            TryCleanupObsoleteBundledRuntimeVersions(installed?.Version, installed?.TargetTriple);
        }

        private Task<bool> EnsurePendingBundledRuntimeUpdateAsync()
        {
            return _bundledRuntimePreparationCount > 0 || _bundledRuntimeUpdates?.HasPendingUpdate == true
                ? EnsureBundledRuntimeUpdatedAsync() : Task.FromResult(true);
        }

        private async Task<bool> EnsureBundledRuntimeUpdatedAsync()
        {
            if (_appConfigSnapshot == null) LoadAppConfig();
            if (_bundledRuntimeUpdates == null)
                _bundledRuntimeUpdates = new BundledRuntimeUpdateCoordinator(async token =>
                {
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(15));
                        return await CreateCodexRuntimeUpdateService().CheckLatestAsync(timeout.Token);
                    }
                }, OfferBundledRuntimeUpdateAsync);
            var preparing = CodexRuntimeSources.IsBundledFamily(_appConfigSnapshot.CodexRuntimeSource) && _bundledRuntimeUpdates.NeedsPreparation;
            if (preparing)
            {
                _bundledRuntimePreparationCount++;
                PublishTargetStatus(TargetInstanceLifecycle.Running);
            }
            try { return await _bundledRuntimeUpdates.EnsureAsync(_appConfigSnapshot.CodexRuntimeSource, _targetShutdownCancellation.Token); }
            finally
            {
                if (preparing)
                {
                    _bundledRuntimePreparationCount--;
                    PublishTargetStatus(TargetInstanceLifecycle.Running);
                }
            }
        }

        private Task<bool> OfferBundledRuntimeUpdateAsync(CodexRuntimeUpdateInfo info, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var owner = FindVisibleModalDialog(this) ?? this;
            if (MessageBox.Show(owner, string.Format(UiLocalization.Text("BundledRuntimeUpdateForm.Confirm",
                    "A new built-in Codex runtime is available: {0} → {1}.\r\n\r\nUpdate now? Choose No to continue using the existing runtime."),
                    info.InstalledVersion, info.LatestVersion), UiLocalization.Text("BundledRuntimeUpdateForm.ConfirmTitle", "Runtime Update"),
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return Task.FromResult(false);
            token.ThrowIfCancellationRequested();
            using (var form = new BundledRuntimeUpdateForm(CreateCodexRuntimeUpdateService(), info, token))
            {
                if (form.ShowDialog(owner) == DialogResult.OK)
                {
                    _installedBundledRuntimeUpdate = form.UpdateResult;
                    MessageBox.Show(owner, string.Format(UiLocalization.Text("BundledRuntimeUpdateForm.Completed",
                            "Runtime updated to {0}."), form.UpdateResult.Version),
                        UiLocalization.Text("BundledRuntimeUpdateForm.ConfirmTitle", "Runtime Update"),
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }
    }
}
