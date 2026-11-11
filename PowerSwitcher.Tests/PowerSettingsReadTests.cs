using PowerSwitcher.Settings;
using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PowerSwitcher.Tests
{
    /// <summary>Integration tests against this PC's real power settings. They only read; nothing here changes a plan.</summary>
    public class PowerSettingsReadTests
    {
        static readonly Guid ProcessorSubgroup = new Guid("54533251-82be-4824-96c1-47b60b740d00");
        static readonly Guid DiskSubgroup = new Guid("0012ee47-9041-4b5d-9b77-535fba8b1442");

        readonly Win32PowSettingsWrapper wrapper = new Win32PowSettingsWrapper();

        List<Guid> plans() => new Win32PowSchemasWrapper().GetCurrentSchemas().Select(plan => plan.Guid).ToList();

        List<PowerSettingDefinition> allSettings() => wrapper.GetSubgroups().SelectMany(subgroup => wrapper.GetSettings(subgroup.Guid)).ToList();

        [Fact]
        public void KnownSubgroupsAreListed()
        {
            var subgroups = wrapper.GetSubgroups().Select(subgroup => subgroup.Guid).ToList();

            Assert.Contains(Win32PowSettingsWrapper.NoSubgroupGuid, subgroups);
            Assert.Contains(ProcessorSubgroup, subgroups);
            Assert.Contains(DiskSubgroup, subgroups);
            Assert.Equal(subgroups.Count, subgroups.Distinct().Count());
        }

        [Fact]
        public void RangeSettingsHaveAValidRange()
        {
            var ranges = allSettings().Where(setting => setting.Kind == PowerSettingKind.Range).ToList();

            Assert.NotEmpty(ranges);
            Assert.All(ranges, setting => Assert.True(setting.Min <= setting.Max, setting.Name));
        }

        [Fact]
        public void ListSettingsHaveChoices()
        {
            var lists = allSettings().Where(setting => setting.Kind == PowerSettingKind.Index).ToList();

            Assert.NotEmpty(lists);
            Assert.All(lists, setting => Assert.NotEmpty(setting.Choices));
        }

        [Fact]
        public void TreeAndValuesMatchPowercfgForEveryPlan()
        {
            var definitions = allSettings();
            var subgroups = wrapper.GetSubgroups();
            var problems = new List<string>();

            foreach (var plan in plans())
            {
                var expected = PowercfgDump.Query(plan, includeHidden: true);

                problems.AddRange(expected.Subgroups.Keys.Except(subgroups.Select(s => s.Guid)).Select(g => $"{plan}: subgroup {g} is missing"));
                problems.AddRange(subgroups.Where(s => expected.Subgroups.TryGetValue(s.Guid, out var name) && name != s.Name)
                    .Select(s => $"{plan}: subgroup {s.Guid} is named '{s.Name}', powercfg says '{expected.Subgroups[s.Guid]}'"));

                problems.AddRange(expected.Settings.Keys.Except(definitions.Select(d => d.Guid)).Select(g => $"{plan}: setting {g} is missing"));
                problems.AddRange(definitions.Select(d => d.Guid).Except(expected.Settings.Keys).Select(g => $"{plan}: setting {g} isn't in powercfg"));

                var values = wrapper.GetValues(plan, definitions).ToDictionary(value => value.SettingGuid);

                foreach (var definition in definitions.Where(d => expected.Settings.ContainsKey(d.Guid)))
                {
                    compare(plan, definition, expected.Settings[definition.Guid], values[definition.Guid], problems);
                }
            }

            Assert.Empty(problems);
        }

        static void compare(Guid plan, PowerSettingDefinition actual, PowercfgSetting expected, PowerSettingValue value, List<string> problems)
        {
            void same<T>(string field, T theirs, T ours)
            {
                if (!EqualityComparer<T>.Default.Equals(theirs, ours)) { problems.Add($"{plan}: {actual.Name} ({actual.Guid}) {field}: powercfg '{theirs}', wrapper '{ours}'"); }
            }

            same("subgroup", expected.SubgroupGuid, actual.SubgroupGuid);
            same("name", expected.Name, actual.Name);
            same("min", expected.Min, actual.Min);
            same("max", expected.Max, actual.Max);
            same("increment", expected.Increment, actual.Increment);
            same("units", expected.Units, actual.Units);
            same("choices", string.Join("|", expected.Choices), string.Join("|", actual.Choices.Select(choice => choice.Name)));
            same("AC", (uint?)expected.Ac, value.AcValue);
            same("DC", (uint?)expected.Dc, value.DcValue);

            var expectedKind = expected.Min != null ? PowerSettingKind.Range : expected.Choices.Count > 0 ? PowerSettingKind.Index : PowerSettingKind.Unknown;
            same("kind", expectedKind, actual.Kind);
        }

        [Fact]
        public void HiddenFlagMatchesWhatPowercfgLeavesOut()
        {
            var plan = plans().First();
            var visible = PowercfgDump.Query(plan, includeHidden: false).Settings.Keys.ToHashSet();

            var wrong = allSettings().Where(setting => setting.IsHidden == visible.Contains(setting.Guid)).Select(setting => setting.Name).ToList();

            Assert.Empty(wrong);
        }

        [Fact]
        public void EveryPlanFollowsABuiltInPersonality()
        {
            var builtIn = new[] { WellKnownSchemas.PowerSaver, WellKnownSchemas.Balanced, WellKnownSchemas.HighPerformance };

            Assert.All(plans(), plan => Assert.Contains(wrapper.GetPersonality(plan).GetValueOrDefault(), builtIn));
        }

        [Fact]
        public void IndexValuesAreOneOfTheChoices()
        {
            var lists = allSettings().Where(setting => setting.Kind == PowerSettingKind.Index).ToList();
            var problems = new List<string>();

            foreach (var plan in plans())
            {
                var values = wrapper.GetValues(plan, lists).ToDictionary(value => value.SettingGuid);
                foreach (var setting in lists)
                {
                    var value = values[setting.Guid];
                    if (!setting.Choices.Any(choice => choice.Index == value.AcValue)) { problems.Add($"{plan}: {setting.Name} AC {value.AcValue}"); }
                    if (!setting.Choices.Any(choice => choice.Index == value.DcValue)) { problems.Add($"{plan}: {setting.Name} DC {value.DcValue}"); }
                }
            }

            Assert.Empty(problems);
        }

        [Fact]
        public void DefaultsAreReportedForEverySetting()
        {
            var definitions = allSettings();

            Assert.All(plans(), plan => Assert.All(wrapper.GetValues(plan, definitions), value =>
            {
                Assert.NotNull(value.AcDefault);
                Assert.NotNull(value.DcDefault);
            }));
        }
    }
}
