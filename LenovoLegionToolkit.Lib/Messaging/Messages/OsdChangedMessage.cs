namespace LenovoLegionToolkit.Lib.Messaging.Messages;

public readonly struct OsdChangedMessage(ToggleState state, bool setPreference = true) : IMessage
{
    public ToggleState State { get; } = state;

    public bool SetPreference { get; } = setPreference;
}
