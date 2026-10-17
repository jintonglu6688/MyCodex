using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerOfficialHostSettingsOperator : IHostSettingsOperator
    {
        private readonly IHostSettingsOperator _settingsOperator;
        private readonly IAppServerConfigClient _configClient;

        public AppServerOfficialHostSettingsOperator(IHostSettingsOperator settingsOperator, IAppServerConfigClient configClient)
        {
            _settingsOperator = settingsOperator ?? throw new ArgumentNullException(nameof(settingsOperator));
            _configClient = configClient ?? throw new ArgumentNullException(nameof(configClient));
        }

        public async Task<HostSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
        {
            var settings = await _settingsOperator.GetAsync(cancellationToken).ConfigureAwait(false);
            return await IncludeOfficialSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
        }

        public async Task<HostSettingsUpdateResult> UpdateAsync(HostSettingsUpdateRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new HostSettingsUpdateRequest();
            if (request.Official == null)
            {
                var result = await _settingsOperator.UpdateAsync(request, cancellationToken).ConfigureAwait(false);
                result = result ?? new HostSettingsUpdateResult();
                result.Settings = await IncludeOfficialSettingsAsync(result.Settings, cancellationToken).ConfigureAwait(false);
                return result;
            }
            if (request.Defaults != null || request.CustomApproval != null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand,
                    "Official Codex settings must be updated separately.");
            }

            var config = await _configClient.ReadConfigAsync(null, true, cancellationToken).ConfigureAwait(false);
            List<AppServerConfigWriteEdit> edits;
            try
            {
                edits = OfficialCodexSettingsEditPlanner.BuildEdits(config, request.Official);
            }
            catch (ArgumentException)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand,
                    "Unsupported official Codex settings.");
            }

            if (edits.Count != 0)
            {
                var guard = AppServerConfigWriteGuard.EvaluateUserConfig(config, edits);
                if (!guard.CanWrite)
                {
                    throw new CodingRemoteOperationException(RemoteErrorCodes.OperationFailed,
                        "Official Codex settings could not be verified. Reload and try again.");
                }
                cancellationToken.ThrowIfCancellationRequested();
                await _configClient.WriteConfigBatchAsync(edits, expectedVersion: guard.ExpectedVersion,
                    reloadUserConfig: true).ConfigureAwait(false);
            }

            return new HostSettingsUpdateResult
            {
                Updated = edits.Count != 0,
                Settings = await GetAsync(cancellationToken).ConfigureAwait(false)
            };
        }

        private async Task<HostSettingsSnapshot> IncludeOfficialSettingsAsync(
            HostSettingsSnapshot settings, CancellationToken cancellationToken)
        {
            var config = await _configClient.ReadConfigAsync(null, true, cancellationToken).ConfigureAwait(false);
            if (config == null)
            {
                throw new CodingRemoteOperationException(RemoteErrorCodes.OperationFailed,
                    "Official Codex settings could not be read.");
            }
            settings = settings ?? new HostSettingsSnapshot();
            settings.Official = OfficialCodexSettingsEditPlanner.ReadUserSettings(config);
            return settings;
        }
    }
}
