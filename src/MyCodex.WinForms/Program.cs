using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MyCodex.Application.InternalTools;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Git;
using MyCodex.Infrastructure.GitHosting;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Markets;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Diagnostics;
using MyCodex.Infrastructure.Tts;
using MyCodex.Shared.Constants;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using MyCodex.WinForms.Theming;

namespace MyCodex.WinForms
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            if (InstallationExitStartup.TryRun(args, out var installationExitCode))
            {
                Environment.ExitCode = installationExitCode;
                return;
            }
            if (MyCodex.Infrastructure.AuthenticationCenter.AuthenticationHostStartup.TryRun(args, out var maintenanceExitCode))
            {
                Environment.ExitCode = maintenanceExitCode;
                return;
            }
            if (MyCodex.Infrastructure.RemoteRelay.SharedEmbeddedRelayHost.TryRun(args, out var relayHostExitCode))
            {
                Environment.ExitCode = relayHostExitCode;
                return;
            }
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
            System.Windows.Forms.Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            var appChannel = AppChannel.Resolve(args);
            var legacyPaths = new AppConfigService(appDirectoryName: appChannel.AppDataDirectoryName);
            ConfigureDiagnostics(legacyPaths);
            var catalog = new TargetCatalogStore(legacyPaths, appChannel.AppDataDirectoryName);
            var options = StartupCommandLineOptions.Empty;
            try
            {
                using (var legacyLease = LegacyStartupLease.TryAcquire(appChannel.Id))
                {
                    if (legacyLease == null)
                    {
                        MessageBox.Show(UiLocalization.Text("Program.Migration.OtherInstance", "Another instance is using the legacy layout or performing an upgrade. Exit it normally before upgrading; running tasks will not be stopped."),
                            AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    if (!TargetMigrationStartup.EnsureCatalog(legacyPaths)) return;
                }

                var snapshot = catalog.Load();
                TargetStartupSelection selection;
                try
                {
                    options = StartupCommandLineOptions.Parse(args);
                    selection = TargetStartupPlanner.Resolve(snapshot, options.TargetId, options.WorkspacePath);
                }
                catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is NotSupportedException)
                {
                    // Planner errors contain only fixed validation reasons, never parsed config contents.
                    TargetManagerForm.ShowStartup(catalog, appChannel.Id, appChannel.DisplayName, exception.Message);
                    return;
                }
                using (var instance = TargetInstanceCoordinator.Acquire(appChannel.Id, selection.Target.Id, legacyPaths.GetSharedDataDirectory()))
                {
                    if (!instance.IsPrimaryInstance)
                    {
                        var reply = TargetInstanceCoordinator.Send(appChannel.Id, selection.Target.Id,
                            legacyPaths.GetSharedDataDirectory(), new TargetInstanceRequest
                            {
                                Operation = TargetInstanceOperation.Activate, WorkspacePath = selection.WorkspacePath
                            });
                        if (reply.Success) catalog.RememberOpened(selection.Target.Id);
                        else TargetManagerForm.ShowStartup(catalog, appChannel.Id, appChannel.DisplayName,
                            UiLocalization.Text("Program.Target.ActivationFailed", "The target did not confirm activation. It may be starting or closing; retry later or review its settings in the target manager.") +
                            Environment.NewLine + reply.Error);
                        return;
                    }

                    var appConfigService = catalog.GetPaths(selection.Target.Id);
                    args = string.IsNullOrEmpty(selection.WorkspacePath)
                        ? new string[0] : new[] { "--workspace", selection.WorkspacePath };
                    RunTargetWindow(args, appConfigService, appChannel, catalog, instance, selection.Target.Id);
                }
            }
            catch (Exception exception)
            {
                LogStartupFailure(exception);
                var message = BuildStartupFailureMessage(exception);
                if (exception is PersistenceRecoveryRequiredException)
                    MessageBox.Show(message, AppConstants.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                TargetManagerForm.ShowStartup(catalog, appChannel.Id, appChannel.DisplayName,
                    message);
            }
        }

        private static void RunTargetWindow(string[] args, AppConfigService appConfigService, AppChannel appChannel,
            TargetCatalogStore catalog, TargetInstanceCoordinator instance, string targetId)
        {
            ConfigureDiagnostics(appConfigService);
            RemoteSshSensitiveFileDirectory.Configure(appConfigService.GetTempDirectory());

            var appConfigStore = new JsonAppConfigStore(appConfigService);
            InitializeUiPreferences(appConfigStore);
            try
            {
                UserCacheMaintenance.CleanupOnStartup(appConfigService);
                RemoteSshPasswordFile.CleanupStaleFiles(appConfigService.GetTempDirectory(), TimeSpan.Zero);
                RemoteSshSensitiveCommandFile.CleanupStaleFiles(appConfigService.GetTempDirectory(), TimeSpan.Zero);
            }
            catch
            {
            }

            BundledGitRuntime.ConfigureProcessEnvironment();
            System.Windows.Forms.Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

            var appStateStore = new JsonAppStateStore(appConfigService, appConfigStore);
            var screenReaderService = new ConfigurableScreenReaderService(appConfigService, appConfigStore);
            var codexRuntimeManager = new CodexRuntimeManager(appConfigService);
            var internalToolRegistry = CreateInternalToolRegistry();
            var appServerClient = new AppServerClient(
                codexRuntimeManager,
                appConfigStore,
                internalToolRegistry);
            var automationStore = new JsonAutomationStore(appConfigService);
            var automationScheduleCalculator = new AutomationScheduleCalculator();
            var automationService = new AutomationService(automationStore, automationScheduleCalculator);
            var automationResultClassifier = new AutomationResultClassifier();
            var automationRunner = new AutomationRunner(
                automationStore,
                appServerClient,
                automationResultClassifier,
                new AutomationCollaborationModePlanner());
            var automationRuntimePlanner = new AutomationRuntimePlanner(automationScheduleCalculator);
            var automationTarget = catalog.GetTarget(targetId);
            var automationScope = new AutomationTargetScopePlanner(automationTarget.RuntimeScopeId, automationTarget.Kind != "Windows");
            automationRunner.TargetScope = automationScope;
            automationRuntimePlanner.TargetScope = automationScope;
            var automationRuntimeHost = new AutomationRuntimeHost(
                automationStore,
                automationRunner,
                automationRuntimePlanner);
            var pluginMarketCatalogClient = new PluginMarketCatalogClient();
            var gitHostingProviderService = new GitHostingProviderService();
            var mainForm = new MainForm(
                appServerClient,
                appStateStore,
                appConfigStore,
                appConfigService,
                screenReaderService,
                codexRuntimeManager,
                pluginMarketCatalogClient,
                gitHostingProviderService,
                appChannel.DisplayName,
                internalToolRegistry,
                automationStore,
                automationService,
                automationRuntimeHost,
                automationRunner);

            using (mainForm)
            {
                mainForm.ConfigureTargetContext(catalog, instance, appChannel.Id);
                mainForm.ApplyStartupArguments(args);
                instance.StartServer(mainForm.HandleTargetInstanceRequestAsync);
                System.Windows.Forms.Application.Run(mainForm);
            }
        }

        internal static void InitializeUiPreferences(JsonAppConfigStore appConfigStore)
        {
            if (appConfigStore == null)
            {
                UiLocalization.ApplyPreferredCulture(UiLanguagePreference.Auto);
                return;
            }

            try
            {
                var snapshot = appConfigStore.Load() ?? new AppConfigSnapshot();
                UiLocalization.ApplyPreferredCulture(snapshot.UiLanguagePreference);
                UiTheme.InitializeUiFontScale(snapshot.UiFontScalePercent);
            }
            catch
            {
                UiLocalization.ApplyPreferredCulture(UiLanguagePreference.Auto);
            }

            UiTheme.InitializeUiFontScale(100);
            UiTheme.InitializeUiFontFamily(System.Globalization.CultureInfo.CurrentUICulture.Name);
        }

        private static IInternalToolRegistry CreateInternalToolRegistry()
        {
            return new InternalToolRegistry(new IInternalToolExecutor[0]);
        }

        internal static void ConfigureDiagnostics(AppConfigService appConfigService)
        {
            try
            {
                var startedAt = DateTimeOffset.Now;
                var processId = Process.GetCurrentProcess().Id;
                var logFilePath = SessionTraceLog.Configure(
                    appConfigService.GetLogDirectory(),
                    startedAt,
                    processId);
                Trace.TraceInformation(
                    "[Diagnostics] Session started. Version={0}; ProcessId={1}; Is64BitProcess={2}; OS={3}; LogFile={4}",
                    System.Windows.Forms.Application.ProductVersion,
                    processId,
                    Environment.Is64BitProcess,
                    Environment.OSVersion.VersionString,
                    Path.GetFileName(logFilePath));
            }
            catch
            {
            }
        }

        internal static void LogStartupFailure(Exception exception)
        {
            try
            {
                var recovery = exception as PersistenceRecoveryRequiredException;
                Trace.TraceError("[Startup] Target startup failed. ExceptionType={0}; Path={1}; Backup={2}; Stack={3}",
                    exception.GetType().FullName, recovery?.PrimaryPath, recovery?.BackupPath, exception.StackTrace);
                var cause = exception.GetBaseException();
                if (!ReferenceEquals(cause, exception))
                    Trace.TraceError("[Startup] CauseType={0}; Stack={1}", cause.GetType().FullName, cause.StackTrace);
            }
            catch
            {
            }
        }

        internal static string BuildStartupFailureMessage(Exception exception)
        {
            var recovery = exception as PersistenceRecoveryRequiredException;
            if (recovery != null)
                return string.Format(UiLocalization.Text("Program.Target.RecoveryRequired.Template",
                    "MyCodex could not recover the target's saved data from its backup. The affected files have been kept. Restore them from a valid backup, then restart MyCodex.\n\nAffected file: {0}\nBackup file: {1}"),
                    recovery.PrimaryPath, recovery.BackupPath);
            return UiLocalization.Text("Program.Target.StartupFailed", "The target could not be opened. Review it in the target manager.") +
                " (" + exception.GetType().Name + ")";
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportUnhandledException(
                e.Exception,
                UiLocalization.Text("Program.Source.UiThreadException", "UI Thread Exception"),
                true);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception
                ?? new InvalidOperationException(
                    UiLocalization.Text("Program.UnknownUnhandledException", "An unknown unhandled exception occurred."));
            ReportUnhandledException(
                exception,
                UiLocalization.Text("Program.Source.UnhandledException", "Unhandled Exception"),
                true);
        }

        private static void OnUnobservedTaskException(
            object sender,
            System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            ReportUnhandledException(
                e.Exception,
                UiLocalization.Text("Program.Source.BackgroundTaskException", "Background Task Exception"),
                false);
            e.SetObserved();
        }

        private static void ReportUnhandledException(Exception exception, string source, bool showMessageBox)
        {
            if (exception == null)
            {
                return;
            }

            try
            {
                Trace.TraceError("[{0}] {1}", source, exception);
            }
            catch
            {
            }

            if (!showMessageBox)
            {
                return;
            }

            try
            {
                MessageBox.Show(
                    string.Format(
                        UiLocalization.Text(
                            "Program.ErrorDialogTemplate",
                            "{0}\r\n\r\n{1}\r\n\r\nDetails have been written to the log directory."),
                        source,
                        exception.Message),
                    AppConstants.AppName,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
            }
        }

        // Only migration preparation uses the legacy channel gate. Target lifetimes use their own gate.
        private sealed class LegacyStartupLease : IDisposable
        {
            private readonly Mutex _mutex;

            private LegacyStartupLease(Mutex mutex)
            {
                _mutex = mutex;
            }

            public static LegacyStartupLease TryAcquire(string channelId)
            {
                var mutex = new Mutex(false, @"Local\MyCodex.SingleInstance." + channelId);
                try
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(3000); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (acquired) return new LegacyStartupLease(mutex);
                    mutex.Dispose();
                    return null;
                }
                catch { mutex.Dispose(); throw; }
            }

            public void Dispose()
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }

        private sealed class AppChannel
        {
            private const string StableChannelId = "Stable";
            private const string DevChannelId = "Dev";

            private AppChannel(string id, string displayName, string appDataDirectoryName)
            {
                Id = id;
                DisplayName = displayName;
                AppDataDirectoryName = appDataDirectoryName;
            }

            public string Id { get; }

            public string DisplayName { get; }

            public string AppDataDirectoryName { get; }

            public static AppChannel Resolve(string[] args)
            {
                var requestedChannel = GetCommandLineChannel(args);
                if (string.IsNullOrWhiteSpace(requestedChannel))
                {
                    requestedChannel = Environment.GetEnvironmentVariable("MYCODEX_CHANNEL");
                }

                if (string.IsNullOrWhiteSpace(requestedChannel))
                {
#if MYCODEX_DEV
                    requestedChannel = DevChannelId;
#else
                    requestedChannel = StableChannelId;
#endif
                }

                if (string.Equals(requestedChannel, DevChannelId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(requestedChannel, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    return new AppChannel(DevChannelId, "MyCodex 开发版", "MyCodex-Dev");
                }

                return new AppChannel(StableChannelId, AppConstants.AppName, AppConstants.AppName);
            }

            private static string GetCommandLineChannel(string[] args)
            {
                if (args == null || args.Length == 0)
                {
                    return null;
                }

                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        continue;
                    }

                    if (arg.StartsWith("--channel=", StringComparison.OrdinalIgnoreCase))
                    {
                        return arg.Substring("--channel=".Length);
                    }

                    if (string.Equals(arg, "--channel", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        return args[i + 1];
                    }
                }

                return null;
            }
        }
    }
}
