namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct FanStateMessage(FanState state) : IMessage
{
    public FanState State { get; } = state;
}
