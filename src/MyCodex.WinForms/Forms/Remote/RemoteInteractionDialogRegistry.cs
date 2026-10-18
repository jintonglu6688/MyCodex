using System;
using System.Collections.Generic;

namespace MyCodex.WinForms.Forms
{
    internal sealed class RemoteInteractionDialogRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, Action> _closers = new Dictionary<string, Action>(StringComparer.Ordinal);

        public bool HasPending
        {
            get { lock (_sync) return _closers.Count > 0; }
        }

        public IDisposable Register(string requestId, Action close)
        {
            if (string.IsNullOrWhiteSpace(requestId) || close == null)
            {
                return new Registration(null, null);
            }

            lock (_sync)
            {
                _closers[requestId] = close;
            }

            return new Registration(this, requestId);
        }

        public bool Complete(string requestId)
        {
            Action close;
            lock (_sync)
            {
                if (string.IsNullOrWhiteSpace(requestId) || !_closers.TryGetValue(requestId, out close))
                {
                    return false;
                }

                _closers.Remove(requestId);
            }

            close();
            return true;
        }

        public void CloseAll()
        {
            Action[] closers;
            lock (_sync)
            {
                closers = new List<Action>(_closers.Values).ToArray();
                _closers.Clear();
            }

            foreach (var close in closers)
            {
                try
                {
                    close();
                }
                catch
                {
                }
            }
        }

        private void Unregister(string requestId)
        {
            lock (_sync)
            {
                if (!string.IsNullOrWhiteSpace(requestId))
                {
                    _closers.Remove(requestId);
                }
            }
        }

        private sealed class Registration : IDisposable
        {
            private readonly RemoteInteractionDialogRegistry _owner;
            private readonly string _requestId;
            private bool _disposed;

            public Registration(RemoteInteractionDialogRegistry owner, string requestId)
            {
                _owner = owner;
                _requestId = requestId;
            }

            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                if (_owner != null)
                {
                    _owner.Unregister(_requestId);
                }
            }
        }
    }
}
