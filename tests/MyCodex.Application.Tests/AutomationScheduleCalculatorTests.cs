using System;
using MyCodex.Application.Services;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;
using NUnit.Framework;

namespace MyCodex.Application.Tests
{
    [TestFixture]
    public sealed class AutomationScheduleCalculatorTests
    {
        [Test]
        public void CalculateNextRun_ManualOnly_ReturnsNull()
        {
            var calculator = new AutomationScheduleCalculator();
            var definition = BuildDefinition(AutomationScheduleKind.ManualOnly);

            var next = calculator.CalculateNextRun(definition, new DateTime(2026, 6, 5, 9, 0, 0));

            Assert.That(next, Is.Null);
        }

        [Test]
        public void CalculateNextRun_EveryMinutes_UsesMinimumOneMinute()
        {
            var calculator = new AutomationScheduleCalculator();
            var definition = BuildDefinition(AutomationScheduleKind.EveryMinutes);
            definition.Schedule.Interval = 0;

            var next = calculator.CalculateNextRun(definition, new DateTime(2026, 6, 5, 9, 0, 0));

            Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 5, 9, 1, 0)));
        }

        [Test]
        public void CalculateNextRun_EveryHours_AddsHours()
        {
            var calculator = new AutomationScheduleCalculator();
            var definition = BuildDefinition(AutomationScheduleKind.EveryHours);
            definition.Schedule.Interval = 3;

            var next = calculator.CalculateNextRun(definition, new DateTime(2026, 6, 5, 9, 0, 0));

            Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 5, 12, 0, 0)));
        }

        [Test]
        public void CalculateNextRun_Daily_UsesTodayWhenFuture()
        {
            var calculator = new AutomationScheduleCalculator();
            var definition = BuildDefinition(AutomationScheduleKind.Daily);
            definition.Schedule.TimeOfDayMinutes = 10 * 60 + 30;

            var next = calculator.CalculateNextRun(definition, new DateTime(2026, 6, 5, 9, 0, 0));

            Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 5, 10, 30, 0)));
        }

        [Test]
        public void CalculateNextRun_Daily_UsesTomorrowWhenPast()
        {
            var calculator = new AutomationScheduleCalculator();
            var definition = BuildDefinition(AutomationScheduleKind.Daily);
            definition.Schedule.TimeOfDayMinutes = 8 * 60;

            var next = calculator.CalculateNextRun(definition, new DateTime(2026, 6, 5, 9, 0, 0));

            Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 6, 8, 0, 0)));
        }

        [Test]
        public void CalculateNextRun_Weekly_UsesNextMatchingDay()
        {
            var calculator = new AutomationScheduleCalculator();
            var definition = BuildDefinition(AutomationScheduleKind.Weekly);
            definition.Schedule.DayOfWeek = DayOfWeek.Monday;
            definition.Schedule.TimeOfDayMinutes = 8 * 60;

            var next = calculator.CalculateNextRun(definition, new DateTime(2026, 6, 5, 9, 0, 0));

            Assert.That(next, Is.EqualTo(new DateTime(2026, 6, 8, 8, 0, 0)));
        }

        [Test]
        public void IsDue_RequiresEnabledDefinitionAndNextRunAtReached()
        {
            var calculator = new AutomationScheduleCalculator();
            var definition = BuildDefinition(AutomationScheduleKind.EveryMinutes);
            definition.Enabled = true;
            definition.NextRunAt = new DateTime(2026, 6, 5, 9, 0, 0);

            Assert.That(calculator.IsDue(definition, new DateTime(2026, 6, 5, 8, 59, 59)), Is.False);
            Assert.That(calculator.IsDue(definition, new DateTime(2026, 6, 5, 9, 0, 0)), Is.True);

            definition.Enabled = false;
            Assert.That(calculator.IsDue(definition, new DateTime(2026, 6, 5, 9, 0, 0)), Is.False);
        }

        private static AutomationDefinitionSnapshot BuildDefinition(AutomationScheduleKind kind)
        {
            return new AutomationDefinitionSnapshot
            {
                Id = "automation-1",
                Enabled = true,
                Schedule = new AutomationScheduleSnapshot
                {
                    Kind = kind,
                    SkipMissedRuns = true
                }
            };
        }
    }
}
