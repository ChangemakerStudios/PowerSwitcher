using Petrroll.Helpers;
using PowerSwitcher.Settings;
using PowerSwitcher.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerSwitcher.TrayApp.ViewModels
{
    /// <summary>A category heading in the editor's list of settings.</summary>
    public sealed class SubgroupHeaderRow
    {
        public string Name { get; }
        public string Glyph { get; }
        public string CountText { get; }
        public bool IsFirst { get; }

        public SubgroupHeaderRow(Guid subgroup, string name, int count, bool isFirst)
        {
            Name = name;
            Glyph = SubgroupIcons.For(subgroup);
            CountText = count == 1 ? "1 setting" : $"{count} settings";
            IsFirst = isFirst;
        }
    }

    /// <summary>A Segoe Fluent Icons glyph for each subgroup Windows ships, so the long list has landmarks.</summary>
    public static class SubgroupIcons
    {
        const string Fallback = ""; // Sliders

        static readonly Dictionary<Guid, string> glyphs = new Dictionary<Guid, string>
        {
            [new Guid("0012ee47-9041-4b5d-9b77-535fba8b1442")] = "", // Hard disk
            [new Guid("02f815b5-a5cf-4c84-bf20-649d1f75d3d8")] = "", // Internet Explorer: globe
            [new Guid("0d7dbae2-4294-402a-ba8e-26777e8488cd")] = "", // Desktop background: brush
            [new Guid("19cbb8fa-5279-450e-9fac-8a3d5fedd0c1")] = "", // Wireless adapter
            [new Guid("238c9fa8-0aad-41ed-83f4-97be242c8f20")] = "", // Sleep: moon
            [new Guid("2a737441-1930-4402-8d77-b2bebba308a3")] = "", // USB
            [new Guid("2e601130-5351-4d9d-8e04-252966bad054")] = "", // Idle resiliency: stopwatch
            [new Guid("44f3beca-a7c0-460e-9df2-bb8b99e0cba6")] = "", // Intel graphics
            [new Guid("48672f38-7a9a-4bb2-8bf8-3d85be19de4e")] = "", // Interrupt steering
            [new Guid("48df9d60-4f68-11dc-8314-0800200c9a66")] = "", // Intel Dynamic Tuning: thermometer
            [new Guid("4f971e89-eebd-4455-a8de-9e59040e7347")] = "", // Power buttons and lid
            [new Guid("501a4d13-42af-4429-9fd1-a8218c268e20")] = "", // PCI Express: link
            [new Guid("54533251-82be-4824-96c1-47b60b740d00")] = "", // Processor
            [new Guid("5fb4938d-1ee8-4b0f-9a3c-5036b0ab995c")] = "", // Graphics
            [new Guid("7516b95f-f776-4464-8c53-06167f40cc99")] = "", // Display
            [new Guid("8619b916-e004-4dd8-9b66-dae86f806698")] = "", // Presence aware: person
            [new Guid("9596fb26-9850-41fd-ac3e-f7c3c00afd4b")] = "", // Multimedia: video
            [new Guid("de830923-a562-41af-a086-e3a2c6bad2da")] = "", // Energy saver: leaf
            [new Guid("e73a048d-bf27-4f12-9731-8b2076e8891f")] = "", // Battery
            [Win32PowSettingsWrapper.NoSubgroupGuid] = "", // No subgroup: gear
        };

        public static string For(Guid subgroup) => glyphs.TryGetValue(subgroup, out var glyph) ? glyph : Fallback;
    }

    /// <summary>A setting in the editor's list, with its plugged-in and battery values.</summary>
    public sealed class SettingRow : ObservableObject
    {
        public PowerSettingDefinition Definition { get; }
        public PowerSettingValue Value { get; private set; }
        public ValueCell Ac { get; }
        public ValueCell Dc { get; }

        public string Name => Definition.Name;
        public string Description => Definition.Description;
        public bool HasDescription => !string.IsNullOrWhiteSpace(Definition.Description);
        public bool IsHidden => Definition.IsHidden;
        public bool CanReset => Ac.CanReset || Dc.CanReset;

        public SettingRow(PowerSettingDefinition definition, PowerSettingValue value)
        {
            Definition = definition;
            Value = value;
            Ac = new ValueCell(this, isAc: true);
            Dc = new ValueCell(this, isAc: false);
        }

        /// <summary>Shows fresh values read back from Windows, which also puts a refused edit back as it was.</summary>
        public void Update(PowerSettingValue value)
        {
            Value = value;
            Ac.Refresh();
            Dc.Refresh();
            RaisePropertyChangedEvent(nameof(CanReset));
        }
    }

    /// <summary>One side of a setting: its plugged-in value or its battery value.</summary>
    public sealed class ValueCell : ObservableObject
    {
        public SettingRow Row { get; }
        public bool IsAc { get; }

        public ValueCell(SettingRow row, bool isAc)
        {
            Row = row;
            IsAc = isAc;
        }

        public uint? Value => IsAc ? Row.Value.AcValue : Row.Value.DcValue;
        public uint? Default => IsAc ? Row.Value.AcDefault : Row.Value.DcDefault;

        public bool IsRange => Row.Definition.Kind == PowerSettingKind.Range;
        public bool IsIndex => Row.Definition.Kind == PowerSettingKind.Index;
        public bool IsReadOnly => Row.Definition.Kind == PowerSettingKind.Unknown;
        public IReadOnlyList<PowerSettingChoice> Choices => Row.Definition.Choices;
        public string Units => Row.Definition.Units;
        public string AccessibleName => $"{Row.Name}, {(IsAc ? "plugged in" : "on battery")}";

        public string Text => Value?.ToString() ?? string.Empty;
        public PowerSettingChoice Choice => Choices.FirstOrDefault(choice => choice.Index == Value);
        public string Display => Row.Definition.FormatValue(Value);
        public bool IsModified => IsAc ? Row.Value.IsAcModified : Row.Value.IsDcModified;
        public string DefaultNote => $"Default: {Row.Definition.FormatValue(Default)}";

        public bool CanReset => IsModified && Default != null && Row.Definition.AcceptsValue(Default.Value);

        public void Refresh()
        {
            foreach (var property in new[] { nameof(Value), nameof(Text), nameof(Choice), nameof(Display), nameof(IsModified), nameof(DefaultNote), nameof(CanReset) })
            {
                RaisePropertyChangedEvent(property);
            }
        }
    }
}
