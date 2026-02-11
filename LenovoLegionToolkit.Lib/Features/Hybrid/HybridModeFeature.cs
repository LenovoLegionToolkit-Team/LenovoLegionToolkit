using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Features.Hybrid.Notify;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features.Hybrid;

public class HybridModeFeature(GSyncFeature gSyncFeature, IGPUModeFeature igpuModeFeature, DGPUNotify dgpuNotify) : IFeature<HybridModeState>
{
    private readonly CancellationTokenSource _ensureDGPUEjectedIfNeededCancellationTokenSource = new();

    public async Task<bool> IsSupportedAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
        return mi.Properties.SupportsGSync || mi.Properties.SupportsIGPUMode;
    }

    public async Task<HybridModeState[]> GetAllStatesAsync()
    {
        var mi = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);

        return (mi.Properties.SupportsGSync, mi.Properties.SupportsIGPUMode) switch
        {
            (true, true) => [HybridModeState.On, HybridModeState.OnIGPUOnly, HybridModeState.OnAuto, HybridModeState.Off],
            (false, true) => [HybridModeState.On, HybridModeState.OnIGPUOnly, HybridModeState.OnAuto],
            (true, false) => [HybridModeState.On, HybridModeState.Off],
            _ => []
        };
    }

    public async Task<HybridModeState> GetStateAsync()
    {
        Log.Instance.Trace($"Getting state...");

        var gSyncSupported = await gSyncFeature.IsSupportedAsync().ConfigureAwait(false);
        var igpuModeSupported = await igpuModeFeature.IsSupportedAsync().ConfigureAwait(false);

        var gSync = GSyncState.Off;
        var igpuMode = IGPUModeState.Default;

        if (gSyncSupported)
            gSync = await gSyncFeature.GetStateAsync().ConfigureAwait(false);

        if (igpuModeSupported)
            igpuMode = await igpuModeFeature.GetStateAsync().ConfigureAwait(false);

        var state = Pack(gSync, igpuMode);

        Log.Instance.Trace($"State is {state} [gSync={gSync}, igpuMode={igpuMode}]");

        return state;
    }

    public async Task SetStateAsync(HybridModeState state)
    {
        await _ensureDGPUEjectedIfNeededCancellationTokenSource.CancelAsync().ConfigureAwait(false);

        var (gSync, igpuMode) = Unpack(state);

        Log.Instance.Trace($"Setting state to {state}... [gSync={gSync}, igpuMode={igpuMode}]");

        var gSyncSupported = await gSyncFeature.IsSupportedAsync().ConfigureAwait(false);
        var igpuModeSupported = await igpuModeFeature.IsSupportedAsync().ConfigureAwait(false);

        var gSyncChanged = false;

        if (gSyncSupported && await gSyncFeature.GetStateAsync().ConfigureAwait(false) != gSync)
        {
            await gSyncFeature.SetStateAsync(gSync).ConfigureAwait(false);
            gSyncChanged = true;
        }

        if (igpuModeSupported && await igpuModeFeature.GetStateAsync().ConfigureAwait(false) != igpuMode)
        {
            try
            {
                await igpuModeFeature.SetStateAsync(igpuMode).ConfigureAwait(false);
            }
            catch (IGPUModeChangeException)
            {
                if (!gSyncChanged)
                    throw;
            }
            finally
            {
                if (!gSyncChanged && igpuMode is IGPUModeState.Default or IGPUModeState.Auto or IGPUModeState.IGPUOnly)
                    await dgpuNotify.NotifyLaterIfNeededAsync().ConfigureAwait(false);
            }
        }

        Log.Instance.Trace($"State set to {state} [gSync={gSync}, igpuMode={igpuMode}]");
    }

    public async Task EnsureDGPUEjectedIfNeededAsync()
    {
        if (!await igpuModeFeature.IsSupportedAsync().ConfigureAwait(false) || !await dgpuNotify.IsSupportedAsync().ConfigureAwait(false))
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                const int maxRetries = 5;
                const int delay = 5 * 1000;

                var retry = 1;

                Log.Instance.Trace($"Will make sure that dGPU is ejected. [maxRetries={maxRetries}, delay={delay}ms]");

                while (retry <= maxRetries)
                {
                    await Task.Delay(delay).ConfigureAwait(false);

                    if (_ensureDGPUEjectedIfNeededCancellationTokenSource.IsCancellationRequested)
                    {
                        Log.Instance.Trace($"Cancelled, aborting...");
                        break;
                    }

                    var currentMode = await igpuModeFeature.GetStateAsync().ConfigureAwait(false);

                    if (currentMode != IGPUModeState.IGPUOnly && currentMode != IGPUModeState.Auto)
                    {
                        Log.Instance.Trace($"Not in iGPU-only or Auto mode, aborting... [mode={currentMode}]");
                        break;
                    }

                    if (currentMode == IGPUModeState.Auto)
                    {
                        if (await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false) == PowerAdapterStatus.Connected)
                        {
                            Log.Instance.Trace($"Auto mode with AC connected, no eject needed.");
                            break;
                        }
                    }

                    if (!await dgpuNotify.IsDGPUAvailableAsync().ConfigureAwait(false))
                    {
                        Log.Instance.Trace($"dGPU already unavailable, aborting...");
                        break;
                    }

                    Log.Instance.Trace($"Notifying dGPU... [retry={retry}, maxRetries={maxRetries}]");

                    await dgpuNotify.NotifyAsync(false).ConfigureAwait(false);

                    retry++;
                }
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Failed to ensure dGPU is ejected", ex);
            }
        });
    }

    private static (GSyncState, IGPUModeState) Unpack(HybridModeState state) => state switch
    {
        HybridModeState.On => (GSyncState.Off, IGPUModeState.Default),
        HybridModeState.OnIGPUOnly => (GSyncState.Off, IGPUModeState.IGPUOnly),
        HybridModeState.OnAuto => (GSyncState.Off, IGPUModeState.Auto),
        HybridModeState.Off => (GSyncState.On, IGPUModeState.Default),
        _ => throw new InvalidOperationException("Invalid state"),
    };

    private static HybridModeState Pack(GSyncState state1, IGPUModeState state2) => (state1, state2) switch
    {
        (GSyncState.Off, IGPUModeState.Default) => HybridModeState.On,
        (GSyncState.Off, IGPUModeState.IGPUOnly) => HybridModeState.OnIGPUOnly,
        (GSyncState.Off, IGPUModeState.Auto) => HybridModeState.OnAuto,
        (GSyncState.On, _) => HybridModeState.Off,
        _ => throw new InvalidOperationException("Invalid state"),
    };
}
