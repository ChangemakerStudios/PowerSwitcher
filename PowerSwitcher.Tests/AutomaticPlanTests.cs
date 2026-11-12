using PowerSwitcher.Scheduling;
using System;
using Xunit;

namespace PowerSwitcher.Tests
{
    public class AutomaticPlanTests
    {
        static readonly Guid PluggedIn = Guid.NewGuid(), OnBattery = Guid.NewGuid(), Work = Guid.NewGuid(), Outside = Guid.NewGuid();
        static readonly WorkSchedule NineToFive = new WorkSchedule(
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            TimeSpan.FromHours(9), TimeSpan.FromHours(17));

        static readonly DateTime MondayNoon = new DateTime(2026, 11, 9, 12, 0, 0);
        static readonly DateTime MondayNight = new DateTime(2026, 11, 9, 22, 0, 0);

        static AutomaticSwitching rules(bool byPowerSource, WorkSchedule schedule) =>
            new AutomaticSwitching(byPowerSource, PluggedIn, OnBattery, schedule, Work, Outside);

        [Fact]
        public void NothingSetUpMeansNoSay()
        {
            Assert.Null(AutomaticPlan.For(rules(false, null), PowerPlugStatus.Online, MondayNoon));
        }

        [Fact]
        public void TheScheduleAloneFollowsWorkHours()
        {
            Assert.Equal(Work, AutomaticPlan.For(rules(false, NineToFive), PowerPlugStatus.Online, MondayNoon));
            Assert.Equal(Outside, AutomaticPlan.For(rules(false, NineToFive), PowerPlugStatus.Online, MondayNight));
            Assert.Equal(Work, AutomaticPlan.For(rules(false, NineToFive), PowerPlugStatus.Offline, MondayNoon)); // no battery rule to step in
        }

        [Fact]
        public void PluggedInTheScheduleComesBeforeThePluggedInPlan()
        {
            Assert.Equal(Work, AutomaticPlan.For(rules(true, NineToFive), PowerPlugStatus.Online, MondayNoon));
            Assert.Equal(Outside, AutomaticPlan.For(rules(true, NineToFive), PowerPlugStatus.Online, MondayNight));
        }

        [Fact]
        public void OnBatteryTheBatteryPlanWins()
        {
            Assert.Equal(OnBattery, AutomaticPlan.For(rules(true, NineToFive), PowerPlugStatus.Offline, MondayNoon));
        }

        [Fact]
        public void AScheduleWithNoWorkHoursStepsAside()
        {
            var noDays = new WorkSchedule(Array.Empty<DayOfWeek>(), TimeSpan.FromHours(9), TimeSpan.FromHours(17));

            Assert.Equal(PluggedIn, AutomaticPlan.For(rules(true, noDays), PowerPlugStatus.Online, MondayNoon));
            Assert.Null(AutomaticPlan.For(rules(false, noDays), PowerPlugStatus.Online, MondayNoon));
        }
    }
}
