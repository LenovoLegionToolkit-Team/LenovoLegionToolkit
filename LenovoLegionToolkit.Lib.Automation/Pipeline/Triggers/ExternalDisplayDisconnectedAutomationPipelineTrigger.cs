using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.System;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

public class ExternalDisplayDisconnectedAutomationPipelineTrigger : INativeWindowsMessagePipelineTrigger, IDisallowDuplicatesAutomationPipelineTrigger
{
    [JsonIgnore] public string DisplayName => Resource.ExternalDisplayDisconnectedAutomationPipelineTrigger_DisplayName;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is not (NativeWindowsMessageEvent { Message: NativeWindowsMessage.ExternalMonitorDisconnected } or StartupAutomationEvent))
            return Task.FromResult(false);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        return Task.FromResult(true);
    }

    public async Task<bool> IsMatchingState()
    {
        var displays = await ExternalDisplays.GetAsync().ConfigureAwait(false);
        return displays.Length < 1;
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.ExternalDisplayConnected = false;

    public IAutomationPipelineTrigger DeepCopy() => new ExternalDisplayDisconnectedAutomationPipelineTrigger();

    public override bool Equals(object? obj) => obj is ExternalDisplayDisconnectedAutomationPipelineTrigger;

    public override int GetHashCode() => HashCode.Combine(DisplayName);
}
