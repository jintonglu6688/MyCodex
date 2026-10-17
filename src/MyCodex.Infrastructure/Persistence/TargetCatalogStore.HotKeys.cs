using System;
using System.Collections.Generic;
using System.Linq;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed partial class TargetCatalogStore
    {
        // Check and persist while holding the same catalog lock, including targets without a running window.
        // Only the owner's save callback writes preferences; foreign snapshots are never written back.
        public T WithHotKeyReservation<T>(string id, bool enabled, int modifiers, int virtualKey, Func<T> save)
        {
            if (save == null) throw new ArgumentNullException(nameof(save));
            modifiers = MainWindowHotKeyConfig.NormalizeModifiers(modifiers);
            virtualKey = MainWindowHotKeyConfig.NormalizeVirtualKey(virtualKey);
            return WithLock(() =>
            {
                var catalog = ReadCatalog();
                Find(catalog, id);
                if (enabled)
                {
                    var conflicts = new List<TargetProfileSnapshot>();
                    foreach (var target in catalog.Targets.Where(target => target.Id != id))
                    {
                        var paths = CreatePaths(target.Id);
                        ValidateTargetData(paths, target);
                        var config = new JsonAppConfigStore(paths).Load();
                        if (config.MainWindowHotKeyEnabled != false &&
                            MainWindowHotKeyConfig.NormalizeModifiers(config.MainWindowHotKeyModifiers) == modifiers &&
                            MainWindowHotKeyConfig.NormalizeVirtualKey(config.MainWindowHotKeyVirtualKey) == virtualKey)
                            conflicts.Add(target);
                    }
                    if (conflicts.Count > 0) throw new TargetHotKeyConflictException(conflicts);
                }
                return save();
            });
        }
    }

    public sealed class TargetHotKeyConflictException : InvalidOperationException
    {
        public TargetHotKeyConflictException(IReadOnlyList<TargetProfileSnapshot> targets)
            : base("The shortcut is already configured for another target.")
        {
            Targets = targets;
        }

        public IReadOnlyList<TargetProfileSnapshot> Targets { get; }
    }
}
