using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Listeners;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

public class DisplayOffAutomationPipelineTrigger : INativeWindowsMessagePipelineTrigger, IDisallowDuplicatesAutomationPipelineTrigger
{
    [JsonIgnore]
    public string DisplayName => Resource.DisplayOffAutomationPipelineTrigger_DisplayName;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is not (NativeWindowsMessageEvent { Message: NativeWindowsMessage.MonitorOff } or StartupAutomationEvent))
            return Task.FromResult(false);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        return Task.FromResult(true);
    }

    public Task<bool> IsMatchingState()
    {
        var listener = IoCContainer.Resolve<NativeWindowsMessageListener>();
        var result = !listener.IsMonitorOn;
        return Task.FromResult(result);
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.DisplayOn = false;

    public IAutomationPipelineTrigger DeepCopy() => new DisplayOffAutomationPipelineTrigger();

    public override bool Equals(object? obj) => obj is DisplayOffAutomationPipelineTrigger;

    public override int GetHashCode() => HashCode.Combine(DisplayName);
}
