using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace MyCodex.Infrastructure.Diagnostics
{
    public static class SessionTraceLog
    {
        internal const int RetainedSessionLogCount = 5;

        public static string Configure(string directory, DateTimeOffset startedAt, int processId)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("A log directory is required.", nameof(directory));
            }

            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, BuildFileName(startedAt, processId));
            const string name = "MyCodexFileTraceListener";
            var existing = Trace.Listeners[name];
            if (existing != null)
            {
                Trace.Listeners.Remove(existing);
                existing.Dispose();
            }

            Trace.Listeners.Add(new TimestampedFileTraceListener(path, name));
            Trace.AutoFlush = true;
            foreach (var expired in FindExpiredSessionLogs(directory, path, RetainedSessionLogCount))
            {
                try
                {
                    File.Delete(expired);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }

            return path;
        }

        internal static string BuildFileName(DateTimeOffset startedAt, int processId)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "app-{0:yyyyMMdd-HHmmss-fff}-p{1}.log",
                startedAt,
                Math.Max(0, processId));
        }

        internal static IReadOnlyList<string> FindExpiredSessionLogs(
            string directory,
            string currentPath,
            int retainedCount)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory) || retainedCount < 1)
            {
                return new string[0];
            }

            var current = string.IsNullOrWhiteSpace(currentPath) ? null : Path.GetFullPath(currentPath);
            return new DirectoryInfo(directory)
                .GetFiles("app-*.log", SearchOption.TopDirectoryOnly)
                .Where(file => current == null || !string.Equals(file.FullName, current, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
                .Skip(Math.Max(0, retainedCount - (current == null ? 0 : 1)))
                .Select(file => file.FullName)
                .ToList();
        }

        private sealed class TimestampedFileTraceListener : TraceListener
        {
            private readonly object _sync = new object();
            private readonly StreamWriter _writer;

            public TimestampedFileTraceListener(string path, string name)
                : base(name)
            {
                _writer = new StreamWriter(
                    new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete),
                    new UTF8Encoding(false))
                {
                    AutoFlush = true
                };
            }

            public override void Write(string message) { WriteRecord(TraceEventType.Verbose, null, message); }
            public override void WriteLine(string message) { WriteRecord(TraceEventType.Verbose, null, message); }

            public override void TraceEvent(TraceEventCache cache, string source, TraceEventType type, int id, string message)
            {
                WriteRecord(type, source, message);
            }

            public override void TraceEvent(TraceEventCache cache, string source, TraceEventType type, int id, string format, params object[] args)
            {
                WriteRecord(type, source, args == null || args.Length == 0
                    ? format
                    : string.Format(CultureInfo.InvariantCulture, format ?? string.Empty, args));
            }

            public override void Flush()
            {
                lock (_sync) { _writer.Flush(); }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    lock (_sync) { _writer.Dispose(); }
                }

                base.Dispose(disposing);
            }

            private void WriteRecord(TraceEventType type, string source, string message)
            {
                var prefix = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:yyyy-MM-ddTHH:mm:ss.fffzzz} [{1}] [T{2}] [{3}] ",
                    DateTimeOffset.Now,
                    FormatLevel(type),
                    Thread.CurrentThread.ManagedThreadId,
                    string.IsNullOrWhiteSpace(source) ? "MyCodex" : source.Trim());
                var lines = (message ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                lock (_sync)
                {
                    for (var index = 0; index < lines.Length; index++)
                    {
                        _writer.Write(prefix);
                        _writer.WriteLine(index == 0 ? lines[index] : "| " + lines[index]);
                    }
                }
            }

            private static string FormatLevel(TraceEventType type)
            {
                switch (type)
                {
                    case TraceEventType.Critical: return "CRITICAL";
                    case TraceEventType.Error: return "ERROR";
                    case TraceEventType.Warning: return "WARN";
                    case TraceEventType.Information: return "INFO";
                    default: return "TRACE";
                }
            }
        }
    }
}
