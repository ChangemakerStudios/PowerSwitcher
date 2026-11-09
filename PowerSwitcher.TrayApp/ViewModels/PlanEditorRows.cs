using Petrroll.Helpers;
using PowerSwitcher.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerSwitcher.TrayApp.ViewModels
{
    /// <summary>A category heading in the editor's list of settings.</summary>
    public sealed class SubgroupHeaderRow
    {
        public string Name { get; }
        public SubgroupHeaderRow(string name) { Name = name; }
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
        public string HiddenNote => Definition.IsHidden ? " · hidden" : string.Empty;
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
