using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal static class CurrentListItemMarker
    {
        internal static string Append(string text, bool isCurrent)
        {
            var normalized = text ?? string.Empty;
            return isCurrent
                ? normalized + UiLocalization.Text("MainForm.ListMarker.Current", " (Current)")
                : normalized;
        }
    }
}
