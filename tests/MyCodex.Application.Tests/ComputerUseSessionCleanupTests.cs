using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using MyCodex.Application.Tests.TestSupport;
using MyCodex.Infrastructure.ComputerUse;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class ComputerUseSessionCleanupTests
    {
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        [TestCase(true, true, true)]
        [TestCase(true, false, true)]
        public async Task AuxiliaryHome_IsRetainedUnlessEveryHostCleanupWasConfirmed(
            bool priorHostUnconfirmed, bool currentHostConfirmed, bool retained)
        {
            var root = CreateHelperHome();
            var manager = CreateManager(root);
            try
            {
                if (priorHostUnconfirmed)
                    FormTestDriver.Invoke(manager, "StopHost", DisposedHost(false));
                FormTestDriver.SetField(manager, "_host", DisposedHost(currentHostConfirmed));

                manager.Dispose();
                await (Task)FormTestDriver.Invoke(manager, "CleanupAuxiliaryHomeAsync");

                Assert.That(File.Exists(Path.Combine(root, "policy-marker")), Is.EqualTo(retained));
                Assert.That(Directory.Exists(root), Is.EqualTo(retained));
            }
            finally
            {
                manager.Dispose();
                DeleteTestHome(root);
            }
        }

        [Test]
        public async Task PreviousHostCleanupFailureReport_PermanentlyRetainsAuxiliaryHome()
        {
            var root = CreateHelperHome();
            var manager = CreateManager(root);
            try
            {
                var report = (Action<string, string>)typeof(ComputerUseSessionManager)
                    .GetField("_report", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
                report("CleanupFailed", null);
                FormTestDriver.SetField(manager, "_host", DisposedHost(true));

                manager.Dispose();
                await (Task)FormTestDriver.Invoke(manager, "CleanupAuxiliaryHomeAsync");

                Assert.That(File.Exists(Path.Combine(root, "policy-marker")), Is.True);
            }
            finally
            {
                manager.Dispose();
                DeleteTestHome(root);
            }
        }

        private static ComputerUseSessionManager CreateManager(string root)
        {
            var manager = new ComputerUseSessionManager("unused", "unused", "unused", null, null);
            FormTestDriver.SetField(manager, "_homeRoot", root);
            return manager;
        }

        private static ComputerUseHostProcess DisposedHost(bool confirmed)
        {
            // Model both completed shutdown and another Dispose still in progress without
            // starting a real host, holding desktop ownership, or launching an executor.
            var host = (ComputerUseHostProcess)FormatterServices.GetUninitializedObject(typeof(ComputerUseHostProcess));
            FormTestDriver.SetField(host, "_disposed", 1);
            FormTestDriver.SetField(host, "_cleanupConfirmed", confirmed ? 1 : 0);
            return host;
        }

        private static string CreateHelperHome()
        {
            var root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "computer-use-cleanup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "policy-marker"), "test policy");
            return root;
        }

        private static void DeleteTestHome(string root)
        {
            var allowedRoot = Path.GetFullPath(TestContext.CurrentContext.WorkDirectory) + Path.DirectorySeparatorChar;
            Assert.That(Path.GetFullPath(root).StartsWith(allowedRoot, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
