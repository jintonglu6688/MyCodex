using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public sealed class WebMessageViewerForm : Form
    {
        private readonly ChatHistoryItem _item;
        private readonly WebView2 _webView;
        private bool _documentLoaded;
        private bool _isLoadingLocalDocument;

        public WebMessageViewerForm(ChatHistoryItem item)
        {
            _item = item ?? new ChatHistoryItem();
            _webView = new WebView2();

            SuspendLayout();
            UiTheme.ApplyForm(this);

            _webView.AccessibleName = string.Empty;
            _webView.DefaultBackgroundColor = UiTheme.CardBackColor;
            _webView.Dock = DockStyle.Fill;
            _webView.TabIndex = 0;
            _webView.TabStop = false;
            _webView.Visible = false;
            _webView.KeyDown += WebView_KeyDown;
            _webView.NavigationCompleted += WebView_NavigationCompleted;

            ClientSize = new Size(820, 600);
            Controls.Add(_webView);
            KeyPreview = true;
            MinimizeBox = false;
            MinimumSize = new Size(480, 320);
            Name = "WebMessageViewerForm";
            Padding = new Padding(12);
            StartPosition = FormStartPosition.CenterParent;
            Text = string.IsNullOrWhiteSpace(_item.Sender)
                ? UiLocalization.Text("WebMessageViewerForm.Title.Content", "Message Content")
                : string.Format(
                    CultureInfo.InvariantCulture,
                    UiLocalization.Text("WebMessageViewerForm.Title.SenderContent", "{0} Message Content"),
                    _item.Sender);

            Shown += WebMessageViewerForm_Shown;

            ResumeLayout(false);
            PerformLayout();
        }

        public bool InitializationFailed { get; private set; }

        public static bool IsWebView2RuntimeAvailable()
        {
            try
            {
                return !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString());
            }
            catch
            {
                return false;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _webView.KeyDown -= WebView_KeyDown;
                _webView.NavigationCompleted -= WebView_NavigationCompleted;
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                    _webView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
                    _webView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                }

                _webView.Dispose();
            }

            base.Dispose(disposing);
        }

        private async void WebMessageViewerForm_Shown(object sender, EventArgs e)
        {
            await InitializeWebViewAsync().ConfigureAwait(true);
        }

        private async Task InitializeWebViewAsync()
        {
            try
            {
                await _webView.EnsureCoreWebView2Async(null).ConfigureAwait(true);
                ConfigureWebView();
                _isLoadingLocalDocument = true;
                _webView.NavigateToString(MessageMarkdownHtmlRenderer.RenderDocument(
                    GetViewerText(_item),
                    UiLocalization.Text("WebMessageViewerForm.Accessible.Content", "Message content")));
            }
            catch (Exception ex)
            {
                InitializationFailed = true;
                StructuredMessageViewerForm.MessageViewerDiagnostics.LogException("Initializing WebView2 message viewer failed.", ex);
                Close();
            }
        }

        private void ConfigureWebView()
        {
            var settings = _webView.CoreWebView2.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = true;
            settings.AreDefaultContextMenusEnabled = true;
            settings.AreDevToolsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsStatusBarEnabled = true;
            settings.IsWebMessageEnabled = true;
            settings.IsZoomControlEnabled = true;

            _webView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;
            _webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
            _webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        }

        private void WebView_KeyDown(object sender, KeyEventArgs e)
        {
            if (!ShouldCloseForAcceleratorKey((int)e.KeyCode, true))
            {
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
            Close();
        }

        private static bool ShouldCloseForAcceleratorKey(int virtualKey, bool isKeyDown)
        {
            return isKeyDown && virtualKey == (int)Keys.Escape;
        }

        private async void WebView_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (_documentLoaded || !e.IsSuccess || _webView.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                var hasContent = await _webView.CoreWebView2.ExecuteScriptAsync(
                    "Boolean(document.getElementById('content'))").ConfigureAwait(true);
                if (!string.Equals(hasContent, "true", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _documentLoaded = true;
                _isLoadingLocalDocument = false;
                _webView.Visible = true;
                _webView.TabStop = true;
                if (ShouldFocusContentAfterLoad())
                {
                    _webView.Focus();
                    await _webView.CoreWebView2.ExecuteScriptAsync(BuildInitialContentFocusScript()).ConfigureAwait(true);
                }
            }
            catch (Exception ex)
            {
                StructuredMessageViewerForm.MessageViewerDiagnostics.LogException("Focusing WebView2 message content failed.", ex);
            }
        }

        private static bool ShouldFocusContentAfterLoad()
        {
            return true;
        }

        private static string BuildInitialContentFocusScript()
        {
            return "window.scrollTo(0,0);" +
                   "const selection=window.getSelection&&window.getSelection();" +
                   "if(selection){selection.removeAllRanges();}" +
                   "const content=document.getElementById('content');" +
                   "if(content){content.focus({preventScroll:true});}";
        }

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = e.TryGetWebMessageAsString();
                var copyMessage = new JavaScriptSerializer().Deserialize<CopyCodeWebMessage>(message);
                if (copyMessage == null ||
                    string.IsNullOrEmpty(copyMessage.Kind))
                {
                    return;
                }

                if (string.Equals(copyMessage.Kind, "close", StringComparison.Ordinal))
                {
                    Close();
                    return;
                }

                if (string.Equals(copyMessage.Kind, "copyCode", StringComparison.Ordinal) &&
                    !string.IsNullOrEmpty(copyMessage.Text))
                {
                    Clipboard.SetText(copyMessage.Text);
                }
            }
            catch (Exception ex)
            {
                StructuredMessageViewerForm.MessageViewerDiagnostics.LogException("Handling WebView2 message viewer command failed.", ex);
            }
        }

        private void CoreWebView2_NavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(e.Uri))
            {
                return;
            }

            Uri uri;
            if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out uri))
            {
                e.Cancel = true;
                return;
            }

            if (ShouldAllowNavigation(e.Uri, _isLoadingLocalDocument))
            {
                return;
            }

            if (IsExternalUri(uri))
            {
                e.Cancel = true;
                OpenExternalUri(uri.AbsoluteUri);
                return;
            }

            e.Cancel = true;
        }

        private static bool ShouldAllowNavigation(string uriText, bool isLoadingLocalDocument)
        {
            if (string.IsNullOrWhiteSpace(uriText))
            {
                return true;
            }

            Uri uri;
            if (!Uri.TryCreate(uriText, UriKind.Absolute, out uri))
            {
                return false;
            }

            if (string.Equals(uri.Scheme, "about", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return isLoadingLocalDocument && string.Equals(uri.Scheme, "data", StringComparison.OrdinalIgnoreCase);
        }

        private void CoreWebView2_NewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            if (string.IsNullOrWhiteSpace(e.Uri))
            {
                return;
            }

            Uri uri;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out uri) && IsExternalUri(uri))
            {
                OpenExternalUri(uri.AbsoluteUri);
            }
        }

        private static bool IsExternalUri(Uri uri)
        {
            return uri != null &&
                   (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(uri.Scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetViewerText(ChatHistoryItem item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(item.MarkdownText))
            {
                return item.MarkdownText;
            }

            if (!string.IsNullOrWhiteSpace(item.FullText))
            {
                return item.FullText;
            }

            return item.DisplayText ?? string.Empty;
        }

        private static void OpenExternalUri(string uri)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = uri, UseShellExecute = true });
            }
            catch
            {
            }
        }

        private sealed class CopyCodeWebMessage
        {
            public string Kind { get; set; }

            public string Text { get; set; }
        }
    }
}
