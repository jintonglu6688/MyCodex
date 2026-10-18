using System.Reflection;
using System.Text;
using MyCodex.Application.Tests.SourceGuards;
using MyCodex.Infrastructure.ByCtrl;
using NUnit.Framework;
using System;
using System.IO;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ByCtrlScreenReaderServiceTests
    {
        [Test]
        public void NormalizeChannelName_TrimsAndRemovesLineBreaks()
        {
            var method = typeof(ByCtrlScreenReaderService).GetMethod(
                "NormalizeChannelName",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            var text = (string)method.Invoke(null, new object[] { "  My\r\nChannel\nName  " });

            Assert.That(text, Is.EqualTo("My Channel Name"));
        }

        [Category(SourceGuardCategory.SourceBehavior)]
        [Test]
        public void ByCtrlService_UsesSpeak2AndDoesNotWriteConf()
        {
            var sourcePath = RepositorySource.FindSourceFile("ByCtrlScreenReaderService.cs");
            var source = File.ReadAllText(sourcePath, Encoding.UTF8);

            Assert.That(source, Does.Contain("\"BoyCtrlSpeak2\""));
            Assert.That(source, Does.Contain("\"BoyCtrlStopSpeaking2\""));
            Assert.That(source, Does.Not.Contain("\"BoyCtrlSpeakEx\""));
            Assert.That(source, Does.Not.Contain("\"BoyCtrlStopSpeakingEx\""));
            Assert.That(source, Does.Not.Contain("ConfigRelativePath"));
            Assert.That(source, Does.Not.Contain("File.WriteAllText"));
            Assert.That(source, Does.Not.Contain("byctrl.log"));
            Assert.That(source, Does.Not.Contain("GetLogDirectory"));
            Assert.That(source, Does.Contain("_initialize(null)"));
        }

        [Category(SourceGuardCategory.Assets)]
        [Test]
        public void WinFormsProject_DoesNotPackageByCtrlConf()
        {
            var projectPath = RepositorySource.GetPath("src", "MyCodex.WinForms", "MyCodex.WinForms.csproj");
            var project = File.ReadAllText(projectPath, Encoding.UTF8);

            Assert.That(project, Does.Not.Contain("tts\\byctrl\\byctrl.conf"));
            Assert.That(project, Does.Contain("tts\\byctrl\\byctrl-x64.dll"));
        }

        [Test]
        public void SplitSpeakText_KeepsThousandChineseCharactersInOneChunk()
        {
            var chunks = SplitSpeakTextForTests(new string('\u6D4B', 1000));

            Assert.That(chunks, Has.Length.EqualTo(1));
            Assert.That(chunks[0], Is.EqualTo(new string('\u6D4B', 1000)));
            Assert.That(Encoding.UTF8.GetByteCount(chunks[0]), Is.LessThanOrEqualTo(5120));
        }

        [Test]
        public void SplitSpeakText_SplitsLongChineseTextWithoutDroppingContent()
        {
            var text = new string('\u6D4B', 2500);

            var chunks = SplitSpeakTextForTests(text);

            Assert.That(chunks.Length, Is.GreaterThan(1));
            Assert.That(string.Concat(chunks), Is.EqualTo(text));
            foreach (var chunk in chunks)
            {
                Assert.That(Encoding.UTF8.GetByteCount(chunk), Is.LessThanOrEqualTo(5120));
            }
        }

        private static string[] SplitSpeakTextForTests(string text)
        {
            var method = typeof(ByCtrlScreenReaderService).GetMethod(
                "SplitSpeakText",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);

            return (string[])method.Invoke(null, new object[] { text });
        }
    }
}
