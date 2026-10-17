using System;
using System.Security.Cryptography;
using System.Text;
using MyCodex.Application.DTOs;

namespace MyCodex.Application.CodingRemote
{
    public static class RemoteConversationStableIdentity
    {
        public static string CreateMessageId(AppServerThreadMessage message)
        {
            if (message == null)
            {
                return null;
            }

            var identity = !string.IsNullOrWhiteSpace(message.SourceItemId)
                ? (message.ThreadId ?? string.Empty) + "\n" + message.SourceItemId
                : (message.ThreadId ?? string.Empty) + "\n" + (message.TurnId ?? string.Empty) + "\n" + message.TurnItemIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return "m1." + Base64UrlHash(identity);
        }

        public static string CreateContentVersion(string text)
        {
            return "c1." + Base64UrlHash(text ?? string.Empty);
        }

        public static string CreateHistoryRevision(System.Collections.Generic.IEnumerable<CodingRemoteMessageItem> messages)
        {
            var builder = new StringBuilder();
            if (messages != null)
            {
                foreach (var message in messages)
                {
                    if (message != null)
                    {
                        builder.Append(message.MessageId).Append('\n').Append(message.ContentVersion).Append('\n');
                    }
                }
            }

            return "h1." + Base64UrlHash(builder.ToString());
        }

        private static string Base64UrlHash(string value)
        {
            using (var algorithm = SHA256.Create())
            {
                var bytes = algorithm.ComputeHash(new UTF8Encoding(false, true).GetBytes(value ?? string.Empty));
                return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }
    }
}
