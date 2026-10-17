using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private readonly McpReauthenticationState _mcpReauthenticationState =
            new McpReauthenticationState();

        private void HandleMcpStartupStatusUpdated(AppServerEventArgs e)
        {
            var notification = _mcpReauthenticationState.Record(e);
            if (notification == null)
            {
                return;
            }

            var instruction = McpReauthenticationGuidance.BuildInstruction(notification.ServerName);
            SetHighPriorityStatus(instruction);
            if (notification.ShouldSpeak)
            {
                SpeakAuxiliaryTextWithScreenReader(instruction);
            }
        }
    }
}
