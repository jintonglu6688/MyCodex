using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;

namespace MyCodex.Infrastructure.ComputerUse
{
    // Windows mutex ownership is thread-affine; all operations run on this one worker.
    internal sealed class ComputerUseDesktopLease : IDisposable
    {
        private readonly BlockingCollection<Action> _work = new BlockingCollection<Action>();
        private readonly Thread _worker;
        private Mutex _mutex;
        private string _owner;

        public ComputerUseDesktopLease()
        {
            var sid = WindowsIdentity.GetCurrent().User;
            var security = new MutexSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new MutexAccessRule(sid, MutexRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));
            bool created;
            _mutex = new Mutex(false, @"Local\MyCodex.ComputerUse." + sid.Value + "." + Process.GetCurrentProcess().SessionId, out created, security);
            _worker = new Thread(Run) { IsBackground = true, Name = "Computer Use desktop lease" };
            _worker.Start();
        }

        public Task<bool> TryAcquireAsync(string owner)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                _work.Add(() =>
                {
                    try
                    {
                        var acquired = _owner == owner;
                        if (_owner == null)
                        {
                            try { acquired = _mutex.WaitOne(0); }
                            catch (AbandonedMutexException)
                            {
                                // Do not inherit an interrupted desktop action in this request.
                                _mutex.ReleaseMutex();
                                acquired = false;
                            }
                            if (acquired) _owner = owner;
                        }
                        completion.TrySetResult(acquired);
                    }
                    catch (Exception ex) { completion.TrySetException(ex); }
                });
            }
            catch (InvalidOperationException) { completion.TrySetResult(false); }
            return completion.Task;
        }

        public void Release(string owner)
        {
            try { _work.Add(() => { if (_owner == owner) ReleaseCore(); }); }
            catch (InvalidOperationException) { }
        }

        private void Run()
        {
            try { foreach (var action in _work.GetConsumingEnumerable()) action(); }
            finally { ReleaseCore(); _mutex.Dispose(); }
        }

        private void ReleaseCore()
        {
            if (_owner == null) return;
            _owner = null;
            _mutex.ReleaseMutex();
        }

        public void Dispose()
        {
            _work.CompleteAdding();
            if (Thread.CurrentThread != _worker) _worker.Join(2000);
        }
    }
}
