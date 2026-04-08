using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers;

public class FanCoolingController
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(10);

    private readonly Lazy<Task<bool>> _isSupportedTask = new(IsSupportedInternalAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    private readonly SemaphoreSlim _runLock = new(1, 1);

    public Task<bool> IsSupportedAsync() => _isSupportedTask.Value;

    private static async Task<bool> IsSupportedInternalAsync() => await WMI.LenovoGameZoneData.IsSupportFanCoolingAsync().ConfigureAwait(false) > 0;

    public async Task StartAsync()
    {
        Log.Instance.Trace($"Starting fan cleaning mode...");
        await WMI.LenovoGameZoneData.SetFanCoolingAsync(1).ConfigureAwait(false);
        Log.Instance.Trace($"Fan cleaning mode started.");
    }

    public async Task StopAsync()
    {
        Log.Instance.Trace($"Stopping fan cleaning mode...");
        await WMI.LenovoGameZoneData.SetFanCoolingAsync(0).ConfigureAwait(false);
        Log.Instance.Trace($"Fan cleaning mode stopped.");
    }

    public Task RunAsync(CancellationToken token = default) => RunAsync(DefaultDuration, token);

    public async Task RunAsync(TimeSpan duration, CancellationToken token = default)
    {
        await _runLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            Log.Instance.Trace($"Running fan cleaning mode. [duration={duration}]");
            await StartAsync().ConfigureAwait(false);

            try
            {
                if (duration > TimeSpan.Zero)
                {
                    Log.Instance.Trace($"Fan cleaning mode will stop automatically after {duration}.");
                    await Task.Delay(duration, token).ConfigureAwait(false);
                }
            }
            finally
            {
                await StopAsync().ConfigureAwait(false);
                Log.Instance.Trace($"Fan cleaning mode run finished.");
            }
        }
        finally
        {
            _runLock.Release();
        }
    }
}
