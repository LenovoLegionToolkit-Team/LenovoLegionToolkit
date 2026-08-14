using System;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers.Sensors;
using LenovoLegionToolkit.Lib.Messaging;
using LenovoLegionToolkit.Lib.Messaging.Messages;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Features;

public class HardwareSensorsFeature(ApplicationSettings settings, OsdSettings osdSettings, SensorsGroupController sensorsGroupController) : IFeature<HardwareSensorsState>
{
    public Task<bool> IsSupportedAsync() => Task.FromResult(PawnIOHelper.GetPawnIOState() == PawnIOState.Installed);

    public Task<HardwareSensorsState[]> GetAllStatesAsync() => Task.FromResult(Enum.GetValues<HardwareSensorsState>());

    public Task<HardwareSensorsState> GetStateAsync()
    {
        var state = settings.Store.EnableHardwareSensors
            ? HardwareSensorsState.On
            : HardwareSensorsState.Off;
        return Task.FromResult(state);
    }

    public async Task SetStateAsync(HardwareSensorsState state)
    {
        if (state == HardwareSensorsState.On && !sensorsGroupController.IsLibreHardwareMonitorInitialized())
            await sensorsGroupController.IsSupportedAsync().ConfigureAwait(false);

        if (state == HardwareSensorsState.Off)
        {
            settings.Store.UseNewSensorDashboard = false;
            osdSettings.Store.ShowOsd = false;
            osdSettings.SynchronizeStore();
            MessagingCenter.Publish(new OsdChangedMessage(ToggleState.Off));
        }

        settings.Store.EnableHardwareSensors = state == HardwareSensorsState.On;
        settings.SynchronizeStore();

        MessagingCenter.Publish(new SensorDashboardSwappedMessage());
    }
}
