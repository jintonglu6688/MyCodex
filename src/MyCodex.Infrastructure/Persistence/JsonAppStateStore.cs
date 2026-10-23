using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.AppServer;

namespace MyCodex.Infrastructure.Persistence
{
    public class JsonAppStateStore : IRuntimeConfigAppStateStore
    {
        private readonly AppConfigService _appConfigService;
        private readonly IAppConfigStore _appConfigStore;
        private readonly IRuntimeScopedConfigFileStore _runtimeScopedConfigFileStore;
        private readonly RuntimeProfileService _runtimeProfileService;
        private readonly OfficeRuntimeScopeService _officeRuntimeScopeService;
        private readonly DataContractJsonSerializer _serializer;
        private readonly object _sync = new object();
        private volatile bool _saveBlocked;

        public JsonAppStateStore(AppConfigService appConfigService)
            : this(appConfigService, null, null)
        {
        }

        public JsonAppStateStore(AppConfigService appConfigService, IAppConfigStore appConfigStore)
            : this(appConfigService, appConfigStore, null)
        {
        }

        internal JsonAppStateStore(
            AppConfigService appConfigService,
            IAppConfigStore appConfigStore,
            IRuntimeScopedConfigFileStore runtimeScopedConfigFileStore = null)
        {
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _appConfigStore = appConfigStore;
            _runtimeScopedConfigFileStore = runtimeScopedConfigFileStore ??
                new RuntimeScopedConfigFileStore(appConfigService.GetRuntimeAppStateFilePath());
            _runtimeProfileService = new RuntimeProfileService();
            _officeRuntimeScopeService = new OfficeRuntimeScopeService();
            _serializer = new DataContractJsonSerializer(typeof(AppStateSnapshot));
        }

        public AppStateSnapshot Load()
        {
            var appConfig = LoadAppConfigSnapshot();
            return Load(appConfig);
        }

        public AppStateSnapshot Load(AppConfigSnapshot appConfig)
        {
            lock (_sync)
            {
                ThrowIfSaveBlocked();
                appConfig = appConfig ?? new AppConfigSnapshot();
                string runtimeAppStatePath;
                if (_runtimeScopedConfigFileStore.TryGetRuntimeAppStateFilePathForAsyncAccess(appConfig, out runtimeAppStatePath))
                {
                    PruneLocalEndpointStateFile();
                    return LoadRuntimeEndpointState(appConfig, runtimeAppStatePath);
                }

                var filePath = _appConfigService.GetAppStateFilePath();
                return CreateLocalOnlySnapshot(LoadLocalFileWithRecovery(filePath));
            }
        }

        public void Save(AppStateSnapshot snapshot)
        {
            Save(snapshot, LoadAppConfigSnapshot());
        }

        public void Save(AppStateSnapshot snapshot, AppConfigSnapshot appConfig)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            lock (_sync)
            {
                ThrowIfSaveBlocked();

                appConfig = appConfig ?? new AppConfigSnapshot();
                string runtimeAppStatePath;
                if (_runtimeScopedConfigFileStore.TryGetRuntimeAppStateFilePathForAsyncAccess(appConfig, out runtimeAppStatePath))
                {
                    SaveRuntimeEndpointState(appConfig, runtimeAppStatePath, snapshot);
                    PruneLocalEndpointStateFile();
                    return;
                }

                var localSnapshot = CreateLocalOnlySnapshot(snapshot);
                localSnapshot = PreserveExistingLocalProjectStateForEndpointOnlySave(snapshot, localSnapshot);
                SaveLocalSnapshot(localSnapshot);
            }
        }

        private void SaveLocalSnapshot(AppStateSnapshot snapshot)
        {
            _appConfigService.EnsureConfigDirectory();
            var filePath = _appConfigService.GetAppStateFilePath();
            var serializable = CreateSerializableSnapshot(snapshot);
            AtomicFileWriter.Write(filePath, stream => _serializer.WriteObject(stream, serializable), filePath + ".bak");
        }

        private void PruneLocalEndpointStateFile()
        {
            var filePath = _appConfigService.GetAppStateFilePath();
            bool fileExists;
            var localSnapshot = CreateLocalOnlySnapshot(LoadLocalFileWithRecovery(filePath, out fileExists));
            if (!fileExists)
            {
                return;
            }

            try
            {
                SaveLocalSnapshot(localSnapshot);
            }
            catch
            {
            }
        }

        private AppStateSnapshot PreserveExistingLocalProjectStateForEndpointOnlySave(AppStateSnapshot sourceSnapshot, AppStateSnapshot localSnapshot)
        {
            if (!HasEndpointProjectState(sourceSnapshot) || HasCodingProjectState(localSnapshot))
            {
                return localSnapshot;
            }

            var filePath = _appConfigService.GetAppStateFilePath();
            var existingLocalSnapshot = CreateLocalOnlySnapshot(LoadLocalFileWithRecovery(filePath));
            if (!HasCodingProjectState(existingLocalSnapshot))
            {
                return localSnapshot;
            }

            localSnapshot.RecentWorkspaces = CloneWorkspaces(existingLocalSnapshot.RecentWorkspaces);
            localSnapshot.Sessions = CopyLocalSessions(existingLocalSnapshot.Sessions);
            localSnapshot.RuntimeProfiles = CopyLocalRuntimeProfileStates(existingLocalSnapshot.RuntimeProfiles);
            localSnapshot.OfficeRuntimeStates = CopyLocalOfficeRuntimeStates(existingLocalSnapshot.OfficeRuntimeStates);
            localSnapshot.CurrentWorkspaceId = existingLocalSnapshot.CurrentWorkspaceId;
            localSnapshot.CurrentSessionId = existingLocalSnapshot.CurrentSessionId;
            localSnapshot.CurrentCodingWorkspaceId = existingLocalSnapshot.CurrentCodingWorkspaceId;
            localSnapshot.CurrentCodingSessionId = existingLocalSnapshot.CurrentCodingSessionId;
            localSnapshot.CurrentOfficeWorkspaceId = existingLocalSnapshot.CurrentOfficeWorkspaceId;
            localSnapshot.CurrentOfficeSessionId = existingLocalSnapshot.CurrentOfficeSessionId;
            return localSnapshot;
        }

        private AppStateSnapshot LoadRuntimeEndpointState(AppConfigSnapshot appConfig, string runtimeAppStatePath)
        {
            try
            {
                string content;
                bool found;
                try
                {
                    found = _runtimeScopedConfigFileStore.ReadTextFile(appConfig, runtimeAppStatePath, out content);
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is IOException ||
                    ex is UnauthorizedAccessException || ex is TimeoutException || ex is System.ComponentModel.Win32Exception)
                {
                    throw new RuntimeStateReadException(runtimeAppStatePath, ex);
                }

                if (!found)
                {
                    Trace.TraceInformation("Runtime endpoint app state is missing. Path={0}", runtimeAppStatePath);
                    return CreateRuntimeEndpointSnapshot(new AppStateSnapshot(), appConfig);
                }

                Trace.TraceInformation("Loaded runtime endpoint app state. Path={0}", runtimeAppStatePath);
                return CreateRuntimeEndpointSnapshot(DeserializeFromString(content), appConfig);
            }
            catch (SerializationException ex)
            {
                _saveBlocked = true;
                throw new PersistenceRecoveryRequiredException(runtimeAppStatePath, null, ex, null);
            }
            catch (IOException)
            {
                _saveBlocked = true;
                throw;
            }
            catch (UnauthorizedAccessException)
            {
                _saveBlocked = true;
                throw;
            }
            catch (InvalidOperationException ex)
            {
                _saveBlocked = true;
                throw new PersistenceRecoveryRequiredException(runtimeAppStatePath, null, ex, null);
            }
        }

        private void SaveRuntimeEndpointState(AppConfigSnapshot appConfig, string runtimeAppStatePath, AppStateSnapshot snapshot)
        {
            var endpointSnapshot = CreateRuntimeEndpointSaveSnapshot(snapshot, appConfig);
            _runtimeScopedConfigFileStore.WriteTextFile(appConfig, runtimeAppStatePath, SerializeToString(endpointSnapshot));
            Trace.TraceInformation(
                "Saved runtime endpoint app state. Path={0}; Workspaces={1}; Sessions={2}",
                runtimeAppStatePath,
                endpointSnapshot.RecentWorkspaces != null ? endpointSnapshot.RecentWorkspaces.Count : 0,
                endpointSnapshot.Sessions != null ? endpointSnapshot.Sessions.Count : 0);
        }

        private AppConfigSnapshot LoadAppConfigSnapshot()
        {
            return _appConfigStore != null
                ? _appConfigStore.Load() ?? new AppConfigSnapshot()
                : new AppConfigSnapshot();
        }

        private AppStateSnapshot LoadLocalFileWithRecovery(string filePath)
        {
            bool fileExists;
            return LoadLocalFileWithRecovery(filePath, out fileExists);
        }

        private AppStateSnapshot LoadLocalFileWithRecovery(string filePath, out bool fileExists)
        {
            var found = true;
            var snapshot = PersistenceFileRecovery.Load(
                filePath,
                filePath + ".bak",
                LoadFromFile,
                () =>
                {
                    found = false;
                    return new AppStateSnapshot();
                },
                () => _saveBlocked = true);
            fileExists = found;
            return snapshot;
        }

        private AppStateSnapshot LoadFromFile(string filePath)
        {
            using (var stream = File.OpenRead(filePath))
            {
                return _serializer.ReadObject(stream) as AppStateSnapshot ?? new AppStateSnapshot();
            }
        }

        private void ThrowIfSaveBlocked()
        {
            if (_saveBlocked)
            {
                var primaryPath = _appConfigService.GetAppStateFilePath();
                throw new PersistenceRecoveryRequiredException(
                    primaryPath,
                    primaryPath + ".bak",
                    null,
                    null);
            }
        }

        private AppStateSnapshot DeserializeFromString(string content)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(content ?? string.Empty)))
            {
                var snapshot = _serializer.ReadObject(stream) as AppStateSnapshot;
                return snapshot ?? new AppStateSnapshot();
            }
        }

        private string SerializeToString(AppStateSnapshot snapshot)
        {
            using (var stream = new MemoryStream())
            {
                _serializer.WriteObject(stream, CreateSerializableSnapshot(snapshot));
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        private static AppStateSnapshot CreateSerializableSnapshot(AppStateSnapshot snapshot)
        {
            var serializableSnapshot = new AppStateSnapshot
            {
                HasCompletedThreadImportPrompt = snapshot.HasCompletedThreadImportPrompt,
                HasCompletedStartupModeOnboarding = snapshot.HasCompletedStartupModeOnboarding,
                HasCompletedOfficeCliOnboarding = snapshot.HasCompletedOfficeCliOnboarding,
                CurrentInteractionMode = snapshot.CurrentInteractionMode,
                CurrentCodingWorkspaceId = snapshot.CurrentCodingWorkspaceId,
                CurrentCodingSessionId = snapshot.CurrentCodingSessionId,
                CurrentOfficeWorkspaceId = snapshot.CurrentOfficeWorkspaceId,
                CurrentOfficeSessionId = snapshot.CurrentOfficeSessionId,
                ThreadSyncSummary = snapshot.ThreadSyncSummary,
                CurrentWorkspaceId = snapshot.CurrentWorkspaceId,
                CurrentSessionId = snapshot.CurrentSessionId,
                MainSplitterDistance = snapshot.MainSplitterDistance,
                ContentSplitterDistance = snapshot.ContentSplitterDistance,
                RightSplitterDistance = snapshot.RightSplitterDistance,
                RecentWorkspaces = new List<WorkspaceInfo>(),
                Sessions = new List<ChatSession>(),
                RuntimeProfiles = new List<RuntimeProfileStateSnapshot>(),
                OfficeRuntimeStates = new List<OfficeRuntimeStateSnapshot>()
            };

            CopyWorkspaces(snapshot.RecentWorkspaces, serializableSnapshot.RecentWorkspaces);
            CopyOfficeRuntimeStates(snapshot.OfficeRuntimeStates, serializableSnapshot.OfficeRuntimeStates);

            if (snapshot.RuntimeProfiles != null)
            {
                foreach (var profile in snapshot.RuntimeProfiles)
                {
                    if (profile == null || string.IsNullOrWhiteSpace(profile.RuntimeProfileId))
                    {
                        continue;
                    }

                    var serializableProfile = new RuntimeProfileStateSnapshot
                    {
                        RuntimeProfileId = profile.RuntimeProfileId,
                        CurrentWorkspaceId = profile.CurrentWorkspaceId,
                        CurrentSessionId = profile.CurrentSessionId,
                        RecentWorkspaces = new List<WorkspaceInfo>()
                    };
                    CopyWorkspaces(profile.RecentWorkspaces, serializableProfile.RecentWorkspaces);
                    serializableSnapshot.RuntimeProfiles.Add(serializableProfile);
                }
            }

            if (snapshot.Sessions != null)
            {
                foreach (var session in snapshot.Sessions)
                {
                    if (session == null)
                    {
                        continue;
                    }

                    var serializableSession = new ChatSession
                    {
                        Id = session.Id,
                        WorkspaceId = session.WorkspaceId,
                        Mode = session.Mode,
                        RuntimeProfileId = session.RuntimeProfileId,
                        OfficeRuntimeScopeId = session.OfficeRuntimeScopeId,
                        ThreadId = session.ThreadId,
                        Title = session.Title,
                        CreatedAt = NormalizeDateTimeForJson(session.CreatedAt),
                        LastActiveAt = NormalizeDateTimeForJson(session.LastActiveAt),
                        ApprovalMode = session.ApprovalMode,
                        IsImported = session.IsImported,
                        ThreadSource = session.ThreadSource,
                        Preview = session.Preview,
                        SessionPath = session.SessionPath,
                        ModelId = session.ModelId,
                        ModelProviderId = session.ModelProviderId,
                        ReasoningEffort = session.ReasoningEffort,
                        IsPlanModeEnabled = session.IsPlanModeEnabled,
                        ThreadGoalObjective = session.ThreadGoalObjective,
                        ThreadGoalStatus = session.ThreadGoalStatus,
                        ThreadGoalTokenBudget = session.ThreadGoalTokenBudget,
                        ThreadGoalTokensUsed = session.ThreadGoalTokensUsed,
                        ThreadGoalTimeUsedSeconds = session.ThreadGoalTimeUsedSeconds,
                        ThreadGoalCreatedAt = session.ThreadGoalCreatedAt,
                        ThreadGoalUpdatedAt = session.ThreadGoalUpdatedAt,
                        ThreadTokenUsageModelContextWindow = session.ThreadTokenUsageModelContextWindow,
                        ThreadTokenUsageTotalTokens = session.ThreadTokenUsageTotalTokens,
                        ThreadTokenUsageInputTokens = session.ThreadTokenUsageInputTokens,
                        ThreadTokenUsageCachedInputTokens = session.ThreadTokenUsageCachedInputTokens,
                        ThreadTokenUsageOutputTokens = session.ThreadTokenUsageOutputTokens,
                        ThreadTokenUsageReasoningOutputTokens = session.ThreadTokenUsageReasoningOutputTokens,
                        ThreadTokenUsageLastTotalTokens = session.ThreadTokenUsageLastTotalTokens,
                        ThreadTokenUsageLastInputTokens = session.ThreadTokenUsageLastInputTokens,
                        ThreadTokenUsageLastCachedInputTokens = session.ThreadTokenUsageLastCachedInputTokens,
                        ThreadTokenUsageLastOutputTokens = session.ThreadTokenUsageLastOutputTokens,
                        ThreadTokenUsageLastReasoningOutputTokens = session.ThreadTokenUsageLastReasoningOutputTokens,
                        IsFork = session.IsFork,
                        ForkSourceThreadId = session.ForkSourceThreadId,
                        ForkSourceTitle = session.ForkSourceTitle,
                        ForkIndex = session.ForkIndex,
                        IsReview = session.IsReview,
                        ReviewSourceThreadId = session.ReviewSourceThreadId,
                        ReviewSourceTitle = session.ReviewSourceTitle,
                        IsSubAgent = session.IsSubAgent,
                        ParentThreadId = session.ParentThreadId,
                        SubAgentDepth = session.SubAgentDepth,
                        SubAgentNickname = session.SubAgentNickname,
                        SubAgentRole = session.SubAgentRole
                    };
                    RepairSubAgentMetadataFromLocalSessionFile(serializableSession);
                    serializableSnapshot.Sessions.Add(serializableSession);
                }
            }

            return serializableSnapshot;
        }

        private AppStateSnapshot CreateLocalOnlySnapshot(AppStateSnapshot snapshot)
        {
            snapshot = snapshot ?? new AppStateSnapshot();
            var result = new AppStateSnapshot
            {
                HasCompletedThreadImportPrompt = snapshot.HasCompletedThreadImportPrompt,
                HasCompletedStartupModeOnboarding = snapshot.HasCompletedStartupModeOnboarding,
                HasCompletedOfficeCliOnboarding = snapshot.HasCompletedOfficeCliOnboarding,
                CurrentInteractionMode = snapshot.CurrentInteractionMode,
                CurrentCodingWorkspaceId = snapshot.CurrentCodingWorkspaceId,
                CurrentCodingSessionId = snapshot.CurrentCodingSessionId,
                CurrentOfficeWorkspaceId = snapshot.CurrentOfficeWorkspaceId,
                CurrentOfficeSessionId = snapshot.CurrentOfficeSessionId,
                ThreadSyncSummary = snapshot.ThreadSyncSummary,
                CurrentWorkspaceId = snapshot.CurrentWorkspaceId,
                CurrentSessionId = snapshot.CurrentSessionId,
                MainSplitterDistance = snapshot.MainSplitterDistance,
                ContentSplitterDistance = snapshot.ContentSplitterDistance,
                RightSplitterDistance = snapshot.RightSplitterDistance,
                RecentWorkspaces = CopyLocalWorkspaces(snapshot.RecentWorkspaces),
                RuntimeProfiles = CopyLocalRuntimeProfileStates(snapshot.RuntimeProfiles),
                OfficeRuntimeStates = CopyLocalOfficeRuntimeStates(snapshot.OfficeRuntimeStates),
                Sessions = CopyLocalSessions(snapshot.Sessions)
            };

            if (!ContainsWorkspace(result.RecentWorkspaces, result.CurrentWorkspaceId))
            {
                result.CurrentWorkspaceId = null;
            }

            BackfillEmptyWindowsRuntimeProfileWorkspaces(result);
            BackfillEmptyLocalTopLevelStateFromWindowsRuntimeProfile(result);

            if (!ContainsSession(result.Sessions, result.CurrentSessionId))
            {
                result.CurrentSessionId = null;
            }

            return result;
        }

        private AppStateSnapshot CreateRuntimeEndpointSnapshot(AppStateSnapshot snapshot, AppConfigSnapshot appConfig)
        {
            snapshot = snapshot ?? new AppStateSnapshot();
            var currentRuntimeProfileId = _runtimeProfileService.GetCurrentRuntimeProfileId(appConfig);
            var currentOfficeRuntimeScopeId = _officeRuntimeScopeService.ResolveTarget(appConfig).RuntimeScopeId;
            var activeProfile = FindRuntimeProfileState(snapshot.RuntimeProfiles, currentRuntimeProfileId);
            var workspaces = activeProfile != null && activeProfile.RecentWorkspaces != null && activeProfile.RecentWorkspaces.Count > 0
                ? activeProfile.RecentWorkspaces
                : snapshot.RecentWorkspaces;
            var currentWorkspaceId = activeProfile != null && !string.IsNullOrWhiteSpace(activeProfile.CurrentWorkspaceId)
                ? activeProfile.CurrentWorkspaceId
                : snapshot.CurrentWorkspaceId;
            var currentSessionId = activeProfile != null && !string.IsNullOrWhiteSpace(activeProfile.CurrentSessionId)
                ? activeProfile.CurrentSessionId
                : snapshot.CurrentSessionId;

            var result = new AppStateSnapshot
            {
                HasCompletedThreadImportPrompt = snapshot.HasCompletedThreadImportPrompt,
                HasCompletedStartupModeOnboarding = snapshot.HasCompletedStartupModeOnboarding,
                HasCompletedOfficeCliOnboarding = snapshot.HasCompletedOfficeCliOnboarding,
                CurrentInteractionMode = snapshot.CurrentInteractionMode,
                CurrentCodingWorkspaceId = snapshot.CurrentCodingWorkspaceId,
                CurrentCodingSessionId = snapshot.CurrentCodingSessionId,
                CurrentOfficeWorkspaceId = snapshot.CurrentOfficeWorkspaceId,
                CurrentOfficeSessionId = snapshot.CurrentOfficeSessionId,
                ThreadSyncSummary = snapshot.ThreadSyncSummary,
                CurrentWorkspaceId = currentWorkspaceId,
                CurrentSessionId = currentSessionId,
                MainSplitterDistance = snapshot.MainSplitterDistance,
                ContentSplitterDistance = snapshot.ContentSplitterDistance,
                RightSplitterDistance = snapshot.RightSplitterDistance,
                RecentWorkspaces = CopyEndpointWorkspaces(workspaces),
                RuntimeProfiles = new List<RuntimeProfileStateSnapshot>(),
                OfficeRuntimeStates = CopyOfficeRuntimeStatesForScope(
                    snapshot.OfficeRuntimeStates,
                    currentOfficeRuntimeScopeId),
                Sessions = CopyEndpointSessions(
                    snapshot.Sessions,
                    currentRuntimeProfileId,
                    currentOfficeRuntimeScopeId)
            };

            if (!ContainsWorkspace(result.RecentWorkspaces, result.CurrentWorkspaceId))
            {
                result.CurrentWorkspaceId = null;
            }

            if (!ContainsSession(result.Sessions, result.CurrentSessionId))
            {
                result.CurrentSessionId = null;
            }

            result.RuntimeProfiles.Add(new RuntimeProfileStateSnapshot
            {
                RuntimeProfileId = currentRuntimeProfileId,
                CurrentWorkspaceId = result.CurrentWorkspaceId,
                CurrentSessionId = result.CurrentSessionId,
                RecentWorkspaces = CloneWorkspaces(result.RecentWorkspaces)
            });
            return result;
        }

        private AppStateSnapshot CreateRuntimeEndpointSaveSnapshot(AppStateSnapshot snapshot, AppConfigSnapshot appConfig)
        {
            snapshot = snapshot ?? new AppStateSnapshot();
            var currentRuntimeProfileId = _runtimeProfileService.GetCurrentRuntimeProfileId(appConfig);
            var currentOfficeRuntimeScopeId = _officeRuntimeScopeService.ResolveTarget(appConfig).RuntimeScopeId;
            var result = new AppStateSnapshot
            {
                HasCompletedThreadImportPrompt = snapshot.HasCompletedThreadImportPrompt,
                HasCompletedStartupModeOnboarding = snapshot.HasCompletedStartupModeOnboarding,
                HasCompletedOfficeCliOnboarding = snapshot.HasCompletedOfficeCliOnboarding,
                CurrentInteractionMode = snapshot.CurrentInteractionMode,
                CurrentCodingWorkspaceId = snapshot.CurrentCodingWorkspaceId,
                CurrentCodingSessionId = snapshot.CurrentCodingSessionId,
                CurrentOfficeWorkspaceId = snapshot.CurrentOfficeWorkspaceId,
                CurrentOfficeSessionId = snapshot.CurrentOfficeSessionId,
                ThreadSyncSummary = snapshot.ThreadSyncSummary,
                CurrentWorkspaceId = snapshot.CurrentWorkspaceId,
                CurrentSessionId = snapshot.CurrentSessionId,
                MainSplitterDistance = snapshot.MainSplitterDistance,
                ContentSplitterDistance = snapshot.ContentSplitterDistance,
                RightSplitterDistance = snapshot.RightSplitterDistance,
                RecentWorkspaces = CopyEndpointWorkspaces(snapshot.RecentWorkspaces),
                RuntimeProfiles = new List<RuntimeProfileStateSnapshot>(),
                OfficeRuntimeStates = CopyOfficeRuntimeStatesForScope(
                    snapshot.OfficeRuntimeStates,
                    currentOfficeRuntimeScopeId),
                Sessions = CopyCurrentRuntimeSessionsForEndpoint(
                    snapshot.Sessions,
                    currentRuntimeProfileId,
                    currentOfficeRuntimeScopeId)
            };

            if (!ContainsWorkspace(result.RecentWorkspaces, result.CurrentWorkspaceId))
            {
                result.CurrentWorkspaceId = null;
            }

            if (!ContainsSession(result.Sessions, result.CurrentSessionId))
            {
                result.CurrentSessionId = null;
            }

            return result;
        }

        private List<RuntimeProfileStateSnapshot> CopyLocalRuntimeProfileStates(IEnumerable<RuntimeProfileStateSnapshot> profiles)
        {
            var result = new List<RuntimeProfileStateSnapshot>();
            if (profiles == null)
            {
                return result;
            }

            foreach (var profile in profiles)
            {
                if (profile == null ||
                    string.IsNullOrWhiteSpace(profile.RuntimeProfileId) ||
                    IsEndpointRuntimeProfileId(profile.RuntimeProfileId))
                {
                    continue;
                }

                var recentWorkspaces = CopyLocalWorkspaces(profile.RecentWorkspaces);
                result.Add(new RuntimeProfileStateSnapshot
                {
                    RuntimeProfileId = profile.RuntimeProfileId,
                    CurrentWorkspaceId = ContainsWorkspace(recentWorkspaces, profile.CurrentWorkspaceId)
                        ? profile.CurrentWorkspaceId
                        : null,
                    CurrentSessionId = profile.CurrentSessionId,
                    RecentWorkspaces = recentWorkspaces
                });
            }

            return result;
        }

        private static List<OfficeRuntimeStateSnapshot> CopyLocalOfficeRuntimeStates(
            IEnumerable<OfficeRuntimeStateSnapshot> states)
        {
            var result = new List<OfficeRuntimeStateSnapshot>();
            if (states == null)
            {
                return result;
            }

            foreach (var state in states)
            {
                if (state == null || !IsLocalOfficeRuntimeScopeId(state.RuntimeScopeId))
                {
                    continue;
                }

                result.Add(CloneOfficeRuntimeState(state));
            }

            return result;
        }

        private static List<OfficeRuntimeStateSnapshot> CopyOfficeRuntimeStatesForScope(
            IEnumerable<OfficeRuntimeStateSnapshot> states,
            string runtimeScopeId)
        {
            var result = new List<OfficeRuntimeStateSnapshot>();
            if (states == null || string.IsNullOrWhiteSpace(runtimeScopeId))
            {
                return result;
            }

            foreach (var state in states)
            {
                if (state != null && string.Equals(
                    state.RuntimeScopeId,
                    runtimeScopeId,
                    StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(CloneOfficeRuntimeState(state));
                    break;
                }
            }

            return result;
        }

        private static void BackfillEmptyWindowsRuntimeProfileWorkspaces(AppStateSnapshot snapshot)
        {
            if (snapshot == null ||
                snapshot.RecentWorkspaces == null ||
                snapshot.RecentWorkspaces.Count == 0 ||
                snapshot.RuntimeProfiles == null)
            {
                return;
            }

            foreach (var profile in snapshot.RuntimeProfiles)
            {
                if (profile == null ||
                    !string.Equals(profile.RuntimeProfileId, RuntimeProfileService.WindowsRuntimeProfileId, StringComparison.OrdinalIgnoreCase) ||
                    (profile.RecentWorkspaces != null && profile.RecentWorkspaces.Count > 0))
                {
                    continue;
                }

                profile.RecentWorkspaces = CopyCodingWorkspaces(snapshot.RecentWorkspaces);
                if (profile.RecentWorkspaces.Count == 0)
                {
                    continue;
                }

                if (!ContainsWorkspace(profile.RecentWorkspaces, profile.CurrentWorkspaceId))
                {
                    profile.CurrentWorkspaceId = ContainsWorkspace(profile.RecentWorkspaces, snapshot.CurrentWorkspaceId)
                        ? snapshot.CurrentWorkspaceId
                        : profile.RecentWorkspaces[0].Id;
                }
            }
        }

        private static void BackfillEmptyLocalTopLevelStateFromWindowsRuntimeProfile(AppStateSnapshot snapshot)
        {
            if (snapshot == null ||
                snapshot.RuntimeProfiles == null ||
                (snapshot.RecentWorkspaces != null && snapshot.RecentWorkspaces.Count > 0))
            {
                return;
            }

            var windowsProfile = FindRuntimeProfileState(snapshot.RuntimeProfiles, RuntimeProfileService.WindowsRuntimeProfileId);
            if (windowsProfile == null ||
                windowsProfile.RecentWorkspaces == null ||
                windowsProfile.RecentWorkspaces.Count == 0)
            {
                return;
            }

            snapshot.RecentWorkspaces = CloneWorkspaces(windowsProfile.RecentWorkspaces);
            if (!ContainsWorkspace(snapshot.RecentWorkspaces, snapshot.CurrentWorkspaceId))
            {
                snapshot.CurrentWorkspaceId = ContainsWorkspace(snapshot.RecentWorkspaces, windowsProfile.CurrentWorkspaceId)
                    ? windowsProfile.CurrentWorkspaceId
                    : snapshot.RecentWorkspaces[0].Id;
            }

            if (!ContainsSession(snapshot.Sessions, snapshot.CurrentSessionId) &&
                ContainsSession(snapshot.Sessions, windowsProfile.CurrentSessionId))
            {
                snapshot.CurrentSessionId = windowsProfile.CurrentSessionId;
            }
        }

        private static List<WorkspaceInfo> CopyLocalWorkspaces(IEnumerable<WorkspaceInfo> workspaces)
        {
            var result = new List<WorkspaceInfo>();
            if (workspaces == null)
            {
                return result;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace == null || LooksLikeEndpointPath(workspace.RootPath))
                {
                    continue;
                }

                result.Add(CloneWorkspace(workspace));
            }

            return result;
        }

        private static List<WorkspaceInfo> CopyCodingWorkspaces(IEnumerable<WorkspaceInfo> workspaces)
        {
            var result = new List<WorkspaceInfo>();
            if (workspaces == null)
            {
                return result;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace != null && workspace.Kind == WorkspaceKind.Coding)
                {
                    result.Add(CloneWorkspace(workspace));
                }
            }

            return result;
        }

        private static List<WorkspaceInfo> CopyEndpointWorkspaces(IEnumerable<WorkspaceInfo> workspaces)
        {
            var result = new List<WorkspaceInfo>();
            if (workspaces == null)
            {
                return result;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace == null || !LooksLikeEndpointPath(workspace.RootPath))
                {
                    continue;
                }

                result.Add(CloneWorkspace(workspace));
            }

            return result;
        }

        private static List<ChatSession> CopyLocalSessions(IEnumerable<ChatSession> sessions)
        {
            var result = new List<ChatSession>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == AppInteractionMode.Office &&
                    !string.IsNullOrWhiteSpace(session.OfficeRuntimeScopeId))
                {
                    if (IsLocalOfficeRuntimeScopeId(session.OfficeRuntimeScopeId))
                    {
                        result.Add(CloneSession(session, session.RuntimeProfileId));
                    }

                    continue;
                }

                if (session == null ||
                    IsEndpointRuntimeProfileId(session.RuntimeProfileId) ||
                    LooksLikeEndpointPath(session.WorkspaceId) ||
                    LooksLikeEndpointPath(session.SessionPath))
                {
                    continue;
                }

                result.Add(CloneSession(session, session.RuntimeProfileId));
            }

            return result;
        }

        private static List<ChatSession> CopyEndpointSessions(
            IEnumerable<ChatSession> sessions,
            string currentRuntimeProfileId,
            string currentOfficeRuntimeScopeId)
        {
            var result = new List<ChatSession>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == AppInteractionMode.Office &&
                    !string.IsNullOrWhiteSpace(session.OfficeRuntimeScopeId))
                {
                    if (string.Equals(
                        session.OfficeRuntimeScopeId,
                        currentOfficeRuntimeScopeId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(CloneSession(session, currentRuntimeProfileId));
                    }

                    continue;
                }

                if (session == null || IsLocalRuntimeSession(session))
                {
                    continue;
                }

                result.Add(CloneSession(session, currentRuntimeProfileId));
            }

            return result;
        }

        private List<ChatSession> CopyCurrentRuntimeSessionsForEndpoint(
            IEnumerable<ChatSession> sessions,
            string currentRuntimeProfileId,
            string currentOfficeRuntimeScopeId)
        {
            var result = new List<ChatSession>();
            if (sessions == null)
            {
                return result;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == AppInteractionMode.Office &&
                    !string.IsNullOrWhiteSpace(session.OfficeRuntimeScopeId))
                {
                    if (string.Equals(
                        session.OfficeRuntimeScopeId,
                        currentOfficeRuntimeScopeId,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(CloneSession(session, null));
                    }

                    continue;
                }

                if (session == null ||
                    !_runtimeProfileService.IsSameRuntimeProfile(session.RuntimeProfileId, currentRuntimeProfileId))
                {
                    continue;
                }

                result.Add(CloneSession(session, null));
            }

            return result;
        }

        private static bool IsLocalOfficeRuntimeScopeId(string runtimeScopeId)
        {
            if (string.IsNullOrWhiteSpace(runtimeScopeId))
            {
                return false;
            }

            var normalized = runtimeScopeId.Trim();
            return string.Equals(normalized, "windows", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("wsl|", StringComparison.OrdinalIgnoreCase);
        }

        private static RuntimeProfileStateSnapshot FindRuntimeProfileState(IEnumerable<RuntimeProfileStateSnapshot> profiles, string runtimeProfileId)
        {
            if (profiles == null || string.IsNullOrWhiteSpace(runtimeProfileId))
            {
                return null;
            }

            foreach (var profile in profiles)
            {
                if (profile != null &&
                    string.Equals(profile.RuntimeProfileId, runtimeProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        private static bool IsEndpointRuntimeProfileId(string runtimeProfileId)
        {
            if (string.IsNullOrWhiteSpace(runtimeProfileId))
            {
                return false;
            }

            var normalized = runtimeProfileId.Trim();
            return normalized.StartsWith("remote-unix|", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("wsl|", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLocalRuntimeSession(ChatSession session)
        {
            return session != null &&
                !string.IsNullOrWhiteSpace(session.RuntimeProfileId) &&
                !IsEndpointRuntimeProfileId(session.RuntimeProfileId) &&
                !LooksLikeEndpointPath(session.WorkspaceId) &&
                !LooksLikeEndpointPath(session.SessionPath);
        }

        private static bool HasCodingProjectState(AppStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            if (HasAnyCodingWorkspace(snapshot.RecentWorkspaces) ||
                HasAnyCodingSession(snapshot.Sessions))
            {
                return true;
            }

            if (snapshot.RuntimeProfiles == null)
            {
                return false;
            }

            foreach (var profile in snapshot.RuntimeProfiles)
            {
                if (profile == null)
                {
                    continue;
                }

                if (HasAnyCodingWorkspace(profile.RecentWorkspaces))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEndpointProjectState(AppStateSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return false;
            }

            if (HasEndpointWorkspace(snapshot.RecentWorkspaces) ||
                HasEndpointSession(snapshot.Sessions) ||
                HasEndpointOfficeRuntimeState(snapshot.OfficeRuntimeStates))
            {
                return true;
            }

            if (snapshot.RuntimeProfiles == null)
            {
                return false;
            }

            foreach (var profile in snapshot.RuntimeProfiles)
            {
                if (profile == null)
                {
                    continue;
                }

                if (IsEndpointRuntimeProfileId(profile.RuntimeProfileId) ||
                    HasEndpointWorkspace(profile.RecentWorkspaces))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyCodingWorkspace(IEnumerable<WorkspaceInfo> workspaces)
        {
            if (workspaces == null)
            {
                return false;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace != null &&
                    workspace.Kind == WorkspaceKind.Coding &&
                    !string.IsNullOrWhiteSpace(workspace.Id))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnyCodingSession(IEnumerable<ChatSession> sessions)
        {
            if (sessions == null)
            {
                return false;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    session.Mode == AppInteractionMode.Coding &&
                    !string.IsNullOrWhiteSpace(session.Id))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEndpointOfficeRuntimeState(IEnumerable<OfficeRuntimeStateSnapshot> states)
        {
            if (states == null)
            {
                return false;
            }

            foreach (var state in states)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.RuntimeScopeId))
                {
                    continue;
                }

                var scopeId = state.RuntimeScopeId.Trim();
                if (scopeId.StartsWith("wsl|", StringComparison.OrdinalIgnoreCase) ||
                    scopeId.StartsWith("remote-unix|", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEndpointWorkspace(IEnumerable<WorkspaceInfo> workspaces)
        {
            if (workspaces == null)
            {
                return false;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace != null && LooksLikeEndpointPath(workspace.RootPath))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasEndpointSession(IEnumerable<ChatSession> sessions)
        {
            if (sessions == null)
            {
                return false;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    (IsEndpointRuntimeProfileId(session.RuntimeProfileId) ||
                     LooksLikeEndpointPath(session.WorkspaceId) ||
                     LooksLikeEndpointPath(session.SessionPath)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeEndpointPath(string path)
        {
            return WorkspacePathUtilities.LooksLikeUnixAbsolutePath(path);
        }

        private static bool ContainsWorkspace(IEnumerable<WorkspaceInfo> workspaces, string workspaceId)
        {
            if (string.IsNullOrWhiteSpace(workspaceId) || workspaces == null)
            {
                return false;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace != null &&
                    string.Equals(workspace.Id, workspaceId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSession(IEnumerable<ChatSession> sessions, string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId) || sessions == null)
            {
                return false;
            }

            foreach (var session in sessions)
            {
                if (session != null &&
                    string.Equals(session.Id, sessionId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<WorkspaceInfo> CloneWorkspaces(IEnumerable<WorkspaceInfo> workspaces)
        {
            var result = new List<WorkspaceInfo>();
            if (workspaces == null)
            {
                return result;
            }

            foreach (var workspace in workspaces)
            {
                if (workspace != null)
                {
                    result.Add(CloneWorkspace(workspace));
                }
            }

            return result;
        }

        private static OfficeRuntimeStateSnapshot CloneOfficeRuntimeState(OfficeRuntimeStateSnapshot state)
        {
            return new OfficeRuntimeStateSnapshot
            {
                RuntimeScopeId = state.RuntimeScopeId,
                CurrentWorkspaceId = state.CurrentWorkspaceId,
                CurrentSessionId = state.CurrentSessionId,
                RecentWorkspaces = CloneWorkspaces(state.RecentWorkspaces)
            };
        }

        private static WorkspaceInfo CloneWorkspace(WorkspaceInfo workspace)
        {
            return new WorkspaceInfo
            {
                Id = workspace.Id,
                Name = workspace.Name,
                RootPath = workspace.RootPath,
                GitBranch = workspace.GitBranch,
                Kind = workspace.Kind,
                LastOpenedAt = workspace.LastOpenedAt
            };
        }

        private static ChatSession CloneSession(ChatSession session, string runtimeProfileId)
        {
            var clone = new ChatSession
            {
                Id = session.Id,
                WorkspaceId = session.WorkspaceId,
                Mode = session.Mode,
                RuntimeProfileId = runtimeProfileId,
                OfficeRuntimeScopeId = session.OfficeRuntimeScopeId,
                ThreadId = session.ThreadId,
                Title = session.Title,
                CreatedAt = session.CreatedAt,
                LastActiveAt = session.LastActiveAt,
                ApprovalMode = session.ApprovalMode,
                IsImported = session.IsImported,
                ThreadSource = session.ThreadSource,
                Preview = session.Preview,
                SessionPath = session.SessionPath,
                ModelId = session.ModelId,
                ModelProviderId = session.ModelProviderId,
                ReasoningEffort = session.ReasoningEffort,
                IsPlanModeEnabled = session.IsPlanModeEnabled,
                ThreadGoalObjective = session.ThreadGoalObjective,
                ThreadGoalStatus = session.ThreadGoalStatus,
                ThreadGoalTokenBudget = session.ThreadGoalTokenBudget,
                ThreadGoalTokensUsed = session.ThreadGoalTokensUsed,
                ThreadGoalTimeUsedSeconds = session.ThreadGoalTimeUsedSeconds,
                ThreadGoalCreatedAt = session.ThreadGoalCreatedAt,
                ThreadGoalUpdatedAt = session.ThreadGoalUpdatedAt,
                ThreadTokenUsageModelContextWindow = session.ThreadTokenUsageModelContextWindow,
                ThreadTokenUsageTotalTokens = session.ThreadTokenUsageTotalTokens,
                ThreadTokenUsageInputTokens = session.ThreadTokenUsageInputTokens,
                ThreadTokenUsageCachedInputTokens = session.ThreadTokenUsageCachedInputTokens,
                ThreadTokenUsageOutputTokens = session.ThreadTokenUsageOutputTokens,
                ThreadTokenUsageReasoningOutputTokens = session.ThreadTokenUsageReasoningOutputTokens,
                ThreadTokenUsageLastTotalTokens = session.ThreadTokenUsageLastTotalTokens,
                ThreadTokenUsageLastInputTokens = session.ThreadTokenUsageLastInputTokens,
                ThreadTokenUsageLastCachedInputTokens = session.ThreadTokenUsageLastCachedInputTokens,
                ThreadTokenUsageLastOutputTokens = session.ThreadTokenUsageLastOutputTokens,
                ThreadTokenUsageLastReasoningOutputTokens = session.ThreadTokenUsageLastReasoningOutputTokens,
                IsFork = session.IsFork,
                ForkSourceThreadId = session.ForkSourceThreadId,
                ForkSourceTitle = session.ForkSourceTitle,
                ForkIndex = session.ForkIndex,
                IsReview = session.IsReview,
                ReviewSourceThreadId = session.ReviewSourceThreadId,
                ReviewSourceTitle = session.ReviewSourceTitle,
                IsSubAgent = session.IsSubAgent,
                ParentThreadId = session.ParentThreadId,
                SubAgentDepth = session.SubAgentDepth,
                SubAgentNickname = session.SubAgentNickname,
                SubAgentRole = session.SubAgentRole
            };
            RepairSubAgentMetadataFromLocalSessionFile(clone);
            return clone;
        }

        private static void RepairSubAgentMetadataFromLocalSessionFile(ChatSession session)
        {
            if (session == null ||
                string.IsNullOrWhiteSpace(session.SessionPath) ||
                (!string.Equals(session.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase) &&
                 !session.IsSubAgent &&
                 string.IsNullOrWhiteSpace(session.ParentThreadId)))
            {
                return;
            }

            var summary = LocalSessionCatalog.TryReadLocalThreadSummary(session.SessionPath);
            if (summary == null ||
                (!string.IsNullOrWhiteSpace(session.ThreadId) &&
                 !string.Equals(session.ThreadId, summary.ThreadId, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            if (string.Equals(summary.ThreadSource, "subagent", StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(summary.ParentThreadId))
            {
                session.IsSubAgent = true;
            }

            if (string.IsNullOrWhiteSpace(session.ThreadSource) && !string.IsNullOrWhiteSpace(summary.ThreadSource))
            {
                session.ThreadSource = summary.ThreadSource;
            }

            if (string.IsNullOrWhiteSpace(session.ParentThreadId) && !string.IsNullOrWhiteSpace(summary.ParentThreadId))
            {
                session.ParentThreadId = summary.ParentThreadId;
            }

            if (!session.SubAgentDepth.HasValue && summary.SubagentDepth.HasValue)
            {
                session.SubAgentDepth = summary.SubagentDepth;
            }

            if (string.IsNullOrWhiteSpace(session.SubAgentNickname) && !string.IsNullOrWhiteSpace(summary.AgentNickname))
            {
                session.SubAgentNickname = summary.AgentNickname;
            }

            if (string.IsNullOrWhiteSpace(session.SubAgentRole) && !string.IsNullOrWhiteSpace(summary.AgentRole))
            {
                session.SubAgentRole = summary.AgentRole;
            }
        }

        private static void CopyWorkspaces(IEnumerable<WorkspaceInfo> source, ICollection<WorkspaceInfo> target)
        {
            if (source == null || target == null)
            {
                return;
            }

            foreach (var workspace in source)
            {
                if (workspace == null)
                {
                    continue;
                }

                target.Add(new WorkspaceInfo
                {
                    Id = workspace.Id,
                    Name = workspace.Name,
                    RootPath = workspace.RootPath,
                    GitBranch = workspace.GitBranch,
                    Kind = workspace.Kind,
                    LastOpenedAt = NormalizeDateTimeForJson(workspace.LastOpenedAt)
                });
            }
        }

        private static void CopyOfficeRuntimeStates(
            IEnumerable<OfficeRuntimeStateSnapshot> source,
            ICollection<OfficeRuntimeStateSnapshot> target)
        {
            if (source == null || target == null)
            {
                return;
            }

            foreach (var state in source)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.RuntimeScopeId))
                {
                    continue;
                }

                var serializableState = new OfficeRuntimeStateSnapshot
                {
                    RuntimeScopeId = state.RuntimeScopeId,
                    CurrentWorkspaceId = state.CurrentWorkspaceId,
                    CurrentSessionId = state.CurrentSessionId,
                    RecentWorkspaces = new List<WorkspaceInfo>()
                };
                CopyWorkspaces(state.RecentWorkspaces, serializableState.RecentWorkspaces);
                target.Add(serializableState);
            }
        }

        private static DateTime NormalizeDateTimeForJson(DateTime value)
        {
            return value == default(DateTime)
                ? DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)
                : value;
        }
    }
}
