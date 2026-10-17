using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using MyCodex.Application.DTOs;
using MyCodex.Application.Interfaces;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using MyCodex.Infrastructure.Codex;
using MyCodex.Infrastructure.Persistence;
using MyCodex.WinForms.Forms;
using MyCodex.WinForms.Localization;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    [Apartment(ApartmentState.STA)]
    public sealed class AuthenticationReconnectNotificationTests
    {
        [TestCase("auth_changed", false)]
        [TestCase("auth_login_completed", false)]
        [TestCase("auth_changed", true)]
        [TestCase("auth_login_completed", true)]
        [TestCase(null, false)]
        [Repeat(10)]
        public void NotificationDuringReconnectDoesNotCancelModelInitialization(string eventType, bool recovering)
        {
            var paths = new AppConfigService(Path.Combine(TestContext.CurrentContext.WorkDirectory,
                "auth-reconnect-" + Guid.NewGuid().ToString("N")));
            var config = new JsonAppConfigStore(paths);
            var runtime = new CodexRuntimeManager(paths);
            using (var client = new Client(runtime, config))
            using (var form = new MainForm(client, new JsonAppStateStore(paths), config, paths, new Reader(), runtime))
            {
                try
                {
                    var handle = form.Handle;
                    Set(form, "_connectionStatus", UiLocalization.Text("MainForm.Connection.Connected", "Connected"));
                    // The transition owns the fresh read after restarting App Server.
                    var transitionField = recovering ? "_isRecoveringAppServerConnection" : "_isRuntimeWindowTransitionInProgress";
                    Set(form, transitionField, true);
                    var read = (Task<AppServerModelConfigurationObservation>)Invoke(form,
                        "RefreshModelConfigurationAsync", null, false, false);
                    PumpUntil(() => client.ConfigReadStarted);
                    if (eventType != null) client.Raise(eventType);
                    client.ConfigReply.SetResult(new AppServerConfigSnapshot { ModelId = "new-provider-model" });
                    PumpUntil(() => read.IsCompleted);

                    Assert.That(read.GetAwaiter().GetResult(), Is.Not.Null,
                        "An authentication notification must not invalidate the model read that determines reconnect success.");
                    Assert.That(client.ConnectCalls, Is.Zero, "The active transition already owns the connection.");
                    Assert.That(client.AuthenticationReads, Is.Zero);
                    Assert.That(client.ModelReads, Is.EqualTo(1));

                    Set(form, transitionField, false);
                    client.Raise("auth_changed");
                    PumpUntil(() => client.ModelReads == 2 && !(bool)Field(form, "_isLoadingModels"));
                    Assert.That(client.AuthenticationReads, Is.EqualTo(1), "Ordinary account notifications still refresh authentication.");
                }
                finally
                {
                    // The real FormClosed handler also stops the user's local auth backend;
                    // release only the resources this isolated, never-shown form started.
                    Microsoft.Win32.SystemEvents.UserPreferenceChanged -= (Microsoft.Win32.UserPreferenceChangedEventHandler)
                        Delegate.CreateDelegate(typeof(Microsoft.Win32.UserPreferenceChangedEventHandler), form, "OnSystemUserPreferenceChanged");
                    Invoke(form, "DisposeThreadSyncTimersOnExit");
                    Invoke(form, "DisposeLocalSyncTimersOnExit");
                    Invoke(form, "DisposeRateLimitAutoResetTimerOnExit");
                    Invoke(form, "DisposeRemoteHostService");
                    Invoke(form, "DisposeTrayIconOnExit");
                }
            }
        }

        private static void PumpUntil(Func<bool> complete)
        {
            var timer = Stopwatch.StartNew();
            while (!complete() && timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(1);
            }
            Assert.That(complete(), Is.True, "The isolated UI operation did not complete.");
        }

        private static void Set(object target, string name, object value)
        {
            target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        }

        private static object Field(object target, string name)
        {
            return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        }

        private static object Invoke(object target, string name, params object[] args)
        {
            return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
        }

        private sealed class Client : AppServerClient, IAppServerClient
        {
            internal readonly TaskCompletionSource<AppServerConfigSnapshot> ConfigReply =
                new TaskCompletionSource<AppServerConfigSnapshot>();
            internal bool ConfigReadStarted;
            internal int ConnectCalls, AuthenticationReads, ModelReads;
            public Client(CodexRuntimeManager runtime, IAppConfigStore config) : base(runtime, config) { }
            public new event EventHandler<AppServerEventArgs> EventReceived;
            public new AppServerProtocolCapabilities ProtocolCapabilities { get; } = new AppServerProtocolCapabilities();
            internal void Raise(string type) { EventReceived?.Invoke(this, new AppServerEventArgs(type, "Authentication changed")); }
            public new Task ConnectAsync() { ConnectCalls++; return Task.CompletedTask; }
            public new Task<AppServerAuthenticationState> GetAuthenticationStateAsync(bool refreshToken)
            {
                AuthenticationReads++;
                return Task.FromResult(new AppServerAuthenticationState());
            }
            public new Task<AppServerConfigSnapshot> ReadConfigAsync(string workspacePath, bool includeLayers)
            {
                ConfigReadStarted = true;
                return ConfigReply.Task;
            }
            public new Task<List<AppServerModelOption>> ListModelsAsync()
            {
                ModelReads++;
                return Task.FromResult(new List<AppServerModelOption> { new AppServerModelOption { ModelId = "new-provider-model" } });
            }
        }

        private sealed class Reader : IScreenReaderService
        {
            public bool IsAvailable => false;
            public bool IsInitialized => false;
            public string LastError => null;
            public bool EnsureInitialized() => false;
            public bool IsReaderRunning() => false;
            public int GetReaderState() => 0;
            public bool Speak(string text, bool useSlave, bool append, bool allowBreak) => false;
            public bool Stop(bool useSlave) => false;
            public void Dispose() { }
        }
    }
}
