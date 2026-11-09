using PowerSwitcher.Settings;
using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PowerSwitcher.Tests
{
    /// <summary>
    /// Every write goes to a scratch copy of a plan that's deleted afterwards. Never point these at a real plan. The
    /// editor is told the "active" plan is whatever the test says, so the PC's real active plan is never re-applied.
    /// </summary>
    public sealed class PowerPlanEditorTests : IDisposable
    {
        const string ScratchName = "PowerSwitcher test plan (safe to delete)";

        readonly Win32PowSettingsWrapper wrapper = new Win32PowSettingsWrapper();
        readonly List<Guid> scratchPlans = new List<Guid>();
        readonly string backupFolder = Path.Combine(Path.GetTempPath(), "PowerSwitcherTests-" + Guid.NewGuid());
        readonly PowerPlanBackups backups;
        readonly List<Guid> appliedPlans = new List<Guid>();
        readonly List<PowerSettingDefinition> settings;
        readonly Dictionary<Guid, List<PowerSettingValue>> realPlansBefore;
        readonly Guid scratch;

        Guid pretendActivePlan = Guid.Empty;
        Action onApply = () => { };

        public PowerPlanEditorTests()
        {
            backups = new PowerPlanBackups(wrapper, backupFolder);
            settings = wrapper.GetSubgroups().SelectMany(subgroup => wrapper.GetSettings(subgroup.Guid)).ToList();

            var realPlans = new Win32PowSchemasWrapper().GetCurrentSchemas().Select(plan => plan.Guid).ToList();
            realPlansBefore = realPlans.ToDictionary(plan => plan, plan => wrapper.GetValues(plan, settings));

            scratch = newScratchPlan(realPlans.First());
        }

        public void Dispose()
        {
            foreach (var plan in scratchPlans) { try { wrapper.DeletePlan(plan); } catch (PowerSwitcherWrappersException) { } }
            Assert.Empty(new Win32PowSchemasWrapper().GetCurrentSchemas().Select(plan => plan.Guid).Intersect(scratchPlans));

            if (Directory.Exists(backupFolder)) { Directory.Delete(backupFolder, true); }

            foreach (var (plan, values) in realPlansBefore)
            {
                Assert.Equal(values, wrapper.GetValues(plan, settings));
            }
        }

        Guid newScratchPlan(Guid source)
        {
            var copy = wrapper.DuplicatePlan(source);
            scratchPlans.Add(copy);
            wrapper.RenamePlan(copy, ScratchName);
            return copy;
        }

        PowerPlanEditor editor(PowerPlanBackups useBackups = null) => new PowerPlanEditor(wrapper, useBackups ?? backups, () => pretendActivePlan, plan => { appliedPlans.Add(plan); onApply(); });

        PowerSettingValue valueOf(Guid plan, PowerSettingDefinition setting) => wrapper.GetValues(plan, new[] { setting }).Single();

        PowerSettingDefinition rangeSetting() => settings.First(setting => setting.Kind == PowerSettingKind.Range && setting.Max > setting.Min);

        // Not from the no-subgroup settings, one of which ("Require a password on wakeup") Windows won't let standard users change.
        PowerSettingDefinition listSetting() => settings.First(setting => setting.Kind == PowerSettingKind.Index && setting.Choices.Count > 1 && setting.SubgroupGuid != Win32PowSettingsWrapper.NoSubgroupGuid);

        static uint otherValue(PowerSettingDefinition setting, uint? current)
        {
            if (setting.Kind == PowerSettingKind.Range) { return current == setting.Min ? setting.Max.Value : setting.Min.Value; }
            return setting.Choices.First(choice => choice.Index != current).Index;
        }

        [Fact]
        public void WritesARangeValueThatPowercfgThenReports()
        {
            var setting = rangeSetting();
            var before = valueOf(scratch, setting);
            var wanted = otherValue(setting, before.AcValue);

            editor().SetValues(scratch, setting, wanted, null);

            Assert.Equal(wanted, valueOf(scratch, setting).AcValue);
            Assert.Equal(before.DcValue, valueOf(scratch, setting).DcValue);

            var reported = PowercfgDump.Query(scratch, includeHidden: true).Settings[setting.Guid];
            Assert.Equal(wanted, reported.Ac);
            Assert.Equal(before.DcValue, reported.Dc);
        }

        [Fact]
        public void WritesAListValueForBatteryOnly()
        {
            var setting = listSetting();
            var before = valueOf(scratch, setting);
            var wanted = otherValue(setting, before.DcValue);

            editor().SetValues(scratch, setting, null, wanted);

            Assert.Equal(before.AcValue, valueOf(scratch, setting).AcValue);
            Assert.Equal(wanted, valueOf(scratch, setting).DcValue);
        }

        [Fact]
        public void RejectsBadValuesBeforeChangingOrBackingUpAnything()
        {
            var range = rangeSetting();
            var list = listSetting();
            var unknown = new PowerSettingDefinition { Guid = range.Guid, SubgroupGuid = range.SubgroupGuid, Name = "Unknown", Kind = PowerSettingKind.Unknown };
            var rangeBefore = valueOf(scratch, range);
            var listBefore = valueOf(scratch, list);

            Assert.Throws<ArgumentOutOfRangeException>(() => editor().SetValues(scratch, list, (uint)list.Choices.Count + 50, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => editor().SetValues(scratch, unknown, 1, null));
            if (range.Min > 0) { Assert.Throws<ArgumentOutOfRangeException>(() => editor().SetValues(scratch, range, range.Min - 1, null)); }

            Assert.Equal(rangeBefore, valueOf(scratch, range));
            Assert.Equal(listBefore, valueOf(scratch, list));
            Assert.False(File.Exists(backups.GetPath(scratch)));
        }

        [Fact]
        public void ReappliesTheActivePlanAfterWriting()
        {
            var setting = rangeSetting();
            var wanted = otherValue(setting, valueOf(scratch, setting).AcValue);
            uint? valueWhenApplied = null;

            pretendActivePlan = scratch;
            onApply = () => valueWhenApplied = valueOf(scratch, setting).AcValue;
            editor().SetValues(scratch, setting, wanted, null);

            Assert.Equal(new[] { scratch }, appliedPlans);
            Assert.Equal(wanted, valueWhenApplied);
        }

        [Fact]
        public void DoesNotApplyAPlanThatIsNotActive()
        {
            var setting = rangeSetting();

            editor().SetValues(scratch, setting, otherValue(setting, valueOf(scratch, setting).AcValue), null);

            Assert.Empty(appliedPlans);
        }

        [Fact]
        public void BacksUpOnceAndTheBackupRestoresTheOriginalValue()
        {
            var setting = rangeSetting();
            var original = valueOf(scratch, setting).AcValue;
            var first = otherValue(setting, original);

            editor().SetValues(scratch, setting, first, null);
            var backup = File.ReadAllBytes(backups.GetPath(scratch));

            editor().SetValues(scratch, setting, original, null);
            editor().SetValues(scratch, setting, first, null);
            Assert.Equal(backup, File.ReadAllBytes(backups.GetPath(scratch)));

            editor().RenamePlan(scratch, "PowerSwitcher test plan renamed");

            Assert.Equal(scratch, editor().RestoreBackup(scratch).Plan);
            Assert.Equal(original, valueOf(scratch, setting).AcValue);
            Assert.Equal(ScratchName, new Win32PowSchemasWrapper().GetPowerPlanName(scratch));
        }

        [Fact]
        public void RestoreReportsSettingsWindowsRefusesAndKeepsGoing()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                if (new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)) { return; }
            }

            var refusedWithoutAdmin = settings.Single(setting => setting.Guid == new Guid("0e796bdb-100d-47d6-a2d5-f7d2daa51f51"));
            var range = rangeSetting();
            var original = valueOf(scratch, range).AcValue;
            editor().SetValues(scratch, range, otherValue(range, original), null);

            var backup = backups.Load(scratch);
            var current = valueOf(scratch, refusedWithoutAdmin).AcValue;
            var edited = backup with { Values = backup.Values.Select(v => v.Setting == refusedWithoutAdmin.Guid ? v with { Ac = otherValue(refusedWithoutAdmin, current) } : v).ToList() };
            File.WriteAllText(backups.GetPath(scratch), System.Text.Json.JsonSerializer.Serialize(edited));

            var result = editor().RestoreBackup(scratch);

            Assert.Equal(new[] { refusedWithoutAdmin.Name }, result.RefusedSettings);
            Assert.Equal(original, valueOf(scratch, range).AcValue);
        }

        [Fact]
        public void RestoringADeletedPlanRecreatesItWithItsValues()
        {
            var setting = rangeSetting();
            var doomed = newScratchPlan(scratch);
            var wanted = otherValue(setting, valueOf(doomed, setting).AcValue);
            wrapper.WriteValues(doomed, setting, wanted, null);

            pretendActivePlan = scratch;
            editor().DeletePlan(doomed);
            scratchPlans.Remove(doomed);

            var restored = editor().RestoreBackup(doomed).Plan;
            scratchPlans.Add(restored);

            Assert.NotEqual(doomed, restored);
            Assert.Equal(wanted, valueOf(restored, setting).AcValue);
            Assert.Equal(ScratchName, new Win32PowSchemasWrapper().GetPowerPlanName(restored));
        }

        [Fact]
        public void WritesNothingWhenTheBackupFails()
        {
            var setting = rangeSetting();
            var before = valueOf(scratch, setting);

            Directory.CreateDirectory(backupFolder);
            var aFile = Path.Combine(backupFolder, "not-a-folder");
            File.WriteAllText(aFile, "");
            var broken = new PowerPlanBackups(wrapper, Path.Combine(aFile, "backups"));

            Assert.ThrowsAny<IOException>(() => editor(broken).SetValues(scratch, setting, otherValue(setting, before.AcValue), null));
            Assert.Equal(before, valueOf(scratch, setting));
        }

        [Fact]
        public void RenamesAPlanAndReportsIt()
        {
            var changes = 0;
            var edit = editor();
            edit.PlansChanged += () => changes++;

            edit.RenamePlan(scratch, "  PowerSwitcher renamed test plan  ");

            Assert.Equal("PowerSwitcher renamed test plan", new Win32PowSchemasWrapper().GetPowerPlanName(scratch));
            Assert.Equal(1, changes);
            Assert.Throws<ArgumentException>(() => edit.RenamePlan(scratch, "   "));
        }

        [Fact]
        public void DuplicatesAPlanWithItsValues()
        {
            var setting = rangeSetting();
            var wanted = otherValue(setting, valueOf(scratch, setting).AcValue);
            editor().SetValues(scratch, setting, wanted, null);

            var copy = editor().DuplicatePlan(scratch);
            scratchPlans.Add(copy);

            Assert.NotEqual(scratch, copy);
            Assert.Equal(wanted, valueOf(copy, setting).AcValue);
        }

        [Fact]
        public void DeletesAPlanAfterBackingItUp()
        {
            var doomed = newScratchPlan(scratch);
            var changes = 0;
            var edit = editor();
            edit.PlansChanged += () => changes++;

            edit.DeletePlan(doomed);
            scratchPlans.Remove(doomed);

            Assert.DoesNotContain(doomed, new Win32PowSchemasWrapper().GetCurrentSchemas().Select(plan => plan.Guid));
            Assert.True(File.Exists(backups.GetPath(doomed)));
            Assert.Equal(1, changes);
        }

        [Fact]
        public void RefusesToDeleteTheActivePlanThroughTheEditor()
        {
            pretendActivePlan = scratch;

            Assert.Throws<InvalidOperationException>(() => editor().DeletePlan(scratch));
            Assert.Contains(scratch, new Win32PowSchemasWrapper().GetCurrentSchemas().Select(plan => plan.Guid));
        }

        [Fact]
        public void WindowsOwnPlansAndTheActivePlanCannotBeDeleted()
        {
            var other = Guid.NewGuid();
            var active = Guid.NewGuid();

            Assert.True(PowerPlanEditor.CanDelete(other, active));
            Assert.False(PowerPlanEditor.CanDelete(active, active));
            Assert.False(PowerPlanEditor.CanDelete(WellKnownSchemas.PowerSaver, active));
            Assert.False(PowerPlanEditor.CanDelete(WellKnownSchemas.Balanced, active));
            Assert.False(PowerPlanEditor.CanDelete(WellKnownSchemas.HighPerformance, active));
            Assert.False(PowerPlanEditor.CanDelete(WellKnownSchemas.UltimatePerformance, active));
        }
    }
}
