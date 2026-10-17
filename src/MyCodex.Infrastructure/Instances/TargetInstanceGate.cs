using System;
using System.Threading;

namespace MyCodex.Infrastructure.Instances
{
    // A dedicated thread acquires AND releases the mutex, including async UI shutdown.
    internal sealed class TargetInstanceGate : IDisposable
    {
        private readonly ManualResetEventSlim _release = new ManualResetEventSlim();
        private readonly Thread _thread;
        private int _disposed;

        private TargetInstanceGate(string route)
        {
            Exception failure = null;
            using (var ready = new ManualResetEventSlim())
            {
                _thread = new Thread(() =>
                {
                    Mutex mutex = null;
                    try
                    {
                        bool created;
                        mutex = new Mutex(false, @"Global\" + route, out created, TargetInstanceSecurity.MutexSecurity());
                        try { Acquired = mutex.WaitOne(0); }
                        catch (AbandonedMutexException) { Acquired = true; }
                    }
                    catch (Exception exception) { failure = exception; }
                    finally { ready.Set(); }

                    try { if (Acquired) _release.Wait(); }
                    finally
                    {
                        if (Acquired) mutex.ReleaseMutex();
                        mutex?.Dispose();
                    }
                }) { IsBackground = true, Name = "MyCodex target lifetime gate" };
                _thread.Start();
                ready.Wait();
            }
            if (failure != null)
            {
                Dispose();
                throw new InvalidOperationException("The target ownership gate could not be verified.", failure);
            }
        }

        internal bool Acquired { get; private set; }

        internal static TargetInstanceGate TryAcquire(string route)
        {
            var gate = new TargetInstanceGate(route);
            if (gate.Acquired) return gate;
            gate.Dispose();
            return null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _release.Set();
            _thread.Join();
            _release.Dispose();
        }
    }
}
