using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Collections.Generic;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class EmbeddedRelayProcessControllerTests
    {
        private const long StartTimeUtcTicks = 638877312001234567L;

        [Test]
        public void Stop_WhenIdentityMatches_StopsLookedUpProcessOnce()
        {
            var stopCount = 0;
            var expected = CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks);
            var access = new FakeEmbeddedRelayProcessAccess
            {
                Process = new Process(),
                Identity = expected,
                StopAction = () => stopCount++
            };
            var controller = new EmbeddedRelayProcessController(access);

            controller.Stop(expected);

            Assert.That(stopCount, Is.EqualTo(1));
            Assert.That(access.InspectedProcess, Is.SameAs(access.Process));
            Assert.That(access.StoppedProcess, Is.SameAs(access.Process));
        }

        [Test]
        public void Stop_WhenExecutablePathDoesNotMatch_DoesNotStopProcess()
        {
            var stopCount = 0;
            var expected = CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks);
            var actual = CreateIdentity(@"C:\Other\unrelated.exe", StartTimeUtcTicks);
            var controller = CreateController(actual, () => stopCount++);

            controller.Stop(expected);

            Assert.That(stopCount, Is.EqualTo(0));
        }

        [Test]
        public void Stop_WhenStartTimeDoesNotMatch_DoesNotStopProcess()
        {
            var stopCount = 0;
            var expected = CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks);
            var actual = CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks + 1);
            var controller = CreateController(actual, () => stopCount++);

            controller.Stop(expected);

            Assert.That(stopCount, Is.EqualTo(0));
        }

        [Test]
        public void Stop_WhenProcessInspectionIsDenied_DoesNotStopProcess()
        {
            var stopCount = 0;
            var process = new Process();
            var controller = new EmbeddedRelayProcessController(new FakeEmbeddedRelayProcessAccess
            {
                Process = process,
                InspectionException = new Win32Exception(5),
                StopAction = () => stopCount++
            });

            controller.Stop(CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks));

            Assert.That(stopCount, Is.EqualTo(0));
        }

        [Test]
        public void IsRunning_WhenIdentityDoesNotMatch_ReturnsFalse()
        {
            var expected = CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks);
            var actual = CreateIdentity(@"C:\Other\unrelated.exe", StartTimeUtcTicks);
            var controller = CreateController(actual, () => { });

            var running = controller.IsRunning(expected);

            Assert.That(running, Is.False);
        }

        [Test]
        public void IsRunning_WhenInitialInspectionIsUnavailable_Retries()
        {
            var expected = CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks);
            var access = new FakeEmbeddedRelayProcessAccess
            {
                Process = new Process(),
                Identity = expected,
                UnavailableInspectionCount = 1,
                StopAction = () => { }
            };
            var controller = new EmbeddedRelayProcessController(access);

            var running = controller.IsRunning(expected);

            Assert.That(running, Is.True);
            Assert.That(access.InspectionCount, Is.EqualTo(2));
        }

        [Test]
        public void CaptureStartedProcess_DoesNotAcceptLoaderModuleBeforeExpectedExecutable()
        {
            var expected = CreateIdentity(@"C:\App\relay\mycodex-relay.exe", StartTimeUtcTicks);
            var access = new FakeEmbeddedRelayProcessAccess
            {
                Identities = new Queue<EmbeddedRelayProcessIdentity>(new[]
                {
                    CreateIdentity(@"C:\Windows\System32\ntdll.dll", StartTimeUtcTicks), expected
                })
            };
            var controller = new EmbeddedRelayProcessController(access);
            using (var process = new Process())
            {
                var actual = controller.CaptureStartedProcess(process, expected.ProcessId,
                    @"C:/App/relay/mycodex-relay.exe", TimeSpan.FromSeconds(1));
                Assert.That(actual, Is.SameAs(expected));
                Assert.That(access.InspectionCount, Is.EqualTo(2));
            }
        }

        [Test]
        public void CaptureStartedProcess_WhenImageNeverMatches_RejectsTheIdentity()
        {
            var access = new FakeEmbeddedRelayProcessAccess
            {
                Identity = CreateIdentity(@"C:\Windows\System32\ntdll.dll", StartTimeUtcTicks)
            };
            var controller = new EmbeddedRelayProcessController(access);
            using (var process = new Process())
            {
                Assert.Throws<InvalidOperationException>(() => controller.CaptureStartedProcess(
                    process, 4242, @"C:\App\relay\mycodex-relay.exe", TimeSpan.FromMilliseconds(100)));
            }
            Assert.That(access.InspectionCount, Is.GreaterThan(1));
        }

        private static EmbeddedRelayProcessController CreateController(
            EmbeddedRelayProcessIdentity actual,
            Action stop)
        {
            return new EmbeddedRelayProcessController(new FakeEmbeddedRelayProcessAccess
            {
                Process = new Process(),
                Identity = actual,
                StopAction = stop
            });
        }

        private static EmbeddedRelayProcessIdentity CreateIdentity(string executablePath, long startTimeUtcTicks)
        {
            return new EmbeddedRelayProcessIdentity(4242, executablePath, startTimeUtcTicks);
        }

        private sealed class FakeEmbeddedRelayProcessAccess : IEmbeddedRelayProcessAccess
        {
            public Process Process { get; set; }

            public EmbeddedRelayProcessIdentity Identity { get; set; }
            public Queue<EmbeddedRelayProcessIdentity> Identities { get; set; }

            public Exception InspectionException { get; set; }

            public Action StopAction { get; set; }

            public int UnavailableInspectionCount { get; set; }

            public int InspectionCount { get; private set; }

            public Process InspectedProcess { get; private set; }

            public Process StoppedProcess { get; private set; }

            public Process Resolve(int processId)
            {
                return Process;
            }

            public EmbeddedRelayProcessIdentity Inspect(Process process)
            {
                InspectionCount++;
                InspectedProcess = process;
                if (InspectionException != null)
                {
                    throw InspectionException;
                }

                if (UnavailableInspectionCount > 0)
                {
                    UnavailableInspectionCount--;
                    return null;
                }

                return Identities != null && Identities.Count > 0 ? Identities.Dequeue() : Identity;
            }

            public void Stop(Process process)
            {
                StoppedProcess = process;
                StopAction();
            }
        }
    }
}
