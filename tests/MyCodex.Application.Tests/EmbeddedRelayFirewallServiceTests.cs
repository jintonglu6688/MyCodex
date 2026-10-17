using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.RemoteRelay;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class EmbeddedRelayFirewallServiceTests
    {
        private const string RelayExecutablePath = @"C:\Program Files\MyCodex\runtime\relay\mycodex-relay.exe";

        [Test]
        public void BuildAllowProcessStartInfo_CreatesScopedInboundPrivateSubnetProgramRule()
        {
            var startInfo = EmbeddedRelayFirewallService.BuildAllowProcessStartInfo(RelayExecutablePath);

            Assert.That(startInfo.FileName, Does.EndWith("netsh.exe").IgnoreCase);
            Assert.That(startInfo.UseShellExecute, Is.True);
            Assert.That(startInfo.Verb, Is.EqualTo("runas"));
            Assert.That(startInfo.WindowStyle, Is.EqualTo(ProcessWindowStyle.Hidden));
            Assert.That(startInfo.Arguments, Does.Contain("dir=in"));
            Assert.That(startInfo.Arguments, Does.Contain("action=allow"));
            Assert.That(startInfo.Arguments, Does.Contain("profile=private"));
            Assert.That(startInfo.Arguments, Does.Contain("protocol=TCP"));
            Assert.That(startInfo.Arguments, Does.Contain("program=\"" + RelayExecutablePath + "\""));
            Assert.That(startInfo.Arguments, Does.Contain("remoteip=LocalSubnet"));
            Assert.That(startInfo.Arguments, Does.Not.Contain("profile=public"));
            Assert.That(startInfo.Arguments, Does.Not.Contain("remoteip=any"));
            Assert.That(startInfo.Arguments, Does.Not.Contain("localport="));
        }

        [Test]
        public async Task AllowPrivateLocalSubnet_WhenRuleMissing_RunsElevatedAndVerifiesRule()
        {
            var statuses = new Queue<EmbeddedRelayFirewallStatus>(new[]
            {
                Status(EmbeddedRelayFirewallState.RuleMissing),
                Status(EmbeddedRelayFirewallState.Allowed)
            });
            ProcessStartInfo captured = null;
            var calls = 0;
            var service = new EmbeddedRelayFirewallService(
                () => RelayExecutablePath,
                executablePath => statuses.Dequeue(),
                (startInfo, cancellationToken) =>
                {
                    calls++;
                    captured = startInfo;
                    return 0;
                });

            var result = await service.AllowPrivateLocalSubnetAsync(CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status.State, Is.EqualTo(EmbeddedRelayFirewallState.Allowed));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(captured.Verb, Is.EqualTo("runas"));
        }

        [Test]
        public async Task AllowPrivateLocalSubnet_WhenAlreadyAllowed_DoesNotRunElevated()
        {
            var calls = 0;
            var service = new EmbeddedRelayFirewallService(
                () => RelayExecutablePath,
                executablePath => Status(EmbeddedRelayFirewallState.Allowed),
                (startInfo, cancellationToken) =>
                {
                    calls++;
                    return 0;
                });

            var result = await service.AllowPrivateLocalSubnetAsync(CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(calls, Is.EqualTo(0));
        }

        [Test]
        public async Task AllowPrivateLocalSubnet_OnNonPrivateNetwork_DoesNotOpenPublicProfile()
        {
            var calls = 0;
            var service = new EmbeddedRelayFirewallService(
                () => RelayExecutablePath,
                executablePath => Status(EmbeddedRelayFirewallState.NonPrivateNetwork),
                (startInfo, cancellationToken) =>
                {
                    calls++;
                    return 0;
                });

            var result = await service.AllowPrivateLocalSubnetAsync(CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status.State, Is.EqualTo(EmbeddedRelayFirewallState.NonPrivateNetwork));
            Assert.That(result.ErrorMessage, Does.Contain("not Private"));
            Assert.That(calls, Is.EqualTo(0));
        }

        [Test]
        public async Task AllowPrivateLocalSubnet_WhenAdministratorPromptCanceled_ReturnsCanceled()
        {
            var service = new EmbeddedRelayFirewallService(
                () => RelayExecutablePath,
                executablePath => Status(EmbeddedRelayFirewallState.RuleMissing),
                (startInfo, cancellationToken) => throw new Win32Exception(1223));

            var result = await service.AllowPrivateLocalSubnetAsync(CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Canceled, Is.True);
        }

        private static EmbeddedRelayFirewallStatus Status(EmbeddedRelayFirewallState state)
        {
            return new EmbeddedRelayFirewallStatus
            {
                State = state,
                ExecutablePath = RelayExecutablePath
            };
        }
    }
}
