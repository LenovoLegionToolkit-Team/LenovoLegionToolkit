using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using LenovoLegionToolkit.Lib.System;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

/// <summary>
/// Automation step that performs a power operation (shutdown, restart, sleep, or hibernate)
/// on the machine. Marked as dangerous on startup to prevent accidental power state changes
/// when Lenovo Legion Toolkit launches.
/// </summary>
[method: JsonConstructor]
public class PowerOperationAutomationStep(PowerOperationState state) : IAutomationStep<PowerOperationState>
{
    /// <summary>
    /// Gets the power operation that this step will execute.
    /// </summary>
    public PowerOperationState State { get; } = state;

    /// <inheritdoc />
    public bool IsDangerousOnStartup => true;

    /// <inheritdoc />
    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<PowerOperationState[]> GetAllStatesAsync() => Task.FromResult(new[]
    {
        PowerOperationState.Shutdown,
        PowerOperationState.Restart,
        PowerOperationState.Sleep,
        PowerOperationState.Hibernate,
    });

    /// <inheritdoc />
    public IAutomationStep DeepCopy() => new PowerOperationAutomationStep(State);
}