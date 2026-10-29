namespace MyCodex.WinForms.Forms
{
    public sealed partial class OptionsForm
    {
        private BrowserControlOptionsPage _browserControlPage;

        internal void AddBrowserControlPage(BrowserControlOptionsPage page)
        {
            _browserControlPage = page;
            AddPage("browserControl", Localization.UiLocalization.Text("BrowserControl.Title", "Browser Control"), page);
        }

        internal bool? BrowserControlEnabled => _browserControlPage?.Selection;
    }
}
