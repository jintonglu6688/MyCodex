using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Infrastructure.AppServer;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AppServerWindowsSandboxMapperTests
    {
        [TestCase("ready", AppServerWindowsSandboxReadinessStatus.Ready, true)]
        [TestCase("notConfigured", AppServerWindowsSandboxReadinessStatus.NotConfigured, false)]
        [TestCase("updateRequired", AppServerWindowsSandboxReadinessStatus.UpdateRequired, false)]
        [TestCase("futureStatus", AppServerWindowsSandboxReadinessStatus.Unknown, false)]
        public void BuildReadiness_MapsStatus(string rawStatus, AppServerWindowsSandboxReadinessStatus expectedStatus, bool expectedReady)
        {
            var result = AppServerWindowsSandboxMapper.BuildReadiness(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "status", rawStatus }
                });

            Assert.That(result.RawStatus, Is.EqualTo(rawStatus));
            Assert.That(result.Status, Is.EqualTo(expectedStatus));
            Assert.That(result.IsReady, Is.EqualTo(expectedReady));
        }

        [Test]
        public void BuildSetupStartResult_MapsStarted()
        {
            var result = AppServerWindowsSandboxMapper.BuildSetupStartResult(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "started", true }
                });

            Assert.That(result.Started, Is.True);
        }

        [Test]
        public void BuildSetupCompleted_MapsModeSuccessAndError()
        {
            var result = AppServerWindowsSandboxMapper.BuildSetupCompleted(
                new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    { "mode", "elevated" },
                    { "success", false },
                    { "error", "needs reboot" }
                });

            Assert.That(result.Mode, Is.EqualTo(AppServerWindowsSandboxSetupMode.Elevated));
            Assert.That(result.RawMode, Is.EqualTo("elevated"));
            Assert.That(result.Success, Is.False);
            Assert.That(result.Error, Is.EqualTo("needs reboot"));
        }
    }
}
