using System;
using System.Threading.Tasks;

namespace MyCodex.WinForms.Forms
{
    internal sealed class TargetExitPreparation
    {
        private readonly Func<string, Task<bool>> _prepare;
        private readonly Action<bool> _freeze;
        private readonly Func<bool> _commit;
        private string _operationId;
        private bool _ready;
        private Task<bool> _pending;

        internal TargetExitPreparation(Func<string, Task<bool>> prepare, Action<bool> freeze, Func<bool> commit)
        {
            _prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
            _freeze = freeze ?? throw new ArgumentNullException(nameof(freeze));
            _commit = commit ?? throw new ArgumentNullException(nameof(commit));
        }

        internal bool IsPreparing => _operationId != null;
        internal bool IsReady => IsPreparing && _ready;
        internal bool IsCurrent(string operationId) => IsPreparing && string.Equals(_operationId, operationId, StringComparison.Ordinal);

        internal Task<bool> PrepareAsync(string operationId)
        {
            if (string.IsNullOrWhiteSpace(operationId)) throw new ArgumentException("An exit operation is required.", nameof(operationId));
            if (IsPreparing) return IsCurrent(operationId) ? _pending ?? Task.FromResult(_ready) : Task.FromResult(false);
            _operationId = operationId;
            _ready = false;
            _freeze(true);
            _pending = RunPreparationAsync(operationId);
            return _pending;
        }

        private async Task<bool> RunPreparationAsync(string operationId)
        {
            try
            {
                var prepared = await _prepare(operationId);
                if (!IsCurrent(operationId)) return false;
                if (!prepared)
                {
                    Cancel(operationId);
                    return false;
                }
                _ready = true;
                return true;
            }
            catch
            {
                Cancel(operationId);
                throw;
            }
        }

        internal void Cancel(string operationId)
        {
            if (!IsCurrent(operationId)) return;
            _operationId = null;
            _ready = false;
            _pending = null;
            _freeze(false);
        }

        internal bool Commit(string operationId)
        {
            if (!IsCurrent(operationId) || !_ready) return false;
            if (!_commit())
            {
                Cancel(operationId);
                return false;
            }
            return true;
        }
    }
}
