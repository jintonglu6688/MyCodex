using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using MyCodex.Infrastructure.Instances;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class SharedUpdateResourceUsageInspectorTests
    {
        [Test]
        public void OnlyRuntimeDescendantsOfVerifiedClientOwnersJoinThePreparedScope()
        {
            var owner = new TargetInstanceRecord { ProcessId = 1, StartTimeUtcTicks = 100 };
            var app = Entry(1, 0, 100, "MyCodex.exe", "C:\\test\\MyCodex.exe");
            var bridge = Entry(2, 1, 200, "helper.exe", "C:\\outside\\helper.exe");
            var codex = Entry(3, 2, 300, "codex.exe", "C:\\test\\runtime\\codex\\v1\\codex.exe");
            var unrelated = Entry(4, 0, 400, "codex.exe", "C:\\test\\runtime\\codex\\v1\\codex.exe");
            var result = SharedUpdateResourceUsageInspector.FindUncoordinatedRuntimeUsers("C:\\test", new[] { owner },
                new[] { app, bridge, codex, unrelated }).ToArray();
            Assert.That(result.Select(item => item.ProcessId), Is.EqualTo(new[] { 4 }));
            Assert.That(result[0].ChannelId, Is.EqualTo("UncoordinatedRuntime"));
        }

        [Test]
        public void ReusedParentPidAndMissingRuntimeIdentityCannotAuthorizeAnUpdate()
        {
            var owner = new TargetInstanceRecord { ProcessId = 1, StartTimeUtcTicks = 100 };
            var reused = Entry(1, 0, 500, "other.exe", "C:\\outside\\other.exe");
            var child = Entry(2, 1, 200, "codex.exe", "C:\\test\\runtime\\codex\\codex.exe");
            Assert.That(SharedUpdateResourceUsageInspector.FindUncoordinatedRuntimeUsers("C:\\test", new[] { owner }, new[] { reused, child }).Single().ProcessId, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => SharedUpdateResourceUsageInspector.FindUncoordinatedRuntimeUsers("C:\\test", new[] { owner },
                new[] { Entry(3, 0, 300, "codex.exe", null) }).ToArray());
        }

        [Test]
        public void AuthenticationRelayAndSiblingInstallationRetainTheirExistingMaintenanceBoundary()
        {
            var entries = new[]
            {
                Entry(1, 0, 100, "mycodex-auth-host.exe", "C:\\test\\runtime\\auth-core\\mycodex-auth-host.exe"),
                Entry(2, 0, 200, "mycodex-relay.exe", "C:\\test\\runtime\\relay\\mycodex-relay.exe"),
                Entry(3, 0, 300, "codex.exe", "C:\\test-other\\runtime\\codex.exe")
            };
            Assert.That(SharedUpdateResourceUsageInspector.FindUncoordinatedRuntimeUsers("C:\\test", new TargetInstanceRecord[0], entries), Is.Empty);
        }

        [Test]
        public void SharedRelayHostIsExemptOnlyWhenEveryLiveLeaseBelongsToVerifiedParticipants()
        {
            var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "update-relay-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(directory, "Relay"));
            try
            {
                var path = Path.Combine(directory, "Relay", "shared-host.json");
                var owner = new TargetInstanceRecord { ProcessId = 1, StartTimeUtcTicks = 100, ExecutablePath = "C:\\test\\MyCodex.exe", UserSid = TargetInstanceSecurity.CurrentSid };
                var state = new SharedRelayState { Host = new SharedRelayProcessRecord { ProcessId = 9, StartTimeUtcTicks = 900, ExecutablePath = owner.ExecutablePath } };
                state.Leases.Add(new SharedRelayLease { Id = "target-one", User = new SharedRelayProcessRecord { ProcessId = owner.ProcessId, StartTimeUtcTicks = owner.StartTimeUtcTicks, ExecutablePath = owner.ExecutablePath } });
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(state));
                Assert.That(SharedUpdateResourceUsageInspector.FindCoordinatedRelayHosts(new[] { directory }, new[] { owner }, _ => true).Single().ProcessId, Is.EqualTo(9));
                state.Leases.Add(new SharedRelayLease { Id = "uncoordinated", User = new SharedRelayProcessRecord { ProcessId = 2, StartTimeUtcTicks = 200, ExecutablePath = owner.ExecutablePath } });
                File.WriteAllText(path, new JavaScriptSerializer().Serialize(state));
                Assert.That(SharedUpdateResourceUsageInspector.FindCoordinatedRelayHosts(new[] { directory }, new[] { owner }, _ => true), Is.Empty);
                Assert.That(SharedUpdateResourceUsageInspector.FindCoordinatedRelayHosts(new[] { directory }, new[] { owner }, record => record.ProcessId != 2).Single().ProcessId, Is.EqualTo(9));
                Assert.Throws<InvalidOperationException>(() => SharedUpdateResourceUsageInspector.FindCoordinatedRelayHosts(new[] { directory }, new[] { owner }, record => record.ProcessId == 9 ? (bool?)null : true));
            }
            finally { Directory.Delete(directory, true); }
        }

        private static SharedUpdateResourceUsageInspector.ProcessEntry Entry(int id, int parent, long started, string name, string path)
        {
            return new SharedUpdateResourceUsageInspector.ProcessEntry { Id = id, ParentId = parent, StartTicks = started, Name = name, Path = path };
        }
    }
}
