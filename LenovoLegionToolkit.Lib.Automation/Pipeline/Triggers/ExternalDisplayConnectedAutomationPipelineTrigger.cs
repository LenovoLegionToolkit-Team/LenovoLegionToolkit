using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.System;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

public class ExternalDisplayConnectedAutomationPipelineTrigger : INativeWindowsMessagePipelineTrigger, IDisallowDuplicatesAutomationPipelineTrigger
{
    [JsonIgnore]
    public string DisplayName => Resource.ExternalDisplayConnectedAutomationPipelineTrigger_DisplayName;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is NativeWindowsMessageEvent { Message: NativeWindowsMessage.ExternalMonitorConnected })
            return Task.FromResult(true);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        return Task.FromResult(false);
    }

    public async Task<bool> IsMatchingState()
    {
        var displays = await ExternalDisplays.GetAsync().ConfigureAwait(false);
        return displays.Length > 0;
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.ExternalDisplayConnected = true;

    public IAutomationPipelineTrigger DeepCopy() => new ExternalDisplayConnectedAutomationPipelineTrigger();

    public override bool Equals(object? obj) => obj is ExternalDisplayConnectedAutomationPipelineTrigger;

    public override int GetHashCode() => HashCode.Combine(DisplayName);
}
