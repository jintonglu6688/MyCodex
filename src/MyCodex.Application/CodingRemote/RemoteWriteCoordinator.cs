using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.CodingRemote
{
    public sealed class RemoteWriteCoordinator
    {
        private const int MaxCompletedOperationCount = 256;
        private static readonly TimeSpan CompletedOperationTtl = TimeSpan.FromMinutes(5);
        private readonly object _sync = new object();
        private readonly Dictionary<string, SessionOperationEntry> _sessionOperations =
            new Dictionary<string, SessionOperationEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly TimeSpan _leaseDuration;
        private string _holderDeviceId;
        private string _operationId;
        private DateTime _expiresAtUtc;

        public RemoteWriteCoordinator()
            : this(TimeSpan.FromSeconds(30))
        {
        }

        public RemoteWriteCoordinator(TimeSpan leaseDuration)
        {
            if (leaseDuration <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(leaseDuration));
            }

            _leaseDuration = leaseDuration;
        }

        public RemoteWriteLockResult Acquire(string deviceId, string operationId, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Device ID is required.", nameof(deviceId));
            }

            lock (_sync)
            {
                ExpireIfNeeded(utcNow);
                if (string.IsNullOrWhiteSpace(_holderDeviceId) ||
                    string.Equals(_holderDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    _holderDeviceId = deviceId;
                    _operationId = operationId;
                    _expiresAtUtc = utcNow.Add(_leaseDuration);
                    return BuildResult(true, null);
                }

                return BuildResult(false, "Another device currently holds the remote write lock.");
            }
        }

        public bool Heartbeat(string deviceId, DateTime utcNow)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return false;
            }

            lock (_sync)
            {
                ExpireIfNeeded(utcNow);
                if (!string.Equals(_holderDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                _expiresAtUtc = utcNow.Add(_leaseDuration);
                return true;
            }
        }

        public bool Release(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return false;
            }

            lock (_sync)
            {
                if (!string.Equals(_holderDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (HasActiveSessionOperation(deviceId))
                {
                    return false;
                }

                ClearLock();
                return true;
            }
        }

        public void ReleaseDevice(string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return;
            }

            lock (_sync)
            {
                if (string.Equals(_holderDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    ClearLock();
                }
            }
        }

        public RemoteSessionOperationLease BeginSessionOperation(string deviceId, string sessionId, string operationId)
        {
            if (string.IsNullOrWhiteSpace(deviceId) ||
                string.IsNullOrWhiteSpace(sessionId) ||
                string.IsNullOrWhiteSpace(operationId))
            {
                return RemoteSessionOperationLease.Conflict();
            }

            lock (_sync)
            {
                CleanupCompletedOperations(DateTime.UtcNow);
                if (!string.Equals(_holderDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return RemoteSessionOperationLease.Conflict();
                }

                var key = BuildOperationKey(sessionId, operationId);
                SessionOperationEntry existing;
                if (_sessionOperations.TryGetValue(key, out existing))
                {
                    if (!string.Equals(existing.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return RemoteSessionOperationLease.Conflict();
                    }

                    return new RemoteSessionOperationLease(
                        existing.IsCompleted ? RemoteSessionOperationState.Completed : RemoteSessionOperationState.InFlight,
                        existing.Completion.Task,
                        null);
                }

                foreach (var operation in _sessionOperations.Values)
                {
                    if (!operation.IsCompleted &&
                        string.Equals(operation.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                    {
                        return RemoteSessionOperationLease.Conflict();
                    }
                }

                var entry = new SessionOperationEntry(deviceId, sessionId, operationId);
                _sessionOperations[key] = entry;
                return new RemoteSessionOperationLease(
                    RemoteSessionOperationState.First,
                    entry.Completion.Task,
                    (result, error) => FinishOperation(entry, result, error));
            }
        }

        private void ExpireIfNeeded(DateTime utcNow)
        {
            if (!string.IsNullOrWhiteSpace(_holderDeviceId) && utcNow > _expiresAtUtc)
            {
                ClearLock();
            }
        }

        private void ClearLock()
        {
            _holderDeviceId = null;
            _operationId = null;
            _expiresAtUtc = default(DateTime);
            RemoveInFlightOperations();
        }

        private void FinishOperation(SessionOperationEntry entry, RemoteRelayEvent result, Exception error)
        {
            lock (_sync)
            {
                if (error == null)
                {
                    entry.IsCompleted = true;
                    entry.CompletedAtUtc = DateTime.UtcNow;
                    CleanupCompletedOperations(entry.CompletedAtUtc);
                    entry.Completion.TrySetResult(result);
                }
                else
                {
                    RemoveOperation(entry);
                    if (error is OperationCanceledException)
                    {
                        entry.Completion.TrySetCanceled();
                    }
                    else
                    {
                        entry.Completion.TrySetException(error);
                    }
                }
            }
        }

        private bool HasActiveSessionOperation(string deviceId)
        {
            foreach (var operation in _sessionOperations.Values)
            {
                if (!operation.IsCompleted &&
                    string.Equals(operation.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void RemoveInFlightOperations()
        {
            foreach (var key in new List<string>(_sessionOperations.Keys))
            {
                if (!_sessionOperations[key].IsCompleted)
                {
                    _sessionOperations.Remove(key);
                }
            }
        }

        private void CleanupCompletedOperations(DateTime utcNow)
        {
            var completed = new List<SessionOperationEntry>();
            foreach (var operation in _sessionOperations.Values)
            {
                if (operation.IsCompleted)
                {
                    completed.Add(operation);
                }
            }

            completed.Sort((left, right) => left.CompletedAtUtc.CompareTo(right.CompletedAtUtc));
            for (var index = completed.Count - 1; index >= 0; index--)
            {
                var operation = completed[index];
                if (utcNow - operation.CompletedAtUtc >= CompletedOperationTtl)
                {
                    RemoveOperation(operation);
                    completed.RemoveAt(index);
                }
            }

            for (var index = 0; index < completed.Count - MaxCompletedOperationCount; index++)
            {
                RemoveOperation(completed[index]);
            }
        }

        private void RemoveOperation(SessionOperationEntry entry)
        {
            SessionOperationEntry current;
            var key = BuildOperationKey(entry.SessionId, entry.OperationId);
            if (_sessionOperations.TryGetValue(key, out current) && ReferenceEquals(current, entry))
            {
                _sessionOperations.Remove(key);
            }
        }

        private static string BuildOperationKey(string sessionId, string operationId)
        {
            return sessionId.Length + ":" + sessionId + operationId;
        }

        private RemoteWriteLockResult BuildResult(bool granted, string reason)
        {
            return new RemoteWriteLockResult
            {
                IsGranted = granted,
                HolderDeviceId = _holderDeviceId,
                OperationId = _operationId,
                ExpiresAtUtc = _expiresAtUtc,
                Reason = reason
            };
        }

        private sealed class SessionOperationEntry
        {
            public SessionOperationEntry(string deviceId, string sessionId, string operationId)
            {
                DeviceId = deviceId;
                SessionId = sessionId;
                OperationId = operationId;
                Completion = new TaskCompletionSource<RemoteRelayEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public string DeviceId { get; }

            public string SessionId { get; }

            public string OperationId { get; }

            public TaskCompletionSource<RemoteRelayEvent> Completion { get; }

            public bool IsCompleted { get; set; }

            public DateTime CompletedAtUtc { get; set; }
        }
    }

    public enum RemoteSessionOperationState
    {
        First,
        InFlight,
        Completed,
        Conflict
    }

    public sealed class RemoteSessionOperationLease
    {
        private static readonly Task<RemoteRelayEvent> EmptyCompletion = Task.FromResult<RemoteRelayEvent>(null);
        private readonly Action<RemoteRelayEvent, Exception> _finish;
        private int _owner;

        internal RemoteSessionOperationLease(
            RemoteSessionOperationState state,
            Task<RemoteRelayEvent> completion,
            Action<RemoteRelayEvent, Exception> finish)
        {
            State = state;
            Completion = completion ?? EmptyCompletion;
            _finish = finish;
            _owner = state == RemoteSessionOperationState.First ? 1 : 0;
        }

        public RemoteSessionOperationState State { get; }

        public Task<RemoteRelayEvent> Completion { get; }

        public bool Complete(RemoteRelayEvent result)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }

            if (Interlocked.Exchange(ref _owner, 0) != 1)
            {
                return false;
            }

            _finish(result, null);
            return true;
        }

        public bool Fail(Exception error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            if (Interlocked.Exchange(ref _owner, 0) != 1)
            {
                return false;
            }

            _finish(null, error);
            return true;
        }

        internal static RemoteSessionOperationLease Conflict()
        {
            return new RemoteSessionOperationLease(RemoteSessionOperationState.Conflict, EmptyCompletion, null);
        }
    }
}
