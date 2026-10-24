using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.CodingRemote;
using MyCodex.Application.Capabilities;
using MyCodex.Application.Interfaces;
using MyCodex.Application.InternalTools;
using MyCodex.Application.RemoteRelay;
using MyCodex.Application.Services;
using MyCodex.Domain.Enums;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.Infrastructure.Markets;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.RemoteRelay;
using MyCodex.Infrastructure.Updates;
using MyCodex.Shared.Constants;
using MyCodex.Shared.Helpers;
using MyCodex.WinForms;
using MyCodex.WinForms.Controls;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.RemoteHost;
using MyCodex.WinForms.Skills;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms.Forms
{
    public partial class MainForm : Form
    {
        private const int AutomaticThreadSyncIntervalMilliseconds = 30000;
        private const int AutomaticThreadSyncFollowUpAttempts = 6;
        private const string UnboundComposeDraftSessionId = "__mycodex-unbound-compose__";
        private const int AutomaticThreadSyncFollowUpFirstDelayMilliseconds = 2000;
        private const int AutomaticThreadSyncFollowUpRetryDelayMilliseconds = 5000;
        private const int LocalThreadSessionWatchDebounceMilliseconds = 750;
        private const int LocalSubAgentThreadSyncIntervalMilliseconds = 3000;
        private const int MaxRecentNotificationEventIds = 256;
        private const int TurnCompletedNotificationThrottleMilliseconds = 1000;
        private const int HigherPriorityStatusHoldMilliseconds = 15000;
        private const int LogFlushIntervalMilliseconds = 100;
        private const int MaxVisibleLogCharacters = 200000;
        private const int TargetVisibleLogCharacters = 160000;
        private const int MaxBufferedCommandOutputCharacters = 131072;
        private const int MaxBufferedCommandOutputEvents = 512;
        private const int MaxBufferedCommandOutputEventsPerUiPass = 64;
        private const int MaxBufferedTurnSummaryEvents = 128;
        private const int RealtimeSessionHistoryReloadDelayMilliseconds = 300;
        private const int WorkspaceAvailabilityCacheMilliseconds = 10000;
        private static readonly TimeSpan ExitScreenReaderStopBudget = TimeSpan.FromMilliseconds(500);
        private const string AppServerEventDiagnosticsEnvironmentVariable = "MYCODEX_APP_SERVER_EVENT_DIAGNOSTICS";
        private const string ApprovalSoundFileName = "Approval.wav";
        private const string TurnCompletedSoundFileName = "End of session.wav";
        private const string TurnFailedSoundFileName = "Error.wav";
        private const string SubAgentTurnCompletedSoundFileName = "Subagent completed.wav";
        private const string MessageSentSoundFileName = "MsgSent.wav";
        private readonly IAppServerClient _appServerClient;
        private readonly IAppStateStore _appStateStore;
        private AppStateSnapshot _appStateSnapshot;
        private readonly IAppConfigStore _appConfigStore;
        private readonly AppConfigService _appConfigService;
        private readonly IScreenReaderService _screenReaderService;
        private readonly CodexRuntimeManager _codexRuntimeManager;
        private readonly IGitHostingProviderService _gitHostingProviderService;
        private readonly IPluginMarketCatalogClient _pluginMarketCatalogClient;
        private readonly IInternalToolRegistry _internalToolRegistry;
        private readonly IAutomationStore _automationStore;
        private readonly IAutomationService _automationService;
        private readonly IAutomationRunner _automationRunner;
        private readonly AutomationRuntimeHost _automationRuntimeHost;
        private readonly string _appDisplayName;
        private readonly WorkspaceService _workspaceService;
        private readonly OfficeWorkspaceService _officeWorkspaceService;
        private readonly ProjectInstructionsService _projectInstructionsService;
        private readonly IWslFileSystemBridge _wslFileSystemBridge;
        private readonly IRemoteMacFileSystemBridge _remoteMacFileSystemBridge;
        private readonly SessionService _sessionService;
        private readonly RuntimeProfileService _runtimeProfileService;
        private readonly SessionDefaultsService _sessionDefaultsService;
        private readonly JsonModeSettingsStore _modeSettingsStore;
        private readonly PersistedSessionService _persistedSessionService;
        private readonly SessionThreadService _sessionThreadService;
        private readonly IThreadHistoryRepairService _threadHistoryRepairService;
        private string _validatedRuntimeEndpointScopeKey;
        private readonly List<AppServerModelOption> _availableModels;
        private string _availableModelsScopeKey;
        private readonly List<WorkspaceInfo> _recentWorkspaces;
        private readonly List<ChatSession> _sessions;
        private readonly List<ChatSession> _inactiveRuntimeSessions;
        private readonly List<ChatHistoryItem> _allChatHistoryItems;
        private readonly List<CommandTask> _commandTasks;
        private readonly List<RuntimeFileBrowserForm> _runtimeFileBrowserForms;
        private readonly AppServerFileWindowManager _appServerFileWindowManager;
        private readonly SessionHistoryStateStore _sessionHistoryStateStore;
        private readonly SessionHistoryDisplayBuilder _sessionHistoryDisplayBuilder;
        private readonly SessionHistoryLoadWorkflow _sessionHistoryLoadWorkflow;
        private readonly SessionThreadOperationWorkflow _sessionThreadOperationWorkflow;
        private readonly ArchivedThreadRestoreWorkflow _archivedThreadRestoreWorkflow;
        private readonly AutomaticThreadSyncWorkflow _automaticThreadSyncWorkflow;
        private readonly AutomaticThreadSyncRunCoordinator _automaticThreadSyncRunCoordinator;
        private readonly AutomaticThreadSyncFollowUpTracker _automaticThreadSyncFollowUpTracker;
        private readonly List<FileSystemWatcher> _localThreadSessionWatchers;
        private readonly IRelayHostRealtimeClient _remoteHostRealtimeClient;
        private readonly RemoteHostServiceController _remoteHostServiceController;
        private readonly IEmbeddedRelayService _embeddedRelayService;
        private readonly IRemoteIdentityStore _remoteIdentityStore;
        private readonly IRemoteBindingStore _remoteBindingStore;
        private RemoteAppServerEventForwarder _remoteAppServerEventForwarder;
        private readonly RemoteInteractionDialogRegistry _remoteInteractionDialogs;
        private readonly Dictionary<string, TurnProgressSummaryState> _threadTurnSummaryStates;
        private readonly Dictionary<string, ActivityTimelineState> _threadActivityTimelineStates;
        private readonly Dictionary<string, List<Action<IReadOnlyList<string>>>> _automationRunActivitySubscribers;
        private readonly Dictionary<string, ReasoningSummaryMessageState> _reasoningSummaryMessageStates;
        private readonly Dictionary<string, PendingExecutablePlan> _pendingExecutablePlans;
        private readonly Dictionary<string, PendingRemotePlanExecutionPrompt> _pendingRemotePlanExecutionPrompts;
        private readonly Dictionary<string, PendingInvalidEncryptedContentRetryContext> _pendingInvalidEncryptedContentRetries;
        private readonly Dictionary<string, int> _realtimeSessionHistoryReloadVersions;
        private readonly HashSet<string> _localDesktopRealtimeTurnThreadIds;
        private readonly HashSet<string> _busyThreadIds;
        private readonly HashSet<string> _sessionsPendingDefaultCollaborationMode;
        private readonly HashSet<string> _projectInstructionsHelperThreadIds;
        private readonly InvalidEncryptedContentFailureState _invalidEncryptedContentFailureState;
        private bool _isLocalSubAgentThreadSyncRunning;
        private readonly ThreadOperationStateTracker _threadOperationStateTracker;
        private readonly ComposeDraftStore _composeDraftStore;
        private readonly List<PendingAttachmentItem> _pendingAttachments;
        private bool _isUpdateCheckInProgress;
        private bool _hasAutomaticUpdateCheckRun;
        private bool _isOfficeCliOnboardingPromptActive;
        private readonly Dictionary<string, Tuple<bool, DateTime>> _workspaceAvailabilityCache;
        private readonly Dictionary<string, WorkspaceGitInfoSnapshot> _workspaceGitInfoCache;
        private readonly Dictionary<string, SubAgentWorkbenchForm> _subAgentWorkbenches;
        private readonly BufferedDispatchState<AppServerEventArgs> _bufferedCommandOutputState;
        private readonly CoalescingBufferedDispatchState<AppServerEventArgs> _bufferedTurnSummaryEventState;
        private readonly VisibleLogBuffer _visibleLogState;
        private readonly VisibleLogBuffer _recordedLogState;
        private readonly ActivityDisplayRefreshState _activityDisplayRefreshState;
        private readonly ListView _workspaceList;
        private readonly ListView _sessionList;
        private readonly ChatHistoryListBox _chatHistoryListBox;
        private readonly ListBox _pendingAttachmentListBox;
        private readonly AttachmentComposerTextBox _inputBox;
        private readonly ActivityVirtualBrowseControl _activityBrowseControl;
        private readonly Label _activityCurrentStatusLabel;
        private readonly Button _commandMenuButton;
        private readonly Button _attachmentButton;
        private readonly Button _sendButton;
        private readonly Button _stopButton;
        private readonly Button _openWorkspaceButton;
        private readonly Button _newSessionButton;
        private readonly StatusStrip _statusStrip;
        private readonly MainMenu _mainMenu;
        private readonly NotifyIcon _trayIcon;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly ToolStripStatusLabel _statusIndicator;
        private readonly MenuItem _fileMenuItem;
        private readonly MenuItem _gitMenuItem;
        private readonly MenuItem _accountMenuItem;
        private readonly MenuItem _helpMenuItem;
        private readonly MenuItem _openWorkspaceFileMenuItem;
        private readonly MenuItem _openWslWorkspaceFileMenuItem;
        private readonly MenuItem _openGlobalFileBrowserMenuItem;
        private readonly MenuItem _newSessionFileMenuItem;
        private readonly MenuItem _automationsMenuItem;
        private readonly MenuItem _targetReloadMenuItem;
        private readonly MenuItem _exitMenuItem;
        private readonly MenuItem _gitWorkbenchMenuItem;
        private readonly MenuItem _gitCloneMenuItem;
        private readonly MenuItem _gitRemoteRepositoriesMenuItem;
        private readonly MenuItem _gitMarketMenuItem;
        private readonly MenuItem _gitEnvironmentMenuItem;
        private readonly MenuItem _gitRemoteCredentialsMenuItem;
        private readonly MenuItem _userCenterMenuItem;
        private readonly MenuItem _userManualMenuItem;
        private readonly MenuItem _officialWebsiteMenuItem;
        private readonly MenuItem _checkUpdatesMenuItem;
        private readonly MenuItem _openLogDirectoryMenuItem;
        private readonly MenuItem _donateMenuItem;
        private readonly MenuItem _aboutMenuItem;
        private readonly MenuItem _planModeMenuItem;
        private readonly MenuItem _goalMenuItem;
        private readonly MenuItem _fileImportSyncMenuItem;
        private readonly MenuItem _fileSyncThreadsMenuItem;
        private readonly MenuItem _fileImportOtherSessionsMenuItem;
        private readonly MenuItem _fileExternalAgentConfigImportMenuItem;
        private readonly Label _workspaceNameValueLabel;
        private readonly Label _workspacePathValueLabel;
        private readonly Label _branchValueLabel;
        private Label _workspaceSectionTitleLabel;
        private Label _sessionSectionTitleLabel;
        private Label _inputSectionTitleLabel;
        private Label _pendingAttachmentsSectionTitleLabel;
        private Label _repositoryActionsSectionTitleLabel;
        private readonly Panel _composePanel;
        private readonly Panel _attachmentPanel;
        private readonly ContextMenu _chatHistoryContextMenu;
        private readonly ContextMenu _attachmentMenu;
        private readonly ContextMenu _pendingAttachmentContextMenu;
        private readonly ContextMenu _logContextMenu;
        private readonly ContextMenu _workspaceContextMenu;
        private readonly ContextMenu _sessionContextMenu;
        private readonly ContextMenu _trayContextMenu;
        private readonly MenuItem _enableAutoReadMenuItem;
        private readonly MenuItem _copySelectedMenuItem;
        private readonly MenuItem _forkMenuItem;
        private readonly MenuItem _forkFullMenuItem;
        private readonly MenuItem _forkFromHereMenuItem;
        private readonly MenuItem _findChatMessageContextMenuItem;
        private readonly MenuItem _compactChatHistoryMenuItem;
        private readonly MenuItem _reviewChatHistoryMenuItem;
        private readonly MenuItem _selectImageAttachmentMenuItem;
        private readonly MenuItem _selectFileAttachmentMenuItem;
        private readonly MenuItem _selectFolderAttachmentMenuItem;
        private readonly MenuItem _selectSkillAttachmentMenuItem;
        private readonly MenuItem _selectPluginAttachmentMenuItem;
        private readonly MenuItem _removeSelectedAttachmentMenuItem;
        private readonly MenuItem _clearAttachmentsMenuItem;
        private readonly MenuItem _activitySettingsMenuItem;
        private readonly MenuItem _clearActivityMenuItem;
        private readonly MenuItem _showActivityLogMenuItem;
        private readonly MenuItem _clearLogMenuItem;
        private readonly MenuItem _showMainWindowTrayMenuItem;
        private readonly MenuItem _hideMainWindowTrayMenuItem;
        private readonly MenuItem _exitTrayMenuItem;
        private readonly MenuItem _workspaceNewMenuItem;
        private readonly MenuItem _workspaceOpenWslMenuItem;
        private readonly MenuItem _workspaceAppServerFilesMenuItem;
        private readonly MenuItem _workspaceProjectInstructionsMenuItem;
        private readonly MenuItem _workspaceCreateAutomationMenuItem;
        private readonly MenuItem _workspaceImportSyncMenuItem;
        private readonly MenuItem _workspaceSyncThreadsMenuItem;
        private readonly MenuItem _workspaceImportOtherSessionsMenuItem;
        private readonly MenuItem _workspaceExternalAgentConfigImportMenuItem;
        private readonly MenuItem _workspaceRemoveMenuItem;
        private readonly MenuItem _workspaceArchiveMenuItem;
        private readonly MenuItem _workspaceRestoreArchivedMenuItem;
        private readonly MenuItem _workspaceShowUnavailableMenuItem;
        private readonly MenuItem _sessionNewMenuItem;
        private readonly MenuItem _sessionRenameMenuItem;
        private readonly MenuItem _sessionStatisticsMenuItem;
        private readonly MenuItem _sessionSubAgentsMenuItem;
        private readonly MenuItem _sessionCreateAutomationMenuItem;
        private readonly MenuItem _sessionAdvancedSeparatorMenuItem;
        private readonly MenuItem _goalSetMenuItem;
        private readonly MenuItem _goalViewMenuItem;
        private readonly MenuItem _goalClearMenuItem;
        private readonly MenuItem _sessionRemoveMenuItem;
        private readonly MenuItem _sessionArchiveMenuItem;
        private readonly Timer _automaticThreadSyncTimer;
        private readonly Timer _automaticThreadSyncFollowUpTimer;
        private readonly Timer _localThreadSessionWatchDebounceTimer;
        private readonly Timer _localSubAgentThreadSyncTimer;
        private readonly Timer _rateLimitAutoResetTimer;
        private readonly Timer _logFlushTimer;
        private SplitContainer _mainSplitContainer;
        private SplitContainer _contentSplitContainer;
        private bool _isMainSplitterMoving;
        private bool _isContentSplitterMoving;
        private TabControl _modeTabControl;
        private TabPage _codingTabPage;
        private TabPage _officeTabPage;
        private AppInteractionMode _currentInteractionMode;
        private string _currentCodingWorkspaceId;
        private string _currentCodingSessionId;
        private string _currentOfficeWorkspaceId;
        private string _currentOfficeSessionId;
        private WorkspaceInfo _currentWorkspace;
        private ChatSession _currentSession;
        private CommandTask _activeCommandTask;
        private CommandTerminalForm _commandTerminalForm;
        private CommandTerminalBuffer _commandTerminalBuffer;
        private readonly CommandTerminalResizePlanner _commandTerminalResizePlanner = new CommandTerminalResizePlanner();
        private AutomationsForm _automationsForm;
        private bool _commandTerminalRefreshQueued;
        private AppConfigSnapshot _appConfigSnapshot;
        private AppServerAuthenticationState _authenticationState;
        private string _connectionStatus;
        private string _threadSyncSummary;
        private string _activitySummaryText;
        private string _chatHistorySearchKeyword;
        private StartupCommandLineOptions _startupCommandLineOptions;
        private bool _isCommandRunning;
        private bool _isRefreshingAuthenticationState;
        private bool _isRefreshingSessionList;
        private bool _isLoadingModels;
        private bool _isRecoveringAppServerConnection;
        private bool _isExitRequested;
        private ManualBrowseContext _mainManualBrowseContext;
        private bool _hasCompletedThreadImportPrompt;
        private bool _isUpdatingInputBoxText;
        private Control _lastFocusedControlBeforeTrayHide;
        private DateTime _lastStatusUpdatedAtUtc;
        private StatusPriority _currentStatusPriority;
        private readonly Dictionary<string, SoundPlayer> _notificationSoundPlayers = new Dictionary<string, SoundPlayer>(StringComparer.OrdinalIgnoreCase);
        private AppServerFileBrowserClipboardState _appServerFileBrowserClipboardState;

        private enum StatusPriority
        {
            Low = 0,
            Normal = 1,
            High = 2
        }

        private static string GetConnectionStatusDisconnectedText()
        {
            return UiLocalization.Text("MainForm.Connection.Disconnected", "Disconnected");
        }

        private static string GetConnectionStatusConnectedText()
        {
            return UiLocalization.Text("MainForm.Connection.Connected", "Connected");
        }

        private static string GetConnectionStatusReconnectingText()
        {
            return UiLocalization.Text("MainForm.Connection.Reconnecting", "Reconnecting");
        }

        private static string GetConnectionStatusConnectingText()
        {
            return UiLocalization.Text("MainForm.Connection.Connecting", "Connecting to App Server...");
        }

        private bool IsConnectedStatus()
        {
            return string.Equals(_connectionStatus, GetConnectionStatusConnectedText(), StringComparison.OrdinalIgnoreCase);
        }

        private CodexRuntimeUpdateService CreateCodexRuntimeUpdateService()
        {
            if (_appConfigSnapshot == null)
            {
                LoadAppConfig();
            }

            return new CodexRuntimeUpdateService(
                _appConfigService,
                _appConfigSnapshot != null ? _appConfigSnapshot.UpdateSettings : null);
        }

        public MainForm(
            IAppServerClient appServerClient,
            IAppStateStore appStateStore,
            IAppConfigStore appConfigStore,
            AppConfigService appConfigService,
            IScreenReaderService screenReaderService,
            CodexRuntimeManager codexRuntimeManager,
            IPluginMarketCatalogClient pluginMarketCatalogClient = null,
            IGitHostingProviderService gitHostingProviderService = null,
            string appDisplayName = null,
            IInternalToolRegistry internalToolRegistry = null,
            IAutomationStore automationStore = null,
            IAutomationService automationService = null,
            AutomationRuntimeHost automationRuntimeHost = null,
            IAutomationRunner automationRunner = null)
        {
            _appServerClient = appServerClient ?? throw new ArgumentNullException(nameof(appServerClient));
            _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
            _appConfigStore = appConfigStore ?? throw new ArgumentNullException(nameof(appConfigStore));
            _appConfigService = appConfigService ?? throw new ArgumentNullException(nameof(appConfigService));
            _screenReaderService = screenReaderService ?? throw new ArgumentNullException(nameof(screenReaderService));
            _codexRuntimeManager = codexRuntimeManager ?? throw new ArgumentNullException(nameof(codexRuntimeManager));
            _pluginMarketCatalogClient = pluginMarketCatalogClient ?? new PluginMarketCatalogClient();
            _internalToolRegistry = internalToolRegistry ?? new InternalToolRegistry(null);
            _automationStore = automationStore;
            _automationService = automationService;
            _automationRunner = automationRunner;
            _automationRuntimeHost = automationRuntimeHost;
            _automationRuntimeHost?.SetPersistenceFailureHandler(MarkPersistenceRecoveryPending);
            _gitHostingProviderService = gitHostingProviderService ?? new GitHostingProviderService();
            _appDisplayName = string.IsNullOrWhiteSpace(appDisplayName) ? AppConstants.AppName : appDisplayName.Trim();
            _startupCommandLineOptions = StartupCommandLineOptions.Empty;
            _workspaceService = new WorkspaceService();
            _officeWorkspaceService = new OfficeWorkspaceService();
            _officeRuntimeScopeService = new OfficeRuntimeScopeService();
            _officeRuntimeStateService = new OfficeRuntimeStateService(_officeWorkspaceService);
            _officeRuntimeFileSystem = new OfficeRuntimeFileSystem();
            _projectInstructionsService = new ProjectInstructionsService();
            _wslFileSystemBridge = new WslFileSystemBridge();
            _remoteMacFileSystemBridge = new RemoteMacFileSystemBridge();
            _sessionService = new SessionService();
            _runtimeProfileService = new RuntimeProfileService();
            _sessionDefaultsService = new SessionDefaultsService(_runtimeProfileService);
            _modeSettingsStore = new JsonModeSettingsStore(_appConfigService);
            _persistedSessionService = new PersistedSessionService(_runtimeProfileService);
            _threadHistoryRepairService = CreateThreadHistoryRepairService(new CodexThreadHistoryRepairService());
            _sessionThreadService = new SessionThreadService(
                _appServerClient,
                _workspaceService,
                ReadWorkspaceGitInfo,
                _threadHistoryRepairService);
            _sessionThreadService.ModelSettingsResolver = ReadSessionModelSettingsAsync;
            _availableModels = new List<AppServerModelOption>();
            _recentWorkspaces = new List<WorkspaceInfo>();
            _sessions = new List<ChatSession>();
            _inactiveRuntimeSessions = new List<ChatSession>();
            _allChatHistoryItems = new List<ChatHistoryItem>();
            _commandTasks = new List<CommandTask>();
            _runtimeFileBrowserForms = new List<RuntimeFileBrowserForm>();
            _appServerFileWindowManager = CreateAppServerFileWindowManager();
            _sessionHistoryStateStore = new SessionHistoryStateStore();
            _threadTurnSummaryStates = new Dictionary<string, TurnProgressSummaryState>(StringComparer.OrdinalIgnoreCase);
            _threadActivityTimelineStates = new Dictionary<string, ActivityTimelineState>(StringComparer.OrdinalIgnoreCase);
            _automationRunActivitySubscribers = new Dictionary<string, List<Action<IReadOnlyList<string>>>>(StringComparer.OrdinalIgnoreCase);
            _reasoningSummaryMessageStates = new Dictionary<string, ReasoningSummaryMessageState>(StringComparer.OrdinalIgnoreCase);
            _pendingExecutablePlans = new Dictionary<string, PendingExecutablePlan>(StringComparer.OrdinalIgnoreCase);
            _pendingRemotePlanExecutionPrompts = new Dictionary<string, PendingRemotePlanExecutionPrompt>(StringComparer.OrdinalIgnoreCase);
            _pendingInvalidEncryptedContentRetries = new Dictionary<string, PendingInvalidEncryptedContentRetryContext>(StringComparer.OrdinalIgnoreCase);
            _realtimeSessionHistoryReloadVersions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _localDesktopRealtimeTurnThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _busyThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _sessionsPendingDefaultCollaborationMode = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _projectInstructionsHelperThreadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _invalidEncryptedContentFailureState = new InvalidEncryptedContentFailureState(MaxRecentNotificationEventIds);
            _threadOperationStateTracker = new ThreadOperationStateTracker(
                MaxRecentNotificationEventIds,
                TimeSpan.FromMilliseconds(TurnCompletedNotificationThrottleMilliseconds));
            _composeDraftStore = new ComposeDraftStore();
            _pendingAttachments = new List<PendingAttachmentItem>();
            _workspaceAvailabilityCache = new Dictionary<string, Tuple<bool, DateTime>>(StringComparer.Ordinal);
            _workspaceGitInfoCache = new Dictionary<string, WorkspaceGitInfoSnapshot>(StringComparer.Ordinal);
            _subAgentWorkbenches = new Dictionary<string, SubAgentWorkbenchForm>(StringComparer.OrdinalIgnoreCase);
            _bufferedCommandOutputState = new BufferedDispatchState<AppServerEventArgs>(MaxBufferedCommandOutputEvents);
            _bufferedTurnSummaryEventState = new CoalescingBufferedDispatchState<AppServerEventArgs>(
                BuildBufferedTurnSummaryEventKey,
                MaxBufferedTurnSummaryEvents);
            _visibleLogState = new VisibleLogBuffer(MaxVisibleLogCharacters, TargetVisibleLogCharacters);
            _recordedLogState = new VisibleLogBuffer(MaxVisibleLogCharacters, TargetVisibleLogCharacters);
            _activityDisplayRefreshState = new ActivityDisplayRefreshState();
            _sessionHistoryDisplayBuilder = new SessionHistoryDisplayBuilder(
                ChatHistoryItemFactory.GetSystemSenderText(),
                ChatHistoryItemFactory.GetErrorSenderText(),
                BuildEffectiveModelSummaryText,
                (sender, message, category) => ChatHistoryItemFactory.Create(sender, message, category),
                ChatHistoryItemFactory.GetDefaultCategory);
            _remoteInteractionDialogs = new RemoteInteractionDialogRegistry();
            _sessionHistoryLoadWorkflow = new SessionHistoryLoadWorkflow(
                _sessionHistoryStateStore,
                _sessionHistoryDisplayBuilder,
                _appServerClient.ReadThreadAsync,
                SessionThreadService.IsMissingThreadRolloutError,
                IsAppServerConnectivityFailure);
            _sessionThreadOperationWorkflow = new SessionThreadOperationWorkflow(
                _appServerClient,
                _sessionThreadService,
                _sessionService);
            _archivedThreadRestoreWorkflow = new ArchivedThreadRestoreWorkflow(_appServerClient);
            _automaticThreadSyncWorkflow = new AutomaticThreadSyncWorkflow(_appServerClient);
            _automaticThreadSyncRunCoordinator = new AutomaticThreadSyncRunCoordinator();
            _automaticThreadSyncFollowUpTracker = new AutomaticThreadSyncFollowUpTracker(
                AutomaticThreadSyncFollowUpAttempts,
                AutomaticThreadSyncFollowUpFirstDelayMilliseconds,
                AutomaticThreadSyncFollowUpRetryDelayMilliseconds);
            _localThreadSessionWatchers = new List<FileSystemWatcher>();
            _remoteIdentityStore = new CngRemoteIdentityStore();
            _remoteBindingStore = new JsonRemoteBindingStore(_appConfigService.GetRemoteBindingsFilePath());
            _remoteHostRealtimeClient = CreateRemoteHostRealtimeClient();
            var remoteServiceConfigStore = new JsonRemoteServiceConfigStore(_appConfigService);
            _embeddedRelayService = new SharedEmbeddedRelayService(_appConfigService);
            _remoteHostServiceController = new RemoteHostServiceController(
                remoteServiceConfigStore,
                _remoteHostRealtimeClient,
                _embeddedRelayService,
                snapshot => PrepareOwnedRemoteDisplayName(remoteServiceConfigStore, snapshot));

            _appServerClient.EventReceived += AppServerClientOnEventReceived;
            InitializeSessionHistoryRepair();

            Text = ComposeWindowTitle();
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = MainFormLayoutPlanner.MinimumWindowSize;
            var defaultBounds = new Rectangle(Location, MainFormLayoutPlanner.DefaultWindowSize);
            Size = MainFormLayoutPlanner.ConstrainWindowBounds(defaultBounds, Screen.FromRectangle(defaultBounds).WorkingArea).Size;
            WindowState = MainFormLayoutPlanner.DefaultWindowState;
            KeyPreview = true;
            UiTheme.ApplyForm(this);

            _workspaceList = CreateWorkspaceListView(UiLocalization.Text("MainForm.Accessible.Workspaces", "Workspaces"), 0);
            _sessionList = CreateSessionListView(UiLocalization.Text("MainForm.Accessible.Sessions", "Sessions"), 1);
            _chatHistoryListBox = CreateChatHistoryListBox();
            _pendingAttachmentListBox = CreatePendingAttachmentListBox();
            _inputBox = CreateInputBox();
            _activityBrowseControl = CreateActivityBrowseControl();
            _activityCurrentStatusLabel = CreateActivityCurrentStatusLabel();
            _commandMenuButton = CreateActionButton(
                UiLocalization.Text("MainForm.Button.Command", "Terminal(&K)"),
                UiLocalization.Text("MainForm.Accessible.Command", "Terminal"),
                8,
                OnCommandMenuButtonClick);
            _diffWorkbenchButton = CreateActionButton(
                UiLocalization.Text("MainForm.Button.DiffWorkbench", "Git Workbench(&H)"),
                UiLocalization.Text("MainForm.Accessible.DiffWorkbench", "Git Workbench"),
                9,
                OnDiffWorkbenchButtonClick);
            _attachmentButton = CreateActionButton(
                UiLocalization.Text("MainForm.Button.Attach", "&Attach"),
                UiLocalization.Text("MainForm.Accessible.Attach", "Attach"),
                3,
                OnAttachmentButtonClick);
            _sendButton = CreateActionButton(
                UiLocalization.Text("MainForm.Button.Send", "&Send"),
                UiLocalization.Text("MainForm.Accessible.Send", "Send"),
                4,
                OnSendButtonClick);
            _stopButton = CreateActionButton(
                UiLocalization.Text("MainForm.Button.Stop", "&Stop"),
                UiLocalization.Text("MainForm.Accessible.Stop", "Stop"),
                5,
                OnStopButtonClick);
            InitializeAsyncInputUi();
            _openWorkspaceButton = CreateActionButton(
                UiLocalization.Text("MainForm.Button.NewWorkspace", "&New Workspace"),
                UiLocalization.Text("MainForm.Accessible.NewWorkspace", "New Workspace"),
                6,
                OnOpenWorkspaceButtonClick);
            _newSessionButton = CreateActionButton(
                UiLocalization.Text("MainForm.Button.NewSession", "Ne&w Session"),
                UiLocalization.Text("MainForm.Accessible.NewSession", "New Session"),
                7,
                OnNewSessionButtonClick);
            _mainMenu = new MainMenu();
            _trayIcon = new NotifyIcon();
            _statusStrip = new StatusStrip
            {
                AutoSize = false,
                Height = 28,
                TabStop = false
            };
            _statusIndicator = UiTheme.CreateStatusIndicator();
            _statusLabel = new ToolStripStatusLabel(UiLocalization.Text("Status.Ready", "Ready"));
            _currentStatusPriority = StatusPriority.Low;
            _lastStatusUpdatedAtUtc = DateTime.UtcNow;
            _fileMenuItem = new MenuItem(UiLocalization.Text("MainMenu.File", "&File"));
            _gitMenuItem = new MenuItem(UiLocalization.Text("MainMenu.Git", "&Git"));
            _accountMenuItem = new MenuItem(UiLocalization.Text("MainMenu.Account", "A&ccount"));
            _helpMenuItem = new MenuItem(UiLocalization.Text("MainMenu.Help", "&Help"));
            _openWorkspaceFileMenuItem = new MenuItem(UiLocalization.Text("MainMenu.OpenWorkspace", "&Open Workspace...")) { Shortcut = Shortcut.CtrlO };
            _openWslWorkspaceFileMenuItem = new MenuItem(UiLocalization.Text("MainMenu.OpenWslWorkspace", "Open &WSL Workspace..."));
            _openGlobalFileBrowserMenuItem = new MenuItem(UiLocalization.Text("MainMenu.OpenGlobalFileBrowser", "Open Global File Browser(&E)..."));
            _newSessionFileMenuItem = new MenuItem(UiLocalization.Text("MainMenu.NewSession", "&New Session")) { Shortcut = Shortcut.CtrlN };
            _automationsMenuItem = new MenuItem(UiLocalization.Text("MainMenu.Automations", "Automations(&A)"));
            _targetReloadMenuItem = new MenuItem(UiLocalization.Text("MainMenu.TargetReload", "Reload Target(&R)"));
            _exitMenuItem = new MenuItem(UiLocalization.Text("MainMenu.Exit", "E&xit"));
            _gitWorkbenchMenuItem = new MenuItem(UiLocalization.Text("MainMenu.GitWorkbench", "Git &Workbench..."));
            _gitCloneMenuItem = new MenuItem(UiLocalization.Text("MainMenu.GitClone", "&Clone Repository..."));
            _gitRemoteRepositoriesMenuItem = new MenuItem(UiLocalization.Text("MainMenu.GitRemoteRepositories", "Remote Repositories(&P)..."));
            _gitMarketMenuItem = new MenuItem(UiLocalization.Text("MainMenu.GitMarket", "GitHub Store(&M)..."));
            _gitEnvironmentMenuItem = new MenuItem(UiLocalization.Text("MainMenu.GitEnvironment", "Git &Environment..."));
            _gitRemoteCredentialsMenuItem = new MenuItem(UiLocalization.Text("MainMenu.GitRemoteCredentials", "Remote Credentials(&R)..."));
            _userCenterMenuItem = new MenuItem(UiLocalization.Text("MainMenu.UserCenter", "&User Center"));
            _userManualMenuItem = new MenuItem(UiLocalization.Text("MainMenu.UserManual", "&User Manual"));
            _officialWebsiteMenuItem = new MenuItem(UiLocalization.Text("MainMenu.OfficialWebsite", "Visit &Website"));
            _checkUpdatesMenuItem = new MenuItem(UiLocalization.Text("MainMenu.CheckUpdates", "Check for &Updates..."));
            _openLogDirectoryMenuItem = new MenuItem(UiLocalization.Text("MainMenu.OpenLogDirectory", "Open &Log Directory"));
            _donateMenuItem = new MenuItem(UiLocalization.Text("MainMenu.DonateSupport", "Support the Project(&D)..."));
            _aboutMenuItem = new MenuItem(UiLocalization.Text("MainMenu.About", "A&bout..."));
            _planModeMenuItem = new MenuItem(UiLocalization.Text("MainMenu.PlanMode", "&Plan Mode"));
            _goalMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.Goal", "Goal(&G)"));
            _fileImportSyncMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.Menu", "Import && Sync(&I)"));
            _fileSyncThreadsMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.SyncThreadHistory", "S&ync Thread History..."));
            _fileImportOtherSessionsMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.ImportOtherSessions", "Import Other Sessions(&O)..."));
            _fileExternalAgentConfigImportMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.ExternalAgentConfigImport", "Import External Agent Config(&C)..."));
            _workspaceNameValueLabel = CreateInfoValueLabel();
            _workspacePathValueLabel = CreateInfoValueLabel();
            _branchValueLabel = CreateInfoValueLabel();
            _composePanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = MainFormLayoutPlanner.ComposePanelHeight,
                BackColor = UiTheme.PanelBackColor,
                Padding = new Padding(0, 8, 0, 0),
                TabStop = false
            };
            _attachmentPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = UiTheme.AttachmentPanelHeight,
                BackColor = UiTheme.PanelBackColor,
                Visible = false,
                TabStop = false
            };

            _chatHistoryContextMenu = new ContextMenu();
            _attachmentMenu = new ContextMenu();
            _pendingAttachmentContextMenu = new ContextMenu();
            _logContextMenu = new ContextMenu();
            _workspaceContextMenu = new ContextMenu();
            _sessionContextMenu = new ContextMenu();
            _trayContextMenu = new ContextMenu();
            _enableAutoReadMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.EnableAutoRead", "Enable Auto Read(&U)"));
            _copySelectedMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.CopySelectedMessage", "Copy Message(&C)"));
            _forkMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.Fork", "Fork(&F)"));
            _forkFullMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.ForkFull", "Full Fork..."));
            _forkFromHereMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.ForkFromHere", "Fork from this turn..."));
            _findChatMessageContextMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.FindCurrentMessage", "Find Current Message(&H)..."));
            _compactChatHistoryMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.CompactContext", "Compact Context(&M)"));
            _reviewChatHistoryMenuItem = new MenuItem(UiLocalization.Text("MainForm.Context.ReviewUncommittedCode", "Review(&V)..."));
            _workspaceRemoveMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.Delete", "Delete(&D)")) { Shortcut = Shortcut.Del };
            _workspaceImportSyncMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.Menu", "Import && Sync(&I)"));
            _workspaceSyncThreadsMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.SyncThreadHistory", "S&ync Thread History..."));
            _workspaceImportOtherSessionsMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.ImportOtherSessions", "Import Other Sessions(&O)..."));
            _workspaceExternalAgentConfigImportMenuItem = new MenuItem(UiLocalization.Text("MainForm.ImportSync.ExternalAgentConfigImport", "Import External Agent Config(&C)..."));
            _workspaceArchiveMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.Archive", "Archive(&A)"));
            _workspaceRestoreArchivedMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.RestoreArchivedThreads", "Restore Archived Threads(&R)..."));
            _workspaceShowUnavailableMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.ShowHistoricalRepo", "Show Historical Repositories(&H)"));
            _sessionRemoveMenuItem = new MenuItem(UiLocalization.Text("MainForm.Session.Delete", "Delete(&D)")) { Shortcut = Shortcut.Del };
            _sessionArchiveMenuItem = new MenuItem(UiLocalization.Text("MainForm.Session.Archive", "Archive(&A)"));

            _automaticThreadSyncTimer = new Timer
            {
                Interval = AutomaticThreadSyncIntervalMilliseconds
            };
            _automaticThreadSyncFollowUpTimer = new Timer
            {
                Interval = AutomaticThreadSyncFollowUpFirstDelayMilliseconds
            };
            _localThreadSessionWatchDebounceTimer = new Timer
            {
                Interval = LocalThreadSessionWatchDebounceMilliseconds
            };
            _localSubAgentThreadSyncTimer = new Timer
            {
                Interval = LocalSubAgentThreadSyncIntervalMilliseconds
            };
            _rateLimitAutoResetTimer = new Timer
            {
                Interval = RateLimitAutoResetCheckIntervalMilliseconds
            };
            _logFlushTimer = new Timer
            {
                Interval = LogFlushIntervalMilliseconds
            };
            _selectImageAttachmentMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.Image", "Image(&I)..."));
            _selectFileAttachmentMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.File", "File(&F)..."));
            _selectFolderAttachmentMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.Folder", "Folder(&D)..."));
            _selectSkillAttachmentMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.Skill", "Skill(&K)..."));
            _selectPluginAttachmentMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.Plugin", "Plugin(&P)..."));
            _removeSelectedAttachmentMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.RemoveSelected", "Remove Selected(&R)")) { Shortcut = Shortcut.Del };
            _clearAttachmentsMenuItem = new MenuItem(UiLocalization.Text("MainForm.Attachment.ClearItems", "Clear Items(&C)"));
            _activitySettingsMenuItem = new MenuItem(UiLocalization.Text("MainForm.Activity.Settings", "Message Settings(&S)..."));
            _clearActivityMenuItem = new MenuItem(UiLocalization.Text("MainForm.Activity.ClearAll", "Clear All Activity(&C)"));
            _showActivityLogMenuItem = new MenuItem(UiLocalization.Text("MainForm.Log.ShowActivityLog", "Show Log(&S)"));
            _clearLogMenuItem = new MenuItem(UiLocalization.Text("MainForm.Log.Clear", "Clear Log(&L)"));
            _showMainWindowTrayMenuItem = new MenuItem(UiLocalization.Text("MainForm.Tray.ShowMainWindow", "Show Main Window(&S)"));
            _hideMainWindowTrayMenuItem = new MenuItem(UiLocalization.Text("MainForm.Tray.HideMainWindow", "Hide Main Window(&H)"));
            _exitTrayMenuItem = new MenuItem(UiLocalization.Text("MainForm.Tray.Exit", "Exit(&X)"));
            _workspaceNewMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.New", "New Workspace(&N)")) { Shortcut = Shortcut.CtrlO };
            _workspaceOpenWslMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.OpenWsl", "Open WSL Workspace(&W)..."));
            _workspaceAppServerFilesMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.AppServerFiles", "File Browser(&F)...")) { Shortcut = Shortcut.CtrlE };
            _workspaceProjectInstructionsMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.ProjectInstructions", "Project Instructions(&I)..."));
            _workspaceCreateAutomationMenuItem = new MenuItem(UiLocalization.Text("MainForm.Workspace.CreateAutomation", "Create Project Automation(&A)..."));
            _sessionNewMenuItem = new MenuItem(UiLocalization.Text("MainForm.Session.New", "New Session(&N)")) { Shortcut = Shortcut.CtrlN };
            _sessionRenameMenuItem = new MenuItem(UiLocalization.Text("MainForm.Session.Rename", "Rename Title(&E)...")) { Shortcut = Shortcut.F2 };
            _sessionStatisticsMenuItem = new MenuItem(UiLocalization.Text("MainForm.Session.Statistics", "Statistics(&S)..."));
            _sessionSubAgentsMenuItem = new MenuItem(UiLocalization.Text("MainForm.Session.SubAgents", "View Suba&gents..."));
            _sessionCreateAutomationMenuItem = new MenuItem(UiLocalization.Text("MainForm.SessionMenu.CreateAutomation", "Create Automation(&A)"));
            _sessionAdvancedSeparatorMenuItem = new MenuItem("-");
            _goalSetMenuItem = new MenuItem(UiLocalization.Text("MainForm.Goal.Set", "Set Goal(&S)..."));
            _goalViewMenuItem = new MenuItem(UiLocalization.Text("MainForm.Goal.View", "View Goal(&V)"));
            _goalClearMenuItem = new MenuItem(UiLocalization.Text("MainForm.Goal.Clear", "Clear Goal(&C)"));

            ApplyMainFormLocalizedText();
            _mainManualBrowseContext = CreateMainManualBrowseContext();

            ConfigureConstructedMainFormUi();

            BuildLayout();
            LoadAppConfig();
            RefreshMainWindowHotKeyMenuText();
            ConfigureAutomationModelResolver();
            if (!_isPersistenceRecoveryPending)
            {
                LoadPersistedState(false);
            }
            InitializeModeTabsAfterStateLoad();
            RefreshWorkspaceView();
            RefreshSessionList();
            RefreshHeader();
            RefreshInputState();

            Shown += OnShown;
            Activated += OnModelConfigurationActivated;
            FormClosing += OnFormClosing;
            FormClosed += OnFormClosed;
            Disposed += (sender, args) => StopTargetInitialization();
            TryStartPersistenceDependentBackgroundServices();
        }

        public void ApplyStartupArguments(string[] args)
        {
            _startupCommandLineOptions = StartupCommandLineOptions.Parse(args);
        }

        private async void OnShown(object sender, EventArgs e)
        {
            try
            {
                if (_isPersistenceRecoveryPending)
                {
                    ShowPersistenceRecoveryRequiredAndExit();
                    return;
                }

                if (!await EnsureBundledRuntimeUpdatedAsync()) return;
                var modeSettingsAvailable = await LoadModeSettingsForCurrentRuntimeAsync();
                if (!await WaitForTargetExitDecisionAsync()) return;
                if (modeSettingsAvailable)
                {
                    SelectInteractionModeTab(_activeModeSettings.DefaultStartupMode);
                    await RunStartupModeOnboardingIfNeeded();
                    if (!await WaitForTargetExitDecisionAsync()) return;
                }
                await InitializeAppAsync();
                if (!await WaitForTargetExitDecisionAsync()) return;
                await StartRemoteHostServiceAsync();
                if (!await WaitForTargetExitDecisionAsync()) return;
                // Automatic updates may coordinate exit while their modal prompt is open.
                CompleteTargetStartup();
                await RunAutomaticUpdateCheckAsync();
                if (!await WaitForTargetExitDecisionAsync()) return;
                ScheduleOfficeCliOnboardingPromptIfNeeded();
            }
            catch (Exception ex)
            {
                if (!await WaitForTargetExitDecisionAsync()) return;
                _connectionStatus = UiLocalization.Text("MainForm.Connection.Failed", "Connection failed");
                RefreshHeader();
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.InitializeFailed", "Initialization failed"));
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally { if (!_targetStartupComplete) CompleteTargetStartup(); }
        }

        private void OnFormClosed(object sender, FormClosedEventArgs e)
        {
            MainFormShutdownDiagnostics.RunStep("unsubscribe-system-events", () => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged);
            MainFormShutdownDiagnostics.RunStep("unsubscribe-app-server-events", () => _appServerClient.EventReceived -= AppServerClientOnEventReceived);
            MainFormShutdownDiagnostics.RunStep("unregister-main-window-hotkey", UnregisterMainWindowHotKey);
            MainFormShutdownDiagnostics.RunStep("close-runtime-window-group", CloseRuntimeWindowGroupOnExit);
            MainFormShutdownDiagnostics.RunStep("close-session-workbenches", CloseSessionWorkbenchForms);
            MainFormShutdownDiagnostics.RunStep("close-subagent-workbenches", CloseSubAgentWorkbenchForms);
            MainFormShutdownDiagnostics.RunStep("close-git-workbenches", CloseGitWorkbenchForms);
            MainFormShutdownDiagnostics.RunStep("clear-compose-state", () => ClearAllComposeStates(true));
            MainFormShutdownDiagnostics.RunStep("dispose-remote-host-service", DisposeRemoteHostService);
            MainFormShutdownDiagnostics.RunStep("dispose-thread-sync-timers", DisposeThreadSyncTimersOnExit);
            MainFormShutdownDiagnostics.RunStep("dispose-local-thread-session-watchers", DisposeLocalThreadSessionWatchers);
            MainFormShutdownDiagnostics.RunStep("dispose-local-sync-timers", DisposeLocalSyncTimersOnExit);
            MainFormShutdownDiagnostics.RunStep("dispose-rate-limit-auto-reset-timer", DisposeRateLimitAutoResetTimerOnExit);
            MainFormShutdownDiagnostics.RunStep("dispose-automation-runtime-host", DisposeAutomationRuntimeHostOnExit);
            MainFormShutdownDiagnostics.RunStep("stop-screen-reader-service", StopScreenReaderServiceWithinExitBudget);
            MainFormShutdownDiagnostics.RunStep("dispose-tray-icon", DisposeTrayIconOnExit);
            MainFormShutdownDiagnostics.RunStep("dispose-app-server-client", DisposeAppServerClientOnExit);
            MainFormShutdownDiagnostics.RunStep("stop-local-authentication-backend", StopLocalAuthenticationBackendOnExit);
        }

        private void DisposeThreadSyncTimersOnExit()
        {
            _automaticThreadSyncTimer.Stop();
            _automaticThreadSyncTimer.Dispose();
            _automaticThreadSyncFollowUpTimer.Stop();
            _automaticThreadSyncFollowUpTimer.Dispose();
        }

        private void DisposeLocalSyncTimersOnExit()
        {
            _localThreadSessionWatchDebounceTimer.Stop();
            _localThreadSessionWatchDebounceTimer.Dispose();
            _localSubAgentThreadSyncTimer.Stop();
            _localSubAgentThreadSyncTimer.Dispose();
            _logFlushTimer.Stop();
            _logFlushTimer.Dispose();
        }

        private void DisposeAutomationRuntimeHostOnExit()
        {
            if (_automationRuntimeHost == null)
            {
                return;
            }

            _automationRuntimeHost.StopForRuntimeTransition(UiLocalization.Text("MainForm.Exit.Stopped", "The target exited."));
            _automationRuntimeHost.Dispose();
        }

        private void StopScreenReaderServiceWithinExitBudget()
        {
            if (_screenReaderService == null)
            {
                return;
            }

            var stopTask = Task.Run(() =>
            {
                try
                {
            StopOwnedScreenReaderOutput();
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Screen reader stop failed during shutdown: {0}", ex.Message);
                }
            });

            if (stopTask.Wait(ExitScreenReaderStopBudget))
            {
                return;
            }

            Trace.TraceWarning(
                "MyCodex shutdown screen reader stop timed out. BudgetMs={0}",
                (int)ExitScreenReaderStopBudget.TotalMilliseconds);
        }

        private void DisposeTrayIconOnExit()
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }

        private void DisposeAppServerClientOnExit()
        {
            var disposableAppServerClient = _appServerClient as IDisposable;
            if (disposableAppServerClient != null)
            {
                disposableAppServerClient.Dispose();
            }
        }

        private async void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isExitRequested)
            {
                if (IsTargetExitPrepared && !_targetExitCleanupComplete) { e.Cancel = true; return; }
                MarkTargetClosing();
                return;
            }

            e.Cancel = true;
            await RequestSingleTargetExitAsync(e.CloseReason == CloseReason.UserClosing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (HandleScreenReaderHotkey(keyData))
            {
                return true;
            }

            if (Enabled && keyData == (Keys.Control | Keys.D1))
            {
                SelectInteractionModeTab(AppInteractionMode.Coding);
                return true;
            }

            if (Enabled && keyData == (Keys.Control | Keys.D2))
            {
                SelectInteractionModeTab(AppInteractionMode.Office);
                return true;
            }

            if (_mainManualBrowseContext != null && keyData == (Keys.Alt | Keys.Enter))
            {
                return true;
            }

            if (keyData == (Keys.Alt | Keys.Q))
            {
                OnStopButtonClick(_stopButton, EventArgs.Empty);
                return true;
            }

            if (keyData == (Keys.Alt | Keys.D1) || keyData == (Keys.Alt | Keys.NumPad1))
            {
                FocusWorkspaceList();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.D2) || keyData == (Keys.Alt | Keys.NumPad2))
            {
                FocusSessionList();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.M))
            {
                FocusChatHistoryList();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.I))
            {
                FocusInputBox();
                return true;
            }

            if (keyData == (Keys.Control | Keys.Shift | Keys.A))
            {
                OpenAttachmentMenuFromKeyboard();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.L))
            {
                FocusLogTextBox();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.E))
            {
                _ = OpenAppServerFilesFromDefaultRootAsync();
                return true;
            }

            if (keyData == (Keys.Alt | Keys.U) && _userCenterMenuItem.Enabled)
            {
                OnUserCenterMenuItemClick(_userCenterMenuItem, EventArgs.Empty);
                return true;
            }

            if (keyData == (Keys.Control | Keys.E))
            {
                _ = OpenAppServerFilesForCurrentWorkspaceAsync();
                return true;
            }

            if (keyData == Keys.Escape)
            {
                if (_appConfigSnapshot == null || _appConfigSnapshot.CloseSessionWorkbenchOnEscape)
                {
                    HideMainWindowToTray();
                }

                return true;
            }

            if (keyData == (Keys.Control | Keys.F))
            {
                ShowChatHistorySearchDialog();
                return true;
            }

            if (keyData == Keys.F3)
            {
                return TryContinueChatHistorySearch(true);
            }

            if (keyData == (Keys.Shift | Keys.F3))
            {
                return TryContinueChatHistorySearch(false);
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SetInitialFocus()
        {
            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                ActiveControl = _modeTabControl;
                _modeTabControl.Focus();
                return;
            }

            SelectFirstWorkspaceListItemIfNeeded();

            ActiveControl = _workspaceList;
            _workspaceList.Focus();
        }

        private void ConfigureWorkspaceNavigation()
        {
            _workspaceList.ContextMenu = _workspaceContextMenu;
            _workspaceList.DoubleClick += WorkspaceList_DoubleClick;
            _workspaceList.KeyDown += WorkspaceList_KeyDown;
            _workspaceList.MouseDown += WorkspaceList_MouseDown;
            _sessionList.ContextMenu = _sessionContextMenu;
            _sessionList.MouseDown += SessionList_MouseDown;
        }

        private void ConfigureDeletionContextMenus()
        {
            _workspaceNewMenuItem.Click += OnOpenWorkspaceButtonClick;
            _workspaceOpenWslMenuItem.Click += OnOpenWslWorkspaceMenuItemClick;
            _workspaceAppServerFilesMenuItem.Click += OnWorkspaceAppServerFilesMenuItemClick;
            _workspaceProjectInstructionsMenuItem.Click += OnWorkspaceProjectInstructionsMenuItemClick;
            _workspaceCreateAutomationMenuItem.Click += (sender, e) => CreateAutomationFromSelectedWorkspace();
            _workspaceSyncThreadsMenuItem.Click += OnSyncThreadsMenuItemClick;
            _workspaceImportOtherSessionsMenuItem.Click += OnWorkspaceAssociateOtherSessionsMenuItemClick;
            _workspaceExternalAgentConfigImportMenuItem.Click += ExternalAgentConfigImportMenuItem_Click;
            _workspaceRestoreArchivedMenuItem.Click += OnWorkspaceRestoreArchivedMenuItemClick;
            _workspaceShowUnavailableMenuItem.Click += OnWorkspaceShowUnavailableMenuItemClick;
            _workspaceRemoveMenuItem.Click += OnWorkspaceRemoveMenuItemClick;
            _workspaceArchiveMenuItem.Click += OnWorkspaceArchiveMenuItemClick;
            _workspaceImportSyncMenuItem.MenuItems.Add(_workspaceSyncThreadsMenuItem);
            _workspaceImportSyncMenuItem.MenuItems.Add(_workspaceImportOtherSessionsMenuItem);
            _workspaceImportSyncMenuItem.MenuItems.Add(_workspaceExternalAgentConfigImportMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceNewMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceOpenWslMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceAppServerFilesMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceProjectInstructionsMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceCreateAutomationMenuItem);
            _workspaceContextMenu.MenuItems.Add("-");
            _workspaceContextMenu.MenuItems.Add(_workspaceImportSyncMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceRestoreArchivedMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceShowUnavailableMenuItem);
            _workspaceContextMenu.MenuItems.Add("-");
            _workspaceContextMenu.MenuItems.Add(_workspaceRemoveMenuItem);
            _workspaceContextMenu.MenuItems.Add(_workspaceArchiveMenuItem);
            _workspaceContextMenu.Popup += WorkspaceContextMenu_Popup;

            _sessionNewMenuItem.Click += OnNewSessionButtonClick;
            _sessionRenameMenuItem.Click += OnSessionRenameMenuItemClick;
            _sessionStatisticsMenuItem.Click += OnSessionStatisticsMenuItemClick;
            _sessionSubAgentsMenuItem.Click += OnSessionSubAgentsMenuItemClick;
            _sessionCreateAutomationMenuItem.Click += (sender, e) => CreateAutomationFromCurrentSession();
            _sessionRemoveMenuItem.Click += OnSessionRemoveMenuItemClick;
            _sessionArchiveMenuItem.Click += OnSessionArchiveMenuItemClick;
            _sessionContextMenu.MenuItems.Add(_sessionNewMenuItem);
            _sessionContextMenu.MenuItems.Add(_sessionRenameMenuItem);
            _sessionContextMenu.MenuItems.Add(_sessionStatisticsMenuItem);
            _sessionContextMenu.MenuItems.Add(_sessionSubAgentsMenuItem);
            _sessionContextMenu.MenuItems.Add(_sessionCreateAutomationMenuItem);
            _sessionContextMenu.MenuItems.Add(_sessionAdvancedSeparatorMenuItem);
            _sessionContextMenu.MenuItems.Add(_sessionRemoveMenuItem);
            _sessionContextMenu.MenuItems.Add(_sessionArchiveMenuItem);
            ConfigureSessionWorkbenchContextMenu();
            _sessionContextMenu.Popup += SessionContextMenu_Popup;
        }

        private void OnUserManualMenuItemClick(object sender, EventArgs e)
        {
            const string chineseManualFileName = "\u7528\u6237\u624B\u518C.html";
            const string englishManualFileName = "UserManual.en.html";
            var helpDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "help");
            var preferChinese = string.Equals(
                CultureInfo.CurrentUICulture.TwoLetterISOLanguageName,
                "zh",
                StringComparison.OrdinalIgnoreCase);
            var preferredManualFileName = preferChinese ? chineseManualFileName : englishManualFileName;
            var fallbackManualFileName = preferChinese ? englishManualFileName : chineseManualFileName;
            var manualFileName = ResolveFirstExistingManualFileName(helpDirectoryPath, preferredManualFileName, fallbackManualFileName);
            if (string.IsNullOrWhiteSpace(manualFileName))
            {
                MessageBox.Show(
                    this,
                    string.Format(
                        UiLocalization.Text("MainForm.Message.UserManualNotFound.Template", "User manual not found. Please confirm help\\{0} exists in the application directory."),
                        preferredManualFileName + " / " + fallbackManualFileName),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var manualPath = Path.Combine(helpDirectoryPath, manualFileName);

            try
            {
                OpenExternalUrl(manualPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.OpenUserManualFailed", "Failed to open user manual"));
            }
        }

        private void OnAboutMenuItemClick(object sender, EventArgs e)
        {
            var currentVersion = GetCurrentAppVersion();
            var message = string.Format(
                UiLocalization.Text(
                    "MainForm.About.Message.Template",
                    "{0}\r\n\r\nVersion: {1}\r\nFramework: .NET Framework 4.8\r\n\r\nMyCodex desktop client."),
                AppConstants.AppName,
                currentVersion);

            MessageBox.Show(
                this,
                message,
                UiLocalization.Text("MainForm.About.Title", "About MyCodex"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnDonateMenuItemClick(object sender, EventArgs e)
        {
            using (var form = new DonateForm())
            {
                form.ShowDialog(this);
            }
        }

        private static string GetCurrentAppVersion()
        {
            try
            {
                var fileVersion = FileVersionInfo.GetVersionInfo(System.Windows.Forms.Application.ExecutablePath).ProductVersion;
                if (!string.IsNullOrWhiteSpace(fileVersion))
                {
                    return fileVersion.Trim();
                }
            }
            catch
            {
            }

            var version = typeof(MainForm).Assembly.GetName().Version;
            if (version == null)
            {
                return "0.0.0";
            }

            if (version.Revision > 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.{1}.{2}.{3}",
                    version.Major,
                    version.Minor,
                    Math.Max(0, version.Build),
                    version.Revision);
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}.{1}.{2}",
                version.Major,
                version.Minor,
                Math.Max(0, version.Build));
        }

        private static string ResolveFirstExistingManualFileName(string helpDirectoryPath, string preferredManualFileName, string fallbackManualFileName)
        {
            if (string.IsNullOrWhiteSpace(helpDirectoryPath))
            {
                return null;
            }

            var preferredPath = Path.Combine(helpDirectoryPath, preferredManualFileName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(preferredManualFileName) && File.Exists(preferredPath))
            {
                return preferredManualFileName;
            }

            var fallbackPath = Path.Combine(helpDirectoryPath, fallbackManualFileName ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(fallbackManualFileName) && File.Exists(fallbackPath))
            {
                return fallbackManualFileName;
            }

            return null;
        }

        private void OnExitMenuItemClick(object sender, EventArgs e)
        {
            RequestApplicationExit();
        }

        private async void OnOpenGlobalFileBrowserMenuItemClick(object sender, EventArgs e)
        {
            await OpenAppServerFilesFromDefaultRootAsync();
        }

        private string GetConfiguredUiLanguagePreference()
        {
            if (_appConfigSnapshot == null)
            {
                return UiLanguagePreference.Auto;
            }

            return UiLocalization.NormalizePreference(_appConfigSnapshot.UiLanguagePreference);
        }

        private string GetConfiguredUiThemePreference()
        {
            if (_appConfigSnapshot == null)
            {
                return UiThemePreference.Auto;
            }

            return UiThemePreference.Normalize(_appConfigSnapshot.UiThemePreference);
        }

        private void ApplyConfiguredUiLanguage()
        {
            UiLocalization.ApplyPreferredCulture(GetConfiguredUiLanguagePreference());
            ApplyMainFormLocalizedText();
            ApplySessionSettingsLocalizedText();
            RefreshWorkspaceView();
            RefreshSessionList();
            RefreshHeader();
            RefreshInputState();
            RefreshAuthenticationUi();
            SetLowPriorityStatus(UiLocalization.Text("Status.LanguageApplied", "Language applied."));
        }

        private void ApplyConfiguredTheme()
        {
            var mode = UiTheme.ResolveMode(
                GetConfiguredUiThemePreference(),
                SystemInformation.HighContrast,
                SystemThemeDetector.UsesDarkTheme());
            UiTheme.SetCurrentMode(mode);
            UiTheme.ApplyToControlTree(this);
            UiTheme.ApplyToOpenForms();
            UiTheme.ApplyStatusStrip(_statusStrip);
            _statusIndicator.Image = UiTheme.CreateStatusIndicator(UiTheme.StatusType.Ready);
            WindowDarkMode.TryApply(this, mode == UiThemeMode.Dark);
            Invalidate(true);
        }

        private void OnSystemUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            this.BeginInvokeSafe(delegate
            {
                var mode = UiTheme.ResolveMode(
                    GetConfiguredUiThemePreference(),
                    SystemInformation.HighContrast,
                    SystemThemeDetector.UsesDarkTheme());
                if (mode != UiTheme.CurrentMode)
                {
                    ApplyConfiguredTheme();
                }
            });
        }

        private void LoadAppConfig()
        {
            ClearWorkspaceAvailabilityCache();
            try
            {
                _appConfigSnapshot = _appConfigStore.Load() ?? new AppConfigSnapshot();
                var configurableService = _screenReaderService as IConfigurableScreenReaderService;
                if (configurableService != null)
                {
                    configurableService.ApplyConfig(_appConfigSnapshot);
                }
            }
            catch (Exception ex)
            {
                _appConfigSnapshot = new AppConfigSnapshot();
                AppendLog(string.Format(
                    UiLocalization.Text("MainForm.Log.LoadConfigFailed.Template", "Failed to load config: {0}"),
                    ex.Message));
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.LoadConfigFailedFallbackDefault", "Failed to load config. Default config is now used."));
                MarkPersistenceRecoveryPending(ex);
            }

            ApplyConfiguredTheme();
            RefreshActivityDisplay();
        }

        private void LoadPersistedState(bool preserveCurrentInteractionMode)
        {
            var preservedInteractionMode = _currentInteractionMode;
            _recentWorkspaces.Clear();
            _sessions.Clear();
            _inactiveRuntimeSessions.Clear();
            _currentWorkspace = null;
            _currentSession = null;

            AppStateSnapshot snapshot;
            try
            {
                var runtimeConfigStore = _appStateStore as IRuntimeConfigAppStateStore;
                snapshot = runtimeConfigStore != null
                    ? runtimeConfigStore.Load(GetConfiguredRuntimeConfigSnapshot())
                    : _appStateStore.Load();
            }
            catch (Exception ex)
            {
                AppendLog(string.Format(
                    UiLocalization.Text("MainForm.Log.LoadStateFailed.Template", "Failed to load state: {0}"),
                    ex.Message));
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.LoadStateFailedFallbackEmpty", "Failed to load state. Empty state is now used."));
                MarkPersistenceRecoveryPending(ex);
                return;
            }

            if (snapshot == null)
            {
                return;
            }

            _appStateSnapshot = snapshot;

            _hasCompletedThreadImportPrompt = snapshot.HasCompletedThreadImportPrompt;
            _currentInteractionMode = preserveCurrentInteractionMode
                ? preservedInteractionMode
                : _activeModeSettings.DefaultStartupMode;
            if (!string.IsNullOrWhiteSpace(snapshot.ThreadSyncSummary))
            {
                _threadSyncSummary = snapshot.ThreadSyncSummary;
            }

            var activeRuntimeProfileId = GetConfiguredRuntimeProfileId();
            var activeOfficeTarget = GetActiveOfficeRuntimeTarget();
            var activeOfficeState = _officeRuntimeStateService.MigrateLegacyState(
                snapshot,
                activeOfficeTarget);
            var activeProfileState = FindRuntimeProfileState(snapshot.RuntimeProfiles, activeRuntimeProfileId);
            _currentCodingWorkspaceId = activeProfileState != null && !string.IsNullOrWhiteSpace(activeProfileState.CurrentWorkspaceId)
                ? activeProfileState.CurrentWorkspaceId
                : !string.IsNullOrWhiteSpace(snapshot.CurrentCodingWorkspaceId)
                ? snapshot.CurrentCodingWorkspaceId
                : snapshot.CurrentWorkspaceId;
            _currentCodingSessionId = activeProfileState != null && !string.IsNullOrWhiteSpace(activeProfileState.CurrentSessionId)
                ? activeProfileState.CurrentSessionId
                : !string.IsNullOrWhiteSpace(snapshot.CurrentCodingSessionId)
                ? snapshot.CurrentCodingSessionId
                : snapshot.CurrentSessionId;
            _currentOfficeWorkspaceId = !string.IsNullOrWhiteSpace(activeOfficeState.CurrentWorkspaceId)
                ? activeOfficeState.CurrentWorkspaceId
                : snapshot.CurrentOfficeWorkspaceId;
            _currentOfficeSessionId = !string.IsNullOrWhiteSpace(activeOfficeState.CurrentSessionId)
                ? activeOfficeState.CurrentSessionId
                : snapshot.CurrentOfficeSessionId;

            var activeCodingWorkspaces = activeProfileState != null && activeProfileState.RecentWorkspaces != null
                ? activeProfileState.RecentWorkspaces
                : (_runtimeProfileService.IsSameRuntimeProfile(activeRuntimeProfileId, RuntimeProfileService.WindowsRuntimeProfileId)
                    ? snapshot.RecentWorkspaces
                    : null);
            AddActiveRuntimeWorkspaces(activeCodingWorkspaces, activeOfficeState.RecentWorkspaces);
            var activeCurrentWorkspaceId = _currentInteractionMode == AppInteractionMode.Office
                ? _currentOfficeWorkspaceId
                : _currentCodingWorkspaceId;
            var activeCurrentSessionId = _currentInteractionMode == AppInteractionMode.Office
                ? _currentOfficeSessionId
                : _currentCodingSessionId;

            var activeSessionIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var inactiveSessionIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (snapshot.Sessions != null)
            {
                foreach (var session in snapshot.Sessions)
                {
                    if (session == null || string.IsNullOrWhiteSpace(session.Id) || string.IsNullOrWhiteSpace(session.WorkspaceId))
                    {
                        continue;
                    }

                    _persistedSessionService.NormalizeRestoredSessionTimestamps(session);
                    session.RuntimeProfileId = _runtimeProfileService.NormalizeRuntimeProfileId(session.RuntimeProfileId);
                    if (session.Mode == AppInteractionMode.Office && string.IsNullOrWhiteSpace(session.OfficeRuntimeScopeId))
                    {
                        session.OfficeRuntimeScopeId = activeOfficeTarget.RuntimeScopeId;
                    }

                    var preferredSessionId = session.Mode == AppInteractionMode.Office
                        ? _currentOfficeSessionId
                        : _currentCodingSessionId;
                    if (SessionCollectionQueries.IsSessionInActiveRuntimeContexts(
                        session,
                        activeRuntimeProfileId,
                        activeOfficeTarget.RuntimeScopeId))
                    {
                        if (!HasWorkspace(session.WorkspaceId))
                        {
                            continue;
                        }

                        _persistedSessionService.AddOrReplacePersistedSession(
                            _sessions,
                            activeSessionIndexes,
                            session,
                            preferredSessionId);
                    }
                    else
                    {
                        _persistedSessionService.AddOrReplacePersistedSession(
                            _inactiveRuntimeSessions,
                            inactiveSessionIndexes,
                            session,
                            preferredSessionId);
                    }
                }
            }

            SortSessions();

            _currentSession = FindSessionById(activeCurrentSessionId);
            if (_currentSession != null && _currentSession.Mode != _currentInteractionMode)
            {
                _currentSession = null;
            }
            if (_currentSession != null)
            {
                _currentWorkspace = FindWorkspaceById(_currentSession.WorkspaceId);
            }

            if (_currentWorkspace == null)
            {
                _currentWorkspace = FindWorkspaceById(activeCurrentWorkspaceId);
            }

            if (_currentWorkspace == null && _recentWorkspaces.Count > 0)
            {
                _currentWorkspace = _recentWorkspaces[0];
            }

            EnsureCurrentWorkspaceMatchesVisibilityFilter();

            if (_currentInteractionMode == AppInteractionMode.Office)
            {
                RestoreOfficeSelectionAfterStateLoad();
            }
            else if (_currentSession == null)
            {
                RestoreCurrentSession();
            }

            CaptureCurrentModeSelection();
            ApplyModeVisibility();
        }

        private void ReloadLocalStateForCurrentRuntimeProfile(bool saveCurrentComposeState = true)
        {
            if (saveCurrentComposeState)
            {
                SaveCurrentComposeState();
            }

            LoadPersistedState(true);
            ApplyComposeState(_currentSession);
            RefreshWorkspaceView();
            RefreshSessionList();
            RefreshHeader();
            RefreshInputState();

            if (_currentSession == null)
            {
                ShowNoSessionMessageForCurrentWorkspace();
            }
        }

        private async Task ReloadLocalStateForCurrentRuntimeProfileAsync(bool saveCurrentComposeState = true)
        {
            ReloadLocalStateForCurrentRuntimeProfile(saveCurrentComposeState);
            if (_currentSession != null)
            {
                await LoadSessionHistoryAsync(_currentSession, true);
            }
        }

        private void ShowNoSessionMessageForCurrentWorkspace()
        {
            ClearChatHistoryItems();
            if (_currentWorkspace == null)
            {
                AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), UiLocalization.Text("MainForm.Message.OpenWorkspaceBeforeSession", "Please open a workspace first, then start a new session."));
                return;
            }

            AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), GetNoSessionMessageForCurrentWorkspace());
        }

        private string GetNoSessionMessageForCurrentWorkspace()
        {
            return _currentInteractionMode == AppInteractionMode.Office
                ? UiLocalization.Text("MainForm.Message.OfficeNewTaskReady", "Ready for a new Office task. Type directly to start a new session, or choose a history session from the session list.")
                : UiLocalization.Text("MainForm.Message.NoSessionsInWorkspace", "There are no sessions in the current workspace. You can click \"New Session\" or type directly to create one.");
        }

        private string GetStartupWorkspacePath()
        {
            if (_currentWorkspace != null && !string.IsNullOrWhiteSpace(_currentWorkspace.RootPath))
            {
                return _currentWorkspace.RootPath;
            }

            foreach (var workspace in _recentWorkspaces)
            {
                if (IsWorkspaceAvailable(workspace))
                {
                    return workspace.RootPath;
                }
            }

            if (_recentWorkspaces.Count > 0)
            {
                return _recentWorkspaces[0].RootPath;
            }

            return null;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            var workingArea = Screen.FromRectangle(Bounds).WorkingArea;
            MinimumSize = MainFormLayoutPlanner.ConstrainMinimumSize(MinimumSize, workingArea);
            Bounds = MainFormLayoutPlanner.ConstrainWindowBounds(Bounds, workingArea);
            ApplySplitLayout();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            ApplySplitLayout();
        }

        private void ApplySplitLayout()
        {
            if (_mainSplitContainer == null || _contentSplitContainer == null)
            {
                return;
            }

            if (_mainSplitContainer.Width <= 0 || _contentSplitContainer.Width <= 0)
            {
                return;
            }

            try
            {
                _mainSplitContainer.FixedPanel = FixedPanel.Panel1;
                _mainSplitContainer.Panel1MinSize = UiTheme.Scale(MainFormLayoutPlanner.MinimumNavigationWidth);
                _mainSplitContainer.Panel2MinSize = UiTheme.Scale(MainFormLayoutPlanner.MinimumConversationWidth + MainFormLayoutPlanner.SplitterWidth + MainFormLayoutPlanner.MinimumActivityWidth);

                _mainSplitContainer.SplitterDistance = MainFormLayoutPlanner.ResolveMainSplitterDistance(
                    _mainSplitContainer.Width,
                    _appStateSnapshot?.MainSplitterDistance);

                _contentSplitContainer.FixedPanel = FixedPanel.Panel2;
                _contentSplitContainer.Panel1MinSize = UiTheme.Scale(MainFormLayoutPlanner.MinimumConversationWidth);
                _contentSplitContainer.Panel2MinSize = UiTheme.Scale(MainFormLayoutPlanner.MinimumActivityWidth);

                _contentSplitContainer.SplitterDistance = MainFormLayoutPlanner.ResolveContentSplitterDistance(
                    _contentSplitContainer.Width,
                    _appStateSnapshot?.ContentSplitterDistance);
            }
            catch
            {
                // Keep current layout when adjustment fails to avoid impacting main workflow.
            }
        }

        private void OnMainSplitterMoving(object sender, SplitterCancelEventArgs e)
        {
            _isMainSplitterMoving = true;
        }

        private void OnMainSplitterMoved(object sender, SplitterEventArgs e)
        {
            if (_isMainSplitterMoving && _appStateSnapshot != null)
            {
                _appStateSnapshot.MainSplitterDistance = UiTheme.Unscale(_mainSplitContainer.SplitterDistance);
            }

            _isMainSplitterMoving = false;
        }

        private void OnContentSplitterMoving(object sender, SplitterCancelEventArgs e)
        {
            _isContentSplitterMoving = true;
        }

        private void OnContentSplitterMoved(object sender, SplitterEventArgs e)
        {
            if (_isContentSplitterMoving && _appStateSnapshot != null)
            {
                _appStateSnapshot.ContentSplitterDistance = UiTheme.Unscale(_contentSplitContainer.SplitterDistance);
            }

            _isContentSplitterMoving = false;
        }

        private void ActivitySettingsMenuItem_Click(object sender, EventArgs e)
        {
            if (_appConfigSnapshot == null)
            {
                _appConfigSnapshot = new AppConfigSnapshot();
            }

            using (var form = new ActivitySettingsForm(_appConfigSnapshot))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _appConfigSnapshot.ShowActivityThinkingMessages = form.ShowThinkingMessages;
                _appConfigSnapshot.ShowActivityPlanMessages = form.ShowPlanMessages;
                _appConfigSnapshot.ShowActivityToolMessages = form.ShowToolMessages;
                _appConfigSnapshot.ShowActivityCommandMessages = form.ShowCommandMessages;
                _appConfigSnapshot.ShowActivityFileMessages = form.ShowFileMessages;
                _appConfigSnapshot.ShowActivityNoticeMessages = form.ShowNoticeMessages;
                _appConfigSnapshot.ShowActivityErrorMessages = form.ShowErrorMessages;
                _appConfigSnapshot.ShowActivityApprovalMessages = form.ShowApprovalMessages;
                _appConfigSnapshot.ShowActivityAccountMessages = form.ShowAccountMessages;
                _appConfigSnapshot.ShowActivityProtocolNoticeMessages = form.ShowProtocolNoticeMessages;
                _appConfigSnapshot.ShowActivityProtocolWarningMessages = form.ShowProtocolWarningMessages;
                _appConfigSnapshot.ClearActivityOnNewTurn = form.ClearOnNewTurn;

                if (!SaveAppConfig())
                {
                    return;
                }

                RefreshActivityDisplay();
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.ActivitySettingsSaved", "Activity message settings saved"));
            }
        }

        private void ClearActivityMenuItem_Click(object sender, EventArgs e)
        {
            foreach (var state in _threadActivityTimelineStates.Values)
            {
                if (state != null)
                {
                    state.Clear();
                }
            }

            if (_currentSession != null && !string.IsNullOrWhiteSpace(_currentSession.ThreadId))
            {
                EnsureThreadActivityTimelineState(_currentSession.ThreadId).Clear();
            }

            _activityDisplayRefreshState.ResetRenderedText();
            RefreshActivityDisplay();
            SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.ActivityCleared", "Activity history cleared"));
        }

        private void LogContextMenu_Popup(object sender, EventArgs e)
        {
            _clearActivityMenuItem.Enabled = HasActivityTimelineContent();
        }

        private async void InputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.V && await TryPasteAttachmentFromClipboardAsync())
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            var action = SendMessageShortcutPlanner.ResolveAction(GetConfiguredSendMessageShortcutMode(), e.KeyCode, e.Control);
            if (action == SendMessageShortcutAction.None || action == SendMessageShortcutAction.NewLine && !e.Control)
            {
                return;
            }

            e.SuppressKeyPress = true;
            e.Handled = true;
            if (action == SendMessageShortcutAction.NewLine)
            {
                InsertInputNewLine();
                return;
            }

            await SendCurrentInputAsync();
        }

        private string GetConfiguredSendMessageShortcutMode()
        {
            return SendMessageShortcutMode.Normalize(_appConfigSnapshot == null ? null : _appConfigSnapshot.SendMessageShortcutMode);
        }

        private void InsertInputNewLine()
        {
            _inputBox.InsertNewLine();
            SynchronizePendingAttachmentsWithInputText(true);
        }

        private void InputBox_TextChanged(object sender, EventArgs e)
        {
            if (_isUpdatingInputBoxText)
            {
                return;
            }

            SynchronizePendingAttachmentsWithInputText(true);
        }

        private void OnSessionListSelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isRefreshingSessionList)
            {
                return;
            }

            var selectedSession = GetSelectedSessionListItem();
            if (selectedSession == null)
            {
                return;
            }

            if (_currentSession != null &&
                string.Equals(selectedSession.Session.Id, _currentSession.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

        }

        private static void SelectListItemOnRightClick(ListBox listBox, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || listBox == null)
            {
                return;
            }

            var index = listBox.IndexFromPoint(e.Location);
            if (index >= 0)
            {
                var chatHistoryListBox = listBox as ChatHistoryListBox;
                if (chatHistoryListBox != null)
                {
                    if (chatHistoryListBox.GetSelected(index))
                    {
                        chatHistoryListBox.FocusIndex(index);
                    }
                    else
                    {
                        chatHistoryListBox.SelectSingleIndex(index);
                    }

                    return;
                }

                listBox.SelectedIndex = index;
                return;
            }

            listBox.ClearSelected();
        }

        private static void SelectListViewItemOnRightClick(ListView listView, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right || listView == null)
            {
                return;
            }

            var hit = listView.HitTest(e.Location);
            listView.SelectedItems.Clear();
            if (hit.Item == null)
            {
                return;
            }

            hit.Item.Selected = true;
            hit.Item.Focused = true;
        }

        private void ApplyMainFormRefreshPreset(MainFormRefreshPreset preset)
        {
            var plan = MainFormRefreshPlanner.Build(preset);
            if (plan.RefreshWorkspaceView)
            {
                RefreshWorkspaceView();
            }

            if (plan.RefreshSessionList)
            {
                RefreshSessionList();
            }

            if (plan.RefreshHeader)
            {
                RefreshHeader();
            }

            if (plan.RefreshInputState)
            {
                RefreshInputState();
            }

            if (plan.PersistState)
            {
                PersistState();
            }
        }

        private void OnPlanModeMenuItemClick(object sender, EventArgs e)
        {
            if (!CanToggleSessionPlanMode(_currentSession))
            {
                return;
            }

            SetSessionPlanMode(_currentSession, !_currentSession.IsPlanModeEnabled, true);
        }

        private void FileMenuItem_Popup(object sender, EventArgs e)
        {
            var isWindowsTarget = IsCurrentExecutionTargetWindows();
            var isOfficeMode = _currentInteractionMode == AppInteractionMode.Office;
            _openWorkspaceFileMenuItem.Text = isOfficeMode
                ? UiLocalization.Text("MainForm.File.OpenOfficeDirectory", "Open Office Directory(&O)...")
                : UiLocalization.Text("MainMenu.OpenWorkspace", "&Open Workspace...");
            _openWorkspaceFileMenuItem.Enabled = !isOfficeMode ||
                (!string.IsNullOrWhiteSpace(_resolvedOfficeRootPath) &&
                 string.IsNullOrWhiteSpace(_officeRuntimeRootError));
            _openWslWorkspaceFileMenuItem.Visible = !isOfficeMode && IsWslRuntimeEnabled();
            _openWslWorkspaceFileMenuItem.Enabled = _openWslWorkspaceFileMenuItem.Visible;
            _openGlobalFileBrowserMenuItem.Enabled = true;
            _newSessionFileMenuItem.Enabled = IsCurrentWorkspaceAvailable();
            _fileSyncThreadsMenuItem.Enabled = !isOfficeMode && IsConnectedStatus();
            _fileImportOtherSessionsMenuItem.Enabled =
                !isOfficeMode &&
                _currentWorkspace != null &&
                !string.IsNullOrWhiteSpace(_currentWorkspace.RootPath);
            _fileExternalAgentConfigImportMenuItem.Enabled = !isOfficeMode && IsConnectedStatus();
            _fileImportSyncMenuItem.Enabled =
                !isOfficeMode &&
                (_fileSyncThreadsMenuItem.Enabled ||
                 _fileImportOtherSessionsMenuItem.Enabled ||
                 _fileExternalAgentConfigImportMenuItem.Enabled);
            _targetReloadMenuItem.Visible = isWindowsTarget;
            _targetReloadMenuItem.Enabled = isWindowsTarget;
        }

        private void OpenSubAgentWorkbench(ChatSession parentSession, IReadOnlyList<ChatSession> subAgents)
        {
            if (_isRuntimeWindowTransitionInProgress ||
                parentSession == null || string.IsNullOrWhiteSpace(parentSession.Id))
            {
                return;
            }

            SubAgentWorkbenchForm existingForm;
            if (_subAgentWorkbenches.TryGetValue(parentSession.Id, out existingForm) &&
                existingForm != null &&
                !existingForm.IsDisposed)
            {
                existingForm.RefreshSubAgents(subAgents);
                existingForm.Activate();
                return;
            }

            var workspace = FindWorkspaceById(parentSession.WorkspaceId) ?? _currentWorkspace;
            var form = new SubAgentWorkbenchForm(
                parentSession,
                subAgents,
                CreateSubAgentWorkbenchOperations(workspace, parentSession));
            _subAgentWorkbenches[parentSession.Id] = form;
            form.FormClosed += (sender, args) =>
            {
                SubAgentWorkbenchForm registeredForm;
                if (_subAgentWorkbenches.TryGetValue(parentSession.Id, out registeredForm) &&
                    ReferenceEquals(registeredForm, sender))
                {
                    _subAgentWorkbenches.Remove(parentSession.Id);
                }
            };
            form.Show(this);
        }

        private SubAgentWorkbenchOperations CreateSubAgentWorkbenchOperations(WorkspaceInfo workspace, ChatSession parentSession)
        {
            return new SubAgentWorkbenchOperations
            {
                Workspace = workspace,
                HistoryLoadWorkflow = new SessionHistoryLoadWorkflow(
                    new SessionHistoryStateStore(),
                    _sessionHistoryDisplayBuilder,
                    ReadSubAgentThreadAsync,
                    SessionThreadService.IsMissingThreadRolloutError,
                    IsAppServerConnectivityFailure),
                WorkbenchCallbacks = CreateSubAgentWorkbenchCallbacks(),
                SendAsync = (session, request) => SendSubAgentInputAsync(session, workspace, request),
                StopAsync = StopSubAgentAsync,
                CanStop = session => session != null && CanInterruptThread(session.ThreadId),
                IsWorking = session => session != null && IsThreadBusy(session.ThreadId),
                DeleteAsync = DeleteSubAgentsAsync,
                PersistState = PersistState,
                RefreshAsync = () => RefreshSubAgentParentAsync(parentSession),
                GetSummary = threadId => _subAgentSummaries.TryGetValue(threadId, out var summary) ? summary : null,
                GetRecentWork = threadId => _subAgentRecentWork.TryGetValue(threadId, out var work) ? work : null,
                GetAgentStatus = threadId => _subAgentStatuses.TryGetValue(threadId, out var agentStatus) ? agentStatus : null
            };
        }

        private ChatWorkbenchHostCallbacks CreateSubAgentWorkbenchCallbacks()
        {
            return new ChatWorkbenchHostCallbacks
            {
                SelectImageAttachmentsAsync = SelectSubAgentWorkbenchImageAttachmentsAsync,
                SelectFileAttachmentsAsync = SelectSubAgentWorkbenchFileAttachmentsAsync,
                SelectFolderAttachmentsAsync = SelectSubAgentWorkbenchFolderAttachmentsAsync,
                SelectSkillAttachmentsAsync = SelectSubAgentWorkbenchSkillAttachmentsAsync,
                SelectPluginAttachmentsAsync = SelectSubAgentWorkbenchPluginAttachmentsAsync,
                CanPasteAttachments = CanPasteSubAgentWorkbenchAttachments,
                PasteAttachmentsAsync = PasteSubAgentWorkbenchAttachmentsAsync,
                ReadImageBytes = GetImageAttachmentBytes,
                ValidateAttachmentsAsync = ValidateSubAgentWorkbenchAttachmentsAsync,
                OpenMessageDetail = OpenChatMessageDetail,
                CanSend = () => true,
                CanStop = () => false,
                GetSendMessageShortcutMode = GetConfiguredSendMessageShortcutMode,
                DeleteTemporaryAttachment = DeleteTemporaryAttachmentFile
            };
        }

        private async Task StopSubAgentAsync(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return;
            }

            var threadId = session.ThreadId;
            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("SubAgentWorkbenchForm.Action.Stop", "Stop subagent response")))
            {
                return;
            }

            var client = _appServerClient as IAppServerSubAgentClient;
            if (client == null) throw new NotSupportedException();
            await client.InterruptSubAgentTurnAsync(threadId, System.Threading.CancellationToken.None);
        }

        private static List<ChatSession> NormalizeSubAgentDeletionTargets(IEnumerable<ChatSession> sessions)
        {
            if (sessions == null)
            {
                return new List<ChatSession>();
            }

            return sessions
                .Where(session => session != null && SessionListProjection.IsSubAgent(session))
                .GroupBy(BuildSubAgentDeletionIdentityKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static string BuildSubAgentDeletionIdentityKey(ChatSession session)
        {
            if (session == null)
            {
                return string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(session.Id))
            {
                return "id:" + session.Id;
            }

            return "thread:" + (session.ThreadId ?? string.Empty);
        }

        private void TraceSendDiagnostic(string stage, string details)
        {
            try
            {
                Trace.TraceInformation("[SendCurrentInput] {0}: {1}", stage, details ?? string.Empty);
            }
            catch
            {
            }
        }

        private void TraceSendDiagnosticError(string stage, Exception exception)
        {
            try
            {
                Trace.TraceError("[SendCurrentInput] {0}: {1}", stage, exception);
            }
            catch
            {
            }
        }

        private string BuildSendDiagnosticContext()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "runtime={0}; runtimeProfile={1}; source={2}; connection={3}; workspaceId={4}; workspaceRoot={5}; sessionId={6}; threadId={7}",
                FormatDiagnosticValue(_appConfigSnapshot != null ? _appConfigSnapshot.CodexRuntimeSource : null),
                FormatDiagnosticValue(GetCurrentRuntimeProfileIdForDiagnostics()),
                FormatDiagnosticValue(null),
                FormatDiagnosticValue(_connectionStatus),
                FormatDiagnosticValue(_currentWorkspace != null ? _currentWorkspace.Id : null),
                FormatDiagnosticValue(_currentWorkspace != null ? _currentWorkspace.RootPath : null),
                FormatDiagnosticValue(_currentSession != null ? _currentSession.Id : null),
                FormatDiagnosticValue(_currentSession != null ? _currentSession.ThreadId : null));
        }

        private static string FormatDiagnosticValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(empty)" : value.Trim();
        }

        private Task ApplySessionSendCompletionPlanAsync(SessionSendCompletionPlan completionPlan, ChatSession session, bool acknowledged = true)
        {
            if (completionPlan == null)
            {
                return Task.CompletedTask;
            }

            if (completionPlan.RequiresThreadRecreationUiRefresh)
            {
                ApplySessionThreadRecreationUiState(completionPlan.PreviousThreadIdToReset, session);
            }

            var shouldApplyVisibleUi = MainFormSendDispatchPlanner.ShouldApplyVisibleSessionUi(
                session != null ? session.Id : null,
                _currentSession != null ? _currentSession.Id : null);
            if (shouldApplyVisibleUi && !string.IsNullOrWhiteSpace(completionPlan.HistoryMessage))
            {
                AddChatHistoryItem(ChatHistoryItemFactory.GetSystemSenderText(), completionPlan.HistoryMessage);
            }

            if (shouldApplyVisibleUi && !string.IsNullOrWhiteSpace(completionPlan.StatusText))
            {
                SetStatus(completionPlan.StatusText);
            }

            completionPlan.ApplyLocalSessionTitleUpdate(session);
            if (acknowledged) ScheduleSendMetadataUpdate(session);
            return Task.CompletedTask;
        }

        private void ApplySessionThreadRecreationUiState(
            SessionThreadRecreationResult recreation,
            ChatSession session)
        {
            ApplySessionThreadRecreationUiState(
                recreation != null ? recreation.PreviousThreadId : null,
                session);
        }

        private void ApplySessionThreadRecreationUiState(string previousThreadId, ChatSession session)
        {
            ResetThreadVolatileState(previousThreadId);
            PersistState();
            if (MainFormSendDispatchPlanner.ShouldApplyVisibleSessionUi(
                session != null ? session.Id : null,
                _currentSession != null ? _currentSession.Id : null))
            {
                RefreshHeader();
                RefreshSessionList();
                RefreshInputState();
            }
        }

        private AppServerCollaborationMode BuildCollaborationModeForSend(ChatSession session, WorkspaceInfo workspace, string modelId, string reasoningEffort)
        {
            if (session == null)
            {
                return null;
            }

            if (IsOfficeSessionOrWorkspace(session, workspace))
            {
                var officeRootPath = ResolveOfficeRootPathForSession(session, workspace);
                var officeWorkspacePath = workspace == null ? null : workspace.RootPath;

                // App-server currently exposes collaboration modes as "default" and "plan".
                // Office mode is represented as default/plan plus MyCodex Office developer instructions.
                return CreateOfficeCollaborationMode(
                    session.IsPlanModeEnabled ? "plan" : "default",
                    modelId,
                    reasoningEffort,
                    officeRootPath,
                    officeWorkspacePath);
            }

            if (session.IsPlanModeEnabled)
            {
                return CreateCollaborationMode("plan", modelId, reasoningEffort);
            }

            return !string.IsNullOrWhiteSpace(session.Id) &&
                   _sessionsPendingDefaultCollaborationMode.Contains(session.Id)
                ? CreateCollaborationMode("default", modelId, reasoningEffort)
                : null;
        }

        private Task TryUpdateThreadWorkspaceMetadataAsync(ChatSession session, WorkspaceInfo workspace)
        {
            if (session == null || IsOfficeSessionOrWorkspace(session, workspace))
            {
                return Task.CompletedTask;
            }

            return _sessionThreadService.TryUpdateThreadGitMetadataAsync(session, workspace);
        }

        private static bool IsOfficeSessionOrWorkspace(ChatSession session, WorkspaceInfo workspace)
        {
            if (session != null && session.Mode == AppInteractionMode.Office)
            {
                return true;
            }

            if (workspace != null && workspace.Kind == WorkspaceKind.Office)
            {
                return true;
            }

            if (workspace != null &&
                !string.IsNullOrWhiteSpace(workspace.Id) &&
                workspace.Id.StartsWith("office:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return session != null &&
                   !string.IsNullOrWhiteSpace(session.WorkspaceId) &&
                   session.WorkspaceId.StartsWith("office:", StringComparison.OrdinalIgnoreCase);
        }

        private static AppServerCollaborationMode CreateCollaborationMode(string mode, string modelId, string reasoningEffort)
        {
            return new AppServerCollaborationMode
            {
                Mode = mode,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                DeveloperInstructions = null
            };
        }

        private static AppServerCollaborationMode CreateOfficeCollaborationMode(
            string mode,
            string modelId,
            string reasoningEffort,
            string officeRootPath,
            string officeWorkspacePath)
        {
            return new AppServerCollaborationMode
            {
                Mode = mode,
                ModelId = modelId,
                ReasoningEffort = reasoningEffort,
                DeveloperInstructions = BuildOfficeDeveloperInstructions(
                    officeRootPath,
                    officeWorkspacePath)
            };
        }

        private static string BuildOfficeDeveloperInstructions()
        {
            return OfficeDeveloperInstructionComposer.Compose();
        }

        private static string BuildOfficeDeveloperInstructions(
            string officeRootPath,
            string officeWorkspacePath)
        {
            return OfficeDeveloperInstructionComposer.Compose(
                officeRootPath,
                officeWorkspacePath);
        }

        private static PlanSlashCommand ParsePlanSlashCommand(string text)
        {
            var trimmed = (text ?? string.Empty).TrimStart();
            if (!trimmed.StartsWith("/", StringComparison.Ordinal))
            {
                return PlanSlashCommand.None;
            }

            string promptText;
            if (TryParsePlanSlashCommand(trimmed, "/plan", out promptText) ||
                TryParsePlanSlashCommand(trimmed, "/plan-mode", out promptText))
            {
                return new PlanSlashCommand(true, promptText);
            }

            return PlanSlashCommand.None;
        }

        private static bool TryParsePlanSlashCommand(string text, string command, out string promptText)
        {
            promptText = null;
            if (text.Equals(command, StringComparison.OrdinalIgnoreCase))
            {
                promptText = string.Empty;
                return true;
            }

            if (text.Length <= command.Length ||
                !text.StartsWith(command, StringComparison.OrdinalIgnoreCase) ||
                !char.IsWhiteSpace(text[command.Length]))
            {
                return false;
            }

            promptText = text.Substring(command.Length).TrimStart();
            return true;
        }

        private async void OnStopButtonClick(object sender, EventArgs e)
        {
            if (_currentSession == null || string.IsNullOrWhiteSpace(_currentSession.ThreadId))
            {
                return;
            }

            var threadId = _currentSession.ThreadId;
            var fallbackTurnId = GetInterruptibleTurnId(threadId);
            if (!CanInterruptThread(threadId))
            {
                SetStatus(UiLocalization.Text("MainForm.Status.NoInterruptibleResponse", "There is no in-progress response to stop"));
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.StopResponse", "Stop response")))
            {
                return;
            }

            try
            {
                SetStatus(UiLocalization.Text("MainForm.Status.RequestingStop", "Requesting stop..."));
                await _appServerClient.InterruptAsync(threadId, fallbackTurnId);
                AnnounceConversationStopped();
            }
            catch (Exception ex)
            {
                AddChatHistoryItem(ChatHistoryItemFactory.GetErrorSenderText(), ex.Message, ChatHistoryCategory.Error);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.StopFailed", "Stop failed"));
            }
        }

        private void AnnounceConversationStopped()
        {
            SpeakAuxiliaryTextWithScreenReader(UiLocalization.Text(
                "MainForm.ScreenReader.ConversationStopped",
                "Conversation stopped."));
        }

        private async void OnUserCenterMenuItemClick(object sender, EventArgs e)
        {
            if (!IsChatGptAccountAuthenticationActive())
            {
                SetStatus(UiLocalization.Text(
                    "MainForm.Status.UserCenterUnavailableForApi",
                    "User Center is only available when signed in with a ChatGPT account."));
                MessageBox.Show(
                    this,
                    UiLocalization.Text(
                        "MainForm.Message.UserCenterUnavailableForApi",
                        "User Center is only available when the current authentication is a ChatGPT account."),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (!await EnsureAppServerAvailableForActionAsync(UiLocalization.Text("MainForm.Action.OpenUserCenter", "Open user center")))
            {
                return;
            }

            using (var form = new UserCenterForm(
                       _appServerClient,
                       _authenticationState,
                       _screenReaderService))
            {
                form.ShowDialog(this);
            }

            RefreshHeader();
            RefreshInputState();
        }

        private async void OnGoalSetMenuItemClick(object sender, EventArgs e)
        {
            if (_currentSession == null)
            {
                return;
            }

            await SetSessionGoalAsync(_currentSession);
        }

        private async void OnGoalViewMenuItemClick(object sender, EventArgs e)
        {
            if (_currentSession == null)
            {
                return;
            }

            await ViewSessionGoalAsync(_currentSession);
        }

        private async void OnGoalClearMenuItemClick(object sender, EventArgs e)
        {
            if (_currentSession == null)
            {
                return;
            }

            await ClearSessionGoalAsync(_currentSession);
        }

        private async Task RenameSessionAsync(ChatSession session, string title)
        {
            if (session == null)
            {
                return;
            }

            var normalizedTitle = string.IsNullOrWhiteSpace(title) ? string.Empty : title.Trim();
            if (string.IsNullOrWhiteSpace(normalizedTitle))
            {
                throw new InvalidOperationException(UiLocalization.Text("MainForm.SessionTitle.Validation.EnterPaintingTitle", "Please enter a session title."));
            }

            var currentTitle = string.IsNullOrWhiteSpace(session.Title) ? string.Empty : session.Title.Trim();
            if (string.Equals(currentTitle, normalizedTitle, StringComparison.Ordinal))
            {
                SetLowPriorityStatus(UiLocalization.Text("MainForm.Status.PaintingTitleUnchanged", "Session title unchanged"));
                return;
            }

            session.Title = normalizedTitle;
            string remoteSyncError = null;
            if (!string.IsNullOrWhiteSpace(session.ThreadId))
            {
                try
                {
                    await _appServerClient.SetThreadNameAsync(session.ThreadId, normalizedTitle);
                }
                catch (Exception ex)
                {
                    remoteSyncError = ex.Message;
                }
            }

            SortSessions();
            RefreshSessionList();
            if (_currentSession == null || !string.Equals(_currentSession.Id, session.Id, StringComparison.OrdinalIgnoreCase))
            {
                SelectSessionInList(session.Id);
            }

            RefreshHeader();
            RefreshDisplayedSessionTitle(session);
            PersistState();

            if (string.IsNullOrWhiteSpace(remoteSyncError))
            {
                SetLowPriorityStatus(string.Format(
                    UiLocalization.Text("MainForm.Status.PaintingTitleUpdated.Template", "Updated session title: {0}"),
                    SessionDisplayFormatter.BuildDisplayTitle(session)));
                return;
            }

            SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.PaintingTitleUpdatedRemoteSyncFailed", "Session title updated locally, but remote sync failed"));
            MessageBox.Show(
                this,
                string.Format(
                    UiLocalization.Text("MainForm.Message.PaintingTitleRemoteSyncFailed.Template", "Session title was updated locally, but failed to sync to official thread name: {0}"),
                    remoteSyncError),
                AppConstants.AppName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private string ResolveOfficeRootPathForHistorySync()
        {
            return _resolvedOfficeRootPath;
        }

        private string ResolveOfficeRootPathForSession(ChatSession session, WorkspaceInfo workspace)
        {
            var target = GetActiveOfficeRuntimeTarget();
            var sessionScopeId = session != null && !string.IsNullOrWhiteSpace(session.OfficeRuntimeScopeId)
                ? session.OfficeRuntimeScopeId
                : target.RuntimeScopeId;
            if (!string.Equals(sessionScopeId, target.RuntimeScopeId, StringComparison.OrdinalIgnoreCase) ||
                workspace == null ||
                !IsOfficeWorkspaceCompatibleWithActiveRuntime(workspace))
            {
                return null;
            }

            return _resolvedOfficeRootPath;
        }

        private bool IsSameOrChildPath(string candidatePath, string rootPath)
        {
            var filtered = ThreadImportWorkflowPlanner.FilterThreadsForInteractionMode(
                new[] { new AppServerThreadSummary { ThreadId = "path-check", WorkingDirectory = candidatePath } },
                AppInteractionMode.Office,
                rootPath);
            return filtered.Count == 1;
        }

        private ChatSession FindSessionByThreadId(string threadId)
        {
            return SessionCollectionQueries.FindSessionByThreadId(_sessions, threadId);
        }

        private WorkspaceInfo FindWorkspaceById(string workspaceId)
        {
            return SessionCollectionQueries.FindWorkspaceById(_recentWorkspaces, workspaceId);
        }

        private List<string> GetImportedThreadIds()
        {
            return SessionCollectionQueries.GetThreadIds(_sessions, _currentInteractionMode);
        }

        private bool HasImportedSessions()
        {
            return SessionCollectionQueries.HasImportedSessions(_sessions, _currentInteractionMode);
        }

        private void ShowPopupMenuBelowControl(ContextMenu menu, Control control, bool restoreFocusOnCollapse = false)
        {
            if (menu == null || control == null)
            {
                return;
            }

            if (restoreFocusOnCollapse)
            {
                menu.Collapse += RestoreFocusOnCollapse;
            }

            menu.Show(control, new Point(0, control.Height));

            void RestoreFocusOnCollapse(object sender, EventArgs e)
            {
                menu.Collapse -= RestoreFocusOnCollapse;
                if (control.IsDisposed || control.Disposing || !control.CanFocus)
                {
                    return;
                }

                if (!IsDisposed && !Disposing && IsHandleCreated)
                {
                    if (this.BeginInvokeSafe(() =>
                    {
                        if (!control.IsDisposed && !control.Disposing && control.CanFocus)
                        {
                            control.Focus();
                        }
                    }))
                    {
                        return;
                    }
                }

                control.Focus();
            }
        }

        private void ShowPopupMenuForListBoxSelection(ContextMenu menu, ListBox listBox)
        {
            if (menu == null || listBox == null)
            {
                return;
            }

            var selectedIndex = listBox.SelectedIndex;
            var chatHistoryListBox = listBox as ChatHistoryListBox;
            if (chatHistoryListBox != null)
            {
                selectedIndex = chatHistoryListBox.FocusedIndex;
            }

            if (selectedIndex >= 0)
            {
                var rect = listBox.GetItemRectangle(selectedIndex);
                menu.Show(listBox, new Point(rect.Left, rect.Bottom));
                return;
            }

            menu.Show(listBox, new Point(0, Math.Min(listBox.Height, 24)));
        }

        private void ShowPopupMenuForListViewSelection(ContextMenu menu, ListView listView)
        {
            if (menu == null || listView == null)
            {
                return;
            }

            if (listView.SelectedItems.Count > 0)
            {
                var rect = listView.SelectedItems[0].Bounds;
                menu.Show(listView, new Point(rect.Left, rect.Bottom));
                return;
            }

            menu.Show(listView, new Point(0, Math.Min(listView.Height, 24)));
        }

        private void CopySelectedMenuItem_Click(object sender, EventArgs e)
        {
            CopySelectedChatItemToClipboard();
        }

        private async void ForkFullMenuItem_Click(object sender, EventArgs e)
        {
            await StartFullForkFromCurrentSessionAsync();
        }

        private async void ForkFromHereMenuItem_Click(object sender, EventArgs e)
        {
            await StartForkFromSelectedHistoryItemAsync();
        }

        private async void CompactChatHistoryMenuItem_Click(object sender, EventArgs e)
        {
            await StartCurrentSessionCompactionAsync();
        }

        private async void ReviewChatHistoryMenuItem_Click(object sender, EventArgs e)
        {
            await ShowReviewStartDialogAsync(_currentSession);
        }

        private bool SaveAppConfig()
        {
            if (_isPersistenceRecoveryPending)
            {
                return false;
            }

            try
            {
                _appConfigStore.Save(_appConfigSnapshot);
                ClearWorkspaceAvailabilityCache();
                var configurableService = _screenReaderService as IConfigurableScreenReaderService;
                if (configurableService != null)
                {
                    configurableService.ApplyConfig(_appConfigSnapshot);
                }

                return true;
            }
            catch (PersistenceRecoveryRequiredException ex)
            {
                AppendLog(UiLocalization.Text("MainForm.Log.SaveConfigFailedPrefix", "Save config failed: ") + ex.Message);
                MarkPersistenceRecoveryPending(ex);
                return false;
            }
            catch (Exception ex)
            {
                AppendLog(UiLocalization.Text("MainForm.Log.SaveConfigFailedPrefix", "Save config failed: ") + ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SaveConfigFailedPrefix", "Save config failed: ") + ex.Message);
                return false;
            }
        }

        private void CompleteStandaloneCommand(CommandTask commandTask, AppServerCommandExecResult result)
        {
            if (commandTask == null)
            {
                return;
            }

            commandTask.ExitCode = result != null ? result.ExitCode : -1;
            commandTask.Status = commandTask.ExitCode == 0 ? "completed" : (commandTask.TerminationRequested ? "terminated" : "failed");
            commandTask.IsWaitingForInput = false;
            commandTask.Duration = DateTime.Now - commandTask.StartedAt;

            AppendBufferedCommandOutput(commandTask, result != null ? result.StdOut : null, "stdout");
            AppendBufferedCommandOutput(commandTask, result != null ? result.StdErr : null, "stderr");

            AppendLog(string.Format(UiLocalization.Text("MainForm.Log.Command.Template", "Command: {0}"), commandTask.CommandText));
            AppendLog(string.Format(UiLocalization.Text("MainForm.Log.Directory.Template", "Directory: {0}"), commandTask.WorkingDirectory));
            AppendLog(string.Format(UiLocalization.Text("MainForm.Log.DurationSeconds.Template", "Duration: {0:F1} seconds"), commandTask.Duration.HasValue ? commandTask.Duration.Value.TotalSeconds : 0D));
            AppendLog(string.Format(UiLocalization.Text("MainForm.Log.ExitCode.Template", "Exit code: {0}"), commandTask.ExitCode ?? -1));

            if (commandTask.ExitCode == 0)
            {
                AddChatHistoryItem(
                    ChatHistoryItemFactory.GetStatusSenderText(),
                    string.Format(UiLocalization.Text("MainForm.Message.CommandExecutionCompleted.Template", "Command completed: {0}"), commandTask.CommandText),
                    ChatHistoryCategory.Command);
                SetStatus(UiLocalization.Text("MainForm.Status.CommandExecutionCompleted", "Command completed"));
            }
            else if (commandTask.TerminationRequested)
            {
                AddChatHistoryItem(
                    ChatHistoryItemFactory.GetStatusSenderText(),
                    string.Format(UiLocalization.Text("MainForm.Message.CommandStopped.Template", "Command stopped: {0}"), commandTask.CommandText),
                    ChatHistoryCategory.Command);
                SetStatus(UiLocalization.Text("MainForm.Status.CommandStopped", "Command stopped"));
            }
            else
            {
                AddChatHistoryItem(
                    ChatHistoryItemFactory.GetErrorSenderText(),
                    string.Format(UiLocalization.Text("MainForm.Message.CommandExecutionFailedWithExitCode.Template", "Command failed: {0}, exit code {1}"), commandTask.CommandText, commandTask.ExitCode ?? -1));
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.CommandExecutionFailed", "Command execution failed"));
            }

            var targetSession = FindSessionById(commandTask.SessionId);
            if (targetSession != null)
            {
                targetSession.LastActiveAt = DateTime.Now;
                SortSessions();
                ApplyMainFormRefreshPreset(MainFormRefreshPreset.SessionHeaderPersist);
            }

            RefreshCommandTerminalWindow(commandTask);
        }

        private void HandleStreamingWorkspaceCommandOutput(string processId, string text, string outputStream)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            AppServerClientOnEventReceived(
                _appServerClient,
                new AppServerEventArgs(
                    "command_exec_output",
                    text,
                    processId: processId,
                    outputStream: outputStream));
        }

        private void AppendCommandOutput(CommandTask commandTask, string text, string outputStream, bool isStreamDelta)
        {
            CommandOutputAccumulator.Append(
                commandTask,
                text,
                outputStream,
                isStreamDelta,
                MaxBufferedCommandOutputCharacters);
            AppendCommandTerminalEntry(GetCommandTerminalEntryKind(outputStream), text);
            EnsurePendingLogFlushScheduled();
        }

        private void AppendBufferedCommandOutput(CommandTask commandTask, string text, string outputStream)
        {
            var shouldAppendTerminal = ShouldAppendBufferedCommandOutputToTerminal(commandTask, text, outputStream);
            CommandOutputAccumulator.AppendBuffered(
                commandTask,
                text,
                outputStream,
                MaxBufferedCommandOutputCharacters);
            if (shouldAppendTerminal)
            {
                AppendCommandTerminalEntry(GetCommandTerminalEntryKind(outputStream), text);
            }

            EnsurePendingLogFlushScheduled();
        }

        private void EnsureCommandTerminalBuffer(CommandTask commandTask)
        {
            if (_commandTerminalBuffer != null)
            {
                return;
            }

            _commandTerminalBuffer = new CommandTerminalBuffer(MaxBufferedCommandOutputCharacters);
            if (commandTask == null)
            {
                return;
            }

            _commandTerminalBuffer.Append(CommandTerminalEntryKind.System, commandTask.CommandText);
            _commandTerminalBuffer.Append(CommandTerminalEntryKind.System, commandTask.WorkingDirectory);
            _commandTerminalBuffer.Append(CommandTerminalEntryKind.StandardOutput, commandTask.StdOut);
            _commandTerminalBuffer.Append(CommandTerminalEntryKind.StandardError, commandTask.StdErr);
            _commandTerminalBuffer.Append(CommandTerminalEntryKind.Interaction, commandTask.LastInteractionText);
        }

        private void AppendCommandTerminalEntry(CommandTerminalEntryKind kind, string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            EnsureCommandTerminalBuffer(_activeCommandTask);
            _commandTerminalBuffer.Append(kind, text);
            ScheduleCommandTerminalWindowRefresh();
        }

        private void ScheduleCommandTerminalWindowRefresh()
        {
            if (_commandTerminalRefreshQueued ||
                _commandTerminalForm == null ||
                _commandTerminalForm.IsDisposed ||
                IsDisposed ||
                !IsHandleCreated)
            {
                return;
            }

            _commandTerminalRefreshQueued = true;
            if (!this.BeginInvokeSafe(() =>
            {
                _commandTerminalRefreshQueued = false;
                RefreshCommandTerminalWindow(GetTerminalCommand());
            }))
            {
                _commandTerminalRefreshQueued = false;
            }
        }

        private void RefreshCommandTerminalWindow(CommandTask commandTask)
        {
            if (_commandTerminalForm == null || _commandTerminalForm.IsDisposed)
            {
                return;
            }

            _commandTerminalForm.UpdateCommand(
                commandTask,
                IsCommandAvailable(),
                CanSubmitCommandTerminalInput(commandTask),
                CanTerminateCommand(commandTask));
        }

        private static bool ShouldAppendBufferedCommandOutputToTerminal(CommandTask commandTask, string text, string outputStream)
        {
            if (commandTask == null || string.IsNullOrEmpty(text))
            {
                return false;
            }

            if (string.Equals(outputStream, "stderr", StringComparison.OrdinalIgnoreCase))
            {
                return !commandTask.HasStreamedStdErr;
            }

            return !commandTask.HasStreamedStdOut;
        }

        private static CommandTerminalEntryKind GetCommandTerminalEntryKind(string outputStream)
        {
            return string.Equals(outputStream, "stderr", StringComparison.OrdinalIgnoreCase)
                ? CommandTerminalEntryKind.StandardError
                : CommandTerminalEntryKind.StandardOutput;
        }

        private void RefreshCommandState()
        {
            RefreshCommandState(IsCurrentWorkspaceAvailable());
        }

        private void RefreshCommandState(bool workspaceAvailable)
        {
            _newSessionButton.Enabled = workspaceAvailable;
            _commandMenuButton.Enabled = true;
        }

        private void PersistState()
        {
            TryPersistState();
        }

        private bool TryPersistState()
        {
            if (IsTargetStopping || _isPersistenceRecoveryPending)
            {
                return false;
            }

            var saveStage = "build-snapshot";
            try
            {
                var currentRuntimeProfileId = GetConfiguredRuntimeProfileId();
                var activeOfficeTarget = GetActiveOfficeRuntimeTarget();
                var persistedSessions = BuildPersistedSessions(currentRuntimeProfileId);
                CaptureCurrentModeSelection();
                var snapshot = new AppStateSnapshot
                {
                    RecentWorkspaces = new List<WorkspaceInfo>(_recentWorkspaces),
                    Sessions = persistedSessions,
                    RuntimeProfiles = BuildRuntimeProfileStates(currentRuntimeProfileId),
                    OfficeRuntimeStates = BuildOfficeRuntimeStates(activeOfficeTarget.RuntimeScopeId),
                    HasCompletedThreadImportPrompt = _hasCompletedThreadImportPrompt,
                    HasCompletedStartupModeOnboarding = _appStateSnapshot != null && _appStateSnapshot.HasCompletedStartupModeOnboarding,
                    HasCompletedOfficeCliOnboarding = _appStateSnapshot != null && _appStateSnapshot.HasCompletedOfficeCliOnboarding,
                    ThreadSyncSummary = _threadSyncSummary,
                    CurrentInteractionMode = _currentInteractionMode,
                    CurrentCodingWorkspaceId = _currentCodingWorkspaceId,
                    CurrentCodingSessionId = _currentCodingSessionId,
                    CurrentOfficeWorkspaceId = _currentOfficeWorkspaceId,
                    CurrentOfficeSessionId = _currentOfficeSessionId,
                    CurrentWorkspaceId = _currentWorkspace != null ? _currentWorkspace.Id : null,
                    CurrentSessionId = _currentSession != null ? _currentSession.Id : null,
                    MainSplitterDistance = _appStateSnapshot?.MainSplitterDistance,
                    ContentSplitterDistance = _appStateSnapshot?.ContentSplitterDistance
                };
                saveStage = "save-state";
                var runtimeConfigStore = _appStateStore as IRuntimeConfigAppStateStore;
                if (runtimeConfigStore != null)
                {
                    runtimeConfigStore.Save(snapshot, GetConfiguredRuntimeConfigSnapshot());
                }
                else
                {
                    _appStateStore.Save(snapshot);
                }

                _appStateSnapshot = snapshot;
                return true;
            }
            catch (PersistenceRecoveryRequiredException ex)
            {
                TraceTargetStateError(saveStage, ex);
                AppendLog(UiLocalization.Text("MainForm.Log.SaveStateFailedPrefix", "Save state failed: ") + ex.Message);
                MarkPersistenceRecoveryPending(ex);
            }
            catch (Exception ex)
            {
                TraceTargetStateError(saveStage, ex);
                AppendLog(UiLocalization.Text("MainForm.Log.SaveStateFailedPrefix", "Save state failed: ") + ex.Message);
                SetHighPriorityStatus(UiLocalization.Text("MainForm.Status.SaveStateFailedPrefix", "Save state failed: ") + ex.Message);
            }
            return false;
        }

        private bool IsAvailableModelsScopeCurrent()
        {
            return !string.IsNullOrWhiteSpace(_availableModelsScopeKey) &&
                   string.Equals(_availableModelsScopeKey, BuildAvailableModelsScopeKey(), StringComparison.OrdinalIgnoreCase);
        }

        private string BuildAvailableModelsScopeKey()
        {
            return GetConfiguredRuntimeProfileId();
        }

        private List<RuntimeProfileStateSnapshot> BuildRuntimeProfileStates(string currentRuntimeProfileId)
        {
            var result = new List<RuntimeProfileStateSnapshot>();
            if (_appStateSnapshot != null && _appStateSnapshot.RuntimeProfiles != null)
            {
                foreach (var profile in _appStateSnapshot.RuntimeProfiles)
                {
                    if (profile == null || string.IsNullOrWhiteSpace(profile.RuntimeProfileId))
                    {
                        continue;
                    }

                    if (_runtimeProfileService.IsSameRuntimeProfile(profile.RuntimeProfileId, currentRuntimeProfileId))
                    {
                        continue;
                    }

                    result.Add(CloneRuntimeProfileState(profile));
                }
            }

            result.Add(new RuntimeProfileStateSnapshot
            {
                RuntimeProfileId = _runtimeProfileService.NormalizeRuntimeProfileId(currentRuntimeProfileId),
                RecentWorkspaces = CloneCodingWorkspaces(_recentWorkspaces),
                CurrentWorkspaceId = _currentInteractionMode == AppInteractionMode.Coding
                    ? (_currentWorkspace != null ? _currentWorkspace.Id : null)
                    : _currentCodingWorkspaceId,
                CurrentSessionId = _currentInteractionMode == AppInteractionMode.Coding
                    ? (_currentSession != null ? _currentSession.Id : null)
                    : _currentCodingSessionId
            });
            return result;
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

        private static RuntimeProfileStateSnapshot CloneRuntimeProfileState(RuntimeProfileStateSnapshot profile)
        {
            return new RuntimeProfileStateSnapshot
            {
                RuntimeProfileId = profile.RuntimeProfileId,
                RecentWorkspaces = CloneWorkspaces(profile.RecentWorkspaces),
                CurrentWorkspaceId = profile.CurrentWorkspaceId,
                CurrentSessionId = profile.CurrentSessionId
            };
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
                if (workspace == null)
                {
                    continue;
                }

                result.Add(new WorkspaceInfo
                {
                    Id = workspace.Id,
                    Name = workspace.Name,
                    RootPath = workspace.RootPath,
                    GitBranch = workspace.GitBranch,
                    Kind = workspace.Kind,
                    LastOpenedAt = workspace.LastOpenedAt
                });
            }

            return result;
        }

        private List<ChatSession> BuildPersistedSessions(string currentRuntimeProfileId)
        {
            return _persistedSessionService.BuildPersistedSessions(
                _sessions,
                _inactiveRuntimeSessions,
                currentRuntimeProfileId,
                _currentSession != null ? _currentSession.Id : null);
        }

        private bool IsChatGptAccountAuthenticationActive()
        {
            return _authenticationState != null &&
                   _authenticationState.IsAuthenticated;
        }

        private void RefreshInputState()
        {
            RefreshInputState(IsCurrentWorkspaceAvailable());
        }

        private void RefreshInputStateFromCache()
        {
            RefreshInputState(IsCurrentWorkspaceAvailableFromCache());
        }

        private void RefreshInputState(bool workspaceAvailable)
        {
            var currentThreadId = _currentSession != null ? _currentSession.ThreadId : null;
            var plan = MainFormInputStatePlanner.Build(
                workspaceAvailable,
                _currentSession != null,
                IsThreadCompacting(currentThreadId),
                CanInterruptThread(currentThreadId),
                _chatHistoryListBox.Items.Count,
                _pendingAttachments.Count);
            _chatHistoryListBox.Enabled = plan.ChatHistoryEnabled;
            _inputBox.Enabled = plan.InputEnabled;
            _attachmentButton.Enabled = plan.AttachmentButtonEnabled;
            _sendButton.Enabled = plan.SendButtonEnabled;
            _stopButton.Enabled = plan.CanStop;
            _pendingAttachmentListBox.Enabled = plan.PendingAttachmentListEnabled;
            RefreshPendingAttachmentState(workspaceAvailable);
            RefreshCommandState(workspaceAvailable);
        }

        private void InvalidateSessionHistoryCache(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            _sessionHistoryStateStore.Invalidate(threadId);
        }

        private void MarkPendingHistoryReload(ChatSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.ThreadId))
            {
                return;
            }

            _sessionHistoryStateStore.MarkPendingReload(session.ThreadId);
        }

        private void ClearPendingHistoryReload(string threadId)
        {
            _sessionHistoryStateStore.ClearPendingReload(threadId);
        }

        private bool HasPendingHistoryReload(ChatSession session)
        {
            return session != null &&
                   !string.IsNullOrWhiteSpace(session.ThreadId) &&
                   _sessionHistoryStateStore.HasPendingReload(session.ThreadId);
        }

        private void MarkLocalDesktopRealtimeTurn(string threadId)
        {
            if (!string.IsNullOrWhiteSpace(threadId))
            {
                _localDesktopRealtimeTurnThreadIds.Add(threadId);
            }
        }

        private void ClearLocalDesktopRealtimeTurn(string threadId)
        {
            if (!string.IsNullOrWhiteSpace(threadId))
            {
                _localDesktopRealtimeTurnThreadIds.Remove(threadId);
            }
        }

        private bool IsLocalDesktopRealtimeTurn(string threadId)
        {
            return !string.IsNullOrWhiteSpace(threadId) &&
                   _localDesktopRealtimeTurnThreadIds.Contains(threadId);
        }

        private string BuildDisplayedSessionHeaderText(ChatHistoryItem firstItem, ChatSession session)
        {
            return _sessionHistoryDisplayBuilder.BuildDisplayedSessionHeaderText(firstItem, session);
        }

        private ChatHistoryItem[] BuildSessionHeaderItems(ChatSession session, bool isNewSession)
        {
            return _sessionHistoryDisplayBuilder.BuildSessionHeaderItems(session, isNewSession);
        }

        private bool TryConvertMissingThreadSessionToDraft(ChatSession session)
        {
            if (!ShouldRecoverMissingThreadAsDraft(session))
            {
                return false;
            }

            var previousThreadId = session.ThreadId;
            session.ThreadId = string.Empty;
            session.SessionPath = null;

            ResetThreadVolatileState(previousThreadId);

            RefreshHeader();
            RefreshSessionList();
            RefreshInputState();
            PersistState();
            return true;
        }

        private void ResetThreadVolatileState(string threadId)
        {
            if (string.IsNullOrWhiteSpace(threadId))
            {
                return;
            }

            InvalidateSessionHistoryCache(threadId);
            ClearPendingHistoryReload(threadId);
            ClearThreadCompactionState(threadId);
            ClearThreadBusy(threadId);
            ClearLocalDesktopRealtimeTurn(threadId);
            ClearReasoningSummaryMessageStates(threadId);
        }

        private static bool ShouldRecoverMissingThreadAsDraft(ChatSession session)
        {
            return session != null &&
                   !session.IsImported &&
                   !session.IsFork &&
                   !session.IsReview &&
                   !string.IsNullOrWhiteSpace(session.ThreadId) &&
                   string.IsNullOrWhiteSpace(session.Preview) &&
                   string.IsNullOrWhiteSpace(session.SessionPath);
        }

        private bool ShouldPreserveChatHistorySelectionForSilentReload()
        {
            return SessionHistorySilentReloadSelectionPolicy.ShouldPreserveSelection(
                _chatHistoryListBox.ContainsFocus || ReferenceEquals(ActiveControl, _chatHistoryListBox),
                _chatHistoryListBox.FocusedIndex);
        }

        private void ClearChatHistoryItems()
        {
            _allChatHistoryItems.Clear();
            ClearVisibleChatHistoryItems();
        }

        private void ClearVisibleChatHistoryItems()
        {
            _chatHistoryListBox.Items.Clear();
        }

        private ChatHistoryItem AddChatHistoryItem(string sender, string message)
        {
            return AddChatHistoryItem(sender, message, ChatHistoryItemFactory.GetDefaultCategory(sender));
        }

        private ChatHistoryItem AddChatHistoryItem(string sender, string message, ChatHistoryCategory category)
        {
            var item = ChatHistoryItemFactory.Create(sender, message, category);
            item.ThreadId = _currentSession != null ? _currentSession.ThreadId : null;
            _allChatHistoryItems.Add(item);

            if (!ShouldDisplayChatHistoryItem(item))
            {
                return item;
            }

            _chatHistoryListBox.Items.Add(item);
            if (_chatHistoryListBox.Items.Count > 0)
            {
                _chatHistoryListBox.TopIndex = _chatHistoryListBox.Items.Count - 1;
            }

            return item;
        }

        private bool IsDisplayingCurrentSessionHistory()
        {
            return _currentSession != null &&
                   !string.IsNullOrWhiteSpace(_currentSession.ThreadId) &&
                   _allChatHistoryItems.Any(item =>
                       item != null &&
                       string.Equals(item.ThreadId, _currentSession.ThreadId, StringComparison.OrdinalIgnoreCase));
        }

        private void RestoreChatHistoryTopIndex(int topIndex)
        {
            if (topIndex < 0 || topIndex >= _chatHistoryListBox.Items.Count)
            {
                return;
            }

            try
            {
                _chatHistoryListBox.TopIndex = topIndex;
            }
            catch
            {
            }
        }

        private static Label CreateSectionTitle(string text)
        {
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = UiTheme.SectionTitleHeight,
                Font = UiTheme.CreateUiFont(UiTheme.TitleFontSize, FontStyle.Bold),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                TabStop = false
            };

            label.Paint += (sender, e) =>
            {
                if (sender is Label lbl)
                {
                    var rect = lbl.ClientRectangle;
                    using (var pen = new Pen(UiTheme.BorderColor, 1))
                    {
                        e.Graphics.DrawLine(pen, 0, rect.Bottom - 1, rect.Width, rect.Bottom - 1);
                    }
                }
            };

            return label;
        }

        private static Label CreateSubSectionTitle(string text)
        {
            var label = new Label
            {
                Text = text,
                Dock = DockStyle.Top,
                Height = UiTheme.SubSectionTitleHeight,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Bold),
                ForeColor = UiTheme.MutedTextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 8, 0, 0),
                TabStop = false
            };

            label.Paint += (sender, e) =>
            {
                if (sender is Label lbl)
                {
                    var rect = lbl.ClientRectangle;
                    using (var brush = new SolidBrush(UiTheme.AccentColor))
                    {
                        e.Graphics.FillRectangle(brush, 0, UiTheme.Scale(8), UiTheme.Scale(3), rect.Height - UiTheme.Scale(8));
                    }
                }
            };

            return label;
        }

        private static Label CreateInfoValueLabel()
        {
            return new Label
            {
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                ForeColor = UiTheme.TextColor,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                TabStop = false
            };
        }

        private static ChatHistoryListBox CreateChatHistoryListBox()
        {
            return new ChatHistoryListBox
            {
                Dock = DockStyle.Fill,
                AccessibleName = UiLocalization.Text("MainForm.Accessible.ChatHistory", "Messages"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                IntegralHeight = false,
                TabIndex = 2
            };
        }

        private AttachmentComposerTextBox CreateInputBox()
        {
            var textBox = new AttachmentComposerTextBox
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.Input", "Input"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                TabIndex = 3
            };

            textBox.KeyDown += InputBox_KeyDown;
            textBox.TextChanged += InputBox_TextChanged;
            return textBox;
        }

        private ActivityVirtualBrowseControl CreateActivityBrowseControl()
        {
            var control = new ActivityVirtualBrowseControl
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.Activity", "Activity"),
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                TabIndex = 1
            };
            return control;
        }

        private static Label CreateActivityCurrentStatusLabel()
        {
            return new Label
            {
                AccessibleName = UiLocalization.Text("MainForm.Accessible.CurrentActivity", "Current activity"),
                AutoEllipsis = true,
                Dock = DockStyle.Fill,
                Font = UiTheme.CreateUiFont(UiTheme.ReadingFontSize, FontStyle.Regular),
                ForeColor = UiTheme.MutedTextColor,
                Padding = new Padding(4, 0, 0, 0),
                TabStop = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static void OpenExternalUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private static string GetApprovalModeText(ApprovalMode approvalMode)
        {
            return ApprovalModeDisplayText.Name(approvalMode);
        }

        private static string BuildThreadDisplayTitle(AppServerThreadSummary thread)
        {
            if (thread == null)
            {
                return UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session");
            }

            if (!string.IsNullOrWhiteSpace(thread.Title))
            {
                return thread.Title;
            }

            if (!string.IsNullOrWhiteSpace(thread.Preview))
            {
                var preview = thread.Preview.Trim();
                return preview.Length > 30 ? preview.Substring(0, 30) + "..." : preview;
            }

            return string.IsNullOrWhiteSpace(thread.ThreadId)
                ? UiLocalization.Text("MainForm.Value.UnnamedSession", "Unnamed session")
                : thread.ThreadId;
        }

        private sealed class PlanSlashCommand
        {
            public static readonly PlanSlashCommand None = new PlanSlashCommand(false, null);

            public PlanSlashCommand(bool isPlanCommand, string promptText)
            {
                IsPlanCommand = isPlanCommand;
                PromptText = promptText;
            }

            public bool IsPlanCommand { get; }

            public string PromptText { get; }
        }

        private sealed class PendingExecutablePlan
        {
            public PendingExecutablePlan(string threadId, string turnId, string content)
            {
                ThreadId = threadId;
                TurnId = turnId;
                Content = content;
            }

            public string ThreadId { get; }

            public string TurnId { get; }

            public string Content { get; }
        }

        private sealed class PendingRemotePlanExecutionPrompt
        {
            public PendingRemotePlanExecutionPrompt(string requestId, string threadId, string turnId, string content)
            {
                RequestId = requestId;
                ThreadId = threadId;
                TurnId = turnId;
                Content = content;
            }

            public string RequestId { get; }

            public string ThreadId { get; }

            public string TurnId { get; }

            public string Content { get; }

            public string Action { get; set; }
        }

    }
}

