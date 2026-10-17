using System;
using System.Collections.Generic;

namespace MyCodex.Infrastructure.AppServer
{
    internal enum AppServerIncomingMessageKind
    {
        Ignored,
        ServerRequest,
        Notification,
        Response
    }

    internal static class AppServerIncomingMessageRouter
    {
        public static AppServerIncomingMessageKind Classify(IDictionary<string, object> message)
        {
            if (message == null || message.Count == 0)
            {
                return AppServerIncomingMessageKind.Ignored;
            }

            var hasMethod = HasUsableString(message, "method");
            var hasId = HasUsableId(message);

            if (hasMethod && hasId)
            {
                return AppServerIncomingMessageKind.ServerRequest;
            }

            if (hasMethod)
            {
                return AppServerIncomingMessageKind.Notification;
            }

            return hasId ? AppServerIncomingMessageKind.Response : AppServerIncomingMessageKind.Ignored;
        }

        private static bool HasUsableString(IDictionary<string, object> message, string key)
        {
            object value;
            var text = message.TryGetValue(key, out value) ? value as string : null;
            return !string.IsNullOrWhiteSpace(text);
        }

        private static bool HasUsableId(IDictionary<string, object> message)
        {
            object value;
            if (!message.TryGetValue("id", out value) || value == null)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(Convert.ToString(value));
        }
    }
}
