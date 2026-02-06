using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

public class WiFiDisconnectedAutomationPipelineTrigger : IWiFiDisconnectedPipelineTrigger
{
    public string DisplayName => Resource.WiFiDisconnectedAutomationPipelineTrigger_DisplayName;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is not (WiFiAutomationEvent { IsConnected: false } or StartupAutomationEvent))
            return Task.FromResult(false);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        return Task.FromResult(true);
    }

    public Task<bool> IsMatchingState()
    {
        var ssid = WiFi.GetConnectedNetworkSsid();
        return Task.FromResult(ssid is null);
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.WiFiConnected = false;

    public IAutomationPipelineTrigger DeepCopy() => new WiFiDisconnectedAutomationPipelineTrigger();

    public override bool Equals(object? obj) => obj is WiFiDisconnectedAutomationPipelineTrigger;

    public override int GetHashCode() => HashCode.Combine(DisplayName);
}
