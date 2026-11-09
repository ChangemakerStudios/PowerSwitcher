using PowerSwitcher.Settings;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PowerSwitcher.Wrappers
{
    /// <summary>
    /// Reads the tree of power settings, including the ones Windows hides, and reads and writes plans. Go through
    /// <see cref="PowerPlanEditor"/> to change a plan, so that it's validated and backed up first.
    /// </summary>
    public unsafe partial class Win32PowSettingsWrapper
    {
        /// <summary>The subgroup Windows files top-level settings under. PowerEnumerate doesn't list it, but it can be queried.</summary>
        public static readonly Guid NoSubgroupGuid = new Guid("fea3413e-7e05-4911-9a71-700331f1c294");

        // Windows has no name for it; this is what powercfg prints.
        const string NoSubgroupName = "Settings belonging to no subgroup";

        static readonly Guid PersonalitySettingGuid = new Guid("245d8541-3943-4422-b025-13a784f679b7");

        const uint ERROR_SUCCESS = 0;
        const uint ERROR_FILE_NOT_FOUND = 2;
        const uint ERROR_MORE_DATA = 234;
        const uint ERROR_NO_MORE_ITEMS = 259;

        const uint POWER_ATTRIBUTE_HIDE = 1;

        public List<PowerSubgroup> GetSubgroups()
        {
            return enumerate(null, Win32PowSchemasWrapper.AccessFlags.ACCESS_SUBGROUP)
                .Prepend(NoSubgroupGuid)
                .Select(readSubgroup)
                .ToList();
        }

        private static PowerSubgroup readSubgroup(Guid guid)
        {
            var name = readLabel(PowerReadFriendlyName, guid, null);
            if (name.Length == 0 && guid == NoSubgroupGuid) { name = NoSubgroupName; }

            return new PowerSubgroup(guid, name, readLabel(PowerReadDescription, guid, null), isHidden(guid, null));
        }

        public List<PowerSettingDefinition> GetSettings(Guid subgroupGuid)
        {
            return enumerate(subgroupGuid, Win32PowSchemasWrapper.AccessFlags.ACCESS_INDIVIDUAL_SETTING)
                .Select(guid => readDefinition(subgroupGuid, guid))
                .ToList();
        }

        /// <summary>
        /// The built-in plan whose defaults a plan follows: always Power saver, Balanced or High performance, so it
        /// doesn't identify the plan itself (Ultimate Performance reports Balanced). Null if the plan doesn't say.
        /// </summary>
        public Guid? GetPersonality(Guid schemeGuid)
        {
            var index = readIndex(PowerReadACValueIndex, schemeGuid, NoSubgroupGuid, PersonalitySettingGuid);
            if (index == null) { return null; }

            Guid subgroup = NoSubgroupGuid, setting = PersonalitySettingGuid, personality = default;
            uint type = 0, size = (uint)sizeof(Guid);

            var errCode = PowerReadPossibleValue(0, &subgroup, &setting, &type, index.Value, (byte*)&personality, &size);
            if (errCode == ERROR_FILE_NOT_FOUND) { return null; }
            check(errCode, nameof(PowerReadPossibleValue));

            return size == sizeof(Guid) ? personality : null;
        }

        /// <summary>The plan's values for the given settings, in the same order.</summary>
        public List<PowerSettingValue> GetValues(Guid schemeGuid, IEnumerable<PowerSettingDefinition> settings)
        {
            var personality = GetPersonality(schemeGuid);

            return settings.Select(setting => new PowerSettingValue(
                schemeGuid,
                setting.Guid,
                readIndex(PowerReadACValueIndex, schemeGuid, setting.SubgroupGuid, setting.Guid),
                readIndex(PowerReadDCValueIndex, schemeGuid, setting.SubgroupGuid, setting.Guid),
                personality == null ? null : readIndex(PowerReadACDefaultIndex, personality.Value, setting.SubgroupGuid, setting.Guid),
                personality == null ? null : readIndex(PowerReadDCDefaultIndex, personality.Value, setting.SubgroupGuid, setting.Guid)
                )).ToList();
        }

        /// <summary>Stores new values in a plan; null leaves that one as it is. An active plan needs re-applying afterwards.</summary>
        public void WriteValues(Guid schemeGuid, PowerSettingDefinition definition, uint? acValue, uint? dcValue)
        {
            Guid scheme = schemeGuid, subgroup = definition.SubgroupGuid, setting = definition.Guid;

            if (acValue != null) { check(PowerWriteACValueIndex(0, &scheme, &subgroup, &setting, acValue.Value), nameof(PowerWriteACValueIndex)); }
            if (dcValue != null) { check(PowerWriteDCValueIndex(0, &scheme, &subgroup, &setting, dcValue.Value), nameof(PowerWriteDCValueIndex)); }
        }

        /// <summary>Copies a plan and returns the copy's GUID. The copy keeps the original's name.</summary>
        public Guid DuplicatePlan(Guid sourceGuid)
        {
            Guid source = sourceGuid;
            Guid* destination = null;

            check(PowerDuplicateScheme(0, &source, &destination), nameof(PowerDuplicateScheme));
            return takeGuid(destination);
        }

        public void DeletePlan(Guid schemeGuid)
        {
            Guid scheme = schemeGuid;
            check(PowerDeleteScheme(0, &scheme), nameof(PowerDeleteScheme));
        }

        public void RenamePlan(Guid schemeGuid, string name)
        {
            Guid scheme = schemeGuid;
            var buffer = Encoding.Unicode.GetBytes(name + "\0");

            fixed (byte* bufferPointer = buffer)
            {
                check(PowerWriteFriendlyName(0, &scheme, null, null, bufferPointer, (uint)buffer.Length), nameof(PowerWriteFriendlyName));
            }
        }

        private static Guid takeGuid(Guid* allocatedGuid)
        {
            try { return *allocatedGuid; }
            finally { LocalFree((nint)allocatedGuid); }
        }

        private static PowerSettingDefinition readDefinition(Guid subgroupGuid, Guid settingGuid)
        {
            var definition = new PowerSettingDefinition
            {
                Guid = settingGuid,
                SubgroupGuid = subgroupGuid,
                Name = readLabel(PowerReadFriendlyName, subgroupGuid, settingGuid),
                Description = readLabel(PowerReadDescription, subgroupGuid, settingGuid),
                IsHidden = isHidden(subgroupGuid, settingGuid),
                Kind = PowerSettingKind.Unknown
            };

            // Despite the Win32 docs' wording, this returns TRUE for settings that have a range.
            Guid subgroup = subgroupGuid, setting = settingGuid;
            if (PowerIsSettingRangeDefined(&subgroup, &setting))
            {
                var min = readRangeValue(PowerReadValueMin, subgroupGuid, settingGuid);
                var max = readRangeValue(PowerReadValueMax, subgroupGuid, settingGuid);
                if (min == null || max == null) { return definition; }

                return definition with
                {
                    Kind = PowerSettingKind.Range,
                    Min = min,
                    Max = max,
                    Increment = readRangeValue(PowerReadValueIncrement, subgroupGuid, settingGuid),
                    Units = readUnits(subgroupGuid, settingGuid)
                };
            }

            var choices = readChoices(subgroupGuid, settingGuid);
            return choices.Count == 0 ? definition : definition with { Kind = PowerSettingKind.Index, Choices = choices };
        }

        private static List<PowerSettingChoice> readChoices(Guid subgroupGuid, Guid settingGuid)
        {
            var choices = new List<PowerSettingChoice>();

            for (uint index = 0; ; index++)
            {
                var name = readChoiceName(subgroupGuid, settingGuid, index);
                if (name.Length == 0) { return choices; }
                choices.Add(new PowerSettingChoice(index, name));
            }
        }

        private static List<Guid> enumerate(Guid? subgroupGuid, Win32PowSchemasWrapper.AccessFlags accessFlags)
        {
            // A null plan lists every setting Windows defines, whether or not a given plan stores a value for it.
            var guids = new List<Guid>();
            Guid subgroup = subgroupGuid.GetValueOrDefault(), item = default;

            for (uint index = 0; ; index++)
            {
                uint size = (uint)sizeof(Guid);
                var errCode = PowerEnumerate(0, null, subgroupGuid.HasValue ? &subgroup : null, (uint)accessFlags, index, &item, &size);
                if (errCode == ERROR_NO_MORE_ITEMS) { return guids; }

                check(errCode, nameof(PowerEnumerate));
                guids.Add(item);
            }
        }

        private static bool isHidden(Guid subgroupGuid, Guid? settingGuid)
        {
            Guid subgroup = subgroupGuid, setting = settingGuid.GetValueOrDefault();
            return (PowerReadSettingAttributes(&subgroup, settingGuid.HasValue ? &setting : null) & POWER_ATTRIBUTE_HIDE) != 0;
        }

        private static string readLabel(LabelRead read, Guid subgroupGuid, Guid? settingGuid)
        {
            return readText(read.Method.Name, (buffer, size) =>
            {
                Guid subgroup = subgroupGuid, setting = settingGuid.GetValueOrDefault();
                return read(0, null, &subgroup, settingGuid.HasValue ? &setting : null, buffer, size);
            });
        }

        private static string readUnits(Guid subgroupGuid, Guid settingGuid)
        {
            return readText(nameof(PowerReadValueUnitsSpecifier), (buffer, size) =>
            {
                Guid subgroup = subgroupGuid, setting = settingGuid;
                return PowerReadValueUnitsSpecifier(0, &subgroup, &setting, buffer, size);
            });
        }

        private static string readChoiceName(Guid subgroupGuid, Guid settingGuid, uint index)
        {
            return readText(nameof(PowerReadPossibleFriendlyName), (buffer, size) =>
            {
                Guid subgroup = subgroupGuid, setting = settingGuid;
                return PowerReadPossibleFriendlyName(0, &subgroup, &setting, index, buffer, size);
            });
        }

        /// <summary>Calls a Win32 function that fills a buffer with a wide string. Empty when Windows has no such text.</summary>
        private static string readText(string functionName, BufferRead read)
        {
            uint size = 0;
            var errCode = read(null, &size);

            if (errCode == ERROR_FILE_NOT_FOUND) { return string.Empty; }
            if (errCode != ERROR_SUCCESS && errCode != ERROR_MORE_DATA) { check(errCode, functionName); }
            if (size == 0) { return string.Empty; }

            var buffer = new byte[size];
            fixed (byte* bufferPointer = buffer)
            {
                errCode = read(bufferPointer, &size);
            }
            check(errCode, functionName);

            var text = Encoding.Unicode.GetString(buffer, 0, (int)size);
            var terminator = text.IndexOf('\0');
            return terminator >= 0 ? text.Substring(0, terminator) : text;
        }

        private static uint? readRangeValue(RangeRead read, Guid subgroupGuid, Guid settingGuid)
        {
            Guid subgroup = subgroupGuid, setting = settingGuid;
            uint value;

            var errCode = read(0, &subgroup, &setting, &value);
            if (errCode == ERROR_FILE_NOT_FOUND) { return null; }

            check(errCode, read.Method.Name);
            return value;
        }

        private static uint? readIndex(IndexRead read, Guid schemeOrPersonalityGuid, Guid subgroupGuid, Guid settingGuid)
        {
            Guid scheme = schemeOrPersonalityGuid, subgroup = subgroupGuid, setting = settingGuid;
            uint value;

            var errCode = read(0, &scheme, &subgroup, &setting, &value);
            if (errCode == ERROR_FILE_NOT_FOUND) { return null; }

            check(errCode, read.Method.Name);
            return value;
        }

        private static void check(uint errCode, string functionName)
        {
            if (errCode != ERROR_SUCCESS) { throw new PowerSwitcherWrappersException($"{functionName}() failed with code {errCode}"); }
        }

        private delegate uint BufferRead(byte* buffer, uint* bufferSize);
        private delegate uint LabelRead(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, byte* buffer, uint* bufferSize);
        private delegate uint RangeRead(nint rootPowerKey, Guid* subgroup, Guid* setting, uint* value);
        private delegate uint IndexRead(nint rootPowerKey, Guid* schemeOrPersonality, Guid* subgroup, Guid* setting, uint* value);

        #region DLL imports

        const string PowrProf = "powrprof.dll";

        [LibraryImport(PowrProf)]
        private static partial uint PowerEnumerate(nint rootPowerKey, Guid* scheme, Guid* subgroup, uint accessFlags, uint index, Guid* buffer, uint* bufferSize);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadFriendlyName(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, byte* buffer, uint* bufferSize);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadDescription(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, byte* buffer, uint* bufferSize);

        [LibraryImport(PowrProf)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool PowerIsSettingRangeDefined(Guid* subgroup, Guid* setting);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadValueMin(nint rootPowerKey, Guid* subgroup, Guid* setting, uint* value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadValueMax(nint rootPowerKey, Guid* subgroup, Guid* setting, uint* value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadValueIncrement(nint rootPowerKey, Guid* subgroup, Guid* setting, uint* value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadValueUnitsSpecifier(nint rootPowerKey, Guid* subgroup, Guid* setting, byte* buffer, uint* bufferSize);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadPossibleFriendlyName(nint rootPowerKey, Guid* subgroup, Guid* setting, uint index, byte* buffer, uint* bufferSize);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadPossibleValue(nint rootPowerKey, Guid* subgroup, Guid* setting, uint* type, uint index, byte* buffer, uint* bufferSize);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadACValueIndex(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, uint* value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadDCValueIndex(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, uint* value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadACDefaultIndex(nint rootPowerKey, Guid* personality, Guid* subgroup, Guid* setting, uint* value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadDCDefaultIndex(nint rootPowerKey, Guid* personality, Guid* subgroup, Guid* setting, uint* value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerReadSettingAttributes(Guid* subgroup, Guid* setting);

        [LibraryImport(PowrProf)]
        private static partial uint PowerWriteACValueIndex(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, uint value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerWriteDCValueIndex(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, uint value);

        [LibraryImport(PowrProf)]
        private static partial uint PowerDuplicateScheme(nint rootPowerKey, Guid* source, Guid** destination);

        [LibraryImport(PowrProf)]
        private static partial uint PowerDeleteScheme(nint rootPowerKey, Guid* scheme);

        [LibraryImport(PowrProf)]
        private static partial uint PowerWriteFriendlyName(nint rootPowerKey, Guid* scheme, Guid* subgroup, Guid* setting, byte* buffer, uint bufferSize);

        [LibraryImport("kernel32.dll")]
        private static partial nint LocalFree(nint memory);

        #endregion
    }
}
