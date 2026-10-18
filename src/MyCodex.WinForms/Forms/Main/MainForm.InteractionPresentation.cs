using System.Windows.Forms;
using System.Threading.Tasks;
using System.Linq;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private Task<bool> WaitToPresentTargetInteractionAsync(string requestId)
        {
            var runtimeVersion = _subAgentRuntimeVersion;
            return TargetInteractionPresentationGate.WaitAsync(this, _remoteInteractionDialogs, requestId,
                CanPresentTargetInteraction,
                () => !_isExitRequested && !IsTargetExitPreparing && !_isRuntimeWindowTransitionInProgress && runtimeVersion == _subAgentRuntimeVersion);
        }

        private bool CanPresentTargetInteraction()
        {
            if (FindVisibleModalDialog(this) != null || (_targetWindowGroup != null && _targetWindowGroup.HasNativeModal)) return false;
            var foreground = Control.FromHandle(MainWindowHotKeyNativeMethods.GetForegroundWindow()) as Form;
            if (foreground == null || !foreground.Visible || !foreground.Enabled) return false;
            return ReferenceEquals(foreground, this) ||
                (_targetWindowGroup != null && _targetWindowGroup.GetForms().Contains(foreground));
        }
    }
}
