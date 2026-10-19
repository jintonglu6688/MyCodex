using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Shared.Constants;

namespace MyCodex.Infrastructure.AuthenticationCenter
{
    public sealed partial class AuthenticationCenterClient : IAuthenticationCenterClient
    {
        private static readonly Lazy<LocalAuthenticationHost> LocalHost = new Lazy<LocalAuthenticationHost>(() =>
            new LocalAuthenticationHost(CodexRuntimeSources.Bundled,
                manageStartup: string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MYCODEX_AUTH_CORE_PATH"))));
        private readonly string _targetName;
        private readonly bool _localWindows;
        private readonly Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> _request;
        private Func<CancellationToken, Task> _updateBackend;
        internal Func<string, Task> CredentialDiagnostics { get; set; }

        public bool CanUpdateBackend => _updateBackend != null;

        public Task UpdateBackendAsync(CancellationToken cancellationToken)
        {
            if (_updateBackend == null) throw new AuthenticationCenterException("unsupported_target");
            return _updateBackend(cancellationToken);
        }

        public AuthenticationCenterClient(string targetName,
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> request, bool localWindows = false)
        {
            _targetName = targetName;
            _localWindows = localWindows;
            if (request == null) throw new ArgumentNullException(nameof(request));
            _request = async (method, parameters, token) =>
            {
                try
                {
                    return await request(method != "status" ? "gui/" + method : method, parameters, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    WriteDiagnostic(true, "[AuthenticationCenter] Request failed. TargetKind={0}; Method={1}; ErrorCode={2}; ExceptionType={3}",
                        DiagnosticTargetKind, method, DiagnosticCode((error as AuthenticationCenterException)?.Code), error.GetType().Name);
                    if (error is AuthenticationCenterException failure && failure.Code == "credential_reference_unavailable")
                        await ReportCredentialFailureAsync(method).ConfigureAwait(false);
                    throw;
                }
            };
        }

        private string DiagnosticTargetKind => _localWindows ? "Windows" :
            _targetName?.StartsWith("WSL: ", StringComparison.Ordinal) == true ? "WSL" :
            _targetName?.StartsWith("SSH: ", StringComparison.Ordinal) == true ? "SSH" : "unknown";

        private static string DiagnosticCode(string value)
        {
            return !string.IsNullOrEmpty(value) && value.Length <= 64 && value.All(c => (c >= 'a' && c <= 'z') || c == '_')
                ? value : "unrecognized";
        }

        private static void WriteDiagnostic(bool warning, string format, params object[] values)
        {
            try
            {
                if (warning) Trace.TraceWarning(format, values);
                else Trace.TraceInformation(format, values);
            }
            catch { /* Diagnostics must not change authentication outcomes. */ }
        }

        public static IAuthenticationCenterClient CreateLocal(string runtimeSource)
        {
            return CreateLocal(runtimeSource, null, null);
        }

        public static IAuthenticationCenterClient CreateLocal(string runtimeSource,
            Func<CancellationToken, Task<AppServerConfigSnapshot>> readDiagnosticConfig, string legacyDataRoot)
        {
            var transport = string.Equals(runtimeSource, CodexRuntimeSources.Wsl, StringComparison.OrdinalIgnoreCase) ||
                CodexRuntimeSources.IsRemoteUnixSsh(runtimeSource) ? new LocalAuthenticationHost(runtimeSource) : LocalHost.Value;
            var client = new AuthenticationCenterClient("Windows", transport.RequestAsync, localWindows: true);
            if (readDiagnosticConfig != null)
                client.CredentialDiagnostics = trigger => CollectWindowsCredentialDiagnosticsAsync(trigger, readDiagnosticConfig, legacyDataRoot, transport.CodexHome);
            return client;
        }

        public static async Task EnsureLocalBackendAsync(CancellationToken cancellationToken)
        {
            await EnsureBackendPreparedAsync(LocalHost.Value.RequestAsync, cancellationToken).ConfigureAwait(false);
        }

        internal static async Task EnsureBackendPreparedAsync(
            Func<string, IDictionary<string, object>, CancellationToken, Task<IDictionary<string, object>>> request,
            CancellationToken cancellationToken)
        {
            var status = await request("status", Empty(), cancellationToken).ConfigureAwait(false);
            if (Strings(status, "capabilities")?.Contains("runtimeConfigPreparation") == true)
                await request("runtime/prepare", Empty(), cancellationToken).ConfigureAwait(false);
        }

        public static Task ShutdownLocalBackendAsync(CancellationToken cancellationToken)
        {
            return LocalHost.IsValueCreated ? LocalHost.Value.ShutdownAsync(cancellationToken) : Task.CompletedTask;
        }

        public static bool IsLocalBackendCreated => LocalHost.IsValueCreated;

        public static IAuthenticationCenterClient CreateWsl(WslAppServerConfigSnapshot config)
        {
            var transport = new WslAuthenticationHost(config);
            var distribution = config != null ? config.DistributionName : WslAppServerConfigSnapshot.DefaultDistributionName;
            return new AuthenticationCenterClient("WSL: " + distribution, transport.RequestAsync)
            {
                _updateBackend = transport.UpdateAsync
            };
        }

        public static IAuthenticationCenterClient CreateRemote(RemoteMacSshConfigSnapshot config)
        {
            var transport = new RemoteAuthenticationHost(config);
            var target = config == null ? string.Empty : config.HostName;
            return new AuthenticationCenterClient("SSH: " + target, transport.RequestAsync)
            {
                _updateBackend = transport.UpdateAsync
            };
        }

        public async Task<AuthenticationCenterSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            var status = await _request("status", Empty(), cancellationToken).ConfigureAwait(false);
            if (!status.TryGetValue("protocolVersion", out var protocol) || Convert.ToInt32(protocol) != 3)
            {
                throw new AuthenticationCenterException("protocol_mismatch");
            }
            var supportsCapture = Strings(status, "capabilities")?.Contains("globalAuthCapture") == true;
            var list = await _request("provider/list", Empty(), cancellationToken).ConfigureAwait(false);
            // Capture can adopt an external direct configuration and retire a route.
            // Its list and route must describe the same post-capture observation.
            var routeSource = supportsCapture ? list : status;
            var route = routeSource.TryGetValue("route", out var routeValue) ? routeValue as IDictionary<string, object> : null;
            if (route == null) throw new AuthenticationCenterException("invalid_response");
            status["routeRequired"] = Flag(route, "takeover");
            status["aggregating"] = Flag(route, "stack");
            status["routerRunning"] = Flag(route, "running") && Flag(route, "accepting");
            status["routeInFlight"] = route.TryGetValue("activeConnections", out var activeConnections) ? activeConnections : 0;
            var providers = new List<AuthenticationProvider>();
            if (!list.TryGetValue("providers", out var items) || !(items is IEnumerable sequence))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            foreach (var item in sequence)
            {
                providers.Add(ReadProvider(item as IDictionary<string, object>));
            }
            var liveState = supportsCapture ? Text(list, "liveState") : null;
            var syncError = Text(list, "syncError");
            if (supportsCapture && liveState != "current" && liveState != "signed_out" && liveState != "unavailable")
            {
                liveState = "unavailable";
                syncError = "invalid_response";
            }
            if (supportsCapture && liveState == "current" &&
                (string.IsNullOrEmpty(Text(list, "currentProviderVersion")) ||
                    !providers.Any(provider => provider.Id == Text(list, "currentProviderId"))))
            {
                liveState = "unavailable";
                syncError = "invalid_response";
            }
            var currentConfirmed = !supportsCapture || liveState == "current";
            if (!string.IsNullOrEmpty(syncError))
            {
                WriteDiagnostic(true, "[AuthenticationCenter] Global capture unavailable. TargetKind={0}; HostVersion={1}; LiveState={2}; Providers={3}; ErrorCode={4}",
                    DiagnosticTargetKind, Version.TryParse(Text(status, "hostVersion"), out var hostVersion) ? hostVersion.ToString() : "unknown",
                    DiagnosticCode(liveState), providers.Count, DiagnosticCode(syncError));
                if (syncError == "credential_reference_unavailable")
                    await ReportCredentialFailureAsync("provider/list").ConfigureAwait(false);
            }
            return new AuthenticationCenterSnapshot
            {
                TargetName = _targetName,
                CodexHome = Text(status, "codexHome"),
                HostVersion = Text(status, "hostVersion"),
                SupportsContextSettings = Strings(status, "capabilities")?.Contains("contextSettings") == true,
                SupportsConfigEditing = Strings(status, "capabilities")?.Contains("configEditing") == true,
                SupportsFullConfigEditor = Strings(status, "capabilities")?.Contains("fullConfigEditor") == true,
                SupportsSessionHistory = Strings(status, "capabilities")?.Contains("unifiedSessionHistory") == true,
                SupportsAggregation = Strings(status, "capabilities")?.Contains("aggregation") == true,
                Aggregating = Flag(status, "aggregating"),
                RouteRequired = Flag(status, "routeRequired"),
                RouterRunning = Flag(status, "routerRunning"),
                RouteInFlight = status.TryGetValue("routeInFlight", out var active) ? Convert.ToInt32(active) : 0,
                StartupRecoveryReady = !status.ContainsKey("startupRecoveryReady") || Flag(status, "startupRecoveryReady"),
                ManualRestartRequired = Flag(status, "manualRestartRequired"),
                CurrentProviderId = currentConfirmed ? Text(list, "currentProviderId") : null,
                CurrentProviderVersion = currentConfirmed ? Text(list, "currentProviderVersion") : null,
                LiveState = liveState,
                LiveAccountLabel = currentConfirmed ? Text(list, "liveAccountLabel") : null,
                SyncError = syncError,
                Providers = providers
            };
        }

        public async Task<IReadOnlyList<AuthenticationProviderPreset>> ReadPresetsAsync(CancellationToken cancellationToken)
        {
            var result = await _request("preset/list", Empty(), cancellationToken).ConfigureAwait(false);
            return Objects(result, "presets").Select(value => new AuthenticationProviderPreset
            {
                Id = Text(value, "id"), Name = Text(value, "name"), Kind = Text(value, "kind"),
                BaseUrl = Text(value, "baseUrl"), Model = Text(value, "model"),
                Available = Flag(value, "available"), UnavailableReason = Text(value, "unavailableReason"),
                AllowsProtocolSelection = Flag(value, "allowsProtocolSelection"),
                ProtocolBaseUrls = ReadProtocolBaseUrls(value),
                Models = ReadModels(value), Advanced = ReadAdvanced(value)
            }).ToList();
        }

        private static Dictionary<string, string> ReadProtocolBaseUrls(IDictionary<string, object> value)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (value.TryGetValue("protocolBaseUrls", out var item) && item is IDictionary<string, object> urls)
            {
                foreach (var entry in urls)
                {
                    if (entry.Value is string url && url.Length > 0) result[entry.Key] = url;
                }
            }
            return result;
        }

        public async Task<IReadOnlyList<AuthenticationCatalogModel>> FetchModelsAsync(AuthenticationModelFetchRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            var parameters = new Dictionary<string, object>
            {
                { "kind", request.Kind }, { "baseUrl", request.BaseUrl }
            };
            if (request.Advanced != null) parameters["advanced"] = request.Advanced;
            if (!string.IsNullOrEmpty(request.ProviderId)) parameters["providerId"] = request.ProviderId;
            if (!string.IsNullOrEmpty(request.ApiKey)) parameters["apiKey"] = request.ApiKey;
            if (!string.IsNullOrEmpty(request.AccountId)) parameters["accountId"] = request.AccountId;
            return ReadModels(await _request("model/fetch", parameters, cancellationToken).ConfigureAwait(false));
        }

        private static Dictionary<string, object> WriteEdit(AuthenticationProviderEdit edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }
            var accountLogin = string.Equals(edit.Kind, "chatgpt", StringComparison.Ordinal);
            var parameters = new Dictionary<string, object>
            {
                { "id", string.IsNullOrEmpty(edit.Id) ? Guid.NewGuid().ToString("N") : edit.Id },
                { "name", edit.Name }, { "kind", edit.Kind }, { "model", accountLogin ? edit.Model ?? string.Empty : edit.Model }
            };
            if (!accountLogin) parameters["baseUrl"] = edit.BaseUrl;
            if (!string.IsNullOrEmpty(edit.ExpectedVersion))
            {
                parameters["expectedVersion"] = edit.ExpectedVersion;
            }
            if (edit.PresetId != null) parameters["presetId"] = edit.PresetId;
            if (edit.AccountId != null) parameters["accountId"] = edit.AccountId;
            if (edit.Advanced != null) parameters["advanced"] = edit.Advanced;
            if (edit.Models != null) parameters["models"] = edit.Models.Select(WriteModel).ToArray();
            if (!accountLogin && edit.ClearApiKey)
            {
                parameters["clearApiKey"] = true;
            }
            else if (!accountLogin && !string.IsNullOrEmpty(edit.ApiKey))
            {
                parameters["apiKey"] = edit.ApiKey;
            }
            if (edit.ConfigToml != null) parameters["configToml"] = edit.ConfigToml;
            return parameters;
        }

        public async Task<AuthenticationProvider> SaveAsync(AuthenticationProviderEdit edit, CancellationToken cancellationToken)
        {
            var parameters = WriteEdit(edit);
            WriteDiagnostic(false, "[AuthenticationCenter] Provider save started. TargetKind={0}; Mode={1}; Kind={2}; ApiKeyProvided={3}; ClearApiKey={4}; CredentialReferenceConfigured={5}; PresetSelected={6}; ConfigDraft={7}",
                DiagnosticTargetKind, string.IsNullOrEmpty(edit.ExpectedVersion) ? "add" : "edit",
                new[] { "chatgpt", "official_api", "responses", "chat_completions", "anthropic" }.Contains(edit.Kind) ? edit.Kind : "unknown",
                parameters.ContainsKey("apiKey"), edit.ClearApiKey,
                edit.Advanced != null && edit.Advanced.TryGetValue("envKey", out var envKey) && !string.IsNullOrEmpty(envKey as string),
                !string.IsNullOrEmpty(edit.PresetId), edit.ConfigToml != null);
            if (edit.EditorBase != null && edit.EditorText != null)
            {
                var editor = new Dictionary<string, object> { { "base", edit.EditorBase }, { "text", edit.EditorText } };
                if (edit.EditorConflictPolicy != null) editor["onConflict"] = edit.EditorConflictPolicy;
                parameters["editor"] = editor;
            }
            var result = await _request("provider/save", parameters, cancellationToken).ConfigureAwait(false);
            // A refused conflict writes nothing; the caller lets the user choose which side to keep.
            if (result.TryGetValue("editorConflict", out var conflict) && conflict is IDictionary<string, object> details)
                throw new AuthenticationCenterException("editor_conflict", Strings(details, "keys") ?? new List<string>());
            if (!(result.TryGetValue("globalApplied", out var applied) && applied is bool))
                throw new AuthenticationCenterException("operation_unknown");
            var provider = ReadProvider(result);
            WriteDiagnostic(false, "[AuthenticationCenter] Provider save completed. TargetKind={0}; GlobalApplied={1}; RecoveryRequired={2}", DiagnosticTargetKind, applied, provider.RecoveryRequired);
            if (provider.RecoveryRequired) await ReportCredentialFailureAsync("provider/save").ConfigureAwait(false);
            return provider;
        }

        public async Task<AuthenticationProvider> ReadForEditAsync(AuthenticationProvider provider, CancellationToken cancellationToken)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            cancellationToken.ThrowIfCancellationRequested();
            return ReadProvider(await _request("provider/get", new Dictionary<string, object> { { "providerId", provider.Id } }, cancellationToken).ConfigureAwait(false));
        }

        public async Task<AuthenticationProvider> CopyAsync(AuthenticationProvider provider, string name, CancellationToken cancellationToken)
        {
            var parameters = Selection(provider);
            parameters["newId"] = Guid.NewGuid().ToString("N");
            parameters["name"] = name;
            return ReadProvider(await _request("provider/copy", parameters, cancellationToken).ConfigureAwait(false));
        }

        public async Task<bool> DeleteAsync(AuthenticationProvider provider, CancellationToken cancellationToken)
        {
            var result = await _request("provider/delete", Selection(provider), cancellationToken).ConfigureAwait(false);
            if (!result.TryGetValue("accountRemoved", out var removed) || !(removed is bool))
            {
                throw new AuthenticationCenterException("protocol_mismatch");
            }
            return (bool)removed;
        }

        private static Dictionary<string, object> Selection(AuthenticationProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }
            return new Dictionary<string, object> { { "providerId", provider.Id }, { "expectedVersion", provider.Version } };
        }

        public async Task<AuthenticationApplyPlan> PreflightAsync(AuthenticationProvider provider, CancellationToken cancellationToken)
        {
            var value = await _request("provider/preflight", Selection(provider), cancellationToken).ConfigureAwait(false);
            if (Text(value, "providerId") != provider.Id || Text(value, "version") != provider.Version || string.IsNullOrEmpty(Text(value, "fingerprint")))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            if (value.TryGetValue("recoveryRequired", out var recovery) && !(recovery is bool))
                throw new AuthenticationCenterException("invalid_response");
            return new AuthenticationApplyPlan
            {
                ProviderId = provider.Id, Version = provider.Version, Fingerprint = Text(value, "fingerprint"),
                RecoveryRequired = Flag(value, "recoveryRequired")
            };
        }

        public async Task<AuthenticationOperation> ApplyAsync(AuthenticationApplyPlan plan, string operationId, CancellationToken cancellationToken)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }
            var parameters = new Dictionary<string, object>
            {
                { "providerId", plan.ProviderId }, { "expectedVersion", plan.Version },
                { "expectedFingerprint", plan.Fingerprint }, { "operationId", operationId }
            };
            if (plan.RecoveryRequired)
            {
                // Explicit Enable authorizes recovery; the backend still backs
                // up the old files and checks the preflight fingerprint first.
                parameters["confirmCredentialRecovery"] = true;
            }
            return ReadOperation(await _request("provider/apply", parameters, cancellationToken).ConfigureAwait(false), operationId);
        }

        public async Task<AuthenticationOperation> GetOperationAsync(string operationId, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object> { { "operationId", operationId } };
            return ReadOperation(await _request("operation/get", parameters, cancellationToken).ConfigureAwait(false), operationId);
        }

        private static AuthenticationOperation ReadOperation(IDictionary<string, object> value, string operationId)
        {
            if (Text(value, "operationId") != operationId || string.IsNullOrEmpty(Text(value, "status")) || string.IsNullOrEmpty(Text(value, "providerId")))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            return new AuthenticationOperation { OperationId = operationId, ProviderId = Text(value, "providerId"), Status = Text(value, "status") };
        }

        private static Dictionary<string, object> Empty()
        {
            return new Dictionary<string, object>();
        }

        public async Task<AuthenticationAccountsSnapshot> ReadAccountsAsync(CancellationToken cancellationToken)
        {
            var value = await _request("account/list", Empty(), cancellationToken).ConfigureAwait(false);
            if (!value.TryGetValue("accounts", out var items) || !(items is IEnumerable sequence))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            var accounts = new List<AuthenticationAccount>();
            foreach (var item in sequence)
            {
                accounts.Add(ReadAccount(item as IDictionary<string, object>));
            }
            return new AuthenticationAccountsSnapshot
            {
                Accounts = accounts,
                SupportsBrowserLogin = _localWindows && Flag(value, "browserLoginAvailable"),
                ActiveLogin = value.TryGetValue("activeLogin", out var active) && active is IDictionary<string, object> login
                    ? ReadLogin(login, Text(login, "loginId")) : null,
                LastLogin = value.TryGetValue("lastLogin", out var last) && last is IDictionary<string, object> lastLogin
                    ? ReadLogin(lastLogin, Text(lastLogin, "loginId")) : null
            };
        }

        public async Task<AuthenticationDeviceLogin> StartBrowserLoginAsync(string targetAccountId, string loginId, CancellationToken cancellationToken)
        {
            if (!_localWindows) throw new AuthenticationCenterException("unsupported_target");
            var parameters = new Dictionary<string, object> { { "loginId", loginId }, { "method", "browser" } };
            if (!string.IsNullOrEmpty(targetAccountId)) parameters["targetAccountId"] = targetAccountId;
            return ReadLogin(await _request("account/login/start", parameters, cancellationToken).ConfigureAwait(false), loginId);
        }

        public async Task<AuthenticationDeviceLogin> StartDeviceLoginAsync(string targetAccountId, string loginId, CancellationToken cancellationToken)
        {
            var parameters = new Dictionary<string, object> { { "loginId", loginId } };
            if (!string.IsNullOrEmpty(targetAccountId))
            {
                parameters["targetAccountId"] = targetAccountId;
            }
            return ReadLogin(await _request("account/login/start", parameters, cancellationToken).ConfigureAwait(false), loginId);
        }

        public async Task<AuthenticationDeviceLogin> ReadDeviceLoginAsync(string loginId, CancellationToken cancellationToken)
        {
            return ReadLogin(await _request("account/login/get", new Dictionary<string, object> { { "loginId", loginId } }, cancellationToken).ConfigureAwait(false), loginId);
        }

        public async Task<AuthenticationDeviceLogin> CancelDeviceLoginAsync(string loginId, CancellationToken cancellationToken)
        {
            return ReadLogin(await _request("account/login/cancel", new Dictionary<string, object> { { "loginId", loginId } }, cancellationToken).ConfigureAwait(false), loginId);
        }

        public async Task DeleteAccountAsync(string accountId, CancellationToken cancellationToken)
        {
            await _request("account/delete", new Dictionary<string, object> { { "accountId", accountId } }, cancellationToken).ConfigureAwait(false);
        }

        private static AuthenticationAccount ReadAccount(IDictionary<string, object> value)
        {
            if (value == null || string.IsNullOrEmpty(Text(value, "id")))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            return new AuthenticationAccount
            {
                Id = Text(value, "id"), Email = Text(value, "email"), Label = Text(value, "label"),
                WorkspaceId = Text(value, "workspaceId"),
                IsDefault = value.TryGetValue("isDefault", out var isDefault) && isDefault is bool defaultFlag && defaultFlag,
                NeedsReauthentication = value.TryGetValue("needsReauthentication", out var needs) && needs is bool needsFlag && needsFlag
            };
        }

        private static AuthenticationDeviceLogin ReadLogin(IDictionary<string, object> value, string loginId)
        {
            var status = Text(value, "status");
            var uri = Text(value, "verificationUri");
            var method = Text(value, "method") ?? "device";
            var validUri = method == "device" ? uri == "https://auth.openai.com/codex/device"
                : method == "browser" && (MyCodex.Application.Services.AuthenticationLoginUrl.IsBrowserAuthorization(uri)
                    || (status != "pending" && string.IsNullOrEmpty(uri)));
            if (string.IsNullOrEmpty(loginId) || Text(value, "loginId") != loginId || !validUri ||
                (status != "starting" && status != "pending" && status != "committing" && status != "completed" && status != "cancelled" && status != "expired" && status != "failed"))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            return new AuthenticationDeviceLogin
            {
                Method = method, LoginId = Text(value, "loginId"), TargetAccountId = Text(value, "targetAccountId"), Status = status, UserCode = Text(value, "userCode"),
                VerificationUri = uri, ExpiresAt = Text(value, "expiresAt"), Error = Text(value, "error"),
                Account = value.TryGetValue("account", out var account) && account is IDictionary<string, object> summary ? ReadAccount(summary) : null
            };
        }

        private static AuthenticationProvider ReadProvider(IDictionary<string, object> value)
        {
            if (value == null || string.IsNullOrEmpty(Text(value, "id")) || string.IsNullOrEmpty(Text(value, "version")))
            {
                throw new AuthenticationCenterException("invalid_response");
            }
            return new AuthenticationProvider
            {
                Id = Text(value, "id"), Name = Text(value, "name"), Kind = Text(value, "kind"),
                BaseUrl = Text(value, "baseUrl"), Model = Text(value, "model"), Version = Text(value, "version"),
                PresetId = Text(value, "presetId"), PresetName = Text(value, "presetName"), AccountId = Text(value, "accountId"), AccountLabel = Text(value, "accountLabel"),
                NeedsReauthentication = Flag(value, "needsReauthentication"), Models = ReadModels(value), Advanced = ReadAdvanced(value),
                HasApiKey = value.TryGetValue("hasApiKey", out var exists) && exists is bool flag && flag,
                GlobalApplied = Flag(value, "globalApplied"), RecoveryRequired = Flag(value, "recoveryRequired")
            };
        }

        private static string Text(IDictionary<string, object> value, string key)
        {
            return value.TryGetValue(key, out var item) ? item as string : null;
        }

        private static bool Flag(IDictionary<string, object> value, string key)
        {
            return value.TryGetValue(key, out var item) && item is bool flag && flag;
        }

        private static IEnumerable<IDictionary<string, object>> Objects(IDictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var items) || items == null) yield break;
            if (!(items is IEnumerable sequence) || items is string) throw new AuthenticationCenterException("invalid_response");
            foreach (var item in sequence)
            {
                if (!(item is IDictionary<string, object> entry)) throw new AuthenticationCenterException("invalid_response");
                yield return entry;
            }
        }

        private static List<string> Strings(IDictionary<string, object> value, string key)
        {
            if (!value.TryGetValue(key, out var items) || items == null) return null;
            if (!(items is IEnumerable sequence) || items is string) throw new AuthenticationCenterException("invalid_response");
            return sequence.Cast<object>().Select(item => item as string ?? throw new AuthenticationCenterException("invalid_response")).ToList();
        }

        private static Dictionary<string, object> ReadAdvanced(IDictionary<string, object> value)
        {
            return value.TryGetValue("advanced", out var item) && item is IDictionary<string, object> advanced
                ? new Dictionary<string, object>(advanced) : new Dictionary<string, object>();
        }

        private static List<AuthenticationCatalogModel> ReadModels(IDictionary<string, object> value)
        {
            return Objects(value, "models").Select(entry => new AuthenticationCatalogModel
            {
                Model = Text(entry, "model"), DisplayName = Text(entry, "displayName"),
                ContextWindow = entry.TryGetValue("contextWindow", out var context) && context != null ? (long?)Convert.ToInt64(context) : null,
                ReasoningLevels = Strings(entry, "reasoningLevels"), DefaultReasoningLevel = Text(entry, "defaultReasoningLevel"),
                InputModalities = Strings(entry, "inputModalities"), BaseInstructions = Text(entry, "baseInstructions"),
                Metadata = new Dictionary<string, object>(entry),
                SupportsParallelToolCalls = entry.TryGetValue("supportsParallelToolCalls", out var parallel) && parallel is bool flag ? (bool?)flag : null
            }).ToList();
        }

        private static IDictionary<string, object> WriteModel(AuthenticationCatalogModel model)
        {
            var value = model.Metadata == null ? new Dictionary<string, object>() : new Dictionary<string, object>(model.Metadata);
            value["model"] = model.Model;
            value["displayName"] = model.DisplayName;
            value["contextWindow"] = model.ContextWindow;
            value["reasoningLevels"] = model.ReasoningLevels;
            value["defaultReasoningLevel"] = model.DefaultReasoningLevel;
            value["inputModalities"] = model.InputModalities;
            value["supportsParallelToolCalls"] = model.SupportsParallelToolCalls;
            value["baseInstructions"] = model.BaseInstructions;
            return value;
        }
    }
}
