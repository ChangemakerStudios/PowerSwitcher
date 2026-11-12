using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerSwitcher.Scheduling
{
    /// <summary>
    /// Work hours on chosen days of the week. An end before the start runs past midnight, counting as the day it started.
    /// </summary>
    public sealed class WorkSchedule
    {
        public IReadOnlyCollection<DayOfWeek> Days { get; }
        public TimeSpan Start { get; }
        public TimeSpan End { get; }

        public WorkSchedule(IEnumerable<DayOfWeek> days, TimeSpan start, TimeSpan end)
        {
            if (start < TimeSpan.Zero || start >= TimeSpan.FromDays(1)) { throw new ArgumentOutOfRangeException(nameof(start)); }
            if (end < TimeSpan.Zero || end >= TimeSpan.FromDays(1)) { throw new ArgumentOutOfRangeException(nameof(end)); }

            Days = days.Distinct().ToList();
            Start = start;
            End = end;
        }

        /// <summary>No days, or no time between start and end, means there are never any work hours.</summary>
        public bool HasWorkHours => Days.Count > 0 && Start != End;

        public bool RunsPastMidnight => End < Start;

        public bool IsWorkTime(DateTime local)
        {
            if (!HasWorkHours) { return false; }

            var time = local.TimeOfDay;
            if (!RunsPastMidnight) { return Days.Contains(local.DayOfWeek) && time >= Start && time < End; }

            // From the start on a work day until the end the next morning.
            return (Days.Contains(local.DayOfWeek) && time >= Start)
                || (Days.Contains(local.AddDays(-1).DayOfWeek) && time < End);
        }

        /// <summary>When work hours next start or end after <paramref name="local"/>, or null if they never change.</summary>
        public DateTime? NextChange(DateTime local)
        {
            if (!HasWorkHours) { return null; }

            var now = IsWorkTime(local);

            // Changes only happen at a start or end time; a week and a day covers every pattern of days.
            return Enumerable.Range(0, 9)
                .SelectMany(day => new[] { local.Date.AddDays(day) + Start, local.Date.AddDays(day) + End })
                .Where(moment => moment > local)
                .OrderBy(moment => moment)
                .Cast<DateTime?>()
                .FirstOrDefault(moment => IsWorkTime(moment.Value) != now);
        }
    }
}
