using System;
using System.Collections.Generic;

namespace LenovoLegionToolkit.Lib.Station.Services;

public interface IAutomationStepRegistry
{
    IReadOnlyCollection<ExtensionAutomationStepInfo> Steps { get; }
    event EventHandler? StepsChanged;
    void Register(ExtensionAutomationStepInfo item);
}

public sealed class ExtensionAutomationStepInfo
{
    public required Type StepType { get; init; }
    public required Type ControlType { get; init; }
    public required Func<object> Factory { get; init; }
}
