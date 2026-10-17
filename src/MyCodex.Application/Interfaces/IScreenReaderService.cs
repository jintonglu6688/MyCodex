using System;

namespace MyCodex.Application.Interfaces
{
    public interface IScreenReaderService : IDisposable
    {
        bool IsAvailable { get; }

        bool IsInitialized { get; }

        string LastError { get; }

        bool EnsureInitialized();

        bool IsReaderRunning();

        int GetReaderState();

        bool Speak(string text, bool useSlave, bool append, bool allowBreak);

        bool Stop(bool useSlave);
    }
}
