using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Features;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

[method: JsonConstructor]
public class ITSModeAutomationPipelineTrigger(ITSMode itsModeState) : IITSModeAutomationPipelineTrigger
{
    public string DisplayName => Resource.ITSModeAutomationPipelineTrigger_DisplayName;

    public ITSMode ITSModeState { get; } = itsModeState;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is not (ITSModeAutomationEvent or StartupAutomationEvent))
            return Task.FromResult(false);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        var e = (ITSModeAutomationEvent)automationEvent;
        var result = e.ITSModeState == ITSModeState;
        return Task.FromResult(result);
    }

    public async Task<bool> IsMatchingState()
    {
        var feature = IoCContainer.Resolve<ITSModeFeature>();
        return await feature.GetStateAsync().ConfigureAwait(false) == ITSModeState;
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.ITSMode = ITSModeState;

    public IAutomationPipelineTrigger DeepCopy() => new ITSModeAutomationPipelineTrigger(ITSModeState);

    public IITSModeAutomationPipelineTrigger DeepCopy(ITSMode powerModeState) => new ITSModeAutomationPipelineTrigger(powerModeState);

    public override bool Equals(object? obj)
    {
        return obj is ITSModeAutomationPipelineTrigger t && ITSModeState == t.ITSModeState;
    }

    public override int GetHashCode() => HashCode.Combine(ITSModeState);

    public override string ToString() => $"{nameof(ITSModeState)}: {ITSModeState}";
}
