using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerSwitcher.Settings
{
    /// <summary>The restored plan, and the names of any settings Windows wouldn't change.</summary>
    public sealed record PowerPlanRestoreResult(Guid Plan, IReadOnlyList<string> RefusedSettings);

    /// <summary>A change that can be undone: the values to put back, with null for a side it didn't touch.</summary>
    public sealed record PowerPlanEdit(Guid Plan, PowerSettingDefinition Setting, uint? AcValue, uint? DcValue);

    /// <summary>The one way to change a power plan: values are validated, and the plan is backed up before its first change.</summary>
    public sealed class PowerPlanEditor
    {
        readonly Win32PowSettingsWrapper wrapper;
        readonly PowerPlanBackups backups;
        readonly Win32PowSchemasWrapper schemas = new Win32PowSchemasWrapper();
        readonly Func<Guid> getActivePlan;
        readonly Action<Guid> applyPlan;
        readonly Stack<PowerPlanEdit> undoStack = new Stack<PowerPlanEdit>();

        /// <summary>Plans were added, removed or renamed, which the active-plan watcher doesn't report.</summary>
        public event Action PlansChanged;

        public PowerPlanEditor(Win32PowSettingsWrapper wrapper, PowerPlanBackups backups, Func<Guid> getActivePlan, Action<Guid> applyPlan)
        {
            this.wrapper = wrapper;
            this.backups = backups;
            this.getActivePlan = getActivePlan;
            this.applyPlan = applyPlan;
        }

        public static PowerPlanEditor CreateDefault()
        {
            var settings = new Win32PowSettingsWrapper();
            var schemas = new Win32PowSchemasWrapper();

            return new PowerPlanEditor(settings, new PowerPlanBackups(settings, PowerPlanBackups.DefaultFolder), schemas.GetActiveGuid, schemas.SetActiveGuid);
        }

        /// <summary>Changes a setting's values in a plan; null leaves that one as it is. Takes effect at once on the active plan.</summary>
        public void SetValues(Guid plan, PowerSettingDefinition setting, uint? acValue, uint? dcValue)
        {
            requireAccepted(setting, acValue);
            requireAccepted(setting, dcValue);

            backups.EnsureBackup(plan);

            var before = wrapper.GetValues(plan, new[] { setting }).Single();
            wrapper.WriteValues(plan, setting, acValue, dcValue);
            undoStack.Push(new PowerPlanEdit(plan, setting, acValue == null ? null : before.AcValue, dcValue == null ? null : before.DcValue));

            applyIfActive(plan);
        }

        public bool CanUndo => undoStack.Count > 0;

        /// <summary>Reverses the latest <see cref="SetValues"/> made through this editor and says what was put back.</summary>
        public PowerPlanEdit Undo()
        {
            var edit = undoStack.Peek();

            wrapper.WriteValues(edit.Plan, edit.Setting, edit.AcValue, edit.DcValue);
            undoStack.Pop();

            applyIfActive(edit.Plan);
            return edit;
        }

        // Windows only applies changes to the active plan when it's set as active again.
        private void applyIfActive(Guid plan)
        {
            if (plan == getActivePlan()) { applyPlan(plan); }
        }

        public bool HasBackup(Guid plan) => backups.Exists(plan);

        /// <summary>Backups of plans that no longer exist, which <see cref="RestoreBackup"/> can bring back.</summary>
        public List<PowerPlanBackup> GetDeletedPlanBackups()
        {
            var plans = schemas.GetCurrentSchemas().Select(schema => schema.Guid).ToHashSet();
            return backups.LoadAll().Where(backup => !plans.Contains(backup.Plan)).OrderBy(backup => backup.Name).ToList();
        }

        /// <summary>Copies a plan, giving the copy a name of its own if one is passed. A new plan has nothing to back up yet.</summary>
        public Guid DuplicatePlan(Guid source, string name = null)
        {
            var copy = wrapper.DuplicatePlan(source);

            try
            {
                if (name != null) { wrapper.RenamePlan(copy, name); }
            }
            catch (PowerSwitcherWrappersException)
            {
                wrapper.DeletePlan(copy);
                throw;
            }

            PlansChanged?.Invoke();
            return copy;
        }

        public void RenamePlan(Guid plan, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) { throw new ArgumentException("A plan needs a name.", nameof(name)); }

            backups.EnsureBackup(plan);
            wrapper.RenamePlan(plan, name.Trim());
            PlansChanged?.Invoke();
        }

        /// <summary>Windows' own plans are kept, and the active plan has to be switched away from first.</summary>
        public static bool CanDelete(Guid plan, Guid activePlan)
        {
            return plan != activePlan
                && plan != WellKnownSchemas.PowerSaver
                && plan != WellKnownSchemas.Balanced
                && plan != WellKnownSchemas.HighPerformance
                && plan != WellKnownSchemas.UltimatePerformance;
        }

        public void DeletePlan(Guid plan)
        {
            if (!CanDelete(plan, getActivePlan())) { throw new InvalidOperationException("This plan can't be deleted."); }

            backups.EnsureBackup(plan);
            wrapper.DeletePlan(plan);
            PlansChanged?.Invoke();
        }

        /// <summary>
        /// Puts a plan back as it was when it was backed up, recreating it as a new plan if it's since been deleted.
        /// Settings Windows no longer has are ignored, and any it refuses to change are reported.
        /// </summary>
        public PowerPlanRestoreResult RestoreBackup(Guid plan)
        {
            var backup = backups.Load(plan);
            var exists = schemas.GetCurrentSchemas().Any(schema => schema.Guid == plan);
            var target = exists ? plan : wrapper.DuplicatePlan(getActivePlan());

            try
            {
                var refused = restoreValues(target, backup);

                wrapper.RenamePlan(target, backup.Name);
                applyIfActive(target);

                if (!exists) { backups.Remove(plan); } // It's back under a new GUID, so this one no longer counts as deleted.

                PlansChanged?.Invoke();
                return new PowerPlanRestoreResult(target, refused);
            }
            catch (PowerSwitcherWrappersException)
            {
                if (!exists) { wrapper.DeletePlan(target); } // Don't leave a half-restored plan behind.
                throw;
            }
        }

        private List<string> restoreValues(Guid target, PowerPlanBackup backup)
        {
            var refused = new List<string>();
            var settings = wrapper.GetSubgroups().SelectMany(subgroup => wrapper.GetSettings(subgroup.Guid)).ToList();
            var saved = backup.Values.ToDictionary(value => value.Setting);
            var current = wrapper.GetValues(target, settings);

            for (var index = 0; index < settings.Count; index++)
            {
                if (!saved.TryGetValue(settings[index].Guid, out var value)) { continue; }

                // Only what changed: Windows refuses some settings outright, even when given the value they already have.
                var ac = value.Ac != current[index].AcValue ? value.Ac : null;
                var dc = value.Dc != current[index].DcValue ? value.Dc : null;
                if (ac == null && dc == null) { continue; }

                try { wrapper.WriteValues(target, settings[index], ac, dc); }
                catch (PowerSwitcherWrappersException) { refused.Add(settings[index].Name); }
            }

            return refused;
        }

        private static void requireAccepted(PowerSettingDefinition setting, uint? value)
        {
            if (value != null && !setting.AcceptsValue(value.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, $"'{setting.Name}' can't be set to this value.");
            }
        }
    }
}
