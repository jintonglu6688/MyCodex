using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Controls
{
    internal sealed class ActivityVirtualBrowseControl : Control
    {
        private readonly ActivityVirtualBrowseState _state;
        private int _topIndex;

        public ActivityVirtualBrowseControl()
        {
            _state = new ActivityVirtualBrowseState();
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            AccessibleRole = AccessibleRole.List;
            BackColor = UiTheme.CardBackColor;
            ForeColor = UiTheme.TextColor;
            Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular);
            TabStop = true;
        }

        public int SelectedIndex
        {
            get { return _state.SelectedIndex; }
        }

        public int ItemCount
        {
            get { return _state.Count; }
        }

        public void SetItems(IEnumerable<string> items)
        {
            _state.SetItems(items);
            EnsureSelectedVisible();
            Invalidate();
        }

        public string[] GetReadableItems()
        {
            return _state.Items.ToArray();
        }

        protected override AccessibleObject CreateAccessibilityInstance()
        {
            return new ActivityVirtualBrowseAccessibleObject(this);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                    return true;
                default:
                    return base.IsInputKey(keyData);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            string text = null;
            switch (e.KeyCode)
            {
                case Keys.Up:
                    text = _state.MoveBy(-1);
                    break;
                case Keys.Down:
                    text = _state.MoveBy(1);
                    break;
                case Keys.PageUp:
                    text = _state.MoveBy(-GetVisibleItemCount());
                    break;
                case Keys.PageDown:
                    text = _state.MoveBy(GetVisibleItemCount());
                    break;
                case Keys.Home:
                    text = _state.MoveToStart();
                    break;
                case Keys.End:
                    text = _state.MoveToEnd();
                    break;
            }

            if (text == null)
            {
                return;
            }

            if (text.Length == 0)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            EnsureSelectedVisible();
            Invalidate();
            UpdateAccessibleSummary();
            e.SuppressKeyPress = true;
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();

            var index = _topIndex + Math.Max(0, e.Y / GetItemHeight());
            if (index < 0 || index >= _state.Count)
            {
                return;
            }

            var delta = index - _state.SelectedIndex;
            _state.MoveBy(delta);
            EnsureSelectedVisible();
            Invalidate();
            UpdateAccessibleSummary();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_state.Count <= 0)
            {
                return;
            }

            var direction = e.Delta > 0 ? -1 : 1;
            _topIndex += direction * 3;
            ClampTopIndex();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);

            var itemHeight = GetItemHeight();
            var visibleCount = GetVisibleItemCount();
            var items = _state.Items;
            using (var borderPen = new Pen(UiTheme.BorderColor))
            using (var selectedBrush = new SolidBrush(UiTheme.SelectedBackColor))
            using (var selectedTextBrush = new SolidBrush(UiTheme.SelectedTextColor))
            using (var textBrush = new SolidBrush(ForeColor))
            using (var accentBrush = new SolidBrush(UiTheme.AccentColor))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));

                for (var row = 0; row < visibleCount; row++)
                {
                    var index = _topIndex + row;
                    if (index < 0 || index >= items.Count)
                    {
                        break;
                    }

                    var bounds = new Rectangle(1, 1 + row * itemHeight, Math.Max(0, Width - 2), itemHeight);
                    var selected = index == _state.SelectedIndex;
                    if (selected)
                    {
                        e.Graphics.FillRectangle(selectedBrush, bounds);
                    }

                    e.Graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top, UiTheme.Scale(3), bounds.Height);
                    var textBounds = new Rectangle(bounds.Left + UiTheme.Scale(8), bounds.Top, Math.Max(0, bounds.Width - UiTheme.Scale(12)), bounds.Height);
                    TextRenderer.DrawText(
                        e.Graphics,
                        items[index],
                        Font,
                        textBounds,
                        selected ? UiTheme.SelectedTextColor : ForeColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                    if (selected && Focused)
                    {
                        ControlPaint.DrawFocusRectangle(e.Graphics, bounds);
                    }
                }
            }
        }

        private void EnsureSelectedVisible()
        {
            if (_state.SelectedIndex < 0)
            {
                _topIndex = 0;
                return;
            }

            var visibleCount = GetVisibleItemCount();
            if (_state.SelectedIndex < _topIndex)
            {
                _topIndex = _state.SelectedIndex;
            }
            else if (_state.SelectedIndex >= _topIndex + visibleCount)
            {
                _topIndex = Math.Max(0, _state.SelectedIndex - visibleCount + 1);
            }

            ClampTopIndex();
        }

        private void ClampTopIndex()
        {
            var maxTopIndex = Math.Max(0, _state.Count - GetVisibleItemCount());
            if (_topIndex < 0)
            {
                _topIndex = 0;
            }
            else if (_topIndex > maxTopIndex)
            {
                _topIndex = maxTopIndex;
            }
        }

        private int GetVisibleItemCount()
        {
            return Math.Max(1, ClientSize.Height / GetItemHeight());
        }

        private int GetItemHeight()
        {
            return Math.Max(UiTheme.Scale(20), Font.Height + UiTheme.Scale(8));
        }

        private void UpdateAccessibleSummary()
        {
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }

        private sealed class ActivityVirtualBrowseAccessibleObject : ControlAccessibleObject
        {
            private readonly ActivityVirtualBrowseControl _owner;

            public ActivityVirtualBrowseAccessibleObject(ActivityVirtualBrowseControl owner)
                : base(owner)
            {
                _owner = owner;
            }

            public override string Name
            {
                get { return _owner.AccessibleName; }
                set { base.Name = value; }
            }

            public override AccessibleRole Role
            {
                get { return AccessibleRole.List; }
            }

            public override string Value
            {
                get
                {
                    return _owner._state.Count <= 0
                        ? string.Empty
                        : _owner._state.BuildCurrentSpeechText();
                }
            }

            public override int GetChildCount()
            {
                return 0;
            }
        }
    }
}
