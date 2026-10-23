using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.Persistence
{
    public sealed class JsonModeSettingsStore
    {
        private readonly AppConfigService _appConfigService;
        private readonly IRuntimeScopedConfigFileStore _runtimeScopedConfigFileStore;
        private readonly DataContractJsonSerializer _serializer;

        public JsonModeSettingsStore(AppConfigService appConfigService)
            : this(appConfigService, new RuntimeScopedConfigFileStore())
        {
        }

        internal JsonModeSettingsStore(
            AppConfigService appConfigService,
            IRuntimeScopedConfigFileStore runtimeScopedConfigFileStore)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _runtimeScopedConfigFileStore = runtimeScopedConfigFileStore ??
                throw new ArgumentNullException(nameof(runtimeScopedConfigFileStore));
            _serializer = new DataContractJsonSerializer(typeof(ModeSettingsSnapshot));
        }

        public async Task<ModeSettingsSnapshot> LoadForCurrentRuntimeAsync(AppConfigSnapshot config)
        {
            string runtimeFilePath;
            if (_runtimeScopedConfigFileStore.TryGetRuntimeModeSettingsFilePathForAsyncAccess(
                    config,
                    out runtimeFilePath))
            {
                var result = await _runtimeScopedConfigFileStore.ReadTextFileAsync(
                    config,
                    runtimeFilePath).ConfigureAwait(false);
                return result.Item1
                    ? Deserialize(result.Item2)
                    : new ModeSettingsSnapshot();
            }

            var localFilePath = _appConfigService.GetModeSettingsFilePath();
            if (!File.Exists(localFilePath))
            {
                return new ModeSettingsSnapshot();
            }

            using (var stream = File.OpenRead(localFilePath))
            {
                return Normalize(_serializer.ReadObject(stream) as ModeSettingsSnapshot);
            }
        }

        public async Task SaveForCurrentRuntimeAsync(
            AppConfigSnapshot config,
            ModeSettingsSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            var normalized = Normalize(snapshot);
            string runtimeFilePath;
            if (_runtimeScopedConfigFileStore.TryGetRuntimeModeSettingsFilePathForAsyncAccess(
                    config,
                    out runtimeFilePath))
            {
                await _runtimeScopedConfigFileStore.WriteTextFileAsync(
                    config,
                    runtimeFilePath,
                    Serialize(normalized)).ConfigureAwait(false);
                return;
            }

            _appConfigService.EnsureConfigDirectory();
            var filePath = _appConfigService.GetModeSettingsFilePath();
            AtomicFileWriter.Write(filePath, stream => _serializer.WriteObject(stream, normalized), filePath + ".bak");
        }

        private ModeSettingsSnapshot Deserialize(string content)
        {
            using (var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content ?? string.Empty)))
            {
                return Normalize(_serializer.ReadObject(stream) as ModeSettingsSnapshot);
            }
        }

        private string Serialize(ModeSettingsSnapshot snapshot)
        {
            using (var stream = new MemoryStream())
            {
                _serializer.WriteObject(stream, snapshot);
                return System.Text.Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static ModeSettingsSnapshot Normalize(ModeSettingsSnapshot snapshot)
        {
            snapshot = snapshot ?? new ModeSettingsSnapshot();
            if (!Enum.IsDefined(typeof(AppInteractionMode), snapshot.DefaultStartupMode))
            {
                snapshot.DefaultStartupMode = AppInteractionMode.Coding;
            }

            snapshot.OfficeRootPath = string.IsNullOrWhiteSpace(snapshot.OfficeRootPath)
                ? null
                : snapshot.OfficeRootPath.Trim();
            return snapshot;
        }
    }
}
