using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.Services
{
    public sealed class CommandTerminalResizePlanner
    {
        private readonly TimeSpan _throttleInterval;
        private readonly HashSet<string> _disabledProcessIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _lastProcessId;
        private AppServerCommandTerminalSize _lastSentSize;
        private DateTime _lastSentAtUtc = DateTime.MinValue;

        public CommandTerminalResizePlanner()
            : this(TimeSpan.FromMilliseconds(250))
        {
        }

        public CommandTerminalResizePlanner(TimeSpan throttleInterval)
        {
            _throttleInterval = throttleInterval < TimeSpan.Zero ? TimeSpan.Zero : throttleInterval;
        }

        public CommandTerminalResizePlan Plan(string processId, AppServerCommandTerminalSize size, DateTime nowUtc)
        {
            var plan = new CommandTerminalResizePlan();
            if (string.IsNullOrWhiteSpace(processId) || size == null)
            {
                return plan;
            }

            processId = processId.Trim();
            if (_disabledProcessIds.Contains(processId))
            {
                return plan;
            }

            if (string.Equals(_lastProcessId, processId, StringComparison.OrdinalIgnoreCase) &&
                _lastSentSize != null &&
                _lastSentSize.Rows == size.Rows &&
                _lastSentSize.Cols == size.Cols)
            {
                return plan;
            }

            if (string.Equals(_lastProcessId, processId, StringComparison.OrdinalIgnoreCase) &&
                _lastSentAtUtc != DateTime.MinValue &&
                nowUtc - _lastSentAtUtc < _throttleInterval)
            {
                return plan;
            }

            _lastProcessId = processId;
            _lastSentSize = size;
            _lastSentAtUtc = nowUtc;
            plan.ShouldSend = true;
            plan.ProcessId = processId;
            plan.Size = size;
            return plan;
        }

        public void DisableForProcess(string processId)
        {
            if (!string.IsNullOrWhiteSpace(processId))
            {
                _disabledProcessIds.Add(processId.Trim());
            }
        }
    }

    public sealed class CommandTerminalResizePlan
    {
        public bool ShouldSend { get; set; }

        public string ProcessId { get; set; }

        public AppServerCommandTerminalSize Size { get; set; }
    }
}
