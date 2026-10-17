using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed class JsonAutomationStore : IAutomationStore
    {
        private const int MaxRunsPerAutomation = 100;
        private readonly AppConfigService _appConfigService;
        private readonly DataContractJsonSerializer _serializer;
        private readonly string _filePath;
        private readonly string _mutexName;
        private volatile bool _saveBlocked;

        public JsonAutomationStore(AppConfigService appConfigService)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _serializer = new DataContractJsonSerializer(typeof(AutomationStateSnapshot));
            _filePath = Path.GetFullPath(_appConfigService.GetAutomationsFilePath());
            _mutexName = BuildMutexName(_filePath);
        }

        public AutomationStateSnapshot Load()
        {
            return WithFileLock(LoadCore);
        }

        public void Save(AutomationStateSnapshot snapshot)
        {
            WithFileLock(() => SaveCore(snapshot, Guid.NewGuid().ToString("N") + ".tmp"));
        }

        public AutomationStateSnapshot Mutate(Func<AutomationStateSnapshot, AutomationStateSnapshot> mutation)
        {
            if (mutation == null)
            {
                throw new ArgumentNullException(nameof(mutation));
            }

            return WithFileLock(() =>
            {
                var current = LoadCore();
                var updated = mutation(current);
                if (updated == null)
                {
                    throw new InvalidOperationException("Automation mutation returned null.");
                }

                SaveCore(updated, Guid.NewGuid().ToString("N") + ".tmp");
                return updated;
            });
        }

        private AutomationStateSnapshot LoadCore()
        {
            ThrowIfSaveBlocked();
            return PersistenceFileRecovery.Load(
                _filePath,
                _filePath + ".bak",
                LoadFile,
                () => new AutomationStateSnapshot(),
                () => _saveBlocked = true);
        }

        private void SaveCore(AutomationStateSnapshot snapshot, string tempFileNameSuffix)
        {
            ThrowIfSaveBlocked();
            _appConfigService.EnsureConfigDirectory();
            var tempFilePath = _filePath + "." + tempFileNameSuffix;
            var backupFilePath = _filePath + ".bak";
            var serializable = Normalize(snapshot);

            try
            {
                using (var stream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    _serializer.WriteObject(stream, serializable);
                    stream.Flush();
                }

                if (File.Exists(_filePath))
                {
                    File.Replace(tempFilePath, _filePath, backupFilePath, true);
                }
                else
                {
                    File.Move(tempFilePath, _filePath);
                }
            }
            catch
            {
                TryDeleteFile(tempFilePath);
                throw;
            }
        }

        private T WithFileLock<T>(Func<T> action)
        {
            using (var mutex = new Mutex(false, _mutexName))
            {
                var lockTaken = false;
                try
                {
                    try
                    {
                        lockTaken = mutex.WaitOne();
                    }
                    catch (AbandonedMutexException)
                    {
                        lockTaken = true;
                    }

                    return action();
                }
                finally
                {
                    if (lockTaken)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            }
        }

        private void WithFileLock(Action action)
        {
            WithFileLock(() =>
            {
                action();
                return true;
            });
        }

        private static string BuildMutexName(string filePath)
        {
            var canonicalPath = Path.GetFullPath(filePath).ToUpperInvariant();
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonicalPath));
                var builder = new StringBuilder(hash.Length * 2);
                for (var i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("x2"));
                }

                return @"Local\MyCodex.AutomationStore." + builder;
            }
        }

        private AutomationStateSnapshot LoadFile(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                return Normalize(_serializer.ReadObject(stream) as AutomationStateSnapshot);
            }
        }

        private void ThrowIfSaveBlocked()
        {
            if (_saveBlocked)
            {
                var primaryPath = _appConfigService.GetAutomationsFilePath();
                throw new PersistenceRecoveryRequiredException(
                    primaryPath,
                    primaryPath + ".bak",
                    null,
                    null);
            }
        }

        private static AutomationStateSnapshot Normalize(AutomationStateSnapshot snapshot)
        {
            snapshot = snapshot ?? new AutomationStateSnapshot();
            snapshot.Definitions = NormalizeDefinitions(snapshot.Definitions);
            snapshot.Runs = PruneRuns(snapshot.Runs);
            return snapshot;
        }

        private static List<AutomationDefinitionSnapshot> NormalizeDefinitions(IEnumerable<AutomationDefinitionSnapshot> definitions)
        {
            var result = new List<AutomationDefinitionSnapshot>();
            if (definitions == null)
            {
                return result;
            }

            foreach (var definition in definitions)
            {
                if (definition == null)
                {
                    continue;
                }

                if (definition.CreatedAt == default(DateTime))
                {
                    definition.CreatedAt = DateTime.UtcNow;
                }

                if (definition.UpdatedAt == default(DateTime))
                {
                    definition.UpdatedAt = definition.CreatedAt;
                }

                definition.InputItems = NormalizeInputItems(definition.InputItems);
                result.Add(definition);
            }

            return result;
        }

        private static List<AutomationInputItemSnapshot> NormalizeInputItems(IEnumerable<AutomationInputItemSnapshot> inputItems)
        {
            var result = new List<AutomationInputItemSnapshot>();
            if (inputItems == null)
            {
                return result;
            }

            foreach (var item in inputItems)
            {
                if (item == null)
                {
                    continue;
                }

                result.Add(new AutomationInputItemSnapshot
                {
                    Type = item.Type,
                    Name = string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? null : item.DisplayName.Trim(),
                    Path = string.IsNullOrWhiteSpace(item.Path) ? null : item.Path.Trim()
                });
            }

            return result;
        }

        private static List<AutomationRunSnapshot> PruneRuns(IEnumerable<AutomationRunSnapshot> runs)
        {
            if (runs == null)
            {
                return new List<AutomationRunSnapshot>();
            }

            return runs
                .Where(item => item != null)
                .GroupBy(item => item.AutomationId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => group
                    .OrderBy(item => item.StartedAt ?? item.ScheduledFor ?? DateTime.MinValue)
                    .ThenBy(item => item.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .Skip(Math.Max(0, group.Count() - MaxRunsPerAutomation)))
                .OrderBy(item => item.StartedAt ?? item.ScheduledFor ?? DateTime.MinValue)
                .ThenBy(item => item.Id ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }
}
