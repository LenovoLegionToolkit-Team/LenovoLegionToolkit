using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Utils;
using Newtonsoft.Json;

namespace LenovoLegionToolkit.Lib.Automation.Steps;

/// <summary>
/// Automation step that toggles CPU Turbo Boost mode via Windows Power settings.
/// Uses powercfg to set PERFBOOSTMODE in current power scheme.
/// </summary>
public class TurboBoostAutomationStep : IAutomationStep<TurboBoostState>
{
    public TurboBoostState State { get; }

    [JsonConstructor]
    public TurboBoostAutomationStep(TurboBoostState state)
    {
        State = state;
    }

    public Task<bool> IsSupportedAsync() => Task.FromResult(true);

    public Task<TurboBoostState[]> GetAllStatesAsync() => 
        Task.FromResult(Enum.GetValues<TurboBoostState>());

    public async Task RunAsync(AutomationContext context, AutomationEnvironment environment, CancellationToken token)
    {
        if (Log.Instance.IsTraceEnabled)
            Log.Instance.Trace($"[TurboBoostAutomationStep] Setting Turbo Boost to {State}...");

        try
        {
            var boostMode = State switch
            {
                TurboBoostState.Disabled => "0",
                TurboBoostState.Enabled => "2",      // Aggressive boost
                TurboBoostState.Efficient => "4",    // Efficient Aggressive
                _ => "2"
            };

            // Set for both AC and DC
            await RunPowerCfgAsync($"/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {boostMode}");
            await RunPowerCfgAsync($"/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR PERFBOOSTMODE {boostMode}");
            await RunPowerCfgAsync("/setactive SCHEME_CURRENT");

            if (Log.Instance.IsTraceEnabled)
                Log.Instance.Trace($"[TurboBoostAutomationStep] Turbo Boost set to {State} successfully.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"[TurboBoostAutomationStep] Failed to set Turbo Boost.", ex);
        }
    }

    private static Task RunPowerCfgAsync(string arguments)
    {
        return Task.Run(() =>
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        });
    }

    public IAutomationStep DeepCopy() => new TurboBoostAutomationStep(State);
}

/// <summary>
/// Turbo Boost state options.
/// </summary>
public enum TurboBoostState
{
    /// <summary>Turbo Boost disabled - CPU runs at base frequency only.</summary>
    Disabled,
    
    /// <summary>Turbo Boost enabled - Aggressive boosting.</summary>
    Enabled,
    
    /// <summary>Efficient mode - Balanced between performance and power.</summary>
    Efficient
}
