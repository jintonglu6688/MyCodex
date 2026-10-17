using System;
using System.Collections.Generic;
using MyCodex.Application.DTOs;
using MyCodex.Application.Services;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class RateLimitResetCreditAutoResetPlannerTests
    {
        [Test]
        public void SelectCreditToAutoReset_ChoosesEarliestAvailableCreditInsideThreshold()
        {
            var now = new DateTime(2026, 7, 14, 10, 0, 0);
            var snapshot = new AppServerRateLimitsSnapshot
            {
                ResetCredits = new List<AppServerRateLimitResetCredit>
                {
                    Credit("later", "available", now.AddMinutes(25)),
                    Credit("used", "redeemed", now.AddMinutes(5)),
                    Credit("earliest", "available", now.AddMinutes(10)),
                    Credit("too-far", "available", now.AddMinutes(31))
                }
            };

            var planner = new RateLimitResetCreditAutoResetPlanner();

            var selected = planner.SelectCreditToAutoReset(snapshot, now, new HashSet<string>());

            Assert.That(selected, Is.Not.Null);
            Assert.That(selected.Id, Is.EqualTo("earliest"));
        }

        [Test]
        public void SelectCreditToAutoReset_SkipsCreditsAlreadyAttemptedInCurrentProcess()
        {
            var now = new DateTime(2026, 7, 14, 10, 0, 0);
            var snapshot = new AppServerRateLimitsSnapshot
            {
                ResetCredits = new List<AppServerRateLimitResetCredit>
                {
                    Credit("first", "available", now.AddMinutes(10)),
                    Credit("second", "available", now.AddMinutes(20))
                }
            };
            var attempted = new HashSet<string> { "FIRST" };

            var selected = new RateLimitResetCreditAutoResetPlanner()
                .SelectCreditToAutoReset(snapshot, now, attempted);

            Assert.That(selected, Is.Not.Null);
            Assert.That(selected.Id, Is.EqualTo("second"));
        }

        [Test]
        public void SelectCreditToAutoReset_ReturnsNullWhenNoAvailableCreditIsExpiringSoon()
        {
            var now = new DateTime(2026, 7, 14, 10, 0, 0);
            var snapshot = new AppServerRateLimitsSnapshot
            {
                ResetCredits = new List<AppServerRateLimitResetCredit>
                {
                    Credit("missing-expiry", "available", null),
                    Credit("expired", "available", now.AddMinutes(-1)),
                    Credit("too-far", "available", now.AddMinutes(31)),
                    Credit("used", "redeemed", now.AddMinutes(10))
                }
            };

            var selected = new RateLimitResetCreditAutoResetPlanner()
                .SelectCreditToAutoReset(snapshot, now, new HashSet<string>());

            Assert.That(selected, Is.Null);
        }

        private static AppServerRateLimitResetCredit Credit(string id, string status, DateTime? expiresAt)
        {
            return new AppServerRateLimitResetCredit
            {
                Id = id,
                Status = status,
                ExpiresAtLocalTime = expiresAt
            };
        }
    }
}
