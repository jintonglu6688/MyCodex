using System;
using System.Collections.Generic;
using Microsoft.Win32;
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
        public void NewStartupOwnerRemovesOnlyTheSameHomeAndStoreFromOlderInstallations()
        {
            var path = @"Software\MyCodex.Tests\" + Guid.NewGuid().ToString("N");
            try
            {
                using (var root = Registry.CurrentUser.CreateSubKey(path + @"\Hosts"))
                using (var run = Registry.CurrentUser.CreateSubKey(path + @"\Run"))
                {
                    var oldApp = @"C:\Old\MyCodex.exe";
                    var newApp = @"C:\New\MyCodex.exe";
                    var separateApp = @"C:\SeparateStore\MyCodex.exe";
                    var home = @"C:\Fixture\home";
                    var store = @"C:\Fixture\store";
                    AuthenticationHostStartup.Configure(root, run, oldApp, @"C:\Old\runtime\auth-core\mycodex-auth-host.exe", home, store, true);
                    AuthenticationHostStartup.Configure(root, run, oldApp, @"C:\Old\runtime\auth-core\mycodex-auth-host.exe", home + "2", store + "2", true);
                    AuthenticationHostStartup.Configure(root, run, separateApp, @"C:\SeparateStore\runtime\auth-core\mycodex-auth-host.exe", home, store + "-separate", true);
                    AuthenticationHostStartup.Configure(root, run, newApp, @"C:\New\runtime\auth-core\mycodex-auth-host.exe", home, store, true);
                    using (var old = root.OpenSubKey(AuthenticationHostStartup.KeyFor(oldApp)))
                    {
                        Assert.That(old.GetValue(AuthenticationHostStartup.KeyFor(home)), Is.Null);
                        Assert.That(old.GetValue(AuthenticationHostStartup.KeyFor(home + "2")), Is.Not.Null);
                    }
                    Assert.That(run.GetValue("MyCodex.Authentication." + AuthenticationHostStartup.KeyFor(oldApp)), Is.Not.Null);
                    AuthenticationHostStartup.Configure(root, run, newApp, @"C:\New\runtime\auth-core\mycodex-auth-host.exe", home + "2", store + "2", true);
                    Assert.That(run.GetValue("MyCodex.Authentication." + AuthenticationHostStartup.KeyFor(oldApp)), Is.Null);
                    Assert.That(run.GetValue("MyCodex.Authentication." + AuthenticationHostStartup.KeyFor(newApp)), Is.Not.Null);
                    AuthenticationHostStartup.Configure(root, run, oldApp, @"C:\Old\runtime\auth-core\mycodex-auth-host.exe", home, store, false);
                    using (var current = root.OpenSubKey(AuthenticationHostStartup.KeyFor(newApp)))
                        Assert.That(current.GetValue(AuthenticationHostStartup.KeyFor(home)), Is.Not.Null, "An older client exiting cannot remove the new owner.");
                    using (var separate = root.OpenSubKey(AuthenticationHostStartup.KeyFor(separateApp)))
                        Assert.That(separate.GetValue(AuthenticationHostStartup.KeyFor(home)), Is.Not.Null, "A different store has independent startup ownership.");
                }
            }
            finally { Registry.CurrentUser.DeleteSubKeyTree(path, false); }
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
