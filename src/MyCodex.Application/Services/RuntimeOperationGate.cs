using System;
using System.Threading;
using MyCodex.Application.Interfaces;

namespace MyCodex.Application.Services
{
    public sealed class RuntimeOperationGate
    {
        // All workbenches in this process share one execution target.
        public static RuntimeOperationGate Shared { get; } = new RuntimeOperationGate();

        private readonly object _sync = new object();
        private int _operations;
        private object _transition;
        private readonly AsyncLocal<object> _transitionOwner = new AsyncLocal<object>();

        public IDisposable EnterOperation()
        {
            return EnterOperation(false);
        }

        public IDisposable EnterOperation(bool allowTransitionOwner)
        {
            lock (_sync)
            {
                if (_transition != null && !(allowTransitionOwner && ReferenceEquals(_transitionOwner.Value, _transition)))
                {
                    throw new AuthenticationCenterException("runtime_busy");
                }
                _operations++;
                return new Lease(() =>
                {
                    lock (_sync)
                    {
                        _operations--;
                    }
                });
            }
        }

        public IDisposable TryEnterTransition()
        {
            lock (_sync)
            {
                if (_transition != null || _operations != 0)
                {
                    return null;
                }
                var owner = new object();
                _transition = owner;
                _transitionOwner.Value = owner;
                return new Lease(() =>
                {
                    lock (_sync)
                    {
                        _transition = null;
                        if (ReferenceEquals(_transitionOwner.Value, owner)) _transitionOwner.Value = null;
                    }
                });
            }
        }

        private sealed class Lease : IDisposable
        {
            private Action _release;

            internal Lease(Action release)
            {
                _release = release;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _release, null)?.Invoke();
            }
        }
    }
}
