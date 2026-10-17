using System;
using System.Collections.Generic;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;

namespace MyCodex.Infrastructure.RemoteRelay
{
    // ponytail: snapshots are simply discarded after mutations; append merging can be added only if thread/read becomes measurable.
    public sealed class RemoteConversationSnapshotCache : IRemoteConversationSnapshotInvalidator
    {
        private const int MaxSnapshots = 8;
        private const long MaxBytes = 128L * 1024L * 1024L;
        private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);
        private readonly object _sync = new object();
        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public bool TryGet(string mode, string sessionId, out AppServerThreadDetails details)
        {
            lock (_sync)
            {
                RemoveExpired();
                Entry entry;
                if (!_entries.TryGetValue(Key(mode, sessionId), out entry))
                {
                    details = null;
                    return false;
                }

                entry.LastAccessUtc = DateTime.UtcNow;
                details = entry.Details;
                return true;
            }
        }

        public void Put(string mode, string sessionId, AppServerThreadDetails details)
        {
            if (details == null || string.IsNullOrWhiteSpace(sessionId)) return;
            var size = Estimate(details);
            if (size > MaxBytes) return;
            lock (_sync)
            {
                Remove(Key(mode, sessionId));
                while ((_entries.Count >= MaxSnapshots || TotalBytes() + size > MaxBytes) && RemoveLeastRecentlyUsed()) { }
                if (_entries.Count < MaxSnapshots && TotalBytes() + size <= MaxBytes)
                {
                    _entries[Key(mode, sessionId)] = new Entry { Details = details, Size = size, LastAccessUtc = DateTime.UtcNow };
                }
            }
        }

        public void MarkTailChanged(string mode, string sessionId) { Remove(Key(mode, sessionId)); }
        public void MarkHistoryRewritten(string mode, string sessionId) { Remove(Key(mode, sessionId)); }
        public void Clear() { lock (_sync) _entries.Clear(); }

        private void Remove(string key) { lock (_sync) _entries.Remove(key); }
        private void RemoveExpired() { foreach (var pair in new List<KeyValuePair<string, Entry>>(_entries)) if (DateTime.UtcNow - pair.Value.LastAccessUtc > Lifetime) _entries.Remove(pair.Key); }
        private bool RemoveLeastRecentlyUsed()
        {
            string oldest = null;
            DateTime time = DateTime.MaxValue;
            foreach (var pair in _entries) if (pair.Value.LastAccessUtc < time) { time = pair.Value.LastAccessUtc; oldest = pair.Key; }
            if (oldest == null) return false;
            _entries.Remove(oldest);
            return true;
        }
        private long TotalBytes() { long result = 0; foreach (var entry in _entries.Values) result += entry.Size; return result; }
        private static string Key(string mode, string sessionId) { return (mode ?? string.Empty) + "\n" + sessionId; }
        private static long Estimate(AppServerThreadDetails details)
        {
            long bytes = 256;
            foreach (var message in details.Messages ?? new List<AppServerThreadMessage>()) bytes += 128L + 2L * ((message.Content ?? string.Empty).Length + (message.ThreadId ?? string.Empty).Length + (message.TurnId ?? string.Empty).Length + (message.SourceItemId ?? string.Empty).Length);
            return bytes;
        }
        private sealed class Entry { public AppServerThreadDetails Details; public long Size; public DateTime LastAccessUtc; }
    }
}
