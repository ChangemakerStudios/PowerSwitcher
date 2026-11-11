using System.Windows.Media;

namespace PowerSwitcher.TrayApp
{
    /// <summary>
    /// Column backgrounds in the editor and Compare: a plan's columns are tinted with its tray colour,
    /// plugged in lighter and on battery stronger, so the columns can be told apart at a glance.
    /// </summary>
    public static class PlanTints
    {
        const double PluggedInOpacity = 0.10, OnBatteryOpacity = 0.22;

        public static Brush PluggedIn(Brush planColor) => tint(planColor, PluggedInOpacity);

        public static Brush OnBattery(Brush planColor) => tint(planColor, OnBatteryOpacity);

        private static Brush tint(Brush planColor, double opacity)
        {
            var color = (planColor as SolidColorBrush)?.Color ?? Colors.Gray;
            var brush = new SolidColorBrush(color) { Opacity = opacity };
            brush.Freeze();
            return brush;
        }
    }
}
