using PowerSwitcher.Scheduling;
using System;
using Xunit;

namespace PowerSwitcher.Tests
{
    public class WorkScheduleTests
    {
        static readonly DayOfWeek[] Weekdays = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };

        // 2026-11-09 is a Monday.
        static DateTime at(int day, int hour, int minute = 0) => new DateTime(2026, 11, 9 + day, hour, minute, 0);

        static WorkSchedule nineToFive() => new WorkSchedule(Weekdays, TimeSpan.FromHours(9), TimeSpan.FromHours(17));
        static WorkSchedule nightShift() => new WorkSchedule(Weekdays, TimeSpan.FromHours(22), TimeSpan.FromHours(6));

        [Fact]
        public void WorkHoursStartOnTheMinuteAndEndBeforeTheEndTime()
        {
            var schedule = nineToFive();

            Assert.False(schedule.IsWorkTime(at(0, 8, 59)));
            Assert.True(schedule.IsWorkTime(at(0, 9)));
            Assert.True(schedule.IsWorkTime(at(0, 16, 59)));
            Assert.False(schedule.IsWorkTime(at(0, 17)));
        }

        [Fact]
        public void WeekendsAreOutsideWorkHours()
        {
            Assert.False(nineToFive().IsWorkTime(at(5, 12))); // Saturday
            Assert.False(nineToFive().IsWorkTime(at(6, 12))); // Sunday
        }

        [Fact]
        public void HoursThatRunPastMidnightBelongToTheDayTheyStart()
        {
            var schedule = nightShift();

            Assert.True(schedule.IsWorkTime(at(0, 23)));  // Monday night
            Assert.True(schedule.IsWorkTime(at(1, 5)));   // into Tuesday morning
            Assert.False(schedule.IsWorkTime(at(0, 5)));  // Monday early morning follows Sunday, a day off
            Assert.True(schedule.IsWorkTime(at(5, 3)));   // Saturday morning finishes Friday night
            Assert.False(schedule.IsWorkTime(at(5, 23))); // Saturday night is a day off
        }

        [Fact]
        public void NextChangeIsTheEndDuringWorkHoursAndTheNextStartOutside()
        {
            var schedule = nineToFive();

            Assert.Equal(at(0, 17), schedule.NextChange(at(0, 10)));
            Assert.Equal(at(1, 9), schedule.NextChange(at(0, 17)));
            Assert.Equal(at(0, 9), schedule.NextChange(at(0, 6)));
        }

        [Fact]
        public void NextChangeSkipsTheWeekend()
        {
            Assert.Equal(at(7, 9), nineToFive().NextChange(at(4, 17, 30))); // Friday evening to Monday morning
        }

        [Fact]
        public void NoDaysOrNoTimeMeansNoWorkHours()
        {
            var noDays = new WorkSchedule(Array.Empty<DayOfWeek>(), TimeSpan.FromHours(9), TimeSpan.FromHours(17));
            var noTime = new WorkSchedule(Weekdays, TimeSpan.FromHours(9), TimeSpan.FromHours(9));

            Assert.False(noDays.IsWorkTime(at(0, 12)));
            Assert.False(noTime.IsWorkTime(at(0, 9)));
            Assert.Null(noDays.NextChange(at(0, 12)));
            Assert.Null(noTime.NextChange(at(0, 12)));
        }

        [Fact]
        public void EveryDayAllDayLongExceptOneHourStillChanges()
        {
            var allWeek = new WorkSchedule(Enum.GetValues<DayOfWeek>(), TimeSpan.FromHours(1), TimeSpan.Zero); // 1 AM to midnight

            Assert.False(allWeek.IsWorkTime(at(2, 0, 30)));
            Assert.Equal(at(2, 1), allWeek.NextChange(at(2, 0, 30)));
            Assert.Equal(at(3, 0), allWeek.NextChange(at(2, 12)));
        }
    }
}
