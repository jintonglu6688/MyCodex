using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Infrastructure.RemoteRelay
{
    public sealed class AppServerHostSettingsOperator : IHostSettingsOperator
    {
        private readonly Func<AppConfigSnapshot> _appConfigResolver;
        private readonly Func<bool> _saveAppConfig;
        private readonly Func<CancellationToken, Task<IReadOnlyList<AppServerModelOption>>> _modelLoader;
        private readonly Func<HostSettingsScope> _scopeResolver;
        private readonly Func<HostSettingsDefaults> _defaultsResolver;
        private readonly Action<string, string> _setModelDefaults;
        private readonly Action<ApprovalMode> _setApprovalDefault;
        private readonly Func<Task> _saveRuntimeDefaults;
        private readonly Func<CustomApprovalPermissionKind, string> _permissionDisplayNameResolver;
        private readonly Func<string, string, Task> _writeModelDefaults;

        public AppServerHostSettingsOperator(
            Func<AppConfigSnapshot> appConfigResolver,
            Func<bool> saveAppConfig,
            Func<CancellationToken, Task<IReadOnlyList<AppServerModelOption>>> modelLoader,
            Func<HostSettingsScope> scopeResolver,
            Func<HostSettingsDefaults> defaultsResolver,
            Action<string, string> setModelDefaults,
            Action<ApprovalMode> setApprovalDefault,
            Func<Task> saveRuntimeDefaults,
            Func<CustomApprovalPermissionKind, string> permissionDisplayNameResolver,
            Func<string, string, Task> writeModelDefaults = null)
        {
            _appConfigResolver = appConfigResolver ?? throw new ArgumentNullException(nameof(appConfigResolver));
            _saveAppConfig = saveAppConfig ?? throw new ArgumentNullException(nameof(saveAppConfig));
            _modelLoader = modelLoader ?? throw new ArgumentNullException(nameof(modelLoader));
            _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
            _defaultsResolver = defaultsResolver ?? throw new ArgumentNullException(nameof(defaultsResolver));
            _setModelDefaults = setModelDefaults ?? throw new ArgumentNullException(nameof(setModelDefaults));
            _setApprovalDefault = setApprovalDefault ?? throw new ArgumentNullException(nameof(setApprovalDefault));
            _saveRuntimeDefaults = saveRuntimeDefaults ?? throw new ArgumentNullException(nameof(saveRuntimeDefaults));
            _permissionDisplayNameResolver = permissionDisplayNameResolver ?? throw new ArgumentNullException(nameof(permissionDisplayNameResolver));
            _writeModelDefaults = writeModelDefaults;
        }

        public async Task<HostSettingsSnapshot> GetAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var models = MapModels(await _modelLoader(cancellationToken).ConfigureAwait(false));
            var appConfig = _appConfigResolver() ?? new AppConfigSnapshot();
            var custom = appConfig.CustomApprovalSettings ?? new CustomApprovalSettingsSnapshot();
            var orderedPermissions = CustomApprovalPermissionCatalog.GetOrderedPermissions();
            var availablePermissions = new HashSet<CustomApprovalPermissionKind>(orderedPermissions);
            return new HostSettingsSnapshot
            {
                Scope = _scopeResolver() ?? new HostSettingsScope(),
                Defaults = _defaultsResolver() ?? new HostSettingsDefaults(),
                Models = models,
                CustomApproval = new HostSettingsCustomApproval
                {
                    AvailablePermissions = orderedPermissions
                        .Select(permission => new HostSettingsPermissionOption
                        {
                            Id = permission.ToString(),
                            DisplayName = _permissionDisplayNameResolver(permission)
                        })
                        .ToList(),
                    EnabledPermissions = custom.EnabledPermissions
                        .Where(availablePermissions.Contains)
                        .Select(permission => permission.ToString())
                        .ToList(),
                    CommandPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(custom.CommandPrefixes).ToList()
                }
            };
        }

        public async Task<HostSettingsUpdateResult> UpdateAsync(
            HostSettingsUpdateRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            request = request ?? new HostSettingsUpdateRequest();
            var models = MapModels(await _modelLoader(cancellationToken).ConfigureAwait(false));
            var currentDefaults = CloneDefaults(_defaultsResolver());
            var appConfig = _appConfigResolver() ?? throw Failure("Application configuration is unavailable.");
            var currentCustom = CloneCustom(appConfig.CustomApprovalSettings);

            List<CustomApprovalPermissionKind> permissions = null;
            IReadOnlyList<string> prefixes = null;
            if (request.Defaults != null)
            {
                HostSettingsPolicy.ValidateDefaults(request.Defaults, models);
            }

            if (request.CustomApproval != null)
            {
                permissions = HostSettingsPolicy.ParsePermissionIds(request.CustomApproval.EnabledPermissions);
                prefixes = HostSettingsPolicy.NormalizeCommandPrefixes(request.CustomApproval.CommandPrefixes);
            }

            var defaultsChanged = request.Defaults != null && !DefaultsEqual(currentDefaults, request.Defaults);
            var customChanged = request.CustomApproval != null &&
                !CustomEqual(currentCustom, permissions, prefixes);
            if (!defaultsChanged && !customChanged)
            {
                return new HostSettingsUpdateResult
                {
                    Updated = false,
                    Settings = await GetAsync(cancellationToken).ConfigureAwait(false)
                };
            }

            if (defaultsChanged)
            {
                if (_writeModelDefaults != null)
                {
                    await _writeModelDefaults(request.Defaults.ModelId.Trim(), Normalize(request.Defaults.ReasoningEffort)).ConfigureAwait(false);
                }
                else
                {
                    _setModelDefaults(request.Defaults.ModelId.Trim(), Normalize(request.Defaults.ReasoningEffort));
                }
                _setApprovalDefault(HostSettingsPolicy.ParseApprovalMode(request.Defaults.ApprovalMode));
            }

            if (customChanged)
            {
                appConfig.CustomApprovalSettings = new CustomApprovalSettingsSnapshot
                {
                    EnabledPermissions = permissions,
                    CommandPrefixes = prefixes.ToList()
                };
            }

            if (defaultsChanged || customChanged)
            {
                if (!_saveAppConfig())
                {
                    RestoreLocalState(appConfig, currentDefaults, currentCustom, defaultsChanged, customChanged);
                    throw Failure("Application settings could not be saved.");
                }
            }

            if (defaultsChanged)
            {
                await _saveRuntimeDefaults().ConfigureAwait(false);
            }

            return new HostSettingsUpdateResult
            {
                Updated = true,
                Settings = await GetAsync(cancellationToken).ConfigureAwait(false)
            };
        }

        private void RestoreLocalState(
            AppConfigSnapshot appConfig,
            HostSettingsDefaults defaults,
            CustomApprovalSettingsSnapshot custom,
            bool restoreDefaults,
            bool restoreCustom)
        {
            if (restoreDefaults)
            {
                if (_writeModelDefaults == null)
                {
                    _setModelDefaults(defaults.ModelId, defaults.ReasoningEffort);
                }
                _setApprovalDefault(HostSettingsPolicy.ParseApprovalMode(defaults.ApprovalMode));
            }

            if (restoreCustom)
            {
                appConfig.CustomApprovalSettings = CloneCustom(custom);
            }
        }

        private static List<CodingRemoteModelOption> MapModels(IEnumerable<AppServerModelOption> models)
        {
            return (models ?? Enumerable.Empty<AppServerModelOption>())
                .Where(model => model != null && !string.IsNullOrWhiteSpace(model.ModelId))
                .Select(model => new CodingRemoteModelOption
                {
                    ModelId = model.ModelId.Trim(),
                    DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? model.ModelId.Trim() : model.DisplayName.Trim(),
                    DefaultReasoningEffort = Normalize(model.DefaultReasoningEffort),
                    ReasoningEfforts = model.ReasoningEfforts
                        .Where(effort => effort != null && !string.IsNullOrWhiteSpace(effort.EffortId))
                        .Select(effort => effort.EffortId.Trim())
                        .ToList()
                })
                .ToList();
        }

        private static HostSettingsDefaults CloneDefaults(HostSettingsDefaults source)
        {
            source = source ?? new HostSettingsDefaults();
            return new HostSettingsDefaults
            {
                ModelId = Normalize(source.ModelId),
                ReasoningEffort = Normalize(source.ReasoningEffort),
                ApprovalMode = Normalize(source.ApprovalMode)
            };
        }

        private static CustomApprovalSettingsSnapshot CloneCustom(CustomApprovalSettingsSnapshot source)
        {
            source = source ?? new CustomApprovalSettingsSnapshot();
            return new CustomApprovalSettingsSnapshot
            {
                EnabledPermissions = new List<CustomApprovalPermissionKind>(source.EnabledPermissions),
                CommandPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(source.CommandPrefixes).ToList()
            };
        }

        private static bool DefaultsEqual(HostSettingsDefaults left, HostSettingsDefaults right)
        {
            return string.Equals(Normalize(left.ModelId), Normalize(right.ModelId), StringComparison.Ordinal) &&
                string.Equals(Normalize(left.ReasoningEffort), Normalize(right.ReasoningEffort), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(Normalize(left.ApprovalMode), Normalize(right.ApprovalMode), StringComparison.OrdinalIgnoreCase);
        }

        private static bool CustomEqual(
            CustomApprovalSettingsSnapshot current,
            IEnumerable<CustomApprovalPermissionKind> permissions,
            IReadOnlyList<string> prefixes)
        {
            var currentPermissions = new HashSet<CustomApprovalPermissionKind>(current.EnabledPermissions);
            if (!currentPermissions.SetEquals(permissions))
            {
                return false;
            }

            var currentPrefixes = HostSettingsPolicy.NormalizeCommandPrefixes(current.CommandPrefixes);
            return currentPrefixes.SequenceEqual(prefixes, StringComparer.Ordinal);
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static CodingRemoteOperationException Failure(string message)
        {
            return new CodingRemoteOperationException(RemoteErrorCodes.OperationFailed, message);
        }
    }
}
