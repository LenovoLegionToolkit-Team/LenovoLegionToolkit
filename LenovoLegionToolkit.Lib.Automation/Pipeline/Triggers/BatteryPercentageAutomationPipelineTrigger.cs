using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.System;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

[method: JsonConstructor]
public class BatteryPercentageAutomationPipelineTrigger(int percentage, bool isBelow) : IBatteryPercentageAutomationPipelineTrigger
{
    [JsonIgnore]
    public string DisplayName => Resource.BatteryPercentageAutomationPipelineTrigger_DisplayName;

    public int Percentage { get; } = percentage;

    public bool IsBelow { get; } = isBelow;

    public BatteryPercentageAutomationPipelineTrigger() : this(50, true) { }

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is not (PowerStateAutomationEvent { PowerStateEvent: PowerStateEvent.StatusChange } or BatteryPercentageAutomationEvent or StartupAutomationEvent))
            return Task.FromResult(false);

        return IsMatchingState();
    }

    public Task<bool> IsMatchingState()
    {
        var percentage = Battery.GetBatteryInformation().BatteryPercentage;

        var matches = IsBelow ? percentage < Percentage : percentage > Percentage;

        return Task.FromResult(matches);
    }

    public void UpdateEnvironment(AutomationEnvironment environment) { /* Ignored */ }

    public IAutomationPipelineTrigger DeepCopy() => new BatteryPercentageAutomationPipelineTrigger(Percentage, IsBelow);

    public IBatteryPercentageAutomationPipelineTrigger DeepCopy(int percentage, bool isBelow) => new BatteryPercentageAutomationPipelineTrigger(percentage, isBelow);

    public override bool Equals(object? obj) => obj is BatteryPercentageAutomationPipelineTrigger t && Percentage == t.Percentage && IsBelow == t.IsBelow;

    public override int GetHashCode() => HashCode.Combine(Percentage, IsBelow);

    public override string ToString() => $"{nameof(Percentage)}: {Percentage}, {nameof(IsBelow)}: {IsBelow}";
}
