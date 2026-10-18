using System;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Infrastructure.Codex;
using MyCodex.Shared.Constants;

namespace MyCodex.WinForms.Forms
{
    internal sealed class BundledRuntimeUpdateCoordinator
    {
        private readonly Func<CancellationToken, Task<CodexRuntimeUpdateInfo>> _check;
        private readonly Func<CodexRuntimeUpdateInfo, CancellationToken, Task<bool>> _install;
        private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        private bool _checked;
        private CodexRuntimeUpdateInfo _pending;

        internal bool NeedsPreparation => !_checked || _pending != null;
        internal bool HasPendingUpdate => _pending != null;

        internal BundledRuntimeUpdateCoordinator(Func<CancellationToken, Task<CodexRuntimeUpdateInfo>> check,
            Func<CodexRuntimeUpdateInfo, CancellationToken, Task<bool>> install)
        {
            _check = check ?? throw new ArgumentNullException(nameof(check));
            _install = install ?? throw new ArgumentNullException(nameof(install));
        }

        internal async Task<bool> EnsureAsync(string runtimeSource, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!CodexRuntimeSources.IsBundledFamily(runtimeSource))
                {
                    _checked = false;
                    return true;
                }
                if (_pending == null && !_checked)
                {
                    _checked = true;
                    try
                    {
                        var info = await _check(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (info != null && info.IsUpdateAvailable) _pending = info;
                    }
                    // A failed check is deliberately silent; the existing runtime remains available.
                    catch (Exception) when (!cancellationToken.IsCancellationRequested) { return true; }
                }
                if (_pending == null) return true;
                try
                {
                    await _install(_pending, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    return true;
                }
                finally { _pending = null; }
            }
            finally { _gate.Release(); }
        }
    }
}
