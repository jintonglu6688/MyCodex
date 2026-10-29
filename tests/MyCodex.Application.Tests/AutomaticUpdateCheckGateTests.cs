using System;
using System.Threading;
using MyCodex.WinForms.Forms;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomaticUpdateCheckGateTests
    {
        [Test]
        public void TryClaim_SkipsWhileAnotherInstanceOwnsTheChannel()
        {
            var channel = "test-" + Guid.NewGuid().ToString("N");
            using (var owned = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                var owner = new Thread(() =>
                {
                    using (var mutex = new Mutex(true, @"Local\MyCodex.AutomaticUpdateCheck." + channel))
                    {
                        owned.Set();
                        release.Wait();
                        mutex.ReleaseMutex();
                    }
                });
                owner.Start();
                owned.Wait();

                Assert.That(AutomaticUpdateCheckGate.TryClaim(channel), Is.False);

                release.Set();
                owner.Join();
            }

            Assert.That(AutomaticUpdateCheckGate.TryClaim(channel), Is.True);
        }
    }
}
