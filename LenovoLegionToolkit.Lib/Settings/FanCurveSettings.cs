using System.Collections.Generic;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.Lib.Settings;

public class FanCurveSettingsStore
{
    public List<FanCurveEntry> Entries { get; set; } = new();
}

public class FanCurveSettings : AbstractSettings<FanCurveSettingsStore>
{
    public FanCurveSettings() : base("fan_curves.json") { }
}
