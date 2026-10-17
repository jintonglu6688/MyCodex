using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.RemoteRelay;

namespace MyCodex.Application.CodingRemote
{
    public sealed partial class RemoteCommandDispatcher
    {
        private async Task<RemoteRelayEvent> DispatchHostSettingsGetAsync(
            RemoteRelayCommand command,
            CancellationToken cancellationToken)
        {
            if (_hostSettingsOperator == null)
            {
                return CreateError(
                    command.RequestId,
                    command.OperationId,
                    command.DeviceId,
                    RemoteErrorCodes.OperationNotAvailable,
                    "Windows settings are unavailable.");
            }

            try
            {
                var settings = await _hostSettingsOperator.GetAsync(cancellationToken).ConfigureAwait(false);
                return CreateEvent(
                    command,
                    RemoteEventTypes.HostSettingsResult,
                    MapHostSettings(settings ?? new HostSettingsSnapshot()));
            }
            catch (CodingRemoteOperationException)
            {
                throw;
            }
            catch
            {
                throw new CodingRemoteOperationException(
                    RemoteErrorCodes.OperationFailed,
                    "Windows settings could not be loaded.");
            }
        }

        private async Task<RemoteRelayEvent> DispatchHostSettingsUpdateAsync(
            RemoteRelayCommand command,
            CancellationToken cancellationToken)
        {
            if (_hostSettingsOperator == null)
            {
                return CreateError(
                    command.RequestId,
                    command.OperationId,
                    command.DeviceId,
                    RemoteErrorCodes.OperationNotAvailable,
                    "Windows settings are unavailable.");
            }

            if (string.IsNullOrWhiteSpace(command.DeviceId))
            {
                return CreateError(
                    command.RequestId,
                    command.OperationId,
                    command.DeviceId,
                    RemoteErrorCodes.InvalidCommand,
                    "Device ID is required.");
            }

            var operationId = string.IsNullOrWhiteSpace(command.OperationId)
                ? command.RequestId
                : command.OperationId;
            if (string.IsNullOrWhiteSpace(operationId))
            {
                return CreateError(
                    command.RequestId,
                    command.OperationId,
                    command.DeviceId,
                    RemoteErrorCodes.InvalidCommand,
                    "Operation ID is required.");
            }

            var writeLock = _writeCoordinator.Acquire(command.DeviceId, operationId, DateTime.UtcNow);
            if (!writeLock.IsGranted)
            {
                return CreateError(
                    command.RequestId,
                    command.OperationId,
                    command.DeviceId,
                    RemoteErrorCodes.WriteLockDenied,
                    writeLock.Reason);
            }

            try
            {
                HostSettingsUpdateResult result;
                try
                {
                    result = await _hostSettingsOperator.UpdateAsync(
                        ParseHostSettingsUpdate(command.Payload),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (CodingRemoteOperationException)
                {
                    throw;
                }
                catch
                {
                    throw new CodingRemoteOperationException(
                        RemoteErrorCodes.OperationFailed,
                        "Windows settings could not be updated.");
                }

                result = result ?? new HostSettingsUpdateResult
                {
                    Settings = new HostSettingsSnapshot()
                };
                if (result.Updated)
                {
                    await PublishHostSettingsMutationAsync(cancellationToken).ConfigureAwait(false);
                }

                return CreateEvent(
                    command,
                    RemoteEventTypes.HostSettingsUpdateResult,
                    new Dictionary<string, object>
                    {
                        { "updated", result.Updated },
                        { "settings", MapHostSettings(result.Settings ?? new HostSettingsSnapshot()) }
                    });
            }
            finally
            {
                _writeCoordinator.ReleaseDevice(command.DeviceId);
            }
        }

        private async Task PublishHostSettingsMutationAsync(CancellationToken cancellationToken)
        {
            if (_mutationSink == null)
            {
                return;
            }

            try
            {
                await _mutationSink.PublishAsync(
                    new RemoteHostMutation
                    {
                        MutationType = RemoteHostMutationTypes.HostSettingsUpdated
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // The remote operation already succeeded. UI refresh is best effort.
            }
        }

        private static Dictionary<string, object> MapHostSettings(HostSettingsSnapshot settings)
        {
            settings = settings ?? new HostSettingsSnapshot();
            var scope = settings.Scope ?? new HostSettingsScope();
            var defaults = settings.Defaults ?? new HostSettingsDefaults();
            var custom = settings.CustomApproval ?? new HostSettingsCustomApproval();
            var payload = new Dictionary<string, object>
            {
                {
                    "scope",
                    new Dictionary<string, object>
                    {
                        { "hostName", scope.HostName },
                        { "runtimeSource", scope.RuntimeSource },
                        { "runtimeDisplayName", scope.RuntimeDisplayName },
                        { "runtimeProfileId", scope.RuntimeProfileId },
                        { "authenticationDisplayName", scope.AuthenticationDisplayName }
                    }
                },
                {
                    "defaults",
                    new Dictionary<string, object>
                    {
                        { "modelId", defaults.ModelId },
                        { "reasoningEffort", defaults.ReasoningEffort },
                        { "approvalMode", defaults.ApprovalMode }
                    }
                },
                { "models", MapModelOptions(settings.Models) },
                {
                    "customApproval",
                    new Dictionary<string, object>
                    {
                        {
                            "availablePermissions",
                            MapPermissionOptions(custom.AvailablePermissions)
                        },
                        { "enabledPermissions", custom.EnabledPermissions ?? new List<string>() },
                        { "commandPrefixes", custom.CommandPrefixes ?? new List<string>() }
                    }
                }
            };
            AddOfficialHostSettings(payload, settings);
            return payload;
        }

        private static IReadOnlyList<Dictionary<string, object>> MapPermissionOptions(
            IEnumerable<HostSettingsPermissionOption> permissions)
        {
            var result = new List<Dictionary<string, object>>();
            foreach (var permission in permissions ?? new List<HostSettingsPermissionOption>())
            {
                if (permission == null)
                {
                    continue;
                }

                result.Add(new Dictionary<string, object>
                {
                    { "id", permission.Id },
                    { "displayName", permission.DisplayName }
                });
            }

            return result;
        }

        private static HostSettingsUpdateRequest ParseHostSettingsUpdate(
            IDictionary<string, object> payload)
        {
            payload = payload ?? new Dictionary<string, object>();
            var request = new HostSettingsUpdateRequest();
            if (payload.ContainsKey("defaults"))
            {
                var section = GetRequiredSection(payload, "defaults");
                request.Defaults = new HostSettingsDefaults
                {
                    ModelId = GetRequiredString(section, "modelId"),
                    ReasoningEffort = GetRequiredNullableString(section, "reasoningEffort"),
                    ApprovalMode = GetRequiredString(section, "approvalMode")
                };
            }

            if (payload.ContainsKey("customApproval"))
            {
                var section = GetRequiredSection(payload, "customApproval");
                request.CustomApproval = new HostSettingsCustomApprovalUpdate
                {
                    EnabledPermissions = GetRequiredStringList(section, "enabledPermissions"),
                    CommandPrefixes = GetRequiredStringList(section, "commandPrefixes")
                };
            }

            request.Official = ParseOfficialHostSettings(payload);
            return request;
        }

        private static IDictionary<string, object> GetRequiredSection(
            IDictionary<string, object> payload,
            string key)
        {
            object raw;
            var section = payload != null && payload.TryGetValue(key, out raw)
                ? raw as IDictionary<string, object>
                : null;
            if (section == null)
            {
                throw InvalidHostSettings(key + " must be an object.");
            }

            return section;
        }

        private static string GetRequiredString(IDictionary<string, object> payload, string key)
        {
            var value = GetRequiredNullableString(payload, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw InvalidHostSettings(key + " is required.");
            }

            return value.Trim();
        }

        private static string GetRequiredNullableString(IDictionary<string, object> payload, string key)
        {
            object raw;
            if (payload == null || !payload.TryGetValue(key, out raw))
            {
                throw InvalidHostSettings(key + " is required.");
            }

            if (raw == null)
            {
                return null;
            }

            var value = raw as string;
            if (value == null)
            {
                throw InvalidHostSettings(key + " must be text or null.");
            }

            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static List<string> GetRequiredStringList(
            IDictionary<string, object> payload,
            string key)
        {
            object raw;
            if (payload == null || !payload.TryGetValue(key, out raw) || raw == null || raw is string)
            {
                throw InvalidHostSettings(key + " must be an array.");
            }

            var values = raw as System.Collections.IEnumerable;
            if (values == null)
            {
                throw InvalidHostSettings(key + " must be an array.");
            }

            var result = new List<string>();
            foreach (var value in values)
            {
                if (!(value is string))
                {
                    throw InvalidHostSettings(key + " must contain only text.");
                }

                result.Add((string)value);
            }

            return result;
        }

        private static CodingRemoteOperationException InvalidHostSettings(string message)
        {
            return new CodingRemoteOperationException(RemoteErrorCodes.InvalidCommand, message);
        }
    }
}
