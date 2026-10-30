using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using MyCodex.Application.Interfaces;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Persistence;
using MyCodex.Infrastructure.Tts;
using MyCodex.Infrastructure.ByCtrl;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ConfigurableScreenReaderServiceTests
    {
        [TestCase(null, "MyCodex")]
        [TestCase(" Shared replies ", "Shared replies")]
        public void TargetChannels_ShareConfiguredNameAndReleaseWithoutStoppingSharedSpeech(string name, string expected)
        {
            var firstTransport = new RecordingChannelTransport();
            var secondTransport = new RecordingChannelTransport();
            var config = new AppConfigSnapshot
            { ScreenReaderProvider = ScreenReaderProviderKind.BaoYiWebApi, ScreenReaderIndependentChannelName = name };
            var first = new ConfigurableScreenReaderService(new UnavailableScreenReaderService(),
                new BaoYiWebApiScreenReaderService(firstTransport), new UnavailableScreenReaderService(),
                new UnavailableScreenReaderService(), config);
            var second = new ConfigurableScreenReaderService(new UnavailableScreenReaderService(),
                new BaoYiWebApiScreenReaderService(secondTransport), new UnavailableScreenReaderService(),
                new UnavailableScreenReaderService(), config);
            try
            {
                Assert.That(first.Speak("A", true, false, true), Is.True);
                Assert.That(second.Speak("B", true, false, true), Is.True);
                Assert.That(first.StopOwnedOutput(), Is.True);
                Assert.That(firstTransport.LastChannel, Is.EqualTo(expected));
                Assert.That(firstTransport.StopCount, Is.Zero);
                Assert.That(secondTransport.LastChannel, Is.EqualTo(expected));
                Assert.That(secondTransport.StopCount, Is.Zero);
                first.Dispose();
                DrainOperations(first);
                Assert.That(firstTransport.StopCount, Is.Zero);
                Assert.That(second.Speak("B still reads", true, false, true), Is.True);
                Assert.That(secondTransport.SpeakCount, Is.EqualTo(2));
            }
            finally
            {
                first.Dispose();
                second.Dispose();
                DrainOperations(first);
                DrainOperations(second);
            }
        }

        [Test]
        public void ChannelRenameAndExplicitStop_UseTheUserSelectedChannelWithoutAutomaticSuffixes()
        {
            var transport = new RecordingChannelTransport();
            var config = new AppConfigSnapshot
            { ScreenReaderProvider = ScreenReaderProviderKind.BaoYiWebApi, ScreenReaderIndependentChannelName = "Shared replies" };
            using (var service = new ConfigurableScreenReaderService(new UnavailableScreenReaderService(),
                new BaoYiWebApiScreenReaderService(transport), new UnavailableScreenReaderService(),
                new UnavailableScreenReaderService(), config))
            {
                Assert.That(service.Speak("shared", true, false, true), Is.True);
                DrainOperations(service);
                Assert.That(transport.LastChannel, Is.EqualTo("Shared replies"));
                config.ScreenReaderIndependentChannelName = "My chosen channel";
                service.ApplyConfig(config);
                Assert.That(service.Speak("custom", true, false, true), Is.True);
                DrainOperations(service);
                Assert.That(transport.LastChannel, Is.EqualTo("My chosen channel"));
                Assert.That(service.Stop(true), Is.True);
                DrainOperations(service);
                Assert.That(transport.LastChannel, Is.EqualTo("My chosen channel"));
                Assert.That(transport.StopCount, Is.EqualTo(1));
                service.Dispose();
                Assert.That(transport.StopCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void TargetCleanup_DoesNotStopNvdaGlobalOutputOrStartUnusedProvider()
        {
            var nvda = new RecordingScreenReaderService();
            using (var service = CreateService(new UnavailableScreenReaderService(), ScreenReaderProviderKind.Nvda,
                nvdaService: nvda))
            {
                service.Stop(true);
                Assert.That(nvda.StopCount, Is.Zero);
                Assert.That(service.Speak("reply", true, false, true), Is.True);
                Assert.That(service.StopOwnedOutput(), Is.True);
                Assert.That(nvda.StopCount, Is.Zero);
                service.Dispose();
                DrainOperations(service);
                Assert.That(nvda.StopCount, Is.Zero);
            }
        }

        [Test]
        public void TargetCleanup_DoesNotStopNativeReaderMainChannel()
        {
            var transport = new RecordingChannelTransport();
            using (var service = new ConfigurableScreenReaderService(new UnavailableScreenReaderService(),
                new BaoYiWebApiScreenReaderService(transport), new UnavailableScreenReaderService(),
                new UnavailableScreenReaderService(), new AppConfigSnapshot
                {
                    ScreenReaderProvider = ScreenReaderProviderKind.BaoYiWebApi,
                    ManualBrowseUseReaderMainChannel = true
                }))
            {
                Assert.That(service.Speak("manual", false, false, true), Is.True);
                Assert.That(transport.LastChannel, Is.Null);
                service.ApplyConfig(new AppConfigSnapshot
                {
                    ScreenReaderProvider = ScreenReaderProviderKind.BaoYiWebApi,
                    ManualBrowseUseReaderMainChannel = true,
                    EnableAutomaticChatReading = false
                });
                Assert.That(service.Speak("manual remains available", false, false, true), Is.True);
                service.StopOwnedOutput();
                Assert.That(transport.StopCount, Is.Zero);
            }
        }

        [Test]
        public void NormalizeProvider_MapsLegacyProviderValueToByCtrl()
        {
            var method = typeof(ConfigurableScreenReaderService).GetMethod(
                "NormalizeProvider",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            var normalized = (ScreenReaderProviderKind)method.Invoke(
                null,
                new object[] { (ScreenReaderProviderKind)1 });

            Assert.That(normalized, Is.EqualTo(ScreenReaderProviderKind.ByCtrl));
        }

        [Test]
        public void BuildAutomaticProviderOrder_PrefersBaoYiWebApiThenByCtrlAndExcludesLegacyProvider()
        {
            var method = typeof(ConfigurableScreenReaderService).GetMethod(
                "BuildAutomaticProviderOrder",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            var providers = (ScreenReaderProviderKind[])method.Invoke(null, new object[0]);

            Assert.That(providers[0], Is.EqualTo(ScreenReaderProviderKind.BaoYiWebApi));
            Assert.That(providers[1], Is.EqualTo(ScreenReaderProviderKind.ByCtrl));
            Assert.That(System.Array.IndexOf(providers, (ScreenReaderProviderKind)1), Is.EqualTo(-1));
        }

        [Test]
        public void NormalizeProvider_AcceptsBaoYiWebApi()
        {
            var method = typeof(ConfigurableScreenReaderService).GetMethod(
                "NormalizeProvider",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            var normalized = (ScreenReaderProviderKind)method.Invoke(
                null,
                new object[] { ScreenReaderProviderKind.BaoYiWebApi });

            Assert.That(normalized, Is.EqualTo(ScreenReaderProviderKind.BaoYiWebApi));
        }

        [Test]
        public void BuildAutomaticProviderOrder_IncludesBaoYiWebApiBeforeByCtrl()
        {
            var method = typeof(ConfigurableScreenReaderService).GetMethod(
                "BuildAutomaticProviderOrder",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            var providers = (ScreenReaderProviderKind[])method.Invoke(null, new object[0]);

            Assert.That(System.Array.IndexOf(providers, ScreenReaderProviderKind.BaoYiWebApi), Is.LessThan(System.Array.IndexOf(providers, ScreenReaderProviderKind.ByCtrl)));
        }

        [Test]
        public void MapUseSlave_TreatsZdsrAsMultiChannelProvider()
        {
            var method = typeof(ConfigurableScreenReaderService).GetMethod(
                "IsMultiChannelScreenReaderService",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            var result = (bool)method.Invoke(
                null,
                new object[] { typeof(MyCodex.Infrastructure.Zdsr.ZdsrScreenReaderService) });

            Assert.That(result, Is.True);
        }

        [Test]
        public void Constructor_ConfigLoadFailureDefersRecoveryToMainForm()
        {
            Assert.DoesNotThrow(() =>
            {
                using (new ConfigurableScreenReaderService(
                    new AppConfigService(TestContext.CurrentContext.WorkDirectory),
                    new ThrowingAppConfigStore()))
                {
                }
            });
        }

        [Test]
        public void Speak_WithAutoProvider_ReturnsBeforeUnderlyingProviderCompletes()
        {
            var zdsr = new BlockingScreenReaderService();
            using (var service = CreateService(zdsr, ScreenReaderProviderKind.Auto))
            {
                var stopwatch = Stopwatch.StartNew();

                var result = service.Speak("hello", true, false, true);

                stopwatch.Stop();
                zdsr.Release.Set();
                Assert.That(result, Is.True);
                Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(250));
                Assert.That(zdsr.SpeakStarted.WaitOne(1000), Is.True);
            }
        }

        [Test]
        public void EnsureInitialized_WithZdsrProvider_ReturnsBeforeUnderlyingProviderCompletes()
        {
            var zdsr = new BlockingScreenReaderService();
            using (var service = CreateService(zdsr, ScreenReaderProviderKind.Zdsr))
            {
                var stopwatch = Stopwatch.StartNew();

                var result = service.EnsureInitialized();

                stopwatch.Stop();
                zdsr.Release.Set();
                Assert.That(result, Is.True);
                Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(250));
                Assert.That(zdsr.InitializeStarted.WaitOne(1000), Is.True);
            }
        }

        [Test]
        public void Speak_WithAutoProvider_ReusesLastSuccessfulProviderForLaterSpeech()
        {
            var baoYiWebApi = new SlowUnavailableScreenReaderService();
            var zdsr = new RecordingScreenReaderService();
            using (var service = CreateService(
                zdsr,
                ScreenReaderProviderKind.Auto,
                baoYiWebApiService: baoYiWebApi))
            {
                Assert.That(service.Speak("first", true, false, true), Is.True);
                Assert.That(zdsr.FirstSpeakStarted.WaitOne(2000), Is.True);
                Assert.That(baoYiWebApi.EnsureInitializedCalls, Is.EqualTo(1));

                Assert.That(service.Speak("second", true, false, true), Is.True);

                Assert.That(zdsr.SecondSpeakStarted.WaitOne(400), Is.True);
                Assert.That(baoYiWebApi.EnsureInitializedCalls, Is.EqualTo(1));
            }
        }

        [TestCase(ScreenReaderProviderKind.BaoYiWebApi, ScreenReaderProviderKind.Nvda)]
        [TestCase(ScreenReaderProviderKind.BaoYiWebApi, ScreenReaderProviderKind.Zdsr)]
        [TestCase(ScreenReaderProviderKind.ByCtrl, ScreenReaderProviderKind.Nvda)]
        [TestCase(ScreenReaderProviderKind.ByCtrl, ScreenReaderProviderKind.Zdsr)]
        public void Speak_AfterBaoYiExits_SwitchesToRunningReader(ScreenReaderProviderKind previousProvider, ScreenReaderProviderKind nextProvider)
        {
            var baoYi = new RecordingScreenReaderService();
            var next = new RecordingScreenReaderService { Running = false };
            using (var service = CreateService(
                nextProvider == ScreenReaderProviderKind.Zdsr ? next : new UnavailableScreenReaderService(),
                ScreenReaderProviderKind.Auto,
                byCtrlService: previousProvider == ScreenReaderProviderKind.ByCtrl ? baoYi : null,
                baoYiWebApiService: previousProvider == ScreenReaderProviderKind.BaoYiWebApi ? baoYi : null,
                nvdaService: nextProvider == ScreenReaderProviderKind.Nvda ? next : null))
            {
                Assert.That(service.Speak("before switch", true, false, true), Is.True);
                DrainOperations(service);
                Assert.That(baoYi.SpeakCount, Is.EqualTo(1));

                baoYi.Running = false;
                next.Running = true;
                Assert.That(service.EnsureInitialized(), Is.True);
                Assert.That(service.Stop(true), Is.True);
                Assert.That(service.Speak("after switch", true, false, true), Is.True);
                DrainOperations(service);

                Assert.That(next.SpeakCount, Is.EqualTo(1), "The first message after switching must reach the running reader.");
                Assert.That(baoYi.SpeakCount, Is.EqualTo(1), "An available API is not evidence that its reader is still running.");
            }
        }

        [Test]
        public void Speak_WithBaoYiWebApi_HandlesIdleExitAndRestartWithoutRecreatingService()
        {
            var transport = new SwitchingBaoYiTransport();
            var nvda = new RecordingScreenReaderService { Running = false };
            using (var service = CreateService(new UnavailableScreenReaderService(), ScreenReaderProviderKind.Auto,
                baoYiWebApiService: new BaoYiWebApiScreenReaderService(transport), nvdaService: nvda))
            {
                service.Speak("idle baoyi", true, false, true);
                DrainOperations(service);
                Assert.That(transport.SpeakCount, Is.EqualTo(1));

                transport.Online = false;
                nvda.Running = true;
                service.EnsureInitialized();
                service.Stop(true);
                service.Speak("nvda", true, false, true);
                DrainOperations(service);
                Assert.That(nvda.SpeakCount, Is.EqualTo(1));

                nvda.Running = false;
                transport.Online = true;
                service.Speak("baoyi restarted", true, false, true);
                DrainOperations(service);
                Assert.That(transport.SpeakCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void Speak_WithExplicitProvider_DoesNotFallBackToAnotherReader()
        {
            var baoYi = new RecordingScreenReaderService();
            var nvda = new RecordingScreenReaderService { SpeakSucceeds = false };
            using (var service = CreateService(new UnavailableScreenReaderService(), ScreenReaderProviderKind.Nvda,
                baoYiWebApiService: baoYi, nvdaService: nvda))
            {
                Assert.That(service.Speak("explicit nvda", true, false, true), Is.False);
                Assert.That(nvda.SpeakCount, Is.EqualTo(1));
                Assert.That(baoYi.SpeakCount, Is.Zero);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Speak_WhenCachedReaderFails_RetriesSameMessageOnNextReader(bool throws)
        {
            var baoYi = new RecordingScreenReaderService();
            var nvda = new RecordingScreenReaderService();
            using (var service = CreateService(new UnavailableScreenReaderService(), ScreenReaderProviderKind.Auto,
                baoYiWebApiService: baoYi, nvdaService: nvda))
            {
                service.Speak("first", true, false, true);
                DrainOperations(service);
                baoYi.SpeakSucceeds = false;
                baoYi.ThrowOnSpeak = throws;
                service.Speak("switch during dispatch", true, false, true);
                DrainOperations(service);
                Assert.That(nvda.SpeakCount, Is.EqualTo(1));
                Assert.That(nvda.LastSpokenText, Is.EqualTo("switch during dispatch"));
                Assert.That(service.LastError, Is.Null);
            }
        }

        [Test]
        public void Speak_WhenNoReaderIsRunning_DoesNotDispatchAndDetectsLaterStartup()
        {
            var baoYi = new RecordingScreenReaderService { Running = false };
            var nvda = new RecordingScreenReaderService { Running = false };
            using (var service = CreateService(new UnavailableScreenReaderService(), ScreenReaderProviderKind.Auto,
                baoYiWebApiService: baoYi, nvdaService: nvda))
            {
                service.EnsureInitialized();
                service.Stop(true);
                service.Speak("no reader", true, false, true);
                DrainOperations(service);
                Assert.That(baoYi.SpeakCount, Is.Zero);
                Assert.That(nvda.SpeakCount, Is.Zero);
                Assert.That(service.LastError, Is.Not.Null.And.Not.Empty);

                nvda.Running = true;
                service.Speak("reader started", true, false, true);
                DrainOperations(service);
                Assert.That(nvda.SpeakCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void Speak_AfterNvdaExits_CanSwitchToPreviouslyUnavailableBaoYiAndBack()
        {
            var baoYi = new RecordingScreenReaderService { Running = false };
            var nvda = new RecordingScreenReaderService();
            using (var service = CreateService(new UnavailableScreenReaderService(), ScreenReaderProviderKind.Auto,
                baoYiWebApiService: baoYi, nvdaService: nvda))
            {
                service.Speak("nvda", true, false, true);
                DrainOperations(service);
                nvda.Running = false;
                baoYi.Running = true;
                service.Speak("baoyi", true, false, true);
                DrainOperations(service);
                Assert.That(baoYi.SpeakCount, Is.EqualTo(1));

                baoYi.Running = false;
                nvda.Running = true;
                service.Speak("nvda again", true, false, true);
                DrainOperations(service);
                Assert.That(nvda.SpeakCount, Is.EqualTo(2));
            }
        }

        private static void DrainOperations(ConfigurableScreenReaderService service)
        {
            var field = typeof(ConfigurableScreenReaderService).GetField("_operationQueueTail", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(((System.Threading.Tasks.Task)field.GetValue(service)).Wait(5000), Is.True);
        }

        private static ConfigurableScreenReaderService CreateService(
            IScreenReaderService zdsrService,
            ScreenReaderProviderKind provider,
            IScreenReaderService byCtrlService = null,
            IScreenReaderService baoYiWebApiService = null,
            IScreenReaderService nvdaService = null)
        {
            var constructor = typeof(ConfigurableScreenReaderService).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(IScreenReaderService),
                    typeof(IScreenReaderService),
                    typeof(IScreenReaderService),
                    typeof(IScreenReaderService),
                    typeof(AppConfigSnapshot)
                },
                null);

            Assert.That(constructor, Is.Not.Null);

            return (ConfigurableScreenReaderService)constructor.Invoke(new object[]
            {
                byCtrlService ?? new UnavailableScreenReaderService(),
                baoYiWebApiService ?? new UnavailableScreenReaderService(),
                zdsrService,
                nvdaService ?? new UnavailableScreenReaderService(),
                new AppConfigSnapshot { ScreenReaderProvider = provider }
            });
        }

        private sealed class SwitchingBaoYiTransport : IBaoYiWebApiScreenReaderTransport
        {
            public bool Online { get; set; } = true;

            public int SpeakCount { get; private set; }

            public string Send(string requestJson, TimeSpan timeout)
            {
                if (!Online)
                {
                    throw new TimeoutException("reader exited");
                }

                var payload = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(requestJson);
                if (Equals(payload["action"], "speech.speak"))
                {
                    SpeakCount++;
                }

                return @"{""error"":""OK"",""status"":0}";
            }

            public void Dispose()
            {
            }
        }

        private sealed class BlockingScreenReaderService : IScreenReaderService
        {
            public ManualResetEvent InitializeStarted { get; } = new ManualResetEvent(false);

            public ManualResetEvent SpeakStarted { get; } = new ManualResetEvent(false);

            // The provider stays busy until the test has measured the caller; the timeout only bounds a failing test.
            public ManualResetEvent Release { get; } = new ManualResetEvent(false);

            public bool IsAvailable
            {
                get { return true; }
            }

            public bool IsInitialized
            {
                get { return false; }
            }

            public string LastError
            {
                get { return null; }
            }

            public bool EnsureInitialized()
            {
                InitializeStarted.Set();
                Release.WaitOne(750);
                return true;
            }

            public bool IsReaderRunning()
            {
                return true;
            }

            public int GetReaderState()
            {
                return 4;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                SpeakStarted.Set();
                Release.WaitOne(750);
                return true;
            }

            public bool Stop(bool useSlave)
            {
                return true;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingAppConfigStore : IAppConfigStore
        {
            public AppConfigSnapshot Load()
            {
                throw new System.IO.IOException("config unavailable");
            }

            public void Save(AppConfigSnapshot snapshot)
            {
                throw new System.NotSupportedException();
            }
        }

        private sealed class RecordingScreenReaderService : IScreenReaderService
        {
            private int _speakCalls;

            public bool Running { get; set; } = true;

            public bool SpeakSucceeds { get; set; } = true;

            public bool ThrowOnSpeak { get; set; }

            public string LastSpokenText { get; private set; }

            public int StopCount { get; private set; }

            public ManualResetEvent FirstSpeakStarted { get; } = new ManualResetEvent(false);

            public ManualResetEvent SecondSpeakStarted { get; } = new ManualResetEvent(false);

            public int SpeakCount
            {
                get { return _speakCalls; }
            }

            public bool IsAvailable
            {
                get { return true; }
            }

            public bool IsInitialized
            {
                get { return true; }
            }

            public string LastError
            {
                get { return null; }
            }

            public bool EnsureInitialized()
            {
                return true;
            }

            public bool IsReaderRunning()
            {
                return Running;
            }

            public int GetReaderState()
            {
                return 4;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                if (ThrowOnSpeak)
                {
                    throw new InvalidOperationException("reader disconnected during speech");
                }

                LastSpokenText = text;
                var callNumber = Interlocked.Increment(ref _speakCalls);
                if (callNumber == 1)
                {
                    FirstSpeakStarted.Set();
                }
                else if (callNumber == 2)
                {
                    SecondSpeakStarted.Set();
                }

                return SpeakSucceeds;
            }

            public bool WaitForSpeakCount(int expectedCount, int timeoutMilliseconds)
            {
                var stopwatch = Stopwatch.StartNew();
                while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
                {
                    if (SpeakCount >= expectedCount)
                    {
                        return true;
                    }

                    Thread.Sleep(10);
                }

                return SpeakCount >= expectedCount;
            }

            public bool Stop(bool useSlave)
            {
                StopCount++;
                return true;
            }

            public void Dispose()
            {
            }
        }

        private sealed class RecordingChannelTransport : IBaoYiWebApiScreenReaderTransport
        {
            public string LastChannel { get; private set; }
            public int StopCount { get; private set; }
            public int SpeakCount { get; private set; }

            public string Send(string requestJson, TimeSpan timeout)
            {
                var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var request = serializer.Deserialize<Dictionary<string, object>>(requestJson);
                var action = (string)request["action"];
                object name;
                LastChannel = request.TryGetValue("slave_name", out name) ? (string)name : null;
                if (action == "speech.speak")
                {
                    SpeakCount++;
                }
                if (action == "speech.stop")
                {
                    StopCount++;
                }
                return serializer.Serialize(new Dictionary<string, object>
                {
                    { "action", action }, { "error", "OK" }, { "status", 0 }
                });
            }

            public void Dispose() { }
        }

        private sealed class SlowUnavailableScreenReaderService : IScreenReaderService
        {
            private int _ensureInitializedCalls;

            public int EnsureInitializedCalls
            {
                get { return _ensureInitializedCalls; }
            }

            public bool IsAvailable
            {
                get { return true; }
            }

            public bool IsInitialized
            {
                get { return false; }
            }

            public string LastError
            {
                get { return null; }
            }

            public bool EnsureInitialized()
            {
                Interlocked.Increment(ref _ensureInitializedCalls);
                Thread.Sleep(1000);
                return false;
            }

            public bool IsReaderRunning()
            {
                return false;
            }

            public int GetReaderState()
            {
                return 0;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                return false;
            }

            public bool Stop(bool useSlave)
            {
                return false;
            }

            public void Dispose()
            {
            }
        }

        private sealed class UnavailableScreenReaderService : IScreenReaderService
        {
            public bool IsAvailable
            {
                get { return false; }
            }

            public bool IsInitialized
            {
                get { return false; }
            }

            public string LastError
            {
                get { return null; }
            }

            public bool EnsureInitialized()
            {
                return false;
            }

            public bool IsReaderRunning()
            {
                return false;
            }

            public int GetReaderState()
            {
                return 0;
            }

            public bool Speak(string text, bool useSlave, bool append, bool allowBreak)
            {
                return false;
            }

            public bool Stop(bool useSlave)
            {
                return false;
            }

            public void Dispose()
            {
            }
        }
    }
}
