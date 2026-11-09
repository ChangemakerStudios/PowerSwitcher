using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

namespace PowerSwitcher.Tests
{
    /// <summary>One setting as printed by <c>powercfg</c>; the reference the wrapper is checked against.</summary>
    public sealed class PowercfgSetting
    {
        public Guid Guid;
        public Guid SubgroupGuid;
        public string Name;
        public uint? Min, Max, Increment;
        public string Units;
        public readonly List<string> Choices = new List<string>();
        public uint Ac, Dc;
    }

    /// <summary>Parses <c>powercfg /q</c> or <c>/qh</c>. The labels it looks for are the English ones.</summary>
    public sealed class PowercfgDump
    {
        static readonly Regex Subgroup = new Regex(@"^\s*Subgroup GUID:\s*([0-9a-f-]{36})\s*\((.*)\)\s*$");
        static readonly Regex Setting = new Regex(@"^\s*Power Setting GUID:\s*([0-9a-f-]{36})\s*\((.*)\)\s*$");
        static readonly Regex Field = new Regex(@"^\s*([^:]+):\s?(.*?)\s*$");

        public Dictionary<Guid, string> Subgroups { get; } = new Dictionary<Guid, string>();
        public Dictionary<Guid, PowercfgSetting> Settings { get; } = new Dictionary<Guid, PowercfgSetting>();

        public static PowercfgDump Query(Guid plan, bool includeHidden)
        {
            var start = new ProcessStartInfo("powercfg", $"{(includeHidden ? "/qh" : "/q")} {plan}") { RedirectStandardOutput = true, UseShellExecute = false };
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return Parse(output);
            }
        }

        public static PowercfgDump Parse(string output)
        {
            var dump = new PowercfgDump();
            var subgroup = Guid.Empty;
            PowercfgSetting setting = null;

            foreach (var line in output.Split('\n'))
            {
                var match = Subgroup.Match(line);
                if (match.Success)
                {
                    subgroup = Guid.Parse(match.Groups[1].Value);
                    dump.Subgroups[subgroup] = match.Groups[2].Value;
                    setting = null;
                    continue;
                }

                match = Setting.Match(line);
                if (match.Success)
                {
                    setting = new PowercfgSetting { Guid = Guid.Parse(match.Groups[1].Value), SubgroupGuid = subgroup, Name = match.Groups[2].Value };
                    dump.Settings[setting.Guid] = setting;
                    continue;
                }

                match = Field.Match(line);
                if (!match.Success || setting == null) { continue; }

                var value = match.Groups[2].Value;
                switch (match.Groups[1].Value)
                {
                    case "Minimum Possible Setting": setting.Min = hex(value); break;
                    case "Maximum Possible Setting": setting.Max = hex(value); break;
                    case "Possible Settings increment": setting.Increment = hex(value); break;
                    case "Possible Settings units": setting.Units = value; break;
                    case "Possible Setting Friendly Name": setting.Choices.Add(value); break;
                    case "Current AC Power Setting Index": setting.Ac = hex(value); break;
                    case "Current DC Power Setting Index": setting.Dc = hex(value); break;
                }
            }

            return dump;
        }

        static uint hex(string value) => uint.Parse(value.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }
}
