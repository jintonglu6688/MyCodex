using System;
using System.Runtime.Serialization;
using MyCodex.Domain.Enums;

namespace MyCodex.Domain.Entities
{
    [DataContract]
    public class AutomationScheduleSnapshot
    {
        [DataMember]
        public AutomationScheduleKind Kind { get; set; } = AutomationScheduleKind.ManualOnly;

        [DataMember]
        public int Interval { get; set; }

        [DataMember]
        public int? TimeOfDayMinutes { get; set; }

        [DataMember]
        public DayOfWeek? DayOfWeek { get; set; }

        [DataMember]
        public bool SkipMissedRuns { get; set; } = true;
    }
}
