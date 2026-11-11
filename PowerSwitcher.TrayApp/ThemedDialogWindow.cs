using Microsoft.Win32;
using PowerSwitcher.TrayApp.Services;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace PowerSwitcher.TrayApp
{
    /// <summary>
    /// Base for PowerSwitcher's ordinary windows (About, Settings). Follows the Windows "app mode" light/dark setting
    /// (the flyout follows the taskbar's "Windows mode" instead), high contrast and the accent colour, live; gives the
    /// title bar a matching dark/light frame; and closes on Esc. Pair with Themes/DialogResources.xaml.
    /// </summary>
    public class ThemedDialogWindow : Window
    {
        public ThemedDialogWindow()
        {
            SourceInitialized += (s, e) => applyTitleBarTheme();
            // KeyDown rather than PreviewKeyDown: an open drop-down handles Esc itself, and that should only close the drop-down.
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); } };
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            ApplyTheme(); // After InitializeComponent, which replaces Resources with the window's XAML dictionary.
        }

        protected override void OnClosed(EventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            base.OnClosed(e);
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Queued so that WPF has refreshed SystemParameters/SystemColors first.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { ApplyTheme(); applyTitleBarTheme(); }));
        }

        protected bool IsLightTheme => SystemParameters.HighContrast ? ThemeService.IsLightTheme : UserSystemPreferencesService.IsAppLightTheme;

        public void ApplyTheme()
        {
            if (SystemParameters.HighContrast) { applyHighContrastColors(); }
            else { applyColors(IsLightTheme); }
        }

        private void applyHighContrastColors()
        {
            setBrush("WindowBackground", SystemColors.WindowColor);
            setBrush("CardBackground", SystemColors.WindowColor);
            setBrush("CardBorder", SystemColors.WindowTextColor);
            // A hovered card keeps its background, so text stays in the system's colours, and gets a highlighted outline instead.
            setBrush("CardHover", SystemColors.WindowColor);
            setBrush("CardHoverBorder", SystemColors.HighlightColor);
            setBrush("SubtleFill", SystemColors.HighlightColor);
            setBrush("TextPrimary", SystemColors.WindowTextColor);
            setBrush("TextSecondary", SystemColors.WindowTextColor);
            setBrush("Accent", SystemColors.HighlightColor);
            setBrush("AccentText", SystemColors.HotTrackColor);
            setBrush("OnAccent", SystemColors.HighlightTextColor);
            setBrush("ButtonBackground", SystemColors.ControlColor);
            setBrush("ButtonBorder", SystemColors.ControlTextColor);
            setBrush("Critical", SystemColors.WindowTextColor);
        }

        private void applyColors(bool light)
        {
            // Windows 11 window/card palette.
            setBrush("WindowBackground", light ? rgb(0xF3, 0xF3, 0xF3) : rgb(0x20, 0x20, 0x20));
            setBrush("CardBackground", light ? rgb(0xFB, 0xFB, 0xFB) : rgb(0x2B, 0x2B, 0x2B));
            setBrush("CardBorder", light ? rgb(0xE5, 0xE5, 0xE5) : rgb(0x1D, 0x1D, 0x1D));
            setBrush("CardHover", light ? rgb(0xF5, 0xF5, 0xF5) : rgb(0x32, 0x32, 0x32));
            setBrush("CardHoverBorder", light ? rgb(0xE5, 0xE5, 0xE5) : rgb(0x1D, 0x1D, 0x1D));
            setBrush("SubtleFill", light ? rgb(0xEA, 0xEA, 0xEA) : rgb(0x3A, 0x3A, 0x3A));
            setBrush("TextPrimary", light ? rgb(0x1B, 0x1B, 0x1B) : Colors.White);
            setBrush("TextSecondary", light ? rgb(0x5F, 0x5F, 0x5F) : rgb(0xC5, 0xC5, 0xC5));
            setBrush("ButtonBackground", light ? rgb(0xFD, 0xFD, 0xFD) : rgb(0x37, 0x37, 0x37));
            setBrush("ButtonBorder", light ? rgb(0xD0, 0xD0, 0xD0) : rgb(0x45, 0x45, 0x45));
            setBrush("Critical", light ? rgb(0xC4, 0x2B, 0x1C) : rgb(0xFF, 0x99, 0xA4));

            // Like Windows: the darker accent shade on light backgrounds, the lighter one on dark, for readable contrast.
            var accentFill = AccentColorService.GetColorByTypeName(light ? "ImmersiveSystemAccentDark1" : "ImmersiveSystemAccentLight2");
            setBrush("Accent", accentFill);
            setBrush("AccentText", AccentColorService.GetColorByTypeName(light ? "ImmersiveSystemAccentDark2" : "ImmersiveSystemAccentLight3"));
            setBrush("OnAccent", luminance(accentFill) > 0.5 ? Colors.Black : Colors.White);
        }

        private void applyTitleBarTheme()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) { return; }

            int useDark = !SystemParameters.HighContrast && !IsLightTheme ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)); // No-op before Windows 10 20H1.
        }

        private void setBrush(string key, Color color) => Resources[key] = new SolidColorBrush(color);
        private static Color rgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
        private static double luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
