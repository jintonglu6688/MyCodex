using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class SessionListItem
    {
        private readonly IReadOnlyList<ChatSession> _subAgents;
        private readonly bool _isCurrent;
        private readonly string _displayOrder;
        private bool _isWorking;

        public SessionListItem(SessionListEntry entry, bool isCurrent, bool isWorking,
            string displayOrder = SessionListDisplayOrder.AgentsTimeTitle)
        {
            Session = entry != null ? entry.Session : null;
            _subAgents = entry != null ? entry.SubAgents : Array.Empty<ChatSession>();
            _isCurrent = isCurrent;
            _isWorking = isWorking;
            _displayOrder = SessionListDisplayOrder.Normalize(displayOrder);
        }

        public ChatSession Session { get; }

        internal static void RefreshList(ListView list, ListViewItem[] items, int currentIndex)
        {
            var selected = list.SelectedItems.Cast<ListViewItem>().ToArray();
            var focused = list.FocusedItem;
            var top = list.TopItem;
            var oldCurrent = list.Items.Cast<ListViewItem>().FirstOrDefault(item => (item.Tag as SessionListItem)?._isCurrent == true);
            var current = currentIndex >= 0 ? items[currentIndex] : null;
            var sameCurrent = string.Equals(SessionId(oldCurrent), SessionId(current), StringComparison.Ordinal);
            var existing = list.Items.Cast<ListViewItem>().GroupBy(SessionId)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var wanted = new HashSet<string>(items.Select(SessionId), StringComparer.Ordinal);
            list.BeginUpdate();
            try
            {
                for (var i = list.Items.Count - 1; i >= 0; i--)
                    if (!wanted.Contains(SessionId(list.Items[i]))) list.Items.RemoveAt(i);
                for (var i = 0; i < items.Length; i++)
                {
                    var replacement = items[i];
                    if (!existing.TryGetValue(SessionId(replacement), out var item)) item = replacement;
                    if (item.Text != replacement.Text) item.Text = replacement.Text;
                    if (item.ToolTipText != replacement.ToolTipText) item.ToolTipText = replacement.ToolTipText;
                    item.Tag = replacement.Tag;
                    if (item.Index != i)
                    {
                        if (item.ListView == list) list.Items.Remove(item);
                        list.Items.Insert(i, item);
                    }
                }
                var survivingSelection = sameCurrent ? selected.Where(item => item.ListView == list).ToArray() : Array.Empty<ListViewItem>();
                if (survivingSelection.Length == 0 && currentIndex >= 0)
                    survivingSelection = new[] { list.Items[currentIndex] };
                foreach (ListViewItem item in list.Items)
                    if (item.Selected != survivingSelection.Contains(item)) item.Selected = survivingSelection.Contains(item);
                var nextFocus = sameCurrent && focused?.ListView == list ? focused : survivingSelection.FirstOrDefault();
                if (nextFocus != null && !nextFocus.Focused) nextFocus.Focused = true;
                if (sameCurrent && top?.ListView == list && list.TopItem != top) list.TopItem = top;
            }
            finally { list.EndUpdate(); }
        }

        private static string SessionId(ListViewItem item)
        {
            return (item?.Tag as SessionListItem)?.Session?.Id ?? string.Empty;
        }

        public IReadOnlyList<ChatSession> SubAgents
        {
            get { return _subAgents; }
        }

        public int SubAgentCount
        {
            get { return _subAgents.Count; }
        }

        public ListViewItem CreateListViewItem()
        {
            var displayText = ToString();
            return new ListViewItem(displayText)
            {
                Tag = this,
                ToolTipText = displayText
            };
        }

        public void UpdateWorkingState(ListViewItem item, bool isWorking)
        {
            if (item == null || _isWorking == isWorking)
            {
                return;
            }

            _isWorking = isWorking;
            var displayText = ToString();
            item.Text = displayText;
            item.ToolTipText = displayText;
        }

        public override string ToString()
        {
            if (Session == null)
            {
                return string.Empty;
            }

            var agoText = SessionDisplayFormatter.GetCreatedAgoText(Session.CreatedAt);
            var titleText = SessionDisplayFormatter.BuildDisplayTitle(Session);
            var agentsText = SubAgentCount == 0
                ? string.Empty
                : string.Format(
                    UiLocalization.Text("MainForm.SessionList.SubAgentCount.Template", "{0} subagent(s)"),
                    SubAgentCount);
            string[] parts;
            switch (_displayOrder)
            {
                case SessionListDisplayOrder.TitleTimeAgents:
                    parts = new[] { titleText, agoText, agentsText };
                    break;
                case SessionListDisplayOrder.TitleAgentsTime:
                    parts = new[] { titleText, agentsText, agoText };
                    break;
                default:
                    parts = new[] { agentsText, agoText, titleText };
                    break;
            }
            var displayText = string.Join(UiLocalization.Text("MainForm.SessionList.Separator", ", "),
                parts.Where(part => !string.IsNullOrWhiteSpace(part)));
            return CurrentListItemMarker.Append(
                SessionDisplayFormatter.PrependWorkingStatus(displayText, _isWorking ? 1 : 0),
                _isCurrent);
        }
    }
}
