using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Shared.Helpers;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class SharedEmbeddedRelayService : IEmbeddedRelayService, IDisposable
    {
        private readonly AppConfigService _config;
        private readonly SharedRelayStateStore _ownership;
        private readonly SharedRelayConfigStore _store;
        private readonly EmbeddedRelayService _reader;
        private readonly SharedRelayProcessRecord _user;
        private readonly string _leaseId = Guid.NewGuid().ToString("N");
        private readonly object _gate = new object();
        private readonly Timer _watchdog;
        private bool _acquired;
        private bool _disposed;
        private string _lastStatusFailure;
        private string _lastCleanupFailure;

        public SharedEmbeddedRelayService(AppConfigService config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _ownership = new SharedRelayStateStore(config.GetSharedDataDirectory());
            _store = new SharedRelayConfigStore(config.GetSharedDataDirectory());
            _reader = new EmbeddedRelayService(config, _store);
            using (var process = Process.GetCurrentProcess())
            {
                _user = SharedRelayProcessRecord.Capture(process);
            }

            _watchdog = new Timer(Recover, null, Timeout.Infinite, Timeout.Infinite);
        }

        public EmbeddedRelayStatus GetStatus(RemoteServiceConfigSnapshot snapshot)
        {
            lock (_gate)
            {
                try
                {
                    var result = _ownership.WithLock(state =>
                    {
                        var status = _reader.GetStatus(_store.Load());
                        if (!string.IsNullOrWhiteSpace(state.Error))
                        {
                            status.State = EmbeddedRelayRunState.Failed;
                            status.Message = state.Error;
                        }
                        else if (!_ownership.IsAlive(state.Host) || !state.Ready)
                        {
                            status.State = EmbeddedRelayRunState.Stopped;
                        }
                        return status;
                    }, CancellationToken.None);
                    _lastStatusFailure = null;
                    return result;
                }
                catch (Exception ex)
                {
                    var signature = ex.GetType().Name + ":" + ex.HResult;
                    if (!string.Equals(signature, _lastStatusFailure, StringComparison.Ordinal))
                    {
                        _lastStatusFailure = signature;
                        Trace.TraceWarning("[RemoteRelay] SharedStatusFailed; SharedRoot={0}; ExceptionType={1}; HResult={2:X8}; OwnershipRetained=true",
                            _config.GetSharedDataDirectory(), ex.GetType().Name, ex.HResult);
                    }
                    return new EmbeddedRelayStatus
                    {
                        State = EmbeddedRelayRunState.Failed,
                        Message = ex is InvalidDataException ? ex.Message : SharedLocalization.Text("RemoteRelay.Error.OwnershipUnreadable",
                            "Phone connection state could not be read. Details are in the application log; please send it for support. The existing service was left unchanged.")
                    };
                }
            }
        }

        public async Task<EmbeddedRelayStartResult> StartAsync(RemoteServiceConfigSnapshot snapshot, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (_gate)
                    {
                        if (_disposed) throw new ObjectDisposedException(nameof(SharedEmbeddedRelayService));
                        EnsureHost(cancellationToken);
                        _acquired = true;
                        _watchdog.Change(2000, 2000);
                    }
                }, cancellationToken).ConfigureAwait(false);

                var timeout = Stopwatch.StartNew();
                while (timeout.Elapsed < TimeSpan.FromSeconds(30))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var status = await Task.Run(() => GetStatus(null), cancellationToken).ConfigureAwait(false);
                    if (status.State == EmbeddedRelayRunState.Running)
                    {
                        return new EmbeddedRelayStartResult { Success = true, Status = status };
                    }

                    if (status.State == EmbeddedRelayRunState.Failed)
                    {
                        throw new InvalidOperationException(status.Message);
                    }

                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }

                throw new TimeoutException("The shared Relay host did not become ready within 30 seconds.");
            }
            catch (OperationCanceledException)
            {
                await ReleaseAfterFailedStartAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await ReleaseAfterFailedStartAsync().ConfigureAwait(false);
                return new EmbeddedRelayStartResult { Success = false, ErrorMessage = RelaySecretRedactor.Redact(ex.Message) };
            }
        }

        private async Task ReleaseAfterFailedStartAsync()
        {
            try
            {
                await StopAsync(CancellationToken.None).ConfigureAwait(false);
                _lastCleanupFailure = null;
            }
            catch (Exception ex)
            {
                var signature = ex.GetType().Name + ":" + ex.HResult;
                if (!string.Equals(signature, _lastCleanupFailure, StringComparison.Ordinal))
                {
                    _lastCleanupFailure = signature;
                    Trace.TraceWarning("[RemoteRelay] SharedStartCleanupFailed; ExceptionType={0}; OwnershipRetained=true", ex.GetType().Name);
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.Run(() => Release(cancellationToken), cancellationToken);
        }

        private void Release(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_disposed) return;
                _acquired = false;
                _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
                var wasLast = _ownership.WithLock(state =>
                {
                    var removed = state.Leases.RemoveAll(lease => lease.Id == _leaseId &&
                        SharedEmbeddedRelayHost.SameProcess(lease.User, _user));
                    if (removed > 0 && state.Leases.Count == 0 && !_ownership.IsAlive(state.Host))
                    {
                        // Recover a dead helper even when the last user's action is to leave.
                        LaunchHost(state);
                    }

                    return removed > 0 && state.Leases.Count == 0;
                }, cancellationToken);
                if (wasLast)
                {
                    var timeout = Stopwatch.StartNew();
                    while (timeout.Elapsed < TimeSpan.FromSeconds(15))
                    {
                        if (_ownership.WithLock(state => state.Leases.Count > 0 || !_ownership.IsAlive(state.Host), cancellationToken)) break;
                        cancellationToken.ThrowIfCancellationRequested();
                        cancellationToken.WaitHandle.WaitOne(100);
                    }
                }
            }
        }

        public EmbeddedRelayDiagnosticInfo GetDiagnostics(RemoteServiceConfigSnapshot snapshot)
        {
            lock (_gate)
            {
                var diagnostics = _reader.GetDiagnostics(_store.Load());
                diagnostics.Status = GetStatus(null);
                return diagnostics;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                try
                {
                    Release(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("[RemoteRelay] SharedDisposeFailed; ExceptionType={0}; OwnershipRetained=true", ex.GetType().Name);
                }
                finally
                {
                    _disposed = true;
                    _watchdog.Dispose();
                }
            }
        }

        private void Recover(object ignored)
        {
            lock (_gate)
            {
                if (!_acquired || _disposed) return;
                try
                {
                    EnsureHost(CancellationToken.None);
                }
                catch
                {
                    // Status queries expose host failures; the next tick retries transient lock/process errors.
                }
            }
        }

        private void EnsureHost(CancellationToken cancellationToken)
        {
            _ownership.WithLock(state =>
            {
                if (!string.IsNullOrWhiteSpace(state.InstallationExecutable) &&
                    !string.Equals(state.InstallationExecutable, _user.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The shared Relay is owned by another MyCodex installation; it was not stopped.");
                }

                if (!state.Leases.Any(lease => lease.Id == _leaseId))
                {
                    state.Leases.Add(new SharedRelayLease { Id = _leaseId, User = _user });
                }

                if (_ownership.IsAlive(state.Host))
                {
                    if (!string.Equals(state.Host.ExecutablePath, _user.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("The recorded Relay host belongs to another installation; it was not stopped.");
                    }

                    return true;
                }
                LaunchHost(state);

                return true;
            }, cancellationToken);
        }

        private void LaunchHost(SharedRelayState state)
        {
            state.Ready = false;
            state.Error = null;
            state.InstallationExecutable = _user.ExecutablePath;
            using (var host = Process.Start(new ProcessStartInfo
            {
                FileName = _user.ExecutablePath,
                Arguments = SharedEmbeddedRelayHost.Argument + " " +
                    WindowsCommandLineArgumentEscaper.Quote(_config.GetSharedDataDirectory()) + " " +
                    WindowsCommandLineArgumentEscaper.Quote(_config.GetInstallationDirectory()) + " MyCodex",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            }))
            {
                try
                {
                    state.Host = SharedRelayProcessRecord.Capture(host, _user.ExecutablePath);
                }
                catch
                {
                    // This handle belongs to the process just launched here; no persisted PID is used.
                    if (!host.HasExited) host.Kill();
                    throw;
                }
            }
        }
    }
}
