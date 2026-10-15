using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

[method: JsonConstructor]
public class PowerOperationAutomationStep(PowerOperationState state) : IAutomationStep<PowerOperationState>
{
    public PowerOperationState State { get; } = state;

    public bool IsDangerousOnStartup => true;

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        return State switch
        {
            PowerOperationState.Shutdown => Power.ShutdownAsync(),
            PowerOperationState.Restart => Power.RestartAsync(),
            PowerOperationState.Sleep => Power.SleepAsync(),
            PowerOperationState.Hibernate => Power.HibernateAsync(),
            _ => Task.CompletedTask,
        };
    }

    public Task<PowerOperationState[]> GetAllStatesAsync() => Task.FromResult(new[]
    {
        PowerOperationState.Shutdown,
        PowerOperationState.Restart,
        PowerOperationState.Sleep,
        PowerOperationState.Hibernate,
    });

    public IAutomationStep DeepCopy() => new PowerOperationAutomationStep(State);
}