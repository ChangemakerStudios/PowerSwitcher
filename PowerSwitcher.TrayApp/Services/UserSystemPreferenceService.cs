using Microsoft.Win32;

namespace PowerSwitcher.TrayApp.Services
{
    ////
    //  Code heavily inspired by https://github.com/File-New-Project/EarTrumpet/blob/master/EarTrumpet/Services/UserSystemPreferencesService.cs
    ////
    public static class UserSystemPreferencesService
    {
        public static bool IsTransparencyEnabled
        {
            get
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    return (int)baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize").GetValue("EnableTransparency", 0) > 0;
                }
            }
        }

        /// <summary>
        /// The "Windows mode" setting, which is what the taskbar and shell flyouts follow.
        /// The value doesn't exist before Windows 10 1903, where the shell is always dark.
        /// </summary>
        public static bool IsSystemLightTheme
        {
            get
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    return (int)baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize").GetValue("SystemUsesLightTheme", 0) > 0;
                }
            }
        }

        /// <summary>
        /// The "app mode" setting, which is what ordinary application windows follow. Defaults to light when absent.
        /// </summary>
        public static bool IsAppLightTheme
        {
            get
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    return (int)baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize").GetValue("AppsUseLightTheme", 1) > 0;
                }
            }
        }

        public static bool UseAccentColor
        {
            get
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
                {
                    return (int)baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize").GetValue("ColorPrevalence", 0) > 0;
                }
            }
        }
    }
}
