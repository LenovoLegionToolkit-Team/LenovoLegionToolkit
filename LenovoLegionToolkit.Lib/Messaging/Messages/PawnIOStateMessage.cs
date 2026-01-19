namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct PawnIOStateMessage(PawnIOState state) : IMessage
{
    public PawnIOState State { get; } = state;
}
