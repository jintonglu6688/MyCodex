using System;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.Codex
{
    public sealed class CodexRuntimeUnavailableException : InvalidOperationException
    {
        public CodexRuntimeUnavailableException(CodexRuntimeStatus status)
            : base((status ?? new CodexRuntimeStatus()).Message ??
                SharedLocalization.Text("CodexRuntime.Error.NoAvailableRuntime", "No available Codex runtime was detected."))
        {
            Status = status ?? new CodexRuntimeStatus();
        }

        public CodexRuntimeStatus Status { get; private set; }
    }
}
