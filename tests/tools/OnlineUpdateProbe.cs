// Process-isolated update UI probe. Reuses the existing offline MainForm fixture.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyVersion("8.0.1.0")]
[assembly: System.Reflection.AssemblyFileVersion("8.0.1.0")]
[assembly: System.Reflection.AssemblyInformationalVersion("8.0.1")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

internal static class OnlineUpdateProbe
{
    private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string Installation = AppDomain.CurrentDomain.BaseDirectory;
    private static string Evidence;
    private static object Field(object value, string name) { return value.GetType().GetField(name, Members).GetValue(value); }
    private static void Set(object value, string name, object field) { value.GetType().GetField(name, Members).SetValue(value, field); }
    private static object Call(object value, string name, params object[] args) { return value.GetType().GetMethod(name, Members).Invoke(value, args); }
    private static void Mark(string name, string text) { File.WriteAllText(Path.Combine(Evidence, name + ".txt"), text); }

    [STAThread]
    private static int Main(string[] args)
    {
        Evidence = File.ReadAllText(Path.Combine(Installation, "evidence-root.txt"));
        AppDomain.CurrentDomain.AssemblyResolve += (sender, request) =>
        {
            if (new AssemblyName(request.Name).Name == "MyCodex")
                return Assembly.LoadFrom(Path.Combine(Installation, "MyCodex.Client.exe"));
            return null;
        };
        try
        {
            if (args.Length == 1 && args[0] == "--exit-for-install")
            {
                var infrastructure = Assembly.LoadFrom(Path.Combine(Installation, "Infrastructure.dll"));
                var exit = infrastructure.GetType("MyCodex.Infrastructure.Instances.InstallationTargetExit").GetMethod("RunAsync");
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60)))
                {
                    var root = File.ReadAllText(Path.Combine(Installation, "fixture-root.txt"));
                    return ((Task<int>)exit.Invoke(null, new object[] { new[] { root }, timeout.Token })).GetAwaiter().GetResult();
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var tests = Assembly.LoadFrom(Path.Combine(Installation, "MyCodex.Application.Tests.dll"));
            var builder = Activator.CreateInstance(Assembly.LoadFrom(Path.Combine(Installation, "nunit.framework.dll"))
                .GetType("NUnit.Framework.Api.DefaultTestAssemblyBuilder"));
            builder.GetType().GetMethod("Build", new[] { typeof(Assembly), typeof(IDictionary<string, object>) })
                .Invoke(builder, new object[] { tests, new Dictionary<string, object> { { "WorkDirectory", Evidence } } });
            var testType = tests.GetType("MyCodex.Application.Tests.MainFormTargetIntegrationTests");
            var fixture = Activator.CreateInstance(testType.GetNestedType("TargetFormFixture", BindingFlags.NonPublic),
                Members, null, new object[] { true, false }, null);
            var rootPath = (string)Field(fixture, "Root");
            if (!Path.GetFullPath(rootPath).StartsWith(Path.GetFullPath(Evidence) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Fixture data escaped the isolated directory.");
            File.WriteAllText(Path.Combine(Installation, "fixture-root.txt"), rootPath);
            var form = (Form)Field(fixture, "Form");
            // Match the existing native-exit fixture: never run shared user-service teardown.
            form.Activated -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), form, "OnModelConfigurationActivated");
            form.FormClosed -= (FormClosedEventHandler)Delegate.CreateDelegate(typeof(FormClosedEventHandler), form, "OnFormClosed");
            Call(form, "DisposeRemoteHostService");
            Set(form, "_remoteHostServiceController", null);
            var config = Field(form, "_appConfigSnapshot");
            config.GetType().GetProperty("CodexRuntimeSource").SetValue(config, "Wsl", null);
            var state = Field(form, "_appStateSnapshot");
            state.GetType().GetProperty("HasCompletedStartupModeOnboarding").SetValue(state, true, null);
            var files = Activator.CreateInstance(testType.GetNestedType("PendingModeSettingsFile", BindingFlags.NonPublic), true);
            var pending = Field(files, "Pending");
            pending.GetType().GetMethod("SetResult").Invoke(pending, new object[] { Tuple.Create(false, string.Empty) });
            var modeType = Assembly.LoadFrom(Path.Combine(Installation, "Infrastructure.dll")).GetType("MyCodex.Infrastructure.Persistence.JsonModeSettingsStore");
            Set(form, "_modeSettingsStore", Activator.CreateInstance(modeType, Members, null,
                new[] { Field(fixture, "Paths"), files }, null));
            bool automatic = args.Length == 0 || args[0] == "automatic";
            if (!automatic)
            {
                form.Shown -= (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), form, "OnShown");
                Call(form, "CompleteTargetStartup");
                form.Shown += (sender, e) => ((MenuItem)Field(form, "_checkUpdatesMenuItem")).PerformClick();
            }
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-2400, -2400);
            var watch = Stopwatch.StartNew();
            using (var timer = new System.Windows.Forms.Timer { Interval = 25 })
            {
                timer.Tick += (sender, e) =>
                {
                    try
                    {
                        if (watch.Elapsed > TimeSpan.FromMinutes(2)) throw new TimeoutException("Online update did not complete.");
                        foreach (var dialog in Application.OpenForms.Cast<Form>().ToArray())
                        {
                            if (dialog.GetType().Name == "AppUpdatePromptForm")
                            {
                                Mark("prompt", automatic ? "startup automatic check" : "Help menu check");
                                dialog.AcceptButton.PerformClick();
                            }
                            if (dialog.GetType().Name == "AppUpdateDownloadForm")
                            {
                                var download = dialog.GetType().GetProperty("DownloadResult").GetValue(dialog, null);
                                if (download == null) continue;
                                var installer = (string)download.GetType().GetProperty("InstallerPath").GetValue(download, null);
                                if (((ProgressBar)Field(dialog, "_progressBar")).Value != 100) throw new InvalidOperationException("Download did not finish.");
                                Mark("download", installer);
                                dialog.AcceptButton.PerformClick();
                            }
                        }
                    }
                    catch (Exception error) { Mark("error", error.ToString()); Environment.Exit(20); }
                };
                timer.Start();
                Application.Run(form);
            }
            Mark("saved", Field(Field(fixture, "State"), "SaveCalls").ToString());
            ((IDisposable)Field(fixture, "_instance")).Dispose();
            // Keep downloaded Setup and fixture evidence until the installer finishes.
            return 0;
        }
        catch (Exception error) { Mark("error", error.ToString()); return 20; }
    }
}
