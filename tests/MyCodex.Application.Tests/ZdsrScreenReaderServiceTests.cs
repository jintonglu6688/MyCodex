using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using MyCodex.Infrastructure.Tts;
using MyCodex.Infrastructure.Zdsr;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ZdsrScreenReaderServiceTests
    {
        [TestCase(ScreenReaderProviderKind.Zdsr, false)]
        [TestCase(ScreenReaderProviderKind.Zdsr, true)]
        [TestCase(ScreenReaderProviderKind.Auto, false)]
        [TestCase(ScreenReaderProviderKind.Auto, true)]
        public void ConfiguredChannel_IsUsedDuringInitializationWithoutCreatingAnExtraDefaultChannel(
            ScreenReaderProviderKind provider, bool initializeFirst)
        {
            var nativeApi = new FakeZdsrNativeApi();
            using (var service = new ConfigurableScreenReaderService(
                new ZdsrScreenReaderService(new FakeZdsrNativeApi { Available = false }),
                new ZdsrScreenReaderService(new FakeZdsrNativeApi { Available = false }),
                new ZdsrScreenReaderService(nativeApi),
                new ZdsrScreenReaderService(new FakeZdsrNativeApi { Available = false }),
                new AppConfigSnapshot
                {
                    ScreenReaderProvider = provider,
                    ScreenReaderIndependentChannelName = "Shared replies"
                }))
            {
                if (initializeFirst)
                {
                    Assert.That(service.EnsureInitialized(), Is.True);
                }
                Assert.That(service.Speak("reply", true, false, true), Is.True);
                Assert.That(service.StopOwnedOutput(), Is.True);
                Assert.That(nativeApi.InitCalls, Has.Count.EqualTo(1));
                Assert.That(nativeApi.InitCalls[0].ChannelName, Is.EqualTo("Shared replies"));
                Assert.That(nativeApi.SpeakCalls, Has.Count.EqualTo(1));
                Assert.That(nativeApi.StopCount, Is.Zero);
            }
            Assert.That(nativeApi.StopCount, Is.Zero);
        }

        [Test]
        public void Speak_WithSlaveUsesIndependentChannelName()
        {
            var nativeApi = new FakeZdsrNativeApi();

            using (var service = new ZdsrScreenReaderService(nativeApi))
            {
                service.ApplyIndependentChannelName("  Work Channel  ");

                var result = service.Speak("hello", true, false, false);

                Assert.That(result, Is.True);
                Assert.That(nativeApi.InitCalls, Has.Count.EqualTo(1));
                Assert.That(nativeApi.InitCalls[0].Type, Is.EqualTo(1));
                Assert.That(nativeApi.InitCalls[0].ChannelName, Is.EqualTo("Work Channel"));
                Assert.That(nativeApi.InitCalls[0].KeyDownInterrupt, Is.False);
                Assert.That(nativeApi.SpeakCalls, Has.Count.EqualTo(1));
                Assert.That(nativeApi.SpeakCalls[0].Text, Is.EqualTo("hello"));
                Assert.That(nativeApi.SpeakCalls[0].Interrupt, Is.True);
            }
        }

        [Test]
        public void Speak_WithoutSlaveUsesReaderMainChannel()
        {
            var nativeApi = new FakeZdsrNativeApi();

            using (var service = new ZdsrScreenReaderService(nativeApi))
            {
                service.ApplyIndependentChannelName("Work Channel");

                var result = service.Speak("line", false, true, true);

                Assert.That(result, Is.True);
                Assert.That(nativeApi.InitCalls, Has.Count.EqualTo(1));
                Assert.That(nativeApi.InitCalls[0].Type, Is.EqualTo(0));
                Assert.That(nativeApi.InitCalls[0].ChannelName, Is.Null);
                Assert.That(nativeApi.InitCalls[0].KeyDownInterrupt, Is.True);
                Assert.That(nativeApi.SpeakCalls[0].Interrupt, Is.False);
            }
        }

        [Test]
        public void Stop_SwitchesToRequestedChannelBeforeStopping()
        {
            var nativeApi = new FakeZdsrNativeApi();

            using (var service = new ZdsrScreenReaderService(nativeApi))
            {
                service.ApplyIndependentChannelName("Work Channel");

                var result = service.Stop(false);

                Assert.That(result, Is.True);
                Assert.That(nativeApi.InitCalls, Has.Count.EqualTo(1));
                Assert.That(nativeApi.InitCalls[0].Type, Is.EqualTo(0));
                Assert.That(nativeApi.StopCount, Is.EqualTo(1));
            }
        }

        [Test]
        public void Speak_WithEmptyIndependentChannelUsesMyCodexChannel()
        {
            var nativeApi = new FakeZdsrNativeApi();

            using (var service = new ZdsrScreenReaderService(nativeApi))
            {
                service.ApplyIndependentChannelName("  ");

                var result = service.Speak("hello", true, false, true);

                Assert.That(result, Is.True);
                Assert.That(nativeApi.InitCalls[0].Type, Is.EqualTo(1));
                Assert.That(nativeApi.InitCalls[0].ChannelName, Is.EqualTo("MyCodex"));
            }
        }

        [Test]
        public void WinFormsProject_AlwaysCopiesZdsrApiDllAndDoesNotPackageIni()
        {
            var project = File.ReadAllText(GetRepositoryPath("src", "MyCodex.WinForms", "MyCodex.WinForms.csproj"), Encoding.UTF8);

            Assert.That(project, Does.Contain("tts\\ZDSRAPI\\ZDSRAPI_x64.dll"));
            Assert.That(project, Does.Contain("<CopyToOutputDirectory>Always</CopyToOutputDirectory>"));
            Assert.That(project, Does.Not.Contain("ZDSRAPI.ini"));
        }

        [Test]
        public void SharedBuildScript_RemovesObsoleteZdsrApiIniAndTextFiles()
        {
            var sharedBuild = File.ReadAllText(
                GetRepositoryPath("tools", "build", "Build-MyCodex.ps1"),
                Encoding.UTF8);

            Assert.That(sharedBuild, Does.Contain("Remove-LegacyOutputFiles"));
            Assert.That(sharedBuild, Does.Contain("ZDSRAPI.ini"));
            Assert.That(sharedBuild, Does.Contain("ZDSRAPI.txt"));
        }

        private sealed class FakeZdsrNativeApi : IZdsrNativeApi
        {
            public bool Available { get; set; } = true;

            public List<InitCall> InitCalls { get; } = new List<InitCall>();

            public List<SpeakCall> SpeakCalls { get; } = new List<SpeakCall>();

            public int StopCount { get; private set; }

            public bool IsAvailable
            {
                get { return Available; }
            }

            public bool EnsureLoaded(out string error)
            {
                error = null;
                return true;
            }

            public int InitTts(int type, string channelName, bool keyDownInterrupt)
            {
                InitCalls.Add(new InitCall(type, channelName, keyDownInterrupt));
                return 0;
            }

            public int Speak(string text, bool interrupt)
            {
                SpeakCalls.Add(new SpeakCall(text, interrupt));
                return 0;
            }

            public int GetSpeakState()
            {
                return 4;
            }

            public void StopSpeak()
            {
                StopCount++;
            }

            public void Dispose()
            {
            }
        }

        private sealed class InitCall
        {
            public InitCall(int type, string channelName, bool keyDownInterrupt)
            {
                Type = type;
                ChannelName = channelName;
                KeyDownInterrupt = keyDownInterrupt;
            }

            public int Type { get; }

            public string ChannelName { get; }

            public bool KeyDownInterrupt { get; }
        }

        private sealed class SpeakCall
        {
            public SpeakCall(string text, bool interrupt)
            {
                Text = text;
                Interrupt = interrupt;
            }

            public string Text { get; }

            public bool Interrupt { get; }
        }

        private static string GetRepositoryPath(params string[] parts)
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MyCodex.sln")))
            {
                directory = directory.Parent;
            }

            Assert.That(directory, Is.Not.Null, "Repository root could not be located.");
            var path = directory.FullName;
            for (var i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
            }

            return path;
        }
    }
}
