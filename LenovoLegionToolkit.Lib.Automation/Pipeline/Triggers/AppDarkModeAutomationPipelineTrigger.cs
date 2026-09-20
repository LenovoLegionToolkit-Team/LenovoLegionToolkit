using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Automation.Resources;
using LenovoLegionToolkit.Lib.System;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;

public class AppDarkModeAutomationPipelineTrigger : ISystemThemePipelineTrigger
{
    [JsonIgnore]
    public string DisplayName => Resource.ResourceManager.GetString("AppDarkModeAutomationPipelineTrigger_DisplayName")!;

    public Task<bool> IsMatchingEvent(IAutomationEvent automationEvent)
    {
        if (automationEvent is SystemThemeAutomationEvent { IsDarkMode: true })
            return Task.FromResult(true);

        if (automationEvent is StartupAutomationEvent)
            return IsMatchingState();

        return Task.FromResult(false);
    }

    public Task<bool> IsMatchingState() => Task.FromResult(SystemTheme.IsDarkMode());

    public void UpdateEnvironment(AutomationEnvironment environment) => environment.AppDarkMode = true;

    public IAutomationPipelineTrigger DeepCopy() => new AppDarkModeAutomationPipelineTrigger();

    public override bool Equals(object? obj) => obj is AppDarkModeAutomationPipelineTrigger;

    public override int GetHashCode() => HashCode.Combine(DisplayName);
}
