using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Controllers.GodMode;

public static class FanTableTypeResolver
{
    public static FanTableType Resolve(byte fanId, byte sensorId, int smartFanVersion, MachineInformation mi)
    {
        if (smartFanVersion <= 7 || IsV3StyleModel(mi))
        {
            return (fanId, sensorId) switch
            {
                (1, 4) => FanTableType.CPU,
                (1, 1) => FanTableType.CPUSensor,
                (2, 5) => FanTableType.GPU,
                (3, 5) => FanTableType.GPU2,
                _ => FanTableType.Unknown,
            };
        }

        return (fanId, sensorId) switch
        {
            (1, 1) or (1, 4) => FanTableType.CPU,
            (2, 5) => FanTableType.GPU,
            (4, 4) or (5, 5) or (4, 1) => FanTableType.SystemFan,
            _ => FanTableType.Unknown,
        };
    }

    private static bool IsV3StyleModel(MachineInformation mi)
    {
        var affectedSeries = new[] { LegionSeries.Legion_5, LegionSeries.Legion_7 };
        var affectedModels = new[] { "Legion 5", "Legion 7", "Legion Pro 5 16IAX10H", "LOQ", "Y7000", "R7000" };
        var isAffectedSeries = affectedSeries.Any(s => Compatibility.GetLegionSeries(mi.Model, mi.MachineType) == s);
        var isAffectedModel = affectedModels.Any(m => mi.Model.Contains(m));
        return (isAffectedSeries || isAffectedModel) && mi.Generation >= 10;
    }
}
