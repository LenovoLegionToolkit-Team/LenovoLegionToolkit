using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.Listeners;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

public class SessionUnlockAutomationPipelineTrigger : ISessionUnlockPipelineTrigger
{
    [JsonIgnore]
    public string DisplayName => Resource.SessionUnlockAutomationPipelineTrigger_DisplayName;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is SessionLockUnlockAutomationEvent { Locked: false })
            return Task.FromResult(true);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        return Task.FromResult(false);
    }

    public Task<bool> IsMatchingState()
    {
        var listener = IoCContainer.Resolve<SessionLockUnlockListener>();
        var result = listener.IsLocked;
        return Task.FromResult(result.HasValue && !result.Value);
    }

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.SessionLocked = false;

    public IAutomationPipelineTrigger DeepCopy() => new SessionUnlockAutomationPipelineTrigger();

    public override bool Equals(object? obj) => obj is SessionUnlockAutomationPipelineTrigger;

    public override int GetHashCode() => HashCode.Combine(DisplayName);
}
