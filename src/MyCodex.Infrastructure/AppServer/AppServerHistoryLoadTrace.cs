using System;
using System.Diagnostics;
using System.Globalization;

namespace MyCodex.Infrastructure.AppServer
{
    // Records where long conversation-history loads spend time without logging message content.
    internal static class AppServerHistoryLoadTrace
    {
        internal const string Prefix = "[HistoryLoadTiming]";
        internal const int LargeMessageCharacters = 1000000;
        internal const int SlowRequestMilliseconds = 5000;

        internal static void Information(string phase, string threadId, string format, params object[] args)
        {
            Trace.TraceInformation(Build(phase, threadId, format, args));
        }

        internal static void Warning(string phase, string threadId, string format, params object[] args)
        {
            Trace.TraceWarning(Build(phase, threadId, format, args));
        }

        internal static string DescribeError(Exception error)
        {
            if (error == null)
            {
                return "ErrorType=; Message=";
            }

            var message = error.Message ?? string.Empty;
            if (message.Length > 300)
            {
                message = message.Substring(0, 300) + "...";
            }

            return "ErrorType=" + error.GetType().FullName + "; Message=" + message.Replace('\r', ' ').Replace('\n', ' ');
        }

        internal static string BuildLargeMessageText(
            string kind, string id, string method, int characters, long parseMilliseconds, long handleMilliseconds)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[AppServerTiming] Phase=large-message; Kind={0}; Id={1}; Method={2}; Characters={3}; ParseMs={4}; HandleMs={5}",
                kind ?? string.Empty,
                id ?? string.Empty,
                method ?? string.Empty,
                characters,
                parseMilliseconds,
                handleMilliseconds);
        }

        internal static string BuildSlowRequestText(string method, string id, long elapsedMilliseconds, string result)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "[AppServerTiming] Phase=slow-request; Method={0}; Id={1}; ElapsedMs={2}; Result={3}",
                method ?? string.Empty,
                id ?? string.Empty,
                elapsedMilliseconds,
                result ?? string.Empty);
        }

        private static string Build(string phase, string threadId, string format, object[] args)
        {
            var details = args == null || args.Length == 0
                ? format ?? string.Empty
                : string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, args);
            return Prefix + " Phase=" + phase + "; ThreadId=" + (threadId ?? string.Empty) +
                (details.Length == 0 ? string.Empty : "; " + details);
        }
    }
}
