namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public sealed class GodModeCapabilityEntry
{
    public required uint RawId { get; init; }
    public required string PropertyName { get; init; }
    public int Min { get; init; }
    public int Max { get; init; }
    public int Step { get; init; }
    public int[] Steps { get; init; } = [];
    public int DefaultValue { get; init; }
    public bool FailAllowed { get; init; }
    public bool UseNvApiWrapper { get; init; }
    public bool OnlyWhenNativeCapabilityIsUnavailable { get; init; }
}
