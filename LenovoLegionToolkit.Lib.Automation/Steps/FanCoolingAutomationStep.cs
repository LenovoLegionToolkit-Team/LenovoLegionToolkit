using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

public class FanCoolingAutomationStep : IAutomationStep
{
    private readonly FanCoolingController _fanCoolingController = IoCContainer.Resolve<FanCoolingController>();

    public bool IsDangerousOnStartup => true;

    public Task<bool> IsSupportedAsync() => _fanCoolingController.IsSupportedAsync();

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token) => _fanCoolingController.RunAsync(token);

    public IAutomationStep DeepCopy() => new FanCoolingAutomationStep();
}
