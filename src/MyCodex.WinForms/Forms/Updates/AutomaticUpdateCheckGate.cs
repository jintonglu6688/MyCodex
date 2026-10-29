using System;
using System.Collections.Generic;
using System.Threading;

namespace MyCodex.WinForms.Forms
{
    // Program updates belong to the installation, so only the first running target of a channel
    // checks automatically; the others would repeat the same prompt and download.
    internal static class AutomaticUpdateCheckGate
    {
        // Held until the process exits; the next owner then acquires the abandoned mutex.
        private static readonly Dictionary<string, Mutex> Claims = new Dictionary<string, Mutex>(StringComparer.Ordinal);

        internal static bool TryClaim(string channelId)
        {
            if (string.IsNullOrWhiteSpace(channelId))
            {
                return true;
            }

            var name = @"Local\MyCodex.AutomaticUpdateCheck." + channelId.Trim();
            if (Claims.ContainsKey(name))
            {
                return true;
            }

            var mutex = new Mutex(false, name);
            try
            {
                if (mutex.WaitOne(0))
                {
                    Claims[name] = mutex;
                    return true;
                }
            }
            catch (AbandonedMutexException)
            {
                Claims[name] = mutex;
                return true;
            }

            mutex.Dispose();
            return false;
        }
    }
}
