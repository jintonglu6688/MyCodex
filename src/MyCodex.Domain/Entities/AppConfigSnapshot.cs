using System.Runtime.Serialization;
using System.Collections.Generic;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class AppConfigSnapshot
    {
        public const string DefaultGitHostingProviderId = "github";
        private const string LegacyIndependentChannelNameMemberName = "Boy" + "CtrlIndependentChannelName";

        private WslAppServerConfigSnapshot _wslAppServer = new WslAppServerConfigSnapshot();
        private RemoteMacSshConfigSnapshot _remoteMacSsh = new RemoteMacSshConfigSnapshot();
        private List<RemoteUnixSshProfileConfigSnapshot> _remoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>();
        private UpdateSettingsSnapshot _updateSettings = new UpdateSettingsSnapshot();
        private CustomApprovalSettingsSnapshot _customApprovalSettings = new CustomApprovalSettingsSnapshot();
        private string _windowsShellPreference = WindowsShellPreferences.Auto;
        private bool? _showActivityThinkingMessages = true;
        private bool? _showActivityPlanMessages = true;
        private bool? _showActivityToolMessages = true;
        private bool? _showActivityCommandMessages = true;
        private bool? _showActivityFileMessages = true;
        private bool? _showActivityNoticeMessages = true;
        private bool? _showActivityErrorMessages = true;
        private bool? _showActivityApprovalMessages = true;
        private bool? _showActivityAccountMessages = true;
        private bool? _showActivityProtocolNoticeMessages = true;
        private bool? _showActivityProtocolWarningMessages = true;
        private string _textFileOpenMode = MyCodex.Shared.Constants.TextFileOpenMode.Internal;

        [DataMember]
        public ApprovalMode DefaultApprovalMode { get; set; } = ApprovalMode.Auto;

        [DataMember]
        public CustomApprovalSettingsSnapshot CustomApprovalSettings
        {
            get
            {
                if (_customApprovalSettings == null)
                {
                    _customApprovalSettings = new CustomApprovalSettingsSnapshot();
                }

                return _customApprovalSettings;
            }
            set { _customApprovalSettings = value ?? new CustomApprovalSettingsSnapshot(); }
        }

        [DataMember(Name = "DefaultModelId", EmitDefaultValue = false)]
        private string LegacyDefaultModelId { get; set; }

        [DataMember(Name = "DefaultReasoningEffort", EmitDefaultValue = false)]
        private string LegacyDefaultReasoningEffort { get; set; }

        [DataMember]
        public string GitHostingProviderId { get; set; } = DefaultGitHostingProviderId;

        [DataMember]
        public bool OfficeShowActivityPane { get; set; } = true;

        [DataMember(Name = "OfficialOfficeCapabilityEnabled", EmitDefaultValue = false)]
        private bool? LegacyOfficialOfficeCapabilityEnabled
        {
            get { return null; }
            set { }
        }

        [DataMember]
        public bool ShowCommandOutput { get; set; } = true;

        [DataMember(Name = "ShowActivityThinkingMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityThinkingMessages
        {
            get { return _showActivityThinkingMessages; }
            set { _showActivityThinkingMessages = value; }
        }

        public bool ShowActivityThinkingMessages
        {
            get { return _showActivityThinkingMessages ?? true; }
            set { _showActivityThinkingMessages = value; }
        }

        [DataMember(Name = "ShowActivityPlanMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityPlanMessages
        {
            get { return _showActivityPlanMessages; }
            set { _showActivityPlanMessages = value; }
        }

        public bool ShowActivityPlanMessages
        {
            get { return _showActivityPlanMessages ?? true; }
            set { _showActivityPlanMessages = value; }
        }

        [DataMember(Name = "ShowActivityToolMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityToolMessages
        {
            get { return _showActivityToolMessages; }
            set { _showActivityToolMessages = value; }
        }

        public bool ShowActivityToolMessages
        {
            get { return _showActivityToolMessages ?? true; }
            set { _showActivityToolMessages = value; }
        }

        [DataMember(Name = "ShowActivityCommandMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityCommandMessages
        {
            get { return _showActivityCommandMessages; }
            set { _showActivityCommandMessages = value; }
        }

        public bool ShowActivityCommandMessages
        {
            get { return _showActivityCommandMessages ?? true; }
            set { _showActivityCommandMessages = value; }
        }

        [DataMember(Name = "ShowActivityFileMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityFileMessages
        {
            get { return _showActivityFileMessages; }
            set { _showActivityFileMessages = value; }
        }

        public bool ShowActivityFileMessages
        {
            get { return _showActivityFileMessages ?? true; }
            set { _showActivityFileMessages = value; }
        }

        [DataMember(Name = "ShowActivityNoticeMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityNoticeMessages
        {
            get { return _showActivityNoticeMessages; }
            set { _showActivityNoticeMessages = value; }
        }

        public bool ShowActivityNoticeMessages
        {
            get { return _showActivityNoticeMessages ?? true; }
            set { _showActivityNoticeMessages = value; }
        }

        [DataMember(Name = "ShowActivityErrorMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityErrorMessages
        {
            get { return _showActivityErrorMessages; }
            set { _showActivityErrorMessages = value; }
        }

        public bool ShowActivityErrorMessages
        {
            get { return _showActivityErrorMessages ?? true; }
            set { _showActivityErrorMessages = value; }
        }

        [DataMember(Name = "ShowActivityApprovalMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityApprovalMessages
        {
            get { return _showActivityApprovalMessages; }
            set { _showActivityApprovalMessages = value; }
        }

        public bool ShowActivityApprovalMessages
        {
            get { return _showActivityApprovalMessages ?? true; }
            set { _showActivityApprovalMessages = value; }
        }

        [DataMember(Name = "ShowActivityAccountMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityAccountMessages
        {
            get { return _showActivityAccountMessages; }
            set { _showActivityAccountMessages = value; }
        }

        public bool ShowActivityAccountMessages
        {
            get { return _showActivityAccountMessages ?? true; }
            set { _showActivityAccountMessages = value; }
        }

        [DataMember(Name = "ShowActivityProtocolNoticeMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityProtocolNoticeMessages
        {
            get { return _showActivityProtocolNoticeMessages; }
            set { _showActivityProtocolNoticeMessages = value; }
        }

        public bool ShowActivityProtocolNoticeMessages
        {
            get { return _showActivityProtocolNoticeMessages ?? true; }
            set { _showActivityProtocolNoticeMessages = value; }
        }

        [DataMember(Name = "ShowActivityProtocolWarningMessages", EmitDefaultValue = false)]
        private bool? SerializedShowActivityProtocolWarningMessages
        {
            get { return _showActivityProtocolWarningMessages; }
            set { _showActivityProtocolWarningMessages = value; }
        }

        public bool ShowActivityProtocolWarningMessages
        {
            get { return _showActivityProtocolWarningMessages ?? true; }
            set { _showActivityProtocolWarningMessages = value; }
        }

        [DataMember]
        public bool ClearActivityOnNewTurn { get; set; }

        [DataMember]
        public bool EnableAutomaticChatReading { get; set; }

        [DataMember]
        public ScreenReaderProviderKind ScreenReaderProvider { get; set; } = ScreenReaderProviderKind.Auto;

        [DataMember]
        public bool ManualBrowseUseReaderMainChannel { get; set; }

        [DataMember]
        public string ScreenReaderIndependentChannelName { get; set; }

        [DataMember(Name = LegacyIndependentChannelNameMemberName, EmitDefaultValue = false)]
        private string LegacyIndependentChannelName
        {
            get { return null; }
            set
            {
                if (string.IsNullOrWhiteSpace(ScreenReaderIndependentChannelName))
                {
                    ScreenReaderIndependentChannelName = value;
                }
            }
        }

        [DataMember]
        public bool ShowUnavailableWorkspaces { get; set; } = true;

        [DataMember]
        public NotificationPromptMode TurnCompletedNotificationMode { get; set; } = NotificationPromptMode.Sound;

        [DataMember]
        public NotificationPromptMode SubAgentTurnCompletedNotificationMode { get; set; } = NotificationPromptMode.Sound;

        [DataMember]
        public NotificationPromptMode ApprovalRequestNotificationMode { get; set; } = NotificationPromptMode.Sound;

        [DataMember]
        public bool SendMessageSoundEnabled { get; set; } = true;

        [DataMember]
        public bool CloseSessionWorkbenchOnEscape { get; set; } = true;

        [DataMember]
        public int MainWindowHotKeyModifiers { get; set; } = MyCodex.Shared.Constants.MainWindowHotKeyConfig.DefaultModifiers;

        [DataMember(EmitDefaultValue = false)]
        public bool? MainWindowHotKeyEnabled { get; set; }

        [DataMember]
        public int MainWindowHotKeyVirtualKey { get; set; } = MyCodex.Shared.Constants.MainWindowHotKeyConfig.DefaultVirtualKey;

        [DataMember]
        public string CodexRuntimeSource { get; set; } = "Bundled";

        [DataMember]
        public string CodexCustomExecutablePath { get; set; }

        [DataMember]
        public string WindowsShellPreference
        {
            get { return WindowsShellPreferences.Normalize(_windowsShellPreference); }
            set { _windowsShellPreference = WindowsShellPreferences.Normalize(value); }
        }

        [DataMember]
        public WslAppServerConfigSnapshot WslAppServer
        {
            get
            {
                if (_wslAppServer == null)
                {
                    _wslAppServer = new WslAppServerConfigSnapshot();
                }

                return _wslAppServer;
            }
            set { _wslAppServer = value ?? new WslAppServerConfigSnapshot(); }
        }

        [DataMember]
        public RemoteMacSshConfigSnapshot RemoteMacSsh
        {
            get
            {
                if (_remoteMacSsh == null)
                {
                    _remoteMacSsh = new RemoteMacSshConfigSnapshot();
                }

                return _remoteMacSsh;
            }
            set { _remoteMacSsh = value ?? new RemoteMacSshConfigSnapshot(); }
        }

        [DataMember]
        public List<RemoteUnixSshProfileConfigSnapshot> RemoteUnixSshProfiles
        {
            get
            {
                if (_remoteUnixSshProfiles == null)
                {
                    _remoteUnixSshProfiles = new List<RemoteUnixSshProfileConfigSnapshot>();
                }

                return _remoteUnixSshProfiles;
            }
            set { _remoteUnixSshProfiles = value ?? new List<RemoteUnixSshProfileConfigSnapshot>(); }
        }

        [DataMember]
        public string SelectedRemoteUnixSshProfileId { get; set; }

        [DataMember]
        public string UiLanguagePreference { get; set; } = "auto";

        private int _uiFontScalePercent = 100;

        [DataMember]
        public int UiFontScalePercent
        {
            get { return _uiFontScalePercent == 125 || _uiFontScalePercent == 150 ? _uiFontScalePercent : 100; }
            set { _uiFontScalePercent = value; }
        }

        [DataMember]
        public string UiThemePreference { get; set; } = "auto";

        [DataMember]
        public string MessageDetailPreviewMode { get; set; } = MyCodex.Shared.Constants.MessageDetailPreviewMode.Web;

        [DataMember]
        public string SessionListDisplayOrder { get; set; } = MyCodex.Shared.Constants.SessionListDisplayOrder.AgentsTimeTitle;

        [DataMember]
        public string SendMessageShortcutMode { get; set; } = MyCodex.Shared.Constants.SendMessageShortcutMode.EnterNewLineCtrlEnterSend;

        [DataMember]
        public string ActiveTurnFollowUpMode { get; set; } = MyCodex.Shared.Constants.ActiveTurnFollowUpMode.Steer;

        [DataMember]
        public string TextFileOpenMode
        {
            get { return MyCodex.Shared.Constants.TextFileOpenMode.Normalize(_textFileOpenMode); }
            set { _textFileOpenMode = MyCodex.Shared.Constants.TextFileOpenMode.Normalize(value); }
        }

        [DataMember]
        public string TextFileExternalEditorPath { get; set; }

        [DataMember]
        public string TextFileExternalEditorArguments { get; set; }

        [DataMember]
        public List<CommandDefinition> CommandDefinitions { get; set; }

        [DataMember]
        public List<RuntimeProfileConfigSnapshot> RuntimeProfiles { get; set; } = new List<RuntimeProfileConfigSnapshot>();

        [DataMember]
        public UpdateSettingsSnapshot UpdateSettings
        {
            get
            {
                if (_updateSettings == null)
                {
                    _updateSettings = new UpdateSettingsSnapshot();
                }

                return _updateSettings;
            }
            set { _updateSettings = value ?? new UpdateSettingsSnapshot(); }
        }

        public RemoteUnixSshProfileConfigSnapshot GetSelectedRemoteUnixSshProfile()
        {
            var profiles = RemoteUnixSshProfiles;
            if (profiles.Count == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(SelectedRemoteUnixSshProfileId))
            {
                for (var i = 0; i < profiles.Count; i++)
                {
                    var profile = profiles[i];
                    if (profile != null &&
                        string.Equals(profile.Id, SelectedRemoteUnixSshProfileId, System.StringComparison.OrdinalIgnoreCase))
                    {
                        return profile;
                    }
                }
            }

            return profiles[0];
        }

        public RemoteMacSshConfigSnapshot GetSelectedRemoteUnixSshConfig()
        {
            var profile = GetSelectedRemoteUnixSshProfile();
            return profile != null ? profile.Ssh : RemoteMacSsh;
        }
    }
}
