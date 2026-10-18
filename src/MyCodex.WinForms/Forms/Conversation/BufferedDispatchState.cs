using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    internal sealed class BufferedDispatchState<T> where T : class
    {
        private readonly Queue<T> _pendingItems;
        private readonly object _syncRoot;
        private readonly int _maxPendingItems;
        private bool _dispatchQueued;

        public BufferedDispatchState()
            : this(0)
        {
        }

        public BufferedDispatchState(int maxPendingItems)
        {
            _pendingItems = new Queue<T>();
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
                _pendingItems.Enqueue(item);
                TrimToCapacity();
                if (_dispatchQueued)
                {
                    return false;
                }

                _dispatchQueued = true;
                return true;
            }
        }

        internal bool TryDequeueBatch(int maxItems, out List<T> items, out bool hasMore)
        {
            lock (_syncRoot)
            {
                if (_pendingItems.Count == 0)
                {
                    _dispatchQueued = false;
                    items = null;
                    hasMore = false;
                    return false;
                }

                var takeCount = maxItems > 0 ? maxItems : _pendingItems.Count;
                items = new List<T>(System.Math.Min(takeCount, _pendingItems.Count));
                while (_pendingItems.Count > 0 && items.Count < takeCount)
                {
                    items.Add(_pendingItems.Dequeue());
                }

                hasMore = _pendingItems.Count > 0;
                if (!hasMore)
                {
                    _dispatchQueued = false;
                }

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
                while (_pendingItems.Count > 0)
                {
                    items.Add(_pendingItems.Dequeue());
                }

                return true;
            }
        }

        internal void Reset()
        {
            lock (_syncRoot)
            {
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

            while (_pendingItems.Count > _maxPendingItems)
            {
                _pendingItems.Dequeue();
            }
        }
    }
}
