using System;
using System.Windows.Forms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm
    {
        private void OnFindChatMessageMenuItemClick(object sender, EventArgs e)
        {
            ShowChatHistorySearchDialog();
        }

        private void ShowChatHistorySearchDialog()
        {
            if (_chatHistoryListBox.Items.Count <= 0)
            {
                SetStatus(UiLocalization.Text("ChatSearch.NoMessages", "No searchable messages in the current list."));
                return;
            }

            using (var form = new MessageSearchForm(_chatHistorySearchKeyword))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _chatHistorySearchKeyword = form.SearchText;
                FindChatHistoryItem(_chatHistorySearchKeyword, form.SearchForward, true);
            }
        }

        private bool TryContinueChatHistorySearch(bool forward)
        {
            if (_chatHistoryListBox.Items.Count <= 0)
            {
                SetStatus(UiLocalization.Text("ChatSearch.NoMessages", "No searchable messages in the current list."));
                return true;
            }

            if (string.IsNullOrWhiteSpace(_chatHistorySearchKeyword))
            {
                ShowChatHistorySearchDialog();
                return true;
            }

            FindChatHistoryItem(_chatHistorySearchKeyword, forward, false);
            return true;
        }

        private bool FindChatHistoryItem(string keyword, bool forward, bool includeCurrent)
        {
            var normalizedKeyword = (keyword ?? string.Empty).Trim();
            if (normalizedKeyword.Length == 0)
            {
                SetStatus(UiLocalization.Text("ChatSearch.EmptyKeyword", "Please enter a keyword to search."));
                return false;
            }

            var itemCount = _chatHistoryListBox.Items.Count;
            if (itemCount <= 0)
            {
                SetStatus(UiLocalization.Text("ChatSearch.NoMessages", "No searchable messages in the current list."));
                return false;
            }

            var selectedIndex = _chatHistoryListBox.FocusedIndex;
            var index = selectedIndex >= 0
                ? selectedIndex
                : (forward ? 0 : itemCount - 1);
            if (selectedIndex >= 0 && !includeCurrent)
            {
                index += forward ? 1 : -1;
            }

            var wrapped = false;
            for (var scanned = 0; scanned < itemCount; scanned++)
            {
                if (index < 0)
                {
                    index = itemCount - 1;
                    wrapped = true;
                }
                else if (index >= itemCount)
                {
                    index = 0;
                    wrapped = true;
                }

                var item = _chatHistoryListBox.Items[index] as ChatHistoryItem;
                if (IsChatHistorySearchMatch(item, normalizedKeyword))
                {
                    MoveChatHistorySearchSelection(index, normalizedKeyword, wrapped);
                    return true;
                }

                index += forward ? 1 : -1;
            }

            SetStatus(string.Format(
                UiLocalization.Text("ChatSearch.NotFound", "No message containing \"{0}\" was found."),
                normalizedKeyword));
            return false;
        }

        private void MoveChatHistorySearchSelection(int index, string keyword, bool wrapped)
        {
            if (index < 0 || index >= _chatHistoryListBox.Items.Count)
            {
                return;
            }

            _chatHistoryListBox.SelectSingleIndex(index);

            EnsureChatHistoryItemVisible(index);
            ActiveControl = _chatHistoryListBox;
            _chatHistoryListBox.Focus();

            SetStatus(wrapped
                ? string.Format(
                    UiLocalization.Text("ChatSearch.FoundWrapped", "Wrapped around to a message containing \"{0}\"."),
                    keyword)
                : string.Format(
                    UiLocalization.Text("ChatSearch.Found", "Moved to a message containing \"{0}\"."),
                    keyword));
        }

        private static bool IsChatHistorySearchMatch(ChatHistoryItem item, string keyword)
        {
            if (item == null || string.IsNullOrWhiteSpace(keyword))
            {
                return false;
            }

            var text = item.FullText;
            if (string.IsNullOrEmpty(text))
            {
                text = item.DisplayText;
            }

            return !string.IsNullOrEmpty(text) &&
                   text.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
