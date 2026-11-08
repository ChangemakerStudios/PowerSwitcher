using System.Windows;
using System.Windows.Media;

namespace PowerSwitcher.TrayApp.Services
{
    ////
    //  Code heavily inspired by https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Services/ThemeService.cs
    ////
    public class ThemeService
    {
        public static bool IsWindowTransparencyEnabled
        {
            get { return !SystemParameters.HighContrast && UserSystemPreferencesService.IsTransparencyEnabled; }
        }

        public static bool IsHighContrast
        {
            get { return SystemParameters.HighContrast; }
        }

        /// <summary>
        /// True when shell surfaces are light, meaning that anything drawn on them, such as the tray icon, has to be dark.
        /// </summary>
        public static bool IsLightTheme
        {
            get
            {
                if (!SystemParameters.HighContrast) { return UserSystemPreferencesService.IsSystemLightTheme; }

                var background = SystemColors.WindowColor;
                return (background.R * 299 + background.G * 587 + background.B * 114) / 1000 > 127;
            }
        }

        public static void UpdateThemeResources(ResourceDictionary dictionary)
        {
            if (SystemParameters.HighContrast)
            {
                ApplyHighContrastColors(dictionary);
            }
            else
            {
                ApplyThemeColors(dictionary, UserSystemPreferencesService.IsSystemLightTheme);
            }
        }

        private static void ApplyHighContrastColors(ResourceDictionary dictionary)
        {
            dictionary["WindowBackground"] = new SolidColorBrush(SystemColors.WindowColor);
            dictionary["WindowForeground"] = new SolidColorBrush(SystemColors.WindowTextColor);
            dictionary["ItemHighlightForeground"] = new SolidColorBrush(SystemColors.HighlightTextColor);
            dictionary["SelectedItemBackground"] = new SolidColorBrush(SystemColors.HighlightColor);
            dictionary["MouseOverSelectedItemBackground"] = new SolidColorBrush(SystemColors.HighlightColor);
            dictionary["MouseOverItemBackground"] = new SolidColorBrush(SystemColors.HighlightColor);
        }

        private static void ApplyThemeColors(ResourceDictionary dictionary, bool isLight)
        {
            var foreground = isLight ? "ImmersiveApplicationTextLightTheme" : "ImmersiveApplicationTextDarkTheme";

            dictionary["WindowBackground"] = new SolidColorBrush(GetWindowBackgroundColor(isLight));

            ReplaceBrush(dictionary, "WindowForeground", foreground);
            ReplaceBrush(dictionary, "ItemHighlightForeground", foreground);
            ReplaceBrushWithOpacity(dictionary, "SelectedItemBackground", "ImmersiveSystemAccent", 0.5);
            ReplaceBrushWithOpacity(dictionary, "MouseOverSelectedItemBackground", "ImmersiveSystemAccent", 0.75);
            ReplaceBrushWithOpacity(dictionary, "MouseOverItemBackground", "ImmersiveControlLightSelectHighlightSelectedHover", 0.3);
        }

        private static Color GetWindowBackgroundColor(bool isLight)
        {
            string resource;
            if (isLight)
            {
                // Windows only offers accent-coloured shell surfaces in dark mode.
                resource = "ImmersiveLightChromeLow";
            }
            else if (UserSystemPreferencesService.UseAccentColor)
            {
                resource = IsWindowTransparencyEnabled ? "ImmersiveSystemAccentDark2" : "ImmersiveSystemAccentDark1";
            }
            else
            {
                resource = "ImmersiveDarkChromeMedium";
            }

            var color = AccentColorService.GetColorByTypeName(resource);
            color.A = (byte)(IsWindowTransparencyEnabled ? 190 : 255);
            return color;
        }

        private static void SetBrushWithOpacity(ResourceDictionary dictionary, string name, string immersiveAccentName, double opacity)
        {
            var color = AccentColorService.GetColorByTypeName(immersiveAccentName);
            color.A = (byte)(opacity * 255);
            ((SolidColorBrush)dictionary[name]).Color = color;
        }

        private static void SetBrush(ResourceDictionary dictionary, string name, string immersiveAccentName)
        {
            SetBrushWithOpacity(dictionary, name, immersiveAccentName, 1.0);
        }

        private static void ReplaceBrush(ResourceDictionary dictionary, string name, string immersiveAccentName)
        {
            dictionary[name] = new SolidColorBrush(AccentColorService.GetColorByTypeName(immersiveAccentName));
        }
        private static void ReplaceBrushWithOpacity(ResourceDictionary dictionary, string name, string immersiveAccentName, double opacity)
        {
            var color = AccentColorService.GetColorByTypeName(immersiveAccentName);
            color.A = (byte)(opacity * 255);
            dictionary[name] = new SolidColorBrush(color);
        }
    }
}
