using Petrroll.Helpers;
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
