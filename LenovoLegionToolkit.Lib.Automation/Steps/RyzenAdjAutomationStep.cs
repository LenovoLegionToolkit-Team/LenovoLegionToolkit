using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

/// <summary>
/// Automation step that applies saved RyzenAdj settings (CPU undervolting, power limits, temperature limits).
/// Used for battery optimization automation - apply UV and low power limits when AC disconnected.
/// </summary>
public class RyzenAdjAutomationStep : IAutomationStep
{
    private readonly RyzenAdjController _controller = IoCContainer.Resolve<RyzenAdjController>();

    public Task<bool> IsSupportedAsync()
    {
        try
        {
            // Check if driver is available (sync method, wrap in Task)
            var driverAvailable = _controller.CheckDriverAvailable();
            return Task.FromResult(driverAvailable);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[RyzenAdjAutomationStep] Running...");

        var isSupported = await IsSupportedAsync();
        if (!isSupported)
        {
            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[RyzenAdjAutomationStep] Not supported, skipping.");
            return;
        }

        await _controller.ApplySettingsAsync();
        
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[RyzenAdjAutomationStep] Settings applied successfully.");
    }

    public IAutomationStep DeepCopy() => new RyzenAdjAutomationStep();
}

