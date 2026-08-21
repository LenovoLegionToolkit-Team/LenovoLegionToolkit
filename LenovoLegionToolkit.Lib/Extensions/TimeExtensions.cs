using System;

namespace LenovoLegionToolkit.Lib.Extensions;

public static class TimeExtensions
{
    public static Time UtcNow
    {
        get
        {
            var utcNow = DateTime.UtcNow;
            return new(utcNow.Hour, utcNow.Minute, utcNow.Second);
        }
    }

    public static bool IsBetween(this Time current, Time start, Time end)
    {
        if (start <= end)
            return current >= start && current <= end;

        return current >= start || current <= end;
    }
}
