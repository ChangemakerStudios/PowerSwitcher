using System;

namespace PowerSwitcher.Scheduling
{
    /// <summary>What automatic switching is set up to do. A null schedule means the schedule is off.</summary>
    public sealed record AutomaticSwitching(
        bool ByPowerSource, Guid PluggedInPlan, Guid OnBatteryPlan,
        WorkSchedule Schedule, Guid WorkHoursPlan, Guid OutsideWorkHoursPlan);

    public static class AutomaticPlan
    {
        /// <summary>
        /// The plan automatic switching wants now, or null if it has no say. On battery the battery plan wins;
        /// plugged in, the schedule (when it's on) comes before the plugged-in plan.
        /// </summary>
        public static Guid? For(AutomaticSwitching rules, PowerPlugStatus power, DateTime now)
        {
            if (rules.ByPowerSource && power == PowerPlugStatus.Offline) { return rules.OnBatteryPlan; }

            if (rules.Schedule != null && rules.Schedule.HasWorkHours)
            {
                return rules.Schedule.IsWorkTime(now) ? rules.WorkHoursPlan : rules.OutsideWorkHoursPlan;
            }

            if (rules.ByPowerSource) { return rules.PluggedInPlan; }
            return null;
        }
    }
}
