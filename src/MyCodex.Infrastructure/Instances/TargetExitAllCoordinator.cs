using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Instances
{
    public sealed class TargetExitAllCoordinator
    {
        private readonly Func<IReadOnlyList<TargetInstanceReply>> _discover;
        private readonly Func<TargetInstanceRecord, TargetInstanceRequest, Task<TargetInstanceReply>> _send;
        private readonly TargetInstanceRecord _local;
        private readonly Func<TargetInstanceRecord, bool?> _isAlive;
        private readonly List<TargetInstanceRecord> _attempted = new List<TargetInstanceRecord>();
        private IReadOnlyList<TargetInstanceRecord> _scope = new TargetInstanceRecord[0];
        private bool _ready;

        public TargetExitAllCoordinator(TargetInstanceRecord local, Func<IReadOnlyList<TargetInstanceReply>> discover,
            Func<TargetInstanceRecord, TargetInstanceRequest, Task<TargetInstanceReply>> send,
            Func<TargetInstanceRecord, bool?> isAlive = null)
        {
            // An installer coordinator has no target window of its own.
            _local = local;
            _discover = discover ?? throw new ArgumentNullException(nameof(discover));
            _send = send ?? throw new ArgumentNullException(nameof(send));
            _isAlive = isAlive ?? TargetInstanceSecurity.IsAlive;
            OperationId = Guid.NewGuid().ToString("N");
        }

        public string OperationId { get; }
        public IReadOnlyList<TargetInstanceRecord> Scope => _scope;
        public IReadOnlyList<TargetInstanceReply> LastInspection { get; private set; } = new TargetInstanceReply[0];
        public IReadOnlyList<TargetInstanceRecord> AbortFailures { get; private set; } = new TargetInstanceRecord[0];

        public IReadOnlyList<TargetInstanceReply> CaptureScope()
        {
            var instances = _discover();
            LastInspection = instances;
            if (instances.Any(item => !item.Success || item.Instance == null || item.Status?.Lifecycle != TargetInstanceLifecycle.Running) ||
                (_local != null && !instances.Any(item => TargetInstanceCoordinator.SameInstance(item.Instance, _local))))
                throw new InvalidOperationException(SharedLocalization.Text("TargetExitAll.Error.NotReady",
                    "Some targets are starting, closing, or cannot be inspected. Finish those operations and retry."));
            _scope = instances.Select(item => item.Instance).ToArray();
            return instances;
        }

        public async Task PrepareAsync(CancellationToken cancellationToken, Action<TargetInstanceRecord> preparing = null)
        {
            if (_scope.Count == 0) throw new InvalidOperationException("The exit scope has not been confirmed.");
            try
            {
                // Freeze the coordinating target first; no target is stopped during preparation.
                foreach (var instance in _scope.OrderBy(item => TargetInstanceCoordinator.SameInstance(item, _local) ? 0 : 1))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    preparing?.Invoke(instance);
                    _attempted.Add(instance);
                    TargetInstanceReply reply;
                    do
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        reply = await _send(instance, Request(TargetInstanceOperation.PrepareExit));
                        if (!reply.Success || !TargetInstanceCoordinator.SameInstance(reply.Instance, instance))
                            throw new InvalidOperationException(reply.Error ?? "A target did not confirm exit preparation.");
                        if (reply.ExitPreparation == TargetExitPreparation.Pending)
                            await Task.Delay(200, cancellationToken);
                    } while (reply.ExitPreparation == TargetExitPreparation.Pending);
                    if (reply.ExitPreparation != TargetExitPreparation.Ready)
                        throw new InvalidOperationException(reply.Error ?? "A target cancelled exit preparation.");
                }
                EnsureScopeUnchanged();
                _ready = true;
            }
            catch
            {
                await AbortAsync();
                throw;
            }
        }

        public async Task CommitOtherTargetsAsync()
        {
            if (!_ready) throw new InvalidOperationException("All targets must prepare before any target is stopped.");
            EnsureScopeUnchanged();
            // A bounded preparation can expire while another target shows a long save dialog.
            // Revalidate every participant before the first stop, using the same prepared identity and operation.
            foreach (var instance in _scope)
            {
                var ready = await _send(instance, Request(TargetInstanceOperation.PrepareExit));
                if (!ready.Success || ready.ExitPreparation != TargetExitPreparation.Ready || !TargetInstanceCoordinator.SameInstance(ready.Instance, instance))
                    throw new InvalidOperationException(ready.Error ?? "Exit preparation expired or changed. Cancel and retry.");
            }
            EnsureScopeUnchanged();
            foreach (var instance in _scope.Where(item => !TargetInstanceCoordinator.SameInstance(item, _local)))
            {
                var reply = await _send(instance, Request(TargetInstanceOperation.CommitExit));
                // A normal exit may finish before the acknowledgement can be read. Verify actual process exit.
                if (!reply.Success && (reply.Instance == null || TargetInstanceCoordinator.SameInstance(reply.Instance, instance)) && _isAlive(instance) == false) continue;
                if (!reply.Success || !TargetInstanceCoordinator.SameInstance(reply.Instance, instance))
                    throw new InvalidOperationException(reply.Error ?? "A target did not confirm exit. Inspect the remaining windows.");
            }
        }

        public IReadOnlyList<TargetInstanceReply> GetRemainingOtherTargets()
        {
            var remaining = _discover().Where(item => !TargetInstanceCoordinator.SameInstance(item.Instance, _local)).ToList();
            foreach (var instance in _scope.Where(item => !TargetInstanceCoordinator.SameInstance(item, _local)))
            {
                if (remaining.Any(item => TargetInstanceCoordinator.SameInstance(item.Instance, instance)) || _isAlive(instance) == false) continue;
                remaining.Add(new TargetInstanceReply
                {
                    Instance = instance,
                    Error = "The owner process has not exited or its exit cannot be verified.",
                    Status = new TargetInstanceStatus { Lifecycle = TargetInstanceLifecycle.Closing }
                });
            }
            return remaining;
        }

        public async Task<IReadOnlyList<TargetInstanceReply>> WaitForOtherTargetsAsync(CancellationToken cancellationToken)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            IReadOnlyList<TargetInstanceReply> remaining;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                remaining = GetRemainingOtherTargets();
                if (remaining.Count == 0) return remaining;
                await Task.Delay(250, cancellationToken);
            } while (DateTime.UtcNow < deadline);
            return remaining;
        }

        public async Task AbortAsync()
        {
            _ready = false;
            var failures = new List<TargetInstanceRecord>();
            foreach (var instance in _attempted.ToArray())
            {
                try
                {
                    var reply = await _send(instance, Request(TargetInstanceOperation.AbortExit));
                    if (!reply.Success && _isAlive(instance) != false) failures.Add(instance);
                }
                catch { if (_isAlive(instance) != false) failures.Add(instance); }
            }
            if (failures.Count > 0) AbortFailures = failures;
            _attempted.Clear();
        }

        private TargetInstanceRequest Request(TargetInstanceOperation operation)
        {
            return new TargetInstanceRequest { Operation = operation, OperationId = OperationId };
        }

        private void EnsureScopeUnchanged()
        {
            var actual = _discover();
            if (actual.Count != _scope.Count || actual.Any(item => !item.Success ||
                !_scope.Any(expected => TargetInstanceCoordinator.SameInstance(expected, item.Instance))))
                throw new InvalidOperationException("The running targets changed. No unprepared target will be closed; cancel and retry.");
        }
    }
}
