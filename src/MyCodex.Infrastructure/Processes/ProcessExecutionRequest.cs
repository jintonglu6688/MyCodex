using System;
using System.Diagnostics;
using System.Threading;

namespace MyCodex.Infrastructure.Processes
{
    public sealed class ProcessExecutionRequest
    {
        public ProcessStartInfo StartInfo { get; set; }

        public int TimeoutMilliseconds { get; set; }

        public string TimeoutMessage { get; set; }

        public CancellationToken CancellationToken { get; set; }

        public string StandardInput { get; set; }

        public Func<string, string> OutputTransform { get; set; }

        public Action<string, bool> OutputReceived { get; set; }

        public int KillWaitMilliseconds { get; set; }
    }
}
