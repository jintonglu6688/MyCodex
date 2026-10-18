using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    internal sealed class CoalescingBufferedDispatchState<T> where T : class
    {
        private readonly Func<T, string> _keySelector;
        private readonly Queue<string> _pendingKeys;
        private readonly Dictionary<string, T> _pendingItems;
        private readonly object _syncRoot;
        private readonly int _maxPendingItems;
        private int _anonymousKeySeed;
        private bool _dispatchQueued;

        public CoalescingBufferedDispatchState(Func<T, string> keySelector)
            : this(keySelector, 0)
        {
        }

        public CoalescingBufferedDispatchState(Func<T, string> keySelector, int maxPendingItems)
        {
            if (keySelector == null)
            {
                throw new ArgumentNullException(nameof(keySelector));
            }

            _keySelector = keySelector;
            _pendingKeys = new Queue<string>();
            _pendingItems = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            _syncRoot = new object();
            _maxPendingItems = maxPendingItems > 0 ? maxPendingItems : 0;
        }

        internal bool Enqueue(T item)
        {
            if (item == null)
            {
                return false;
            }

            lock (_syncRoot)
            {
                var key = _keySelector(item);
                if (string.IsNullOrWhiteSpace(key))
                {
                    _anonymousKeySeed++;
                    key = "__anonymous_" + _anonymousKeySeed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }

                if (!_pendingItems.ContainsKey(key))
                {
                    _pendingKeys.Enqueue(key);
                }

                _pendingItems[key] = item;
                TrimToCapacity();

                if (_dispatchQueued)
                {
                    return false;
                }

                _dispatchQueued = true;
                return true;
            }
        }

        internal bool TryDequeueAll(out List<T> items)
        {
            lock (_syncRoot)
            {
                if (_pendingItems.Count == 0)
                {
                    _dispatchQueued = false;
                    items = null;
                    return false;
                }

                items = new List<T>(_pendingItems.Count);
                while (_pendingKeys.Count > 0)
                {
                    var key = _pendingKeys.Dequeue();
                    T item;
                    if (_pendingItems.TryGetValue(key, out item))
                    {
                        items.Add(item);
                        _pendingItems.Remove(key);
                    }
                }

                _dispatchQueued = false;
                return items.Count > 0;
            }
        }

        internal void Reset()
        {
            lock (_syncRoot)
            {
                _pendingKeys.Clear();
                _pendingItems.Clear();
                _dispatchQueued = false;
            }
        }

        private void TrimToCapacity()
        {
            if (_maxPendingItems <= 0)
            {
                return;
            }

            while (_pendingItems.Count > _maxPendingItems && _pendingKeys.Count > 0)
            {
                var key = _pendingKeys.Dequeue();
                _pendingItems.Remove(key);
            }
        }
    }
}
