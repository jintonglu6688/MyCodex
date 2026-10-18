using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;

namespace MyCodex.WinForms.Forms
{
    public sealed class AutomationRuntimeHost : IDisposable
    {
        private const int TickIntervalMilliseconds = 60000;
        private readonly IAutomationStore _store;
        private readonly IAutomationRunner _runner;
        private readonly AutomationRuntimePlanner _planner;
        private readonly Timer _timer;
        private Action<Exception> _persistenceFailureHandler;
        private bool _isTickRunning;
        private int _runtimeGeneration;
        private bool _exitPreparing;

        internal bool TryPauseForExit()
        {
            if (_isTickRunning) return false;
            _exitPreparing = true;
            return true;
        }

        internal void ResumeAfterCancelledExit()
        {
            _exitPreparing = false;
        }

        public AutomationRuntimeHost(
            IAutomationStore store,
            IAutomationRunner runner,
            AutomationRuntimePlanner planner)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _planner = planner ?? throw new ArgumentNullException(nameof(planner));
            _timer = new Timer { Interval = TickIntervalMilliseconds };
            _timer.Tick += Timer_Tick;
        }

        public void Start()
        {
            SkipMissedRunsOnStartup();
            _timer.Start();
        }

        public void Stop()
        {
            _runtimeGeneration++;
            _timer.Stop();
        }

        public void StopForRuntimeTransition(string reason)
        {
            Stop();
            _runner.CancelRunning(reason);
        }

        internal void SetPersistenceFailureHandler(Action<Exception> handler)
        {
            _persistenceFailureHandler = handler;
        }

        public void Dispose()
        {
            _timer.Tick -= Timer_Tick;
            Stop();
            _timer.Dispose();
        }

        public bool HandleTurnTerminal(string threadId, string turnId, AutomationRunStatus status, string summary, string errorMessage)
        {
            return HandleTurnTerminal(threadId, turnId, new AutomationRunCompletion
            {
                Status = status,
                CompletedAt = DateTime.Now,
                Summary = summary,
                ErrorMessage = errorMessage
            });
        }

        public bool HandleTurnTerminal(string threadId, string turnId, AutomationRunCompletion completion)
        {
            if (completion == null)
            {
                completion = new AutomationRunCompletion();
            }

            if (completion.CompletedAt == default(DateTime))
            {
                completion.CompletedAt = DateTime.Now;
            }

            if (string.IsNullOrWhiteSpace(completion.TurnId))
            {
                completion.TurnId = turnId;
            }

            return _runner.CompleteByThread(threadId, turnId, completion);
        }

        private async void Timer_Tick(object sender, EventArgs e)
        {
            await RunTimerTickAsync().ConfigureAwait(true);
        }

        internal async Task RunTimerTickAsync()
        {
            try
            {
                await RunDueAutomationsAsync().ConfigureAwait(true);
            }
            catch (IOException ex)
            {
                Stop();
                if (_persistenceFailureHandler == null)
                {
                    throw;
                }

                _persistenceFailureHandler(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                Stop();
                if (_persistenceFailureHandler == null)
                {
                    throw;
                }

                _persistenceFailureHandler(ex);
            }
        }

        internal async Task RunDueAutomationsAsync()
        {
            if (_isTickRunning || _exitPreparing)
            {
                return;
            }

            _isTickRunning = true;
            var runtimeGeneration = _runtimeGeneration;
            try
            {
                var snapshot = _store.Load();
                var dueIds = _planner.BuildDueRunIds(snapshot, DateTime.Now);
                foreach (var automationId in dueIds)
                {
                    if (runtimeGeneration != _runtimeGeneration) break;
                    try
                    {
                        await _runner.StartAsync(new AutomationRunRequest
                        {
                            AutomationId = automationId,
                            TriggerReason = AutomationTriggerReason.Scheduled,
                            ScheduledFor = DateTime.Now
                        }).ConfigureAwait(true);
                    }
                    catch (IOException)
                    {
                        throw;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        throw;
                    }
                    catch (AuthenticationCenterException ex) when (string.Equals(ex.Code, "runtime_busy", StringComparison.Ordinal))
                    {
                        // NextRunAt is unchanged, so the next tick retries after the runtime transition.
                        System.Diagnostics.Trace.TraceInformation("[Automation] Scheduled start deferred while the runtime is busy.");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Trace.TraceWarning("[Automation] Scheduled start failed. ExceptionType={0}", ex.GetType().Name);
                    }
                }
            }
            finally
            {
                _isTickRunning = false;
            }
        }

        private void SkipMissedRunsOnStartup()
        {
            _store.Mutate(snapshot =>
            {
                _planner.SkipMissedRunsOnStartup(
                    snapshot,
                    DateTime.Now,
                    () => Guid.NewGuid().ToString("N"));
                return snapshot;
            });
        }
    }
}
