using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerProcessLifecycleTests
    {
        [Test]
        public void BuildStartInfo_ConfiguresRedirectsAndUtf8Encoding()
        {
            var startInfo = AppServerProcessLifecycle.BuildStartInfo(
                "codex.exe",
                "app-server",
                @"E:\MyCodex",
                new Dictionary<string, string>(),
                new List<string>());

            Assert.That(startInfo.FileName, Is.EqualTo("codex.exe"));
            Assert.That(startInfo.Arguments, Is.EqualTo("app-server"));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(@"E:\MyCodex"));
            Assert.That(startInfo.UseShellExecute, Is.False);
            Assert.That(startInfo.RedirectStandardInput, Is.True);
            Assert.That(startInfo.RedirectStandardOutput, Is.True);
            Assert.That(startInfo.RedirectStandardError, Is.True);
            Assert.That(startInfo.CreateNoWindow, Is.True);
            Assert.That(startInfo.StandardOutputEncoding, Is.EqualTo(Encoding.UTF8));
            Assert.That(startInfo.StandardErrorEncoding, Is.EqualTo(Encoding.UTF8));
        }

        [Test]
        public void BuildStartInfo_AppliesEnvironmentVariablesAndReportsNamesOnly()
        {
            var appliedNames = new List<string>();

            var startInfo = AppServerProcessLifecycle.BuildStartInfo(
                "codex.exe",
                "app-server",
                null,
                new Dictionary<string, string> { { "MYCODEX_TEST_SECRET", "secret-value" } },
                appliedNames);

            Assert.That(startInfo.EnvironmentVariables["MYCODEX_TEST_SECRET"], Is.EqualTo("secret-value"));
            Assert.That(appliedNames, Does.Contain("MYCODEX_TEST_SECRET"));
            Assert.That(appliedNames, Does.Not.Contain("secret-value"));
        }

        [Test]
        public void BuildStartInfo_IgnoresBlankEnvironmentKeys()
        {
            var startInfo = AppServerProcessLifecycle.BuildStartInfo(
                "codex.exe",
                "app-server",
                null,
                new Dictionary<string, string>
                {
                    { string.Empty, "empty" },
                    { " ", "blank" },
                    { "MYCODEX_VALID", "valid" }
                },
                new List<string>());

            Assert.That(startInfo.EnvironmentVariables["MYCODEX_VALID"], Is.EqualTo("valid"));
            Assert.That(startInfo.EnvironmentVariables.ContainsKey(string.Empty), Is.False);
            Assert.That(startInfo.EnvironmentVariables.ContainsKey(" "), Is.False);
        }

        [Test]
        public void CleanupResources_NullResources_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => AppServerProcessLifecycle.CleanupResources(null, process => { }));
        }

        [Test]
        public void CleanupResources_DisposesWriterAndCancelsToken()
        {
            var writer = new StringWriter();
            var cts = new CancellationTokenSource();
            var resources = new AppServerProcessResources
            {
                InputWriter = writer,
                LifetimeCts = cts
            };

            AppServerProcessLifecycle.CleanupResources(resources, process => { });

            Assert.That(cts.IsCancellationRequested, Is.True);
            Assert.Throws<ObjectDisposedException>(() => writer.Write("after dispose"));
        }

        [Test]
        public void CleanupResources_TerminatesAppServerProcessTree()
        {
            var source = File.ReadAllText(GetAppServerProcessLifecyclePath());

            Assert.That(source, Does.Contain("ProcessTreeTerminator.TryKill(process)"));
        }

        private static string GetAppServerProcessLifecyclePath()
        {
            var path = Path.Combine(
                TestContext.CurrentContext.TestDirectory,
                "..",
                "..",
                "..",
                "..",
                "src",
                "MyCodex.Infrastructure",
                "AppServer",
                "AppServerProcessLifecycle.cs");
            return Path.GetFullPath(path);
        }
    }
}
