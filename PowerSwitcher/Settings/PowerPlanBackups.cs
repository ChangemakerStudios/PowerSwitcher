using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerSwitcher.Settings
{
    public sealed record PowerPlanBackupValue(Guid Setting, uint? Ac, uint? Dc);

    public sealed record PowerPlanBackup(Guid Plan, string Name, DateTime CreatedUtc, List<PowerPlanBackupValue> Values);

    /// <summary>
    /// Keeps a copy of each plan as it was before PowerPlanSwitcher first changed it. These are the app's own files
    /// because <c>powercfg /export</c> needs admin rights, which editing a plan doesn't.
    /// </summary>
    public sealed class PowerPlanBackups
    {
        public static string DefaultFolder { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Petrroll", "PowerSwitcher", "Backups");

        readonly Win32PowSettingsWrapper wrapper;
        readonly Win32PowSchemasWrapper schemas = new Win32PowSchemasWrapper();
        readonly string folder;

        public PowerPlanBackups(Win32PowSettingsWrapper wrapper, string folder)
        {
            this.wrapper = wrapper;
            this.folder = folder;
        }

        public string GetPath(Guid plan) => Path.Combine(folder, $"{plan}.json");

        /// <summary>Backs up the plan unless it already has a backup, which is never replaced. Throws if it can't be saved.</summary>
        public void EnsureBackup(Guid plan)
        {
            var path = GetPath(plan);
            if (File.Exists(path)) { return; }

            var settings = wrapper.GetSubgroups().SelectMany(subgroup => wrapper.GetSettings(subgroup.Guid));
            var values = wrapper.GetValues(plan, settings).Select(value => new PowerPlanBackupValue(value.SettingGuid, value.AcValue, value.DcValue)).ToList();
            var backup = new PowerPlanBackup(plan, schemas.GetPowerPlanName(plan), DateTime.UtcNow, values);

            Directory.CreateDirectory(folder);

            // Through a temporary name, so that a half-written file can never pass for a backup.
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(backup));
            File.Move(temporaryPath, path);
        }

        public bool Exists(Guid plan) => File.Exists(GetPath(plan));

        public PowerPlanBackup Load(Guid plan) => JsonSerializer.Deserialize<PowerPlanBackup>(File.ReadAllText(GetPath(plan)));

        public void Remove(Guid plan) => File.Delete(GetPath(plan));

        /// <summary>Every readable backup; a damaged file is left out rather than getting in the way of the others.</summary>
        public List<PowerPlanBackup> LoadAll()
        {
            var loaded = new List<PowerPlanBackup>();
            if (!Directory.Exists(folder)) { return loaded; }

            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                try { loaded.Add(JsonSerializer.Deserialize<PowerPlanBackup>(File.ReadAllText(path))); }
                catch (JsonException) { }
                catch (IOException) { }
            }

            return loaded;
        }
    }
}
