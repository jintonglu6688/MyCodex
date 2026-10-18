using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Shared.Localization;

namespace MyCodex.Infrastructure.AppServer
{
    internal delegate Task<IDictionary<string, object>> AppServerRequestInvoker(
        string method,
        object parameters,
        CancellationToken cancellationToken,
        int timeoutMs);

    internal sealed class AppServerClientOperations
    {
        private const int DefaultRequestTimeoutMs = 60000;
        private const int LongRunningRequestTimeoutMs = 300000;

        private readonly Action _ensureConnected;
        private readonly Func<AppServerProtocolCapabilities> _getProtocolCapabilities;
        private readonly AppServerRequestInvoker _sendRequestAsync;
        private readonly Func<string, string, string> _mapRequiredClientPathToServer;
        private readonly Func<string, string> _mapClientOrServerPathToServer;
        private readonly Func<string, string> _mapServerPathToClient;
        private readonly Func<string, string, string, string> _resolveCurrentPluginSkillPath;
        private readonly Action<IReadOnlyCollection<string>, bool, IReadOnlyCollection<AppServerSkillsSnapshot>> _traceSkillsListDiagnostics;
        private readonly Action<IReadOnlyCollection<AppServerPluginSummary>> _tracePluginListDiagnostics;

        public AppServerClientOperations(
            Action ensureConnected,
            Func<AppServerProtocolCapabilities> getProtocolCapabilities,
            AppServerRequestInvoker sendRequestAsync,
            Func<string, string, string> mapRequiredClientPathToServer,
            Func<string, string> mapClientOrServerPathToServer,
            Func<string, string> mapServerPathToClient,
            Func<string, string, string, string> resolveCurrentPluginSkillPath,
            Action<IReadOnlyCollection<string>, bool, IReadOnlyCollection<AppServerSkillsSnapshot>> traceSkillsListDiagnostics,
            Action<IReadOnlyCollection<AppServerPluginSummary>> tracePluginListDiagnostics)
        {
            _ensureConnected = ensureConnected ?? throw new ArgumentNullException(nameof(ensureConnected));
            _getProtocolCapabilities = getProtocolCapabilities ?? throw new ArgumentNullException(nameof(getProtocolCapabilities));
            _sendRequestAsync = sendRequestAsync ?? throw new ArgumentNullException(nameof(sendRequestAsync));
            _mapRequiredClientPathToServer = mapRequiredClientPathToServer ?? throw new ArgumentNullException(nameof(mapRequiredClientPathToServer));
            _mapClientOrServerPathToServer = mapClientOrServerPathToServer ?? throw new ArgumentNullException(nameof(mapClientOrServerPathToServer));
            _mapServerPathToClient = mapServerPathToClient ?? throw new ArgumentNullException(nameof(mapServerPathToClient));
            _resolveCurrentPluginSkillPath = resolveCurrentPluginSkillPath ?? throw new ArgumentNullException(nameof(resolveCurrentPluginSkillPath));
            _traceSkillsListDiagnostics = traceSkillsListDiagnostics ?? throw new ArgumentNullException(nameof(traceSkillsListDiagnostics));
            _tracePluginListDiagnostics = tracePluginListDiagnostics ?? throw new ArgumentNullException(nameof(tracePluginListDiagnostics));
        }

        public async Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
        {
            _ensureConnected();

            var accountState = await SendRequestAsync("account/read", new Dictionary<string, object>
            {
                { "refreshToken", refreshToken }
            }).ConfigureAwait(false);

            return AppServerAccountConfigMapper.BuildAuthenticationState(accountState);
        }

        public async Task<AppServerLoginResult> StartChatGptLoginAsync()
        {
            _ensureConnected();

            var result = await SendRequestAsync("account/login/start", new Dictionary<string, object>
            {
                { "type", "chatgpt" }
            }).ConfigureAwait(false);

            return new AppServerLoginResult
            {
                LoginType = AppServerJsonValueReader.GetString(result, "type"),
                LoginId = AppServerJsonValueReader.GetString(result, "loginId"),
                AuthUrl = AppServerJsonValueReader.GetString(result, "authUrl")
            };
        }

        public async Task<AppServerLoginResult> StartChatGptDeviceCodeLoginAsync()
        {
            _ensureConnected();

            var result = await SendRequestAsync("account/login/start", new Dictionary<string, object>
            {
                { "type", "chatgptDeviceCode" }
            }).ConfigureAwait(false);

            return new AppServerLoginResult
            {
                LoginType = AppServerJsonValueReader.GetString(result, "type"),
                LoginId = AppServerJsonValueReader.GetString(result, "loginId"),
                VerificationUrl = AppServerJsonValueReader.GetString(result, "verificationUrl"),
                UserCode = AppServerJsonValueReader.GetString(result, "userCode")
            };
        }

        public async Task<AppServerLoginResult> StartOpenAiApiKeyLoginAsync(string apiKey)
        {
            _ensureConnected();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException(L("AppServerClient.Error.ApiKeyRequired", "API key cannot be empty."), nameof(apiKey));
            }

            var result = await SendRequestAsync("account/login/start", new Dictionary<string, object>
            {
                { "type", "apiKey" },
                { "apiKey", apiKey.Trim() }
            }).ConfigureAwait(false);

            return new AppServerLoginResult
            {
                LoginType = AppServerJsonValueReader.GetString(result, "type"),
                LoginId = AppServerJsonValueReader.GetString(result, "loginId"),
                AuthUrl = AppServerJsonValueReader.GetString(result, "authUrl")
            };
        }

        public async Task<AppServerLoginCancelResult> CancelLoginAsync(string loginId)
        {
            _ensureConnected();
            if (string.IsNullOrWhiteSpace(loginId))
            {
                throw new ArgumentException(L("AppServerClient.Error.LoginIdRequired", "Login id cannot be empty."), nameof(loginId));
            }

            var result = await SendRequestAsync("account/login/cancel", new Dictionary<string, object>
            {
                { "loginId", loginId.Trim() }
            }).ConfigureAwait(false);

            return new AppServerLoginCancelResult
            {
                Status = AppServerJsonValueReader.GetString(result, "status")
            };
        }

        public async Task LogoutAsync()
        {
            _ensureConnected();
            await SendRequestAsync("account/logout", new Dictionary<string, object>()).ConfigureAwait(false);
        }

        public async Task<AppServerAccountTokenUsageSnapshot> GetAccountTokenUsageAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            var result = await SendRequestAsync(
                "account/usage/read",
                new Dictionary<string, object>(),
                cancellationToken)
                .ConfigureAwait(false);
            return AppServerAccountTokenUsageMapper.BuildSnapshot(result);
        }

        public async Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
            string idempotencyKey,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return await ConsumeRateLimitResetCreditAsync(idempotencyKey, null, cancellationToken).ConfigureAwait(false);
        }

        public async Task<AppServerRateLimitResetConsumeResult> ConsumeRateLimitResetCreditAsync(
            string idempotencyKey,
            string creditId,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                throw new ArgumentException(L("AppServerClient.Error.RateLimitResetIdempotencyKeyRequired", "Rate limit reset idempotency key cannot be empty."), nameof(idempotencyKey));
            }

            var parameters = new Dictionary<string, object>
            {
                { "idempotencyKey", idempotencyKey.Trim() }
            };
            if (!string.IsNullOrWhiteSpace(creditId))
            {
                parameters["creditId"] = creditId.Trim();
            }

            var result = await SendRequestAsync(
                "account/rateLimitResetCredit/consume",
                parameters,
                cancellationToken)
                .ConfigureAwait(false);

            return new AppServerRateLimitResetConsumeResult
            {
                Outcome = AppServerJsonValueReader.GetString(result, "outcome")
            };
        }

        public async Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers, CancellationToken cancellationToken)
        {
            _ensureConnected();

            var parameters = new Dictionary<string, object>
            {
                { "includeLayers", includeLayers }
            };
            AddOptionalString(parameters, "cwd", _mapRequiredClientPathToServer(workspacePath, "workspace"));

            var result = await SendRequestAsync("config/read", parameters, cancellationToken).ConfigureAwait(false);
            return AppServerAccountConfigMapper.BuildConfigSnapshot(result);
        }

        public async Task<AppServerPermissionProfileListResult> ListPermissionProfilesAsync(
            string workspacePath = null,
            int? limit = null,
            string cursor = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            var parameters = new Dictionary<string, object>();
            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                parameters["cwd"] = _mapRequiredClientPathToServer(workspacePath, "workspace");
            }

            if (limit.HasValue)
            {
                parameters["limit"] = limit.Value;
            }

            AddOptionalString(parameters, "cursor", cursor);

            var result = await SendRequestAsync("permissionProfile/list", parameters, cancellationToken).ConfigureAwait(false);
            return AppServerPermissionProfileMapper.BuildListResult(result);
        }

        public async Task WriteConfigValueAsync(string keyPath, object value)
        {
            _ensureConnected();

            if (string.IsNullOrWhiteSpace(keyPath))
            {
                throw new ArgumentException(L("AppServerClient.Error.ConfigKeyPathRequired", "Config key path cannot be empty."), nameof(keyPath));
            }

            await SendRequestAsync("config/value/write", new Dictionary<string, object>
            {
                { "keyPath", keyPath },
                { "mergeStrategy", "replace" },
                { "value", value }
            }).ConfigureAwait(false);
        }

        public async Task WriteConfigBatchAsync(IReadOnlyCollection<AppServerConfigWriteEdit> edits, string filePath = null, string expectedVersion = null, bool reloadUserConfig = false)
        {
            _ensureConnected();

            if (edits == null || edits.Count == 0)
            {
                throw new ArgumentException(L("AppServerClient.Error.BatchConfigEditsRequired", "Batch config edits cannot be empty."), nameof(edits));
            }

            var serializedEdits = new List<object>();
            foreach (var edit in edits)
            {
                if (edit == null)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(edit.KeyPath))
                {
                    throw new ArgumentException(L("AppServerClient.Error.ConfigKeyPathRequired", "Config key path cannot be empty."), nameof(edits));
                }

                if (string.IsNullOrWhiteSpace(edit.MergeStrategy))
                {
                    throw new ArgumentException(L("AppServerClient.Error.ConfigMergeStrategyRequired", "Config merge strategy cannot be empty."), nameof(edits));
                }

                serializedEdits.Add(new Dictionary<string, object>
                {
                    { "keyPath", edit.KeyPath },
                    { "mergeStrategy", edit.MergeStrategy },
                    { "value", edit.Value }
                });
            }

            if (serializedEdits.Count == 0)
            {
                throw new ArgumentException(L("AppServerClient.Error.BatchConfigEditsRequired", "Batch config edits cannot be empty."), nameof(edits));
            }

            var parameters = new Dictionary<string, object>
            {
                { "edits", serializedEdits.ToArray() }
            };
            AddOptionalString(parameters, "filePath", _mapClientOrServerPathToServer(filePath));
            AddOptionalString(parameters, "expectedVersion", expectedVersion);
            if (reloadUserConfig)
            {
                parameters["reloadUserConfig"] = true;
            }

            AppServerEventDiagnostics.TraceConfigWrite(edits, reloadUserConfig, filePath);
            await SendRequestAsync("config/batchWrite", parameters).ConfigureAwait(false);
        }

        public async Task<List<AppServerMcpServerStatus>> ListMcpServerStatusesAsync()
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMcpManagement, "MCP management");

            var statusTask = SendRequestAsync("mcpServerStatus/list", new Dictionary<string, object>());
            var configTask = SendRequestAsync("config/read", new Dictionary<string, object>
            {
                { "includeLayers", false }
            });

            await Task.WhenAll(statusTask, configTask).ConfigureAwait(false);
            return AppServerMcpServerMapper.BuildMcpServerStatuses(statusTask.Result, configTask.Result);
        }

        public async Task ReloadMcpServerConfigAsync()
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMcpManagement, "MCP management");
            await SendRequestAsync("config/mcpServer/reload", new Dictionary<string, object>()).ConfigureAwait(false);
        }

        public async Task<AppServerMcpServerOauthLoginResult> StartMcpServerOauthLoginAsync(string serverName, int? timeoutSecs = null)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMcpOAuth, "MCP OAuth");

            if (string.IsNullOrWhiteSpace(serverName))
            {
                throw new ArgumentException(L("AppServerClient.Error.McpServerNameRequired", "MCP server name cannot be empty."), nameof(serverName));
            }

            var parameters = new Dictionary<string, object>
            {
                { "name", serverName }
            };
            if (timeoutSecs.HasValue && timeoutSecs.Value > 0)
            {
                parameters["timeoutSecs"] = timeoutSecs.Value;
            }

            var result = await SendRequestAsync("mcpServer/oauth/login", parameters).ConfigureAwait(false);
            return new AppServerMcpServerOauthLoginResult
            {
                LoginId = AppServerJsonValueReader.GetFirstNonEmptyString(result, "loginId", "name") ?? serverName,
                AuthUrl = AppServerJsonValueReader.GetFirstNonEmptyString(result, "authorizationUrl", "authUrl")
            };
        }

        public async Task<AppServerMcpResourceReadResult> ReadMcpResourceAsync(
            string server,
            string uri,
            string threadId = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMcpManagement, "MCP management");

            if (string.IsNullOrWhiteSpace(server))
            {
                throw new ArgumentException(L("AppServerClient.Error.McpServerNameRequired", "MCP server name cannot be empty."), nameof(server));
            }

            if (string.IsNullOrWhiteSpace(uri))
            {
                throw new ArgumentException(L("AppServerClient.Error.McpResourceUriRequired", "MCP resource URI cannot be empty."), nameof(uri));
            }

            var parameters = new Dictionary<string, object>
            {
                { "server", server.Trim() },
                { "uri", uri.Trim() }
            };
            AddOptionalString(parameters, "threadId", threadId);

            var result = await SendRequestAsync("mcpServer/resource/read", parameters, cancellationToken).ConfigureAwait(false);
            return AppServerMcpResourceMapper.BuildReadResult(result);
        }

        public async Task<AppServerMcpToolCallResult> CallMcpToolAsync(
            AppServerMcpToolCallRequest request,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMcpManagement, "MCP management");

            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ThreadId))
            {
                throw new ArgumentException(L("AppServerClient.Error.ThreadIdRequired", "Thread ID cannot be empty."), nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Server))
            {
                throw new ArgumentException(L("AppServerClient.Error.McpServerNameRequired", "MCP server name cannot be empty."), nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Tool))
            {
                throw new ArgumentException(L("AppServerClient.Error.McpToolNameRequired", "MCP tool name cannot be empty."), nameof(request));
            }

            var parameters = new Dictionary<string, object>
            {
                { "threadId", request.ThreadId.Trim() },
                { "server", request.Server.Trim() },
                { "tool", request.Tool.Trim() }
            };

            if (request.Arguments != null)
            {
                parameters["arguments"] = request.Arguments;
            }

            if (request.Meta != null)
            {
                parameters["_meta"] = request.Meta;
            }

            var result = await SendRequestAsync("mcpServer/tool/call", parameters, cancellationToken).ConfigureAwait(false);
            return AppServerMcpToolCallMapper.BuildResult(result);
        }

        public async Task<List<AppServerSkillsSnapshot>> ListSkillsAsync(IReadOnlyCollection<string> workspacePaths, bool forceReload)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsSkills, "Skills");

            var parameters = new Dictionary<string, object>();
            if (workspacePaths != null)
            {
                var normalizedPaths = workspacePaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Select(path => _mapRequiredClientPathToServer(path, "workspace"))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (normalizedPaths.Length > 0)
                {
                    parameters["cwds"] = normalizedPaths;
                }
            }

            if (forceReload)
            {
                parameters["forceReload"] = true;
            }

            var result = await SendRequestAsync("skills/list", parameters).ConfigureAwait(false);
            var snapshots = AppServerPluginSkillMapper.BuildSkillsSnapshots(
                result,
                _mapServerPathToClient,
                _resolveCurrentPluginSkillPath);
            _traceSkillsListDiagnostics(workspacePaths, forceReload, snapshots);
            return snapshots;
        }

        public async Task<bool> WriteSkillEnabledAsync(string path, bool enabled)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsSkills, "Skills");

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(L("AppServerClient.Error.SkillPathRequired", "Skill path cannot be empty."), nameof(path));
            }

            var result = await SendRequestAsync("skills/config/write", new Dictionary<string, object>
            {
                { "path", _mapClientOrServerPathToServer(path) },
                { "enabled", enabled }
            }).ConfigureAwait(false);

            var effectiveEnabled = AppServerJsonValueReader.GetNullableBool(result, "effectiveEnabled");
            return effectiveEnabled ?? enabled;
        }

        public async Task<List<AppServerFileSystemEntry>> ReadDirectoryAsync(string path)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            var result = await SendRequestAsync(
                "fs/readDirectory",
                BuildFileSystemPathParameters(path, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
            return AppServerFileSystemMapper.BuildDirectoryEntries(result);
        }

        public async Task<AppServerFileSystemReadResult> ReadFileAsync(string path)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            var result = await SendRequestAsync(
                "fs/readFile",
                BuildFileSystemPathParameters(path, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
            return AppServerFileSystemMapper.BuildReadResult(result);
        }

        public async Task<AppServerFileSystemMetadata> GetMetadataAsync(string path)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            var result = await SendRequestAsync(
                "fs/getMetadata",
                BuildFileSystemPathParameters(path, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
            return AppServerFileSystemMapper.BuildMetadata(result);
        }

        public async Task WriteFileAsync(string path, byte[] data)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            await SendRequestAsync(
                "fs/writeFile",
                BuildFileSystemWriteFileParameters(path, data, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
        }

        public async Task CreateDirectoryAsync(string path, bool? recursive = null)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            await SendRequestAsync(
                "fs/createDirectory",
                BuildFileSystemCreateDirectoryParameters(path, recursive, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
        }

        public async Task RemoveAsync(string path, bool? recursive = null, bool? force = null)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            await SendRequestAsync(
                "fs/remove",
                BuildFileSystemRemoveParameters(path, recursive, force, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
        }

        public async Task CopyAsync(string sourcePath, string destinationPath, bool recursive)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            await SendRequestAsync(
                "fs/copy",
                BuildFileSystemCopyParameters(sourcePath, destinationPath, recursive, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
        }

        public async Task<AppServerFileSystemWatchResult> WatchFileSystemAsync(string path, string watchId)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            var result = await SendRequestAsync(
                "fs/watch",
                BuildFileSystemWatchParameters(path, watchId, value => _mapRequiredClientPathToServer(value, "path")))
                .ConfigureAwait(false);
            return AppServerFileSystemMapper.BuildWatchResult(result);
        }

        public async Task UnwatchFileSystemAsync(string watchId)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFileSystem, "File system");

            await SendRequestAsync("fs/unwatch", BuildFileSystemUnwatchParameters(watchId)).ConfigureAwait(false);
        }

        public async Task<AppServerFuzzyFileSearchSessionEvent> StartFuzzyFileSearchAsync(AppServerFuzzyFileSearchRequest request, CancellationToken cancellationToken)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsFuzzyFileSearch, "Fuzzy file search");

            var result = await SendRequestAsync(
                "fuzzyFileSearch",
                BuildFuzzyFileSearchParameters(request, value => _mapRequiredClientPathToServer(value, "workspace")),
                cancellationToken)
                .ConfigureAwait(false);
            return AppServerFuzzyFileSearchMapper.BuildSearchResponse(result);
        }

        public async Task<AppServerWindowsSandboxReadiness> ReadWindowsSandboxReadinessAsync()
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsWindowsSandbox, "Windows Sandbox");

            var result = await SendRequestAsync("windowsSandbox/readiness", new Dictionary<string, object>())
                .ConfigureAwait(false);
            return AppServerWindowsSandboxMapper.BuildReadiness(result);
        }

        public async Task<AppServerWindowsSandboxSetupStartResult> StartWindowsSandboxSetupAsync(
            AppServerWindowsSandboxSetupMode mode,
            string cwd = null)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsWindowsSandbox, "Windows Sandbox");

            var result = await SendRequestAsync(
                "windowsSandbox/setupStart",
                BuildWindowsSandboxSetupStartParameters(mode, cwd, path => _mapRequiredClientPathToServer(path, "workspace")),
                CancellationToken.None,
                LongRunningRequestTimeoutMs)
                .ConfigureAwait(false);
            return AppServerWindowsSandboxMapper.BuildSetupStartResult(result);
        }

        public async Task<List<AppServerPluginSummary>> ListPluginsAsync(CancellationToken cancellationToken)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsPluginCatalogObservation, "Plugin catalog observation");

            var result = await SendRequestAsync("plugin/list", new Dictionary<string, object>(), cancellationToken).ConfigureAwait(false);
            var plugins = AppServerPluginSkillMapper.BuildPluginSummaries(result);
            _tracePluginListDiagnostics(plugins);
            return plugins;
        }

        public async Task<AppServerPluginInstalledSnapshot> GetInstalledPluginsAsync(
            CancellationToken cancellationToken)
        {
            _ensureConnected();
            EnsureSupported(
                capabilities => capabilities.SupportsInstalledPluginObservation,
                "Installed plugin observation");

            var result = await SendRequestAsync(
                "plugin/installed",
                new Dictionary<string, object>(),
                cancellationToken).ConfigureAwait(false);
            return AppServerPluginSkillMapper.BuildInstalledPluginSnapshot(result);
        }

        public async Task<AppServerPluginDetails> ReadPluginAsync(string marketplacePath, string pluginName)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsPluginManagement, "Plugin management");

            if (string.IsNullOrWhiteSpace(marketplacePath))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginMarketplacePathRequired", "Plugin marketplace path cannot be empty."), nameof(marketplacePath));
            }

            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginNameRequired", "Plugin name cannot be empty."), nameof(pluginName));
            }

            var result = await SendRequestAsync("plugin/read", new Dictionary<string, object>
            {
                { "marketplacePath", marketplacePath.Trim() },
                { "pluginName", pluginName.Trim() }
            }).ConfigureAwait(false);

            return AppServerPluginSkillMapper.BuildPluginDetails(
                result,
                _mapServerPathToClient,
                _resolveCurrentPluginSkillPath);
        }

        public async Task<AppServerPluginSkillReadResult> ReadPluginSkillAsync(
            string remoteMarketplaceName,
            string remotePluginId,
            string skillName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsPluginManagement, "Plugin management");

            if (string.IsNullOrWhiteSpace(remoteMarketplaceName))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginMarketplaceNameRequired", "Plugin marketplace name cannot be empty."), nameof(remoteMarketplaceName));
            }

            if (string.IsNullOrWhiteSpace(remotePluginId))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginIdRequired", "Plugin ID cannot be empty."), nameof(remotePluginId));
            }

            if (string.IsNullOrWhiteSpace(skillName))
            {
                throw new ArgumentException(L("AppServerClient.Error.SkillNameRequired", "Skill name cannot be empty."), nameof(skillName));
            }

            var result = await SendRequestAsync(
                "plugin/skill/read",
                new Dictionary<string, object>
                {
                    { "remoteMarketplaceName", remoteMarketplaceName.Trim() },
                    { "remotePluginId", remotePluginId.Trim() },
                    { "skillName", skillName.Trim() }
                },
                cancellationToken).ConfigureAwait(false);
            return AppServerPluginSkillReadMapper.BuildResult(result);
        }

        public async Task<AppServerMarketplaceUpgradeResult> UpgradeMarketplaceAsync(
            string marketplaceName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMarketplaceRegistration, "Marketplace registration");

            var normalizedMarketplaceName = string.IsNullOrWhiteSpace(marketplaceName)
                ? null
                : marketplaceName.Trim();
            var parameters = new Dictionary<string, object>();
            AddOptionalString(parameters, "marketplaceName", normalizedMarketplaceName);

            var result = await SendRequestAsync(
                "marketplace/upgrade",
                parameters,
                cancellationToken,
                LongRunningRequestTimeoutMs).ConfigureAwait(false);
            return AppServerMarketplaceMapper.BuildUpgradeResult(result, normalizedMarketplaceName);
        }

        public async Task<AppServerMarketplaceAddResult> AddMarketplaceAsync(
            string source,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMarketplaceRegistration, "Marketplace registration");

            if (string.IsNullOrWhiteSpace(source))
            {
                throw new ArgumentException(
                    L("AppServerClient.Error.MarketplaceSourceRequired", "Marketplace source cannot be empty."),
                    nameof(source));
            }

            var result = await SendRequestAsync(
                "marketplace/add",
                new Dictionary<string, object>
                {
                    { "source", source.Trim() }
                },
                cancellationToken,
                LongRunningRequestTimeoutMs).ConfigureAwait(false);
            return AppServerMarketplaceMapper.BuildAddResult(result);
        }

        public async Task RemoveMarketplaceAsync(
            string marketplaceName,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsMarketplaceRegistration, "Marketplace registration");

            if (string.IsNullOrWhiteSpace(marketplaceName))
            {
                throw new ArgumentException(
                    L("AppServerClient.Error.PluginMarketplaceNameRequired", "Plugin marketplace name cannot be empty."),
                    nameof(marketplaceName));
            }

            await SendRequestAsync(
                "marketplace/remove",
                new Dictionary<string, object>
                {
                    { "marketplaceName", marketplaceName.Trim() }
                },
                cancellationToken,
                LongRunningRequestTimeoutMs).ConfigureAwait(false);
        }

        public async Task<AppServerPluginInstallResult> InstallPluginAsync(string marketplacePath, string pluginName)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsPluginMutation, "Plugin mutation");

            if (string.IsNullOrWhiteSpace(marketplacePath))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginMarketplacePathRequired", "Plugin marketplace path cannot be empty."), nameof(marketplacePath));
            }

            if (string.IsNullOrWhiteSpace(pluginName))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginNameRequired", "Plugin name cannot be empty."), nameof(pluginName));
            }

            var result = await SendRequestAsync("plugin/install", new Dictionary<string, object>
            {
                { "marketplacePath", marketplacePath.Trim() },
                { "pluginName", pluginName.Trim() }
            }, CancellationToken.None, LongRunningRequestTimeoutMs).ConfigureAwait(false);

            return BuildPluginInstallResult(result);
        }

        public async Task<AppServerPluginInstallResult> InstallRemotePluginAsync(string remoteMarketplaceName, string remotePluginId)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsPluginMutation, "Plugin mutation");

            if (string.IsNullOrWhiteSpace(remoteMarketplaceName))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginMarketplaceNameRequired", "Plugin marketplace name cannot be empty."), nameof(remoteMarketplaceName));
            }

            if (string.IsNullOrWhiteSpace(remotePluginId))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginIdRequired", "Plugin ID cannot be empty."), nameof(remotePluginId));
            }

            var result = await SendRequestAsync("plugin/install", new Dictionary<string, object>
            {
                { "remoteMarketplaceName", remoteMarketplaceName.Trim() },
                { "pluginName", remotePluginId.Trim() }
            }, CancellationToken.None, LongRunningRequestTimeoutMs).ConfigureAwait(false);

            return BuildPluginInstallResult(result);
        }

        private static AppServerPluginInstallResult BuildPluginInstallResult(IDictionary<string, object> result)
        {
            return new AppServerPluginInstallResult
            {
                AuthPolicy = AppServerJsonValueReader.GetString(result, "authPolicy"),
                AppsNeedingAuth = AppServerPluginSkillMapper.BuildPluginApps(result, "appsNeedingAuth")
            };
        }

        public async Task UninstallPluginAsync(string pluginId)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsPluginMutation, "Plugin mutation");

            if (string.IsNullOrWhiteSpace(pluginId))
            {
                throw new ArgumentException(L("AppServerClient.Error.PluginIdRequired", "Plugin ID cannot be empty."), nameof(pluginId));
            }

            await SendRequestAsync("plugin/uninstall", new Dictionary<string, object>
            {
                { "pluginId", pluginId.Trim() }
            }).ConfigureAwait(false);
        }

        public async Task<AppServerModelProviderCapabilities> ReadModelProviderCapabilitiesAsync()
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsModelProviderCapabilities, "Model provider capabilities");

            var result = await SendRequestAsync("modelProvider/capabilities/read", new Dictionary<string, object>()).ConfigureAwait(false);
            return AppServerModelProviderCapabilitiesMapper.BuildCapabilities(result);
        }

        public async Task<List<AppServerExperimentalFeature>> ListExperimentalFeaturesAsync(uint? limit = null)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsExperimentalApi, "Experimental features");

            var features = new List<AppServerExperimentalFeature>();
            string cursor = null;
            do
            {
                var parameters = new Dictionary<string, object>();
                AddOptionalString(parameters, "cursor", cursor);
                if (limit.HasValue)
                {
                    parameters["limit"] = limit.Value;
                }

                var result = await SendRequestAsync("experimentalFeature/list", parameters).ConfigureAwait(false);
                features.AddRange(AppServerExperimentalFeatureMapper.BuildFeatures(result));
                cursor = AppServerExperimentalFeatureMapper.GetNextCursor(result);
            }
            while (!string.IsNullOrWhiteSpace(cursor));

            return features
                .OrderBy(item => item.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<AppServerConfigRequirementsSnapshot> ReadConfigRequirementsAsync()
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsConfigRequirements, "Config requirements");

            var result = await SendRequestAsync("configRequirements/read", new Dictionary<string, object>()).ConfigureAwait(false);
            return AppServerConfigRequirementsMapper.BuildRequirements(result);
        }

        public async Task<ExternalAgentConfigDetectResult> DetectExternalAgentConfigAsync(
            IReadOnlyCollection<string> workingDirectories,
            bool includeHome,
            CancellationToken cancellationToken)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsExternalAgentConfigImport, "External agent config import");

            var result = await SendRequestAsync(
                "externalAgentConfig/detect",
                BuildExternalAgentConfigDetectParameters(
                    workingDirectories,
                    includeHome,
                    path => _mapRequiredClientPathToServer(path, "workspace")),
                cancellationToken).ConfigureAwait(false);
            return AppServerExternalAgentConfigMapper.BuildDetectResult(result);
        }

        public async Task ImportExternalAgentConfigAsync(
            IReadOnlyCollection<ExternalAgentConfigMigrationItem> migrationItems,
            CancellationToken cancellationToken)
        {
            _ensureConnected();
            EnsureSupported(capabilities => capabilities.SupportsExternalAgentConfigImport, "External agent config import");

            await SendRequestAsync(
                "externalAgentConfig/import",
                new Dictionary<string, object>
                {
                    { "migrationItems", AppServerExternalAgentConfigMapper.ToProtocolMigrationItems(migrationItems) }
                },
                cancellationToken,
                LongRunningRequestTimeoutMs).ConfigureAwait(false);
        }

        public async Task<List<AppServerModelOption>> ListModelsAsync(CancellationToken cancellationToken)
        {
            _ensureConnected();

            var models = new List<AppServerModelOption>();
            string cursor = null;

            do
            {
                var parameters = new Dictionary<string, object>
                {
                    { "limit", 100 },
                    { "includeHidden", false }
                };

                if (!string.IsNullOrWhiteSpace(cursor))
                {
                    parameters["cursor"] = cursor;
                }

                var result = await SendRequestAsync("model/list", parameters, cancellationToken).ConfigureAwait(false);
                foreach (var item in AppServerJsonValueReader.GetArray(result, "data"))
                {
                    var model = item as IDictionary<string, object>;
                    if (model == null)
                    {
                        continue;
                    }

                    var option = AppServerAccountConfigMapper.BuildModelOption(model);
                    if (!string.IsNullOrWhiteSpace(option.ModelId))
                    {
                        models.Add(option);
                    }
                }

                cursor = AppServerJsonValueReader.GetString(result, "nextCursor");
            }
            while (!string.IsNullOrWhiteSpace(cursor));

            return models;
        }

        public async Task<AppServerGitDiffToRemoteResult> GetGitDiffToRemoteAsync(
            string workspacePath,
            CancellationToken cancellationToken)
        {
            _ensureConnected();
            if (string.IsNullOrWhiteSpace(workspacePath))
            {
                throw new ArgumentException(L("AppServerClient.Error.WorkspacePathRequired", "Workspace path cannot be empty."), nameof(workspacePath));
            }

            var result = await SendRequestAsync(
                    "gitDiffToRemote",
                    new Dictionary<string, object>
                    {
                        { "cwd", _mapRequiredClientPathToServer(workspacePath, "workspace") }
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            return AppServerGitDiffToRemoteMapper.BuildResult(result);
        }

        private Task<IDictionary<string, object>> SendRequestAsync(string method, object parameters)
        {
            return SendRequestAsync(method, parameters, CancellationToken.None, DefaultRequestTimeoutMs);
        }

        private Task<IDictionary<string, object>> SendRequestAsync(
            string method,
            object parameters,
            CancellationToken cancellationToken,
            int timeoutMs = DefaultRequestTimeoutMs)
        {
            return _sendRequestAsync(method, parameters, cancellationToken, timeoutMs);
        }

        private void EnsureSupported(Func<AppServerProtocolCapabilities, bool> isSupported, string capabilityName)
        {
            var capabilities = _getProtocolCapabilities();
            AppServerProtocolCapabilityGuards.EnsureSupported(
                capabilities,
                capabilities == null || isSupported(capabilities),
                capabilityName);
        }

        private static Dictionary<string, object> BuildExternalAgentConfigDetectParameters(
            IReadOnlyCollection<string> workingDirectories,
            bool includeHome,
            Func<string, string> mapPath)
        {
            var parameters = new Dictionary<string, object>
            {
                { "includeHome", includeHome }
            };

            if (workingDirectories == null)
            {
                return parameters;
            }

            var normalizedPaths = workingDirectories
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => mapPath == null ? path : mapPath(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (normalizedPaths.Length > 0)
            {
                parameters["cwds"] = normalizedPaths;
            }

            return parameters;
        }

        private static Dictionary<string, object> BuildFileSystemPathParameters(
            string path,
            Func<string, string> mapPath)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException(L("AppServerClient.Error.FileSystemPathRequired", "File system path cannot be empty."), nameof(path));
            }

            return new Dictionary<string, object>
            {
                { "path", mapPath == null ? path : mapPath(path) }
            };
        }

        private static Dictionary<string, object> BuildFileSystemWriteFileParameters(
            string path,
            byte[] data,
            Func<string, string> mapPath)
        {
            var normalizedPath = Convert.ToString(BuildFileSystemPathParameters(path, mapPath)["path"]);
            return AppServerRequestParameterBuilder.BuildFileSystemWriteFileParameters(normalizedPath, data, null);
        }

        private static Dictionary<string, object> BuildFileSystemCreateDirectoryParameters(
            string path,
            bool? recursive,
            Func<string, string> mapPath)
        {
            var parameters = BuildFileSystemPathParameters(path, mapPath);
            if (recursive.HasValue)
            {
                parameters["recursive"] = recursive.Value;
            }

            return parameters;
        }

        private static Dictionary<string, object> BuildFileSystemRemoveParameters(
            string path,
            bool? recursive,
            bool? force,
            Func<string, string> mapPath)
        {
            var parameters = BuildFileSystemPathParameters(path, mapPath);
            if (recursive.HasValue)
            {
                parameters["recursive"] = recursive.Value;
            }

            if (force.HasValue)
            {
                parameters["force"] = force.Value;
            }

            return parameters;
        }

        private static Dictionary<string, object> BuildFileSystemCopyParameters(
            string sourcePath,
            string destinationPath,
            bool recursive,
            Func<string, string> mapPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                throw new ArgumentException(L("AppServerClient.Error.FileSystemSourcePathRequired", "File system source path cannot be empty."), nameof(sourcePath));
            }

            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                throw new ArgumentException(L("AppServerClient.Error.FileSystemDestinationPathRequired", "File system destination path cannot be empty."), nameof(destinationPath));
            }

            return new Dictionary<string, object>
            {
                { "sourcePath", mapPath == null ? sourcePath : mapPath(sourcePath) },
                { "destinationPath", mapPath == null ? destinationPath : mapPath(destinationPath) },
                { "recursive", recursive }
            };
        }

        private static Dictionary<string, object> BuildFileSystemWatchParameters(
            string path,
            string watchId,
            Func<string, string> mapPath)
        {
            if (string.IsNullOrWhiteSpace(watchId))
            {
                throw new ArgumentException(L("AppServerClient.Error.FileSystemWatchIdRequired", "File system watch ID cannot be empty."), nameof(watchId));
            }

            var normalizedPath = Convert.ToString(BuildFileSystemPathParameters(path, mapPath)["path"]);
            return AppServerRequestParameterBuilder.BuildFileSystemWatchParameters(normalizedPath, watchId.Trim(), null);
        }

        private static Dictionary<string, object> BuildFileSystemUnwatchParameters(string watchId)
        {
            if (string.IsNullOrWhiteSpace(watchId))
            {
                throw new ArgumentException(L("AppServerClient.Error.FileSystemWatchIdRequired", "File system watch ID cannot be empty."), nameof(watchId));
            }

            return new Dictionary<string, object>
            {
                { "watchId", watchId.Trim() }
            };
        }

        private static Dictionary<string, object> BuildFuzzyFileSearchParameters(
            AppServerFuzzyFileSearchRequest request,
            Func<string, string> mapRoot)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Query))
            {
                throw new ArgumentException(L("AppServerClient.Error.FuzzyFileSearchQueryRequired", "File search query cannot be empty."), nameof(request));
            }

            var parameters = AppServerRequestParameterBuilder.BuildFuzzyFileSearchParameters(request, mapRoot);
            var roots = parameters["roots"] as string[];
            if (roots == null || roots.Length == 0)
            {
                throw new ArgumentException(L("AppServerClient.Error.FuzzyFileSearchRootsRequired", "At least one file search root is required."), nameof(request));
            }

            return parameters;
        }

        private static Dictionary<string, object> BuildWindowsSandboxSetupStartParameters(
            AppServerWindowsSandboxSetupMode mode,
            string cwd,
            Func<string, string> mapPath)
        {
            var parameters = new Dictionary<string, object>
            {
                { "mode", AppServerWindowsSandboxMapper.MapSetupMode(mode) }
            };

            if (!string.IsNullOrWhiteSpace(cwd))
            {
                parameters["cwd"] = mapPath == null ? cwd : mapPath(cwd);
            }

            return parameters;
        }

        private static void AddOptionalString(IDictionary<string, object> target, string key, string value)
        {
            if (target == null || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            target[key] = value;
        }

        private static string L(string key, string fallback)
        {
            return SharedLocalization.Text(key, fallback);
        }
    }
}
