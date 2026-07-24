using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;

namespace LenovoLegionToolkit.Lib.Extensions;

internal static class SensorExtensions
{
    public static ISensor? FindByKeyword(this IReadOnlyList<ISensor> sensors, SensorType type, string[] keywords)
    {
        for (int ki = 0; ki < keywords.Length; ki++)
        {
            for (int si = 0; si < sensors.Count; si++)
            {
                var s = sensors[si];
                if (s.SensorType == type && s.Name.Contains(keywords[ki], StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }
        }

        return null;
    }

    public static ISensor? FindPowerSensor(this IReadOnlyList<ISensor> sensors, string[] keywords)
    {
        for (int ki = 0; ki < keywords.Length; ki++)
        {
            for (int si = 0; si < sensors.Count; si++)
            {
                var s = sensors[si];
                if (s.SensorType == SensorType.Power && s.Name.Contains(keywords[ki], StringComparison.OrdinalIgnoreCase) && !s.Name.Contains("Limit", StringComparison.OrdinalIgnoreCase))
                {
                    return s;
                }
            }
        }

        return null;
    }

    public static void Accum(this List<ISensor> sensors, Dictionary<SensorItem, float> values, SensorItem maxKey, SensorItem avgKey)
    {
        if (sensors.Count == 0)
        {
            return;
        }

        float max = sensors.Max(s => s.Value) ?? -1;
        float avg = sensors.Average(s => s.Value) ?? -1;

        values[maxKey] = max > 0 ? (float)Math.Round(max) : max;
        values[avgKey] = avg > 0 ? (float)Math.Round(avg) : avg;
    }
}

