using System.Collections.Generic;
using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public interface ISensorsController
{
    IReadOnlyList<FanData> SystemFans { get; }
    Task<bool> IsSupportedAsync();
    Task PrepareAsync();
    Task<SensorsData> GetDataAsync();
    Task<FanSpeedTable> GetFanSpeedsAsync();
}
