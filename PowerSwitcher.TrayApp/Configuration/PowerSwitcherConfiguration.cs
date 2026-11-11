using Petrroll.Helpers;
using PowerSwitcher.Scheduling;
using PowerSwitcher.TrayApp.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using System.Xml.Serialization;

namespace PowerSwitcher.TrayApp.Configuration
{
    [Serializable]
    public class PowerSwitcherSettings : ObservableObject
    {
        //I know that everything should be observable but it's not neccessary so let's leave it as it is for now

        public bool AutomaticFlyoutHideAfterClick { get; set; } = true;

        bool automaticOnACSwitch = false;
        public bool AutomaticOnACSwitch { get { return automaticOnACSwitch; } set { automaticOnACSwitch = value; RaisePropertyChangedEvent(nameof(AutomaticOnACSwitch)); } }

        Guid automaticPlanGuidOnAC = new Guid("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
        public Guid AutomaticPlanGuidOnAC { get { return automaticPlanGuidOnAC; } set { automaticPlanGuidOnAC = value; RaisePropertyChangedEvent(nameof(AutomaticPlanGuidOnAC)); } }

        Guid automaticPlanGuidOffAC = new Guid("a1841308-3541-4fab-bc81-f71556f20b4a");
        public Guid AutomaticPlanGuidOffAC { get { return automaticPlanGuidOffAC; } set { automaticPlanGuidOffAC = value; RaisePropertyChangedEvent(nameof(AutomaticPlanGuidOffAC)); } }

        bool scheduleEnabled = false;
        public bool ScheduleEnabled { get { return scheduleEnabled; } set { scheduleEnabled = value; RaisePropertyChangedEvent(nameof(ScheduleEnabled)); } }

        // Work days as bits, 1 << (int)DayOfWeek; Monday to Friday by default. (A list would be appended to on load, not replaced.)
        int scheduleDays = 0b0111110;
        public int ScheduleDays { get { return scheduleDays; } set { scheduleDays = value; RaisePropertyChangedEvent(nameof(ScheduleDays)); } }

        // Minutes after midnight. An end before the start runs past midnight.
        int scheduleStartMinutes = 9 * 60;
        public int ScheduleStartMinutes { get { return scheduleStartMinutes; } set { scheduleStartMinutes = value; RaisePropertyChangedEvent(nameof(ScheduleStartMinutes)); } }

        int scheduleEndMinutes = 17 * 60;
        public int ScheduleEndMinutes { get { return scheduleEndMinutes; } set { scheduleEndMinutes = value; RaisePropertyChangedEvent(nameof(ScheduleEndMinutes)); } }

        // Empty until chosen; Settings suggests High performance and Balanced when the schedule is first turned on.
        Guid schedulePlanDuring = Guid.Empty;
        public Guid SchedulePlanDuring { get { return schedulePlanDuring; } set { schedulePlanDuring = value; RaisePropertyChangedEvent(nameof(SchedulePlanDuring)); } }

        Guid schedulePlanOutside = Guid.Empty;
        public Guid SchedulePlanOutside { get { return schedulePlanOutside; } set { schedulePlanOutside = value; RaisePropertyChangedEvent(nameof(SchedulePlanOutside)); } }

        public bool IsWorkDay(DayOfWeek day) => (ScheduleDays & (1 << (int)day)) != 0;

        public void SetWorkDay(DayOfWeek day, bool isWorkDay) =>
            ScheduleDays = isWorkDay ? ScheduleDays | (1 << (int)day) : ScheduleDays & ~(1 << (int)day);

        public WorkSchedule GetWorkSchedule() => new WorkSchedule(
            Enum.GetValues<DayOfWeek>().Where(IsWorkDay),
            TimeSpan.FromMinutes(Math.Clamp(ScheduleStartMinutes, 0, 24 * 60 - 1)),
            TimeSpan.FromMinutes(Math.Clamp(ScheduleEndMinutes, 0, 24 * 60 - 1)));

        public AutomaticSwitching GetAutomaticSwitching() => new AutomaticSwitching(
            AutomaticOnACSwitch, AutomaticPlanGuidOnAC, AutomaticPlanGuidOffAC,
            ScheduleEnabled ? GetWorkSchedule() : null, SchedulePlanDuring, SchedulePlanOutside);

        // Changing these doesn't re-register the shortcut by itself; call App.ApplyHotkeyFromConfiguration() afterwards.
        public Key ShowOnShortcutKey { get; set; } = Key.L;
        public KeyModifier ShowOnShortcutKeyModifier { get; set; } = KeyModifier.Shift | KeyModifier.Win;

        bool showOnShortcutSwitch = false;
        public bool ShowOnShortcutSwitch { get { return showOnShortcutSwitch; } set { showOnShortcutSwitch = value; RaisePropertyChangedEvent(nameof(ShowOnShortcutSwitch)); } }

        bool showOnlyDefaultSchemas = false;
        public bool ShowOnlyDefaultSchemas { get { return showOnlyDefaultSchemas; } set { showOnlyDefaultSchemas = value; RaisePropertyChangedEvent(nameof(ShowOnlyDefaultSchemas)); } }

        /// <summary>Tray badge colours the user picked, by plan. Plans that aren't listed use their default colour.</summary>
        public List<PlanColor> PlanColors { get; set; } = new List<PlanColor>();

        /// <summary>The colour picked for a plan as "#RRGGBB", or null for its default.</summary>
        public string GetPlanColor(Guid plan) => PlanColors.FirstOrDefault(c => c.Plan == plan)?.Color;

        /// <summary>Picks a plan's colour ("#RRGGBB"), or goes back to its default with null.</summary>
        public void SetPlanColor(Guid plan, string color)
        {
            PlanColors.RemoveAll(c => c.Plan == plan);
            if (color != null) { PlanColors.Add(new PlanColor { Plan = plan, Color = color }); }
            RaisePropertyChangedEvent(nameof(PlanColors));
        }

    }

    [Serializable]
    public class PlanColor
    {
        [XmlAttribute] public Guid Plan { get; set; }
        [XmlAttribute] public string Color { get; set; }
    }
}
