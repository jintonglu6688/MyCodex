using System;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private string _computerUseStatusThread;
        private string _computerUseStatusText;

        private void SetAppServerStatus(AppServerEventArgs e, bool highPriority, string text = null)
        {
            var isComputerUse = (e.Reason ?? string.Empty).StartsWith("computer_use/", StringComparison.Ordinal);
            if (isComputerUse && e.Reason == "computer_use/Ready" &&
                string.Equals(_computerUseStatusThread, e.ThreadId, StringComparison.Ordinal) &&
                string.Equals(_computerUseStatusText, _statusLabel.Text, StringComparison.Ordinal))
            {
                // Recovery supersedes this feature's own warning, not another high-priority notice.
                _currentStatusPriority = StatusPriority.Low;
            }
            SetStatus(text ?? e.Content, highPriority ? StatusPriority.High : StatusPriority.Normal);
            if (isComputerUse && _statusLabel.Text == (text ?? e.Content))
            {
                _computerUseStatusThread = e.ThreadId;
                _computerUseStatusText = _statusLabel.Text;
            }
        }
    }
}
