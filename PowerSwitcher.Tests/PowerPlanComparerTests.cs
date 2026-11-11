using PowerSwitcher.Settings;
using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PowerSwitcher.Tests
{
    public class PowerPlanComparerTests
    {
        static readonly Guid Left = Guid.NewGuid(), Right = Guid.NewGuid();
        static readonly PowerSubgroup Disk = new PowerSubgroup(Guid.NewGuid(), "Hard disk", "", false);
        static readonly PowerSubgroup Battery = new PowerSubgroup(Guid.NewGuid(), "Battery", "", false);

        static PowerSettingDefinition setting(PowerSubgroup subgroup, string name) =>
            new PowerSettingDefinition { Guid = Guid.NewGuid(), SubgroupGuid = subgroup.Guid, Name = name, Kind = PowerSettingKind.Unknown };

        static PowerSettingValue value(Guid plan, PowerSettingDefinition definition, uint ac, uint dc) =>
            new PowerSettingValue(plan, definition.Guid, ac, dc, null, null);

        [Fact]
        public void LeavesOutSettingsThatMatch()
        {
            var same = setting(Disk, "Same");
            var different = setting(Disk, "Different");
            var settings = new[] { same, different };

            var groups = PowerPlanComparer.Compare(new[] { Disk }, settings,
                new[] { value(Left, same, 1, 2), value(Left, different, 1, 2) },
                new[] { value(Right, same, 1, 2), value(Right, different, 9, 2) });

            var difference = Assert.Single(Assert.Single(groups).Differences);
            Assert.Equal(different, difference.Setting);
            Assert.True(difference.AcDiffers);
            Assert.False(difference.DcDiffers);
        }

        [Fact]
        public void SortsGroupsAndSettingsByNameAndDropsEmptyGroups()
        {
            var empty = new PowerSubgroup(Guid.NewGuid(), "Empty", "", false);
            var zebra = setting(Disk, "Zebra");
            var apple = setting(Disk, "apple");
            var cell = setting(Battery, "Cell");
            var settings = new[] { zebra, apple, cell };

            var groups = PowerPlanComparer.Compare(new[] { Disk, empty, Battery }, settings,
                settings.Select(s => value(Left, s, 1, 1)).ToList(),
                settings.Select(s => value(Right, s, 2, 2)).ToList());

            Assert.Equal(new[] { "Battery", "Hard disk" }, groups.Select(g => g.Subgroup.Name));
            Assert.Equal(new[] { "apple", "Zebra" }, groups[1].Differences.Select(d => d.Setting.Name));
        }

        [Theory]
        [InlineData(PowerSettingKind.Range, "Seconds", 900u, "900 Seconds")]
        [InlineData(PowerSettingKind.Range, "%", 100u, "100%")]
        [InlineData(PowerSettingKind.Unknown, null, 254u, "254")]
        [InlineData(PowerSettingKind.Index, null, 1u, "On")]
        [InlineData(PowerSettingKind.Index, null, 7u, "7")]
        public void FormatsValues(PowerSettingKind kind, string units, uint input, string expected)
        {
            var definition = new PowerSettingDefinition { Kind = kind, Units = units, Choices = new[] { new PowerSettingChoice(0, "Off"), new PowerSettingChoice(1, "On") } };

            Assert.Equal(expected, definition.FormatValue(input));
        }

        [Fact]
        public void MissingValueIsShownAsADash()
        {
            Assert.Equal("—", new PowerSettingDefinition().FormatValue(null));
        }

        [Fact]
        public void RealPlansDifferExactlyWherePowercfgSaysTheyDo()
        {
            var wrapper = new Win32PowSettingsWrapper();
            var plans = new Win32PowSchemasWrapper().GetCurrentSchemas().Select(plan => plan.Guid).ToList();
            if (plans.Count < 2) { return; }

            var subgroups = wrapper.GetSubgroups();
            var settings = subgroups.SelectMany(subgroup => wrapper.GetSettings(subgroup.Guid)).ToList();

            var groups = PowerPlanComparer.Compare(subgroups, settings, wrapper.GetValues(plans[0], settings), wrapper.GetValues(plans[1], settings));
            var actual = groups.SelectMany(group => group.Differences).Select(difference => difference.Setting.Guid).ToHashSet();

            var first = PowercfgDump.Query(plans[0], includeHidden: true).Settings;
            var second = PowercfgDump.Query(plans[1], includeHidden: true).Settings;
            var expected = first.Keys.Where(guid => first[guid].Ac != second[guid].Ac || first[guid].Dc != second[guid].Dc).ToHashSet();

            Assert.Equal(expected, actual);
        }
    }
}
