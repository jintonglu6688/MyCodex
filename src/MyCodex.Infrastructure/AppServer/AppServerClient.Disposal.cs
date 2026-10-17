using System;
using System.Threading.Tasks;

namespace MyCodex.Infrastructure.AppServer
{
    public partial class AppServerClient
    {
        private readonly object _processStartGate = new object();
        private volatile bool _disposed;

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AppServerClient));
        }

        public void Dispose()
        {
            lock (_processStartGate)
            {
                if (_disposed) return;
                _disposed = true;
                DisconnectForRuntimeChange();
            }
            // Do not wait for a network probe or runtime resolution on the UI thread.
            // Their continuations see _disposed before they can launch a process.
            _ = DisposeConnectionResourcesAsync();
        }

        private async Task DisposeConnectionResourcesAsync()
        {
            await _connectionLifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _writeLock.Dispose();
                _runtimeStartupCoordinator.Dispose();
            }
            finally
            {
                _connectionLifecycleLock.Release();
            }
        }
    }
}
