using System.Threading.Tasks;

namespace LenovoLegionToolkit.Lib.Controllers.Sensors;

public interface ISensorsController
{
    bool HasPchFan { get; }
    Task<bool> IsSupportedAsync();
    Task PrepareAsync();
    Task<SensorsData> GetDataAsync();
    Task<FanSpeedTable> GetFanSpeedsAsync();
}
