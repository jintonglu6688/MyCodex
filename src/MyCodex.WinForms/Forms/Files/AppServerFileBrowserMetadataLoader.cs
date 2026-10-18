using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal sealed class AppServerFileBrowserMetadataLoader
    {
        private const int BatchSize = 8;

        internal static string UnavailableValue
        {
            get { return UiLocalization.Text("AppServerFileBrowserForm.Value.Unavailable", "—"); }
        }

        private readonly IAppServerFileSystemClient _client;
        private readonly Dictionary<string, string> _cache = new Dictionary<string, string>(StringComparer.Ordinal);
        private int _generation;

        public AppServerFileBrowserMetadataLoader(IAppServerFileSystemClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task LoadAsync(
            IReadOnlyList<string> paths,
            Action<IReadOnlyDictionary<string, string>> batchCompleted)
        {
            if (paths == null)
            {
                throw new ArgumentNullException(nameof(paths));
            }

            if (batchCompleted == null)
            {
                throw new ArgumentNullException(nameof(batchCompleted));
            }

            var generation = _generation;
            var cachedPaths = paths.Where(path => _cache.ContainsKey(path)).ToList();
            for (var offset = 0; offset < cachedPaths.Count; offset += BatchSize)
            {
                batchCompleted(cachedPaths
                    .Skip(offset)
                    .Take(BatchSize)
                    .ToDictionary(path => path, path => _cache[path], StringComparer.Ordinal));
            }

            var uncachedPaths = paths.Where(path => !_cache.ContainsKey(path)).ToList();
            for (var offset = 0; offset < uncachedPaths.Count; offset += BatchSize)
            {
                var batchPaths = uncachedPaths.Skip(offset).Take(BatchSize).ToList();
                var results = await Task.WhenAll(batchPaths.Select(LoadOneAsync));
                if (generation != _generation)
                {
                    return;
                }

                var batch = results.ToDictionary(result => result.Key, result => result.Value, StringComparer.Ordinal);
                foreach (var pair in batch)
                {
                    _cache[pair.Key] = pair.Value;
                }

                batchCompleted(batch);
            }
        }

        public void Invalidate(IEnumerable<string> paths)
        {
            if (paths == null)
            {
                return;
            }

            var invalidatedPaths = paths.ToList();
            if (invalidatedPaths.Count > 0)
            {
                _generation++;
            }

            foreach (var path in invalidatedPaths)
            {
                _cache.Remove(path);
            }
        }

        public void Clear()
        {
            _generation++;
            _cache.Clear();
        }

        public void Prune(IEnumerable<string> visiblePaths)
        {
            _generation++;
            var visible = new HashSet<string>(visiblePaths ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
            foreach (var path in _cache.Keys.Where(path => !visible.Contains(path)).ToList())
            {
                _cache.Remove(path);
            }
        }

        public static string FormatModifiedTime(long unixMilliseconds)
        {
            if (unixMilliseconds <= 0)
            {
                return UnavailableValue;
            }

            try
            {
                return DateTimeOffset
                    .FromUnixTimeMilliseconds(unixMilliseconds)
                    .LocalDateTime
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
            catch (ArgumentOutOfRangeException)
            {
                return UnavailableValue;
            }
        }

        private async Task<KeyValuePair<string, string>> LoadOneAsync(string path)
        {
            try
            {
                var metadata = await _client.GetMetadataAsync(path);
                return new KeyValuePair<string, string>(
                    path,
                    metadata == null ? UnavailableValue : FormatModifiedTime(metadata.ModifiedAtMs));
            }
            catch
            {
                return new KeyValuePair<string, string>(path, UnavailableValue);
            }
        }
    }
}
