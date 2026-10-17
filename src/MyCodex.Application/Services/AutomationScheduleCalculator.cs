using System;
using MyCodex.Domain.Entities;
using MyCodex.Domain.Enums;

namespace MyCodex.Application.Services
{
    public sealed class AutomationScheduleCalculator
    {
        public DateTime? CalculateNextRun(AutomationDefinitionSnapshot definition, DateTime from)
        {
            if (definition == null || definition.Schedule == null)
            {
                return null;
            }

            var schedule = definition.Schedule;
            switch (schedule.Kind)
            {
                case AutomationScheduleKind.EveryMinutes:
                    return from.AddMinutes(Math.Max(1, schedule.Interval));
                case AutomationScheduleKind.EveryHours:
                    return from.AddHours(Math.Max(1, schedule.Interval));
                case AutomationScheduleKind.Daily:
                    return CalculateDaily(from, NormalizeMinuteOfDay(schedule.TimeOfDayMinutes));
                case AutomationScheduleKind.Weekly:
                    return CalculateWeekly(
                        from,
                        schedule.DayOfWeek.HasValue ? schedule.DayOfWeek.Value : from.DayOfWeek,
                        NormalizeMinuteOfDay(schedule.TimeOfDayMinutes));
                case AutomationScheduleKind.ManualOnly:
                default:
                    return null;
            }
        }

        public bool IsDue(AutomationDefinitionSnapshot definition, DateTime now)
        {
            return definition != null &&
                   definition.Enabled &&
                   definition.NextRunAt.HasValue &&
                   definition.NextRunAt.Value <= now;
        }

        private static DateTime CalculateDaily(DateTime from, int minuteOfDay)
        {
            var candidate = from.Date.AddMinutes(minuteOfDay);
            return candidate > from ? candidate : candidate.AddDays(1);
        }

        private static DateTime CalculateWeekly(DateTime from, DayOfWeek dayOfWeek, int minuteOfDay)
        {
            var days = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;
            var candidate = from.Date.AddDays(days).AddMinutes(minuteOfDay);
            return candidate > from ? candidate : candidate.AddDays(7);
        }

        private static int NormalizeMinuteOfDay(int? value)
        {
            if (!value.HasValue)
            {
                return 9 * 60;
            }

            if (value.Value < 0)
            {
                return 0;
            }

            if (value.Value > 23 * 60 + 59)
            {
                return 23 * 60 + 59;
            }

            return value.Value;
        }
    }
}
