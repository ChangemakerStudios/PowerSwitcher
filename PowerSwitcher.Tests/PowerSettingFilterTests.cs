using PowerSwitcher.Settings;
using System;
using Xunit;

namespace PowerSwitcher.Tests
{
    public class PowerSettingFilterTests
    {
        static readonly PowerSettingDefinition Sleep = new PowerSettingDefinition { Guid = Guid.NewGuid(), Name = "Sleep after", Description = "Puts the PC to sleep when idle" };
        static readonly PowerSettingDefinition Secret = Sleep with { IsHidden = true };

        static PowerSettingValue unchanged() => new PowerSettingValue(Guid.Empty, Sleep.Guid, 5, 5, 5, 5);
        static PowerSettingValue changedOnBattery() => new PowerSettingValue(Guid.Empty, Sleep.Guid, 5, 9, 5, 5);

        [Fact]
        public void HiddenSettingsNeedTheirSwitchOn()
        {
            Assert.False(PowerSettingFilter.Matches(Secret, unchanged(), "", includeHidden: false, onlyChanged: false));
            Assert.True(PowerSettingFilter.Matches(Secret, unchanged(), "", includeHidden: true, onlyChanged: false));
        }

        [Fact]
        public void ChangedFilterCountsEitherSide()
        {
            Assert.False(PowerSettingFilter.Matches(Sleep, unchanged(), "", includeHidden: false, onlyChanged: true));
            Assert.True(PowerSettingFilter.Matches(Sleep, changedOnBattery(), "", includeHidden: false, onlyChanged: true));
        }

        [Theory]
        [InlineData("sleep", true)]
        [InlineData("IDLE", true)]
        [InlineData("after idle", true)]
        [InlineData("sleep display", false)]
        [InlineData("   ", true)]
        public void EveryWordMustBeInTheNameOrDescription(string search, bool expected)
        {
            Assert.Equal(expected, PowerSettingFilter.Matches(Sleep, unchanged(), search, includeHidden: false, onlyChanged: false));
        }
    }
}
