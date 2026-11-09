using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerSwitcher.Settings
{
    public sealed record PowerSettingDifference(PowerSettingDefinition Setting, PowerSettingValue Left, PowerSettingValue Right)
    {
        public bool AcDiffers => Left.AcValue != Right.AcValue;
        public bool DcDiffers => Left.DcValue != Right.DcValue;
    }

    public sealed record PowerSubgroupDifferences(PowerSubgroup Subgroup, IReadOnlyList<PowerSettingDifference> Differences);

    public static class PowerPlanComparer
    {
        /// <summary>
        /// The settings whose plugged-in or battery value differs between two plans, grouped by subgroup and sorted by
        /// name. <paramref name="left"/> and <paramref name="right"/> hold one value per entry in <paramref name="settings"/>, in the same order.
        /// </summary>
        public static List<PowerSubgroupDifferences> Compare(
            IEnumerable<PowerSubgroup> subgroups,
            IReadOnlyList<PowerSettingDefinition> settings,
            IReadOnlyList<PowerSettingValue> left,
            IReadOnlyList<PowerSettingValue> right)
        {
            var differences = settings
                .Select((setting, index) => new PowerSettingDifference(setting, left[index], right[index]))
                .Where(difference => difference.AcDiffers || difference.DcDiffers)
                .ToLookup(difference => difference.Setting.SubgroupGuid);

            return subgroups
                .Where(subgroup => differences.Contains(subgroup.Guid))
                .OrderBy(subgroup => subgroup.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(subgroup => new PowerSubgroupDifferences(
                    subgroup,
                    differences[subgroup.Guid].OrderBy(difference => difference.Setting.Name, StringComparer.CurrentCultureIgnoreCase).ToList()))
                .ToList();
        }
    }
}
