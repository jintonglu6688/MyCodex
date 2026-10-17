using System;
using System.Collections.Generic;
using MyCodex.Application.Interfaces;
using MyCodex.Infrastructure.AuthenticationCenter;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AuthenticationHostStartupTests
    {
        [TestCase("gui/provider/apply", "responses", true, true)]
        [TestCase("gui/provider/apply", "responses", false, false)]
        [TestCase("gui/provider/save", "chat_completions", false, true)]
        [TestCase("gui/provider/save", "anthropic", false, true)]
        [TestCase("gui/provider/save", "chatgpt", true, false)]
        [TestCase("gui/provider/save", "responses", true, false)]
        [TestCase("gui/provider/list", "chat_completions", true, false)]
        public void OnlyConversionWritesRequireRegistrationBeforeMutation(string method, string kind, bool plannedRoute, bool required)
        {
            Assert.That(LocalAuthenticationHost.RequiresStartupRegistration(method,
                new Dictionary<string, object> { { "kind", kind } }, plannedRoute), Is.EqualTo(required));
        }

        [Test]
        public void StartupUsesNativeRouteStateAndRequiresAnAcceptingListener()
        {
            var route = new Dictionary<string, object> { { "takeover", true }, { "running", true }, { "accepting", false } };
            var status = new Dictionary<string, object> { { "route", route } };
            Assert.That(AuthenticationHostStartup.RouteRequired(status), Is.True);
            Assert.That(AuthenticationHostStartup.RouteRunning(status), Is.False);
            route["accepting"] = true;
            Assert.That(AuthenticationHostStartup.RouteRunning(status), Is.True);
            route["takeover"] = false;
            Assert.That(AuthenticationHostStartup.RouteRequired(status), Is.False);
        }

        [Test]
        public void RegistrationKeysSeparateInstallationsAndNormalizeWindowsPaths()
        {
            Assert.That(AuthenticationHostStartup.KeyFor(@"C:\Apps\MyCodex.exe"),
                Is.EqualTo(AuthenticationHostStartup.KeyFor(@"c:\apps\MYCODEX.exe")));
            Assert.That(AuthenticationHostStartup.KeyFor(@"C:\Apps\MyCodex.exe"),
                Is.Not.EqualTo(AuthenticationHostStartup.KeyFor(@"D:\Apps\MyCodex.exe")));
        }

        [Test]
        public void StartupRunsOnlyTheHiddenMaintenanceModeAndRejectsOversizedRunValues()
        {
            Assert.That(AuthenticationHostStartup.StartupCommand(@"C:\Program Files\MyCodex\MyCodex.exe"),
                Is.EqualTo("\"C:\\Program Files\\MyCodex\\MyCodex.exe\" --resume-authentication-hosts"));
            Assert.Throws<AuthenticationCenterException>(() => AuthenticationHostStartup.StartupCommand("relative.exe"));
            Assert.Throws<AuthenticationCenterException>(() => AuthenticationHostStartup.StartupCommand(@"C:\" + new string('a', 260) + ".exe"));
            Assert.That(AuthenticationHostStartup.TryRun(new[] { "--channel=Dev" }, out var result), Is.False);
            Assert.That(result, Is.Zero);
        }
    }
}
