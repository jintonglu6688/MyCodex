using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    internal sealed class ThreadFollowUpQueue<T> where T : class
    {
        private readonly object _syncRoot = new object();
        private readonly Dictionary<string, Queue<T>> _itemsByThreadId =
            new Dictionary<string, Queue<T>>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dispatchingThreadIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal void Enqueue(string threadId, T item)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                throw new ArgumentException("A thread ID is required.", nameof(threadId));
            }

            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            lock (_syncRoot)
            {
                Queue<T> queue;
                if (!_itemsByThreadId.TryGetValue(threadId, out queue))
                {
                    queue = new Queue<T>();
                    _itemsByThreadId[threadId] = queue;
                }

                queue.Enqueue(item);
            }
        }

        internal bool TryBeginDispatch(string threadId, out T item)
        {
            item = default(T);
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return false;
            }

            lock (_syncRoot)
            {
                Queue<T> queue;
                if (_dispatchingThreadIds.Contains(threadId) ||
                    !_itemsByThreadId.TryGetValue(threadId, out queue) ||
                    queue.Count == 0)
                {
                    return false;
                }

                item = queue.Dequeue();
                if (queue.Count == 0)
                {
                    _itemsByThreadId.Remove(threadId);
                }

                _dispatchingThreadIds.Add(threadId);
                return true;
            }
        }

        internal void CompleteDispatch(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            lock (_syncRoot)
            {
                _dispatchingThreadIds.Remove(threadId);
            }
        }

        internal bool HasPending(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return false;
            }

            lock (_syncRoot)
            {
                Queue<T> queue;
                return _itemsByThreadId.TryGetValue(threadId, out queue) && queue.Count > 0;
            }
        }

        internal void MovePending(string sourceThreadId, string targetThreadId)
        {
            if (string.IsNullOrWhiteSpace(sourceThreadId) ||
                string.IsNullOrWhiteSpace(targetThreadId) ||
                string.Equals(sourceThreadId, targetThreadId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            lock (_syncRoot)
            {
                Queue<T> sourceQueue;
                if (!_itemsByThreadId.TryGetValue(sourceThreadId, out sourceQueue) || sourceQueue.Count == 0)
                {
                    return;
                }

                Queue<T> targetQueue;
                if (!_itemsByThreadId.TryGetValue(targetThreadId, out targetQueue) || targetQueue.Count == 0)
                {
                    _itemsByThreadId[targetThreadId] = sourceQueue;
                }
                else
                {
                    var combinedQueue = new Queue<T>();
                    while (sourceQueue.Count > 0)
                    {
                        combinedQueue.Enqueue(sourceQueue.Dequeue());
                    }

                    while (targetQueue.Count > 0)
                    {
                        combinedQueue.Enqueue(targetQueue.Dequeue());
                    }

                    _itemsByThreadId[targetThreadId] = combinedQueue;
                }

                _itemsByThreadId.Remove(sourceThreadId);
            }
        }
    }
}
