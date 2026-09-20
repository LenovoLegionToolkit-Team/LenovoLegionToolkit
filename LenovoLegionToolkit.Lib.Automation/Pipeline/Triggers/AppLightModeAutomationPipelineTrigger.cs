using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.System;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

public class AppLightModeAutomationPipelineTrigger : ISystemThemePipelineTrigger
{
    [JsonIgnore]
    public string DisplayName => Resource.ResourceManager.GetString("AppLightModeAutomationPipelineTrigger_DisplayName")!;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is SystemThemeAutomationEvent { IsDarkMode: false })
            return Task.FromResult(true);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        return Task.FromResult(false);
    }

    public Task<bool> IsMatchingState() => Task.FromResult(!SystemTheme.IsDarkMode());

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.AppDarkMode = false;

    public IAutomationPipelineTrigger DeepCopy() => new AppLightModeAutomationPipelineTrigger();

    public override bool Equals(object? obj) => obj is AppLightModeAutomationPipelineTrigger;

    public override int GetHashCode() => HashCode.Combine(DisplayName);
}
