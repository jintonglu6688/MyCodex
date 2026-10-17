using System;
using System.Diagnostics;
using MyCodex.Domain.Entities;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RemoteSshLocalPortForwardTests
    {
        [Test]
        public void Start_KillsAndDisposesStartedProcessWhenForwardProbeFails()
        {
            var process = new FakePortForwardProcess
            {
                StartResult = true,
                HasExitedValue = false
            };

            Assert.Throws<InvalidOperationException>(() => RemoteSshLocalPortForward.Start(
                new RemoteMacSshConfigSnapshot
                {
                    HostName = "example.com",
                    UserName = "demo"
                },
                65530,
                _ => process,
                _ => false,
                _ => { }));

            Assert.That(process.KillCalled, Is.True);
            Assert.That(process.DisposeCalled, Is.True);
        }

        private sealed class FakePortForwardProcess : IRemoteSshPortForwardProcess
        {
            public bool StartResult { get; set; }

            public bool HasExitedValue { get; set; }

            public bool KillCalled { get; private set; }

            public bool DisposeCalled { get; private set; }

            public bool Start()
            {
                return StartResult;
            }

            public bool WaitForExit(int milliseconds)
            {
                return HasExitedValue;
            }

            public bool HasExited
            {
                get { return HasExitedValue; }
            }

            public void Kill()
            {
                KillCalled = true;
                HasExitedValue = true;
            }

            public void Dispose()
            {
                DisposeCalled = true;
            }
        }
    }
}
