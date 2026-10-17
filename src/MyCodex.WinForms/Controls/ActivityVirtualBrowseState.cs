using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Controls
{
    internal sealed class ActivityVirtualBrowseState
    {
        private readonly List<string> _items;

        public ActivityVirtualBrowseState()
        {
            _items = new List<string>();
            SelectedIndex = -1;
            IsFollowingLatest = true;
        }

        public int SelectedIndex { get; private set; }

        public bool IsFollowingLatest { get; private set; }

        public int Count
        {
            get { return _items.Count; }
        }

        public string CurrentText
        {
            get
            {
                return SelectedIndex >= 0 && SelectedIndex < _items.Count
                    ? _items[SelectedIndex]
                    : string.Empty;
            }
        }

        public IReadOnlyList<string> Items
        {
            get { return _items; }
        }

        public void SetItems(IEnumerable<string> items)
        {
            var previousIndex = SelectedIndex;
            _items.Clear();

            if (items != null)
            {
                foreach (var item in items)
                {
                    if (!string.IsNullOrWhiteSpace(item))
                    {
                        _items.Add(item.Trim());
                    }
                }
            }

            if (_items.Count == 0)
            {
                SelectedIndex = -1;
                IsFollowingLatest = true;
                return;
            }

            if (IsFollowingLatest || previousIndex < 0)
            {
                SelectedIndex = _items.Count - 1;
                IsFollowingLatest = true;
                return;
            }

            SelectedIndex = Math.Min(previousIndex, _items.Count - 1);
        }

        public string MoveBy(int delta)
        {
            if (_items.Count == 0)
            {
                SelectedIndex = -1;
                IsFollowingLatest = true;
                return string.Empty;
            }

            var previousIndex = SelectedIndex;
            if (SelectedIndex < 0)
            {
                SelectedIndex = delta < 0 ? _items.Count - 1 : 0;
            }
            else
            {
                SelectedIndex += delta;
            }

            ClampSelectedIndex();
            IsFollowingLatest = false;
            if (SelectedIndex == previousIndex)
            {
                return string.Empty;
            }

            return BuildCurrentSpeechText();
        }

        public string MoveToStart()
        {
            if (_items.Count == 0)
            {
                SelectedIndex = -1;
                IsFollowingLatest = true;
                return string.Empty;
            }

            SelectedIndex = 0;
            IsFollowingLatest = false;
            return BuildCurrentSpeechText();
        }

        public string MoveToEnd()
        {
            if (_items.Count == 0)
            {
                SelectedIndex = -1;
                IsFollowingLatest = true;
                return string.Empty;
            }

            SelectedIndex = _items.Count - 1;
            IsFollowingLatest = false;
            return BuildCurrentSpeechText();
        }

        public string BuildCurrentSpeechText()
        {
            if (SelectedIndex < 0 || SelectedIndex >= _items.Count)
            {
                return string.Empty;
            }

            return _items[SelectedIndex];
        }

        private void ClampSelectedIndex()
        {
            if (SelectedIndex < 0)
            {
                SelectedIndex = 0;
            }
            else if (SelectedIndex >= _items.Count)
            {
                SelectedIndex = _items.Count - 1;
            }
        }
    }
}
