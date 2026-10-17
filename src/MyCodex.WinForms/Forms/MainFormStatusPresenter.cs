using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    internal static class MainFormStatusPresenter
    {
        internal static UiTheme.StatusType DetermineStatusType(string statusText)
        {
            var lowerText = statusText.ToLowerInvariant();

            // Error indicators
            if (lowerText.Contains("error") || lowerText.Contains("failed") || lowerText.Contains("失败"))
            {
                return UiTheme.StatusType.Error;
            }

            // Processing indicators
            if (lowerText.Contains("processing") || lowerText.Contains("loading") ||
                lowerText.Contains("waiting") || lowerText.Contains("正在") ||
                lowerText.Contains("处理中"))
            {
                return UiTheme.StatusType.Processing;
            }

            // Default to Ready
            return UiTheme.StatusType.Ready;
        }
    }
}
