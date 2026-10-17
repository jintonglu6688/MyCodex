using System;

namespace MyCodex.Application.DTOs
{
    public sealed class SessionThreadRecreationResult
    {
        public string PreviousThreadId { get; set; }

        public string CurrentThreadId { get; set; }

        public bool ReplacedExistingThread
        {
            get
            {
                return !string.IsNullOrWhiteSpace(PreviousThreadId);
            }
        }
    }
}
