using Microsoft.Win32;
using System;

namespace PowerSwitcher.TrayApp.Services
{
    /// <summary>
    /// "Start with Windows" through the HKCU Run key (as Papercut SMTP does). A Velopack install keeps the app at
    /// a stable path (…\PowerPlanSwitcher\current\PowerSwitcher.TrayApp.exe), so the value survives updates.
    /// </summary>
    public static class RunAtStartupService
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        // Where Task Manager's Startup tab records that the user turned an entry off.
        private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

        private const string ValueName = "PowerPlanSwitcher";

        private static string command => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("No process path")}\"";

        /// <summary>True when Windows will start this exe at sign-in.</summary>
        public static bool IsEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    var value = key?.GetValue(ValueName) as string;
                    if (value == null || !string.Equals(value.Trim('"'), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) { return false; }
                }

                using (var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, false))
                {
                    // First byte 0x02/0x06 = enabled, odd values (0x03 …) = disabled in Task Manager.
                    return !(approved?.GetValue(ValueName) is byte[] state && state.Length > 0 && (state[0] & 1) == 1);
                }
            }
        }

        public static void Set(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey, true))
            {
                if (enabled) { key.SetValue(ValueName, command); }
                else { key.DeleteValue(ValueName, false); }
            }

            // Turning it on here should win over an earlier "Disabled" in Task Manager.
            using (var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, true))
            {
                approved?.DeleteValue(ValueName, false);
            }
        }
    }
}
