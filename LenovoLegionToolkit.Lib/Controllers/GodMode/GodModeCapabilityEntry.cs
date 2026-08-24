using System;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public sealed class GodModeCapabilityEntry
{
    public required Enum CapabilityId { get; init; }
    public uint RawId => Convert.ToUInt32(CapabilityId);
    public required string PropertyName { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public int Step { get; init; }
    public int[] Steps { get; init; } = [];
    public int DefaultValue { get; init; }
    public bool FailAllowed { get; init; }
}
