using System;
using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Station.Services;

public interface IAutomationTriggerRegistry
{
    IReadOnlyCollection<ExtensionAutomationTriggerInfo> Triggers { get; }
    event EventHandler? TriggersChanged;
    void Register(ExtensionAutomationTriggerInfo item);
}

public sealed class ExtensionAutomationTriggerInfo
{
    public required Type TriggerType { get; init; }
    public required Func<object> Factory { get; init; }
    public Type? ConfigurationControlType { get; init; }
    public int? Icon { get; init; }
}
