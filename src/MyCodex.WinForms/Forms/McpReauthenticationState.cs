using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class McpReauthenticationNotification
    {
        internal McpReauthenticationNotification(
            string serverName,
            string failureReason,
            bool shouldSpeak)
        {
            ServerName = serverName;
            FailureReason = failureReason;
            ShouldSpeak = shouldSpeak;
        }

        internal string ServerName { get; private set; }

        internal string FailureReason { get; private set; }

        internal bool ShouldSpeak { get; private set; }
    }

    internal sealed class McpReauthenticationState
    {
        private const string UnknownServerKey = "\0";
        private readonly object _syncRoot = new object();
        private readonly HashSet<string> _activeServerKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _pendingServerName;
        private string _pendingServerKey;
        private string _pendingFailureReason;

        internal McpReauthenticationNotification Record(AppServerEventArgs e)
        {
            if (e == null ||
                !string.Equals(e.EventType, "mcp_startup_status_updated", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var serverName = string.IsNullOrWhiteSpace(e.McpServerName)
                ? null
                : e.McpServerName.Trim();
            var serverKey = serverName ?? UnknownServerKey;
            var failureReason = e.Reason;
            lock (_syncRoot)
            {
                if (!string.Equals(failureReason, "reauthenticationRequired", StringComparison.Ordinal))
                {
                    _activeServerKeys.Remove(serverKey);
                    if (string.Equals(_pendingServerKey, serverKey, StringComparison.OrdinalIgnoreCase))
                    {
                        ClearPending();
                    }

                    return null;
                }

                _pendingServerName = serverName;
                _pendingServerKey = serverKey;
                _pendingFailureReason = failureReason;
                var shouldSpeak = _activeServerKeys.Add(serverKey);
                return new McpReauthenticationNotification(serverName, failureReason, shouldSpeak);
            }
        }

        internal McpReauthenticationNotification ConsumePending()
        {
            lock (_syncRoot)
            {
                if (string.IsNullOrWhiteSpace(_pendingFailureReason))
                {
                    return null;
                }

                var pending = new McpReauthenticationNotification(
                    _pendingServerName,
                    _pendingFailureReason,
                    false);
                ClearPending();
                return pending;
            }
        }

        private void ClearPending()
        {
            _pendingServerName = null;
            _pendingServerKey = null;
            _pendingFailureReason = null;
        }
    }

    internal static class McpReauthenticationGuidance
    {
        internal static string BuildInstruction(string serverName)
        {
            var displayName = string.IsNullOrWhiteSpace(serverName)
                ? UiLocalization.Text("McpManagementForm.Value.Unknown", "Unknown")
                : serverName.Trim();
            return string.Format(
                UiLocalization.Text(
                    "McpManagementForm.Status.ReauthenticationRequired.Template",
                    "Authentication for {0} has expired. Refresh status, select the server, then choose Login."),
                displayName);
        }
    }
}
