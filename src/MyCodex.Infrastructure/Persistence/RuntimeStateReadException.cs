using System;
using System.IO;
using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed class RuntimeStateReadException : IOException
    {
        internal RuntimeStateReadException(string statePath, Exception error)
            : base("Saved state could not be read from the execution target.", error)
        {
            StatePath = statePath;
            FailureReason = GitOutputSanitizer.RedactSensitiveText(error.Message);
        }

        public string StatePath { get; private set; }

        public string FailureReason { get; private set; }
    }
}
