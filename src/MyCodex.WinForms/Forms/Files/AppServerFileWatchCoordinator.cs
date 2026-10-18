using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.WinForms.Localization;

namespace MyCodex.WinForms.Forms
{
    internal interface IAppServerFileWatchTarget
    {
        string WatchPath { get; }

        Task HandleFileSystemChangedAsync(AppServerFileSystemChangedEvent change);

        void HandleWatchUnavailable(string message);
    }

    internal sealed class AppServerFileWatchCoordinator
    {
        private readonly IAppServerFileSystemClient _client;
        private readonly Dictionary<string, IAppServerFileWatchTarget> _targets =
            new Dictionary<string, IAppServerFileWatchTarget>(StringComparer.Ordinal);

        public AppServerFileWatchCoordinator(IAppServerFileSystemClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public async Task<string> WatchAsync(IAppServerFileWatchTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            if (string.IsNullOrWhiteSpace(target.WatchPath))
            {
                target.HandleWatchUnavailable(UiLocalization.Text(
                    "AppServerFileWatch.Status.PathRequired",
                    "Live refresh unavailable for this path."));
                return null;
            }

            var watchId = "mycodex-file-" + Guid.NewGuid().ToString("N");
            try
            {
                await _client.WatchFileSystemAsync(target.WatchPath, watchId).ConfigureAwait(false);
                _targets[watchId] = target;
                return watchId;
            }
            catch (Exception ex)
            {
                target.HandleWatchUnavailable(string.Format(
                    UiLocalization.Text(
                        "AppServerFileWatch.Status.Unavailable.Template",
                        "Live refresh unavailable: {0}"),
                    ex.Message));
                return null;
            }
        }

        public async Task<string> ReplaceWatchAsync(IAppServerFileWatchTarget target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var existing = _targets
                .Where(pair => ReferenceEquals(pair.Value, target))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var watchId in existing)
            {
                await UnwatchAsync(watchId).ConfigureAwait(false);
            }

            return await WatchAsync(target).ConfigureAwait(false);
        }

        public async Task UnwatchTargetAsync(IAppServerFileWatchTarget target)
        {
            if (target == null)
            {
                return;
            }

            var existing = _targets
                .Where(pair => ReferenceEquals(pair.Value, target))
                .Select(pair => pair.Key)
                .ToArray();
            foreach (var watchId in existing)
            {
                await UnwatchAsync(watchId).ConfigureAwait(false);
            }
        }

        public async Task UnwatchAsync(string watchId)
        {
            if (string.IsNullOrWhiteSpace(watchId))
            {
                return;
            }

            _targets.Remove(watchId);
            try
            {
                await _client.UnwatchFileSystemAsync(watchId).ConfigureAwait(false);
            }
            catch
            {
            }
        }

        public async Task ClearAsync()
        {
            var watchIds = _targets.Keys.ToArray();
            _targets.Clear();
            foreach (var watchId in watchIds)
            {
                try
                {
                    await _client.UnwatchFileSystemAsync(watchId).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        public Task HandleFileSystemChangedAsync(AppServerFileSystemChangedEvent change)
        {
            if (change == null || string.IsNullOrWhiteSpace(change.WatchId))
            {
                return Task.CompletedTask;
            }

            IAppServerFileWatchTarget target;
            return _targets.TryGetValue(change.WatchId, out target) && target != null
                ? target.HandleFileSystemChangedAsync(change)
                : Task.CompletedTask;
        }
    }
}
