using System;
using System.Diagnostics;
using MyCodex.Application.DTOs;

namespace MyCodex.WinForms.Forms
{
    internal static class TurnFailureNotifier
    {
        internal static bool TryNotify(AppServerEventArgs notification, bool wasBusy, bool soundEnabled, Action playSound)
        {
            if (notification == null || notification.IsHistorical || !wasBusy || !soundEnabled ||
                !string.Equals(notification.EventType, "turn_failed", StringComparison.Ordinal))
                return false;

            try
            {
                playSound();
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Could not play the turn failure sound. ErrorType={0}", ex.GetType().FullName);
                return false;
            }
        }
    }
}
