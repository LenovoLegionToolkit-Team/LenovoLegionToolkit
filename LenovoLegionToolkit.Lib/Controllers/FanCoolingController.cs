using System;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.System.Management;

namespace LenovoLegionToolkit.Lib.Controllers;

public class FanCoolingController
{
    public static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(10);

    private Task<bool>? _isSupportedTask;

    public Task<bool> IsSupportedAsync() => _isSupportedTask ??= IsSupportedInternalAsync();

    private static async Task<bool> IsSupportedInternalAsync() => await WMI.LenovoGameZoneData.IsSupportFanCoolingAsync().ConfigureAwait(false) > 0;

    public Task StartAsync() => WMI.LenovoGameZoneData.SetFanCoolingAsync(1);

    public Task StopAsync() => WMI.LenovoGameZoneData.SetFanCoolingAsync(0);

    public Task RunAsync(CancellationToken token = default) => RunAsync(DefaultDuration, token);

    public async Task RunAsync(TimeSpan duration, CancellationToken token = default)
    {
        await StartAsync().ConfigureAwait(false);

        try
        {
            if (duration > TimeSpan.Zero)
                await Task.Delay(duration, token).ConfigureAwait(false);
        }
        finally
        {
            await StopAsync().ConfigureAwait(false);
        }
    }
}
