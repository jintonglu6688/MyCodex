using System;
using System.IO;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.Updates;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class DeferredUpdateInstallerLauncherTests
    {
        private string _directory;
        private readonly DateTime _now = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
        private const string Installation = "C:\\fixture\\MyCodex";

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "deferred-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown() { Directory.Delete(_directory, true); }

        [Test]
        public void ParentHandoffAndLongRunningInstallerBothBlockNewStartup()
        {
            WriteMarker(_now.AddMinutes(10), false);
            Assert.Throws<InvalidOperationException>(() => Check(record => true));
            WriteMarker(_now.AddMinutes(-1), true);
            Assert.Throws<InvalidOperationException>(() => Check(record => record.ProcessId == 2));
            Assert.That(File.Exists(Marker), Is.True);
        }

        [Test]
        public void OnlyProvenExitedOwnersPermitRemovingStaleMarker()
        {
            WriteMarker(_now.AddMinutes(10), true);
            Assert.Throws<InvalidOperationException>(() => Check(record => null));
            Assert.That(File.Exists(Marker), Is.True);
            Check(record => false);
            Assert.That(File.Exists(Marker), Is.False);
        }

        [TestCase(2, Installation)]
        [TestCase(1, "C:\\other-installation")]
        public void UnverifiedSchemaOrInstallationBlocksAndPreservesMarker(int schema, string installation)
        {
            WriteMarker(_now.AddMinutes(10), true, schema, installation);
            Assert.Throws<InvalidDataException>(() => Check(record => false));
            Assert.That(File.Exists(Marker), Is.True);
        }

        [Test]
        public void OversizedOrUnboundedMarkerCannotAuthorizeStartup()
        {
            WriteMarker(_now.AddDays(1), true);
            Assert.Throws<InvalidDataException>(() => Check(record => false));
            File.WriteAllText(Marker, new string(' ', 32769));
            Assert.Throws<InvalidDataException>(() => Check(record => false));
        }

        private string Marker => Path.Combine(_directory, "active.json");

        private void Check(Func<TargetInstanceRecord, bool?> alive)
        {
            DeferredUpdateInstallerLauncher.ThrowIfUpdatePending(Installation, _directory, alive, _now);
        }

        private void WriteMarker(DateTime expires, bool helper, int schema = 1, string installation = Installation)
        {
            File.WriteAllText(Marker, new JavaScriptSerializer().Serialize(new
            {
                SchemaVersion = schema, OperationId = Guid.NewGuid().ToString("N"), InstallationDirectory = installation,
                ExpiresUtcTicks = expires.Ticks, Parent = new TargetInstanceRecord { ProcessId = 1 },
                Helper = helper ? new TargetInstanceRecord { ProcessId = 2 } : null
            }));
        }
    }
}
