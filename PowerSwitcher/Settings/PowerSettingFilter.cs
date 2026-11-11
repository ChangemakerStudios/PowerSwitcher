using System;
using System.Linq;

namespace PowerSwitcher.Settings
{
    public static class PowerSettingFilter
    {
        /// <summary>Whether a setting passes the editor's filters. Every word searched for must be in its name or description.</summary>
        public static bool Matches(PowerSettingDefinition setting, PowerSettingValue value, string search, bool includeHidden, bool onlyChanged)
        {
            if (setting.IsHidden && !includeHidden) { return false; }
            if (onlyChanged && !value.IsAcModified && !value.IsDcModified) { return false; }
            if (string.IsNullOrWhiteSpace(search)) { return true; }

            var text = $"{setting.Name} {setting.Description}";
            return search.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .All(word => text.Contains(word, StringComparison.CurrentCultureIgnoreCase));
        }
    }
}
