using System;
using System.Collections.Generic;

namespace PowerSwitcher.Settings
{
    public sealed record PowerSubgroup(Guid Guid, string Name, string Description, bool IsHidden);

    public enum PowerSettingKind
    {
        /// <summary>A number between <see cref="PowerSettingDefinition.Min"/> and <see cref="PowerSettingDefinition.Max"/>.</summary>
        Range,

        /// <summary>One of <see cref="PowerSettingDefinition.Choices"/>.</summary>
        Index,

        /// <summary>Windows reports neither a range nor choices, so the value can only be shown as a number.</summary>
        Unknown
    }

    public sealed record PowerSettingChoice(uint Index, string Name);

    public sealed record PowerSettingDefinition
    {
        public Guid Guid { get; init; }
        public Guid SubgroupGuid { get; init; }
        public string Name { get; init; }
        public string Description { get; init; }
        public bool IsHidden { get; init; }
        public PowerSettingKind Kind { get; init; }

        // Range settings only.
        public uint? Min { get; init; }
        public uint? Max { get; init; }
        public uint? Increment { get; init; }
        public string Units { get; init; }

        // Index settings only; empty for the other kinds.
        public IReadOnlyList<PowerSettingChoice> Choices { get; init; } = Array.Empty<PowerSettingChoice>();
    }

    /// <summary>A setting's values in one plan. Any value is null when Windows has none stored for it.</summary>
    public sealed record PowerSettingValue(Guid SchemeGuid, Guid SettingGuid, uint? AcValue, uint? DcValue, uint? AcDefault, uint? DcDefault)
    {
        public bool IsAcModified => AcDefault.HasValue && AcValue != AcDefault;
        public bool IsDcModified => DcDefault.HasValue && DcValue != DcDefault;
    }
}
