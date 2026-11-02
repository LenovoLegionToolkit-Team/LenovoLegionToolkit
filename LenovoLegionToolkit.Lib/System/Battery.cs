using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using Windows.Win32;
using Windows.Win32.System.Power;
using LenovoLegionToolkit.Lib.Extensions;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.Utils;
using Microsoft.Win32.SafeHandles;

namespace LenovoLegionToolkit.Lib.System;

public static class Battery
{
    private static readonly ApplicationSettings Settings = IoCContainer.Resolve<ApplicationSettings>();
    private static int MinDischargeRate { get; set; } = int.MaxValue;
    private static int MaxDischargeRate { get; set; }

    public static bool IsBatteryPresent => Devices.TryGetBattery(out _);

    public static void SetMinMaxDischargeRate()
    {
        if (!TryGetBatteryHandleAndTag(out var handle, out var batteryTag))
        {
            return;
        }

        if (!TryGetBatteryStatus(handle, batteryTag, out var status))
        {
            return;
        }

        UpdateMinMaxDischargeRate(status);
    }

    public static bool TryGetBatteryInformation(out BatteryInformation information)
    {
        information = default;

        if (!TryGetBatteryHandleAndTag(out var handle, out var batteryTag))
        {
            return false;
        }

        if (!TryGetBatteryInfo(handle, batteryTag, out var batteryInfo))
        {
            return false;
        }

        if (!TryGetBatteryStatus(handle, batteryTag, out var status))
        {
            return false;
        }

        UpdateMinMaxDischargeRate(status);

        var powerStatus = GetSystemPowerStatus();

        double? temperatureC = null;
        DateTime? manufactureDate = null;
        DateTime? firstUseDate = null;

        try
        {
            var lenovoBatteryInformation = FindLenovoBatteryInformation();
            if (lenovoBatteryInformation.HasValue)
            {
                temperatureC = DecodeTemperatureC(lenovoBatteryInformation.Value.Temperature);
                manufactureDate = DecodeDateTime(lenovoBatteryInformation.Value.ManufactureDate);
                firstUseDate = DecodeDateTime(lenovoBatteryInformation.Value.FirstUseDate);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get temperature of battery.", ex);
        }

        information = new(powerStatus.ACLineStatus == 1,
            powerStatus.BatteryLifePercent,
            (int)powerStatus.BatteryLifeTime,
            (int)powerStatus.BatteryFullLifeTime,
            status.Rate,
            (status.Rate == 0 || status.Rate == int.MinValue) ? 0 : MinDischargeRate,
            MaxDischargeRate,
            (int)status.Capacity,
            (int)batteryInfo.DesignedCapacity,
            (int)batteryInfo.FullChargedCapacity,
            (int)batteryInfo.CycleCount,
            powerStatus.ACLineStatus == 0 && batteryInfo.DefaultAlert2 >= status.Capacity,
            temperatureC,
            manufactureDate,
            firstUseDate);

        return true;
    }

    public static BatteryInformation GetBatteryInformation()
    {
        if (TryGetBatteryInformation(out var information))
        {
            return information;
        }

        throw new InvalidOperationException("Battery is not present.");
    }

    public static double? GetBatteryTemperatureC()
    {
        if (!IsBatteryPresent)
        {
            return null;
        }

        try
        {
            var lenovoBatteryInformation = FindLenovoBatteryInformation();
            return lenovoBatteryInformation.HasValue ? DecodeTemperatureC(lenovoBatteryInformation.Value.Temperature) : null;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get temperature of battery.", ex);
            return null;
        }
    }

    public static DateTime? GetOnBatterySince()
    {
        try
        {
            var resetOnReboot = Settings.Store.ResetBatteryOnSinceTimerOnReboot;

            var lastRebootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount);

            var logs = new List<(DateTime Date, bool IsACOnline)>();

            var query = new EventLogQuery("System", PathType.LogName, "*[System[EventID=105]]");
            using var logReader = new EventLogReader(query);
            using var propertySelector = new EventLogPropertySelector(["Event/EventData/Data[@Name='AcOnline']"]);

            while (logReader.ReadEvent() is EventLogRecord record)
            {
                var date = record.TimeCreated;
                var isAcOnline = record.GetPropertyValues(propertySelector)[0] as bool?;

                if (date is null || isAcOnline is null)
                    continue;

                if (resetOnReboot && date < lastRebootTime)
                    continue;

                logs.Add((date.Value, isAcOnline.Value));
            }

            if (logs.Count < 1)
                return null;

            logs.Reverse();

            var (dateTime, _) = logs
                .TakeWhile(log => log.IsACOnline != true)
                .LastOrDefault();

            return dateTime;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to get event.", ex);
        }

        return null;
    }

    private static void UpdateMinMaxDischargeRate(BATTERY_STATUS status)
    {
        if (status.Rate == int.MinValue)
        {
            return;
        }

        if (status.Rate == 0
            || (status.Rate > 0 && (MinDischargeRate < 0 || MaxDischargeRate < 0))
            || (status.Rate < 0 && (MinDischargeRate > 0 || MaxDischargeRate > 0)))
        {
            MinDischargeRate = int.MaxValue;
            MaxDischargeRate = 0;
        }

        if (status.Rate != 0)
        {
            if (MaxDischargeRate == 0 || Math.Abs(status.Rate) < Math.Abs(MinDischargeRate))
            {
                MinDischargeRate = status.Rate;
            }

            if (Math.Abs(status.Rate) > Math.Abs(MaxDischargeRate))
            {
                MaxDischargeRate = status.Rate;
            }
        }
    }

    private static SYSTEM_POWER_STATUS GetSystemPowerStatus()
    {
        var result = PInvoke.GetSystemPowerStatus(out var sps);

        if (!result)
            PInvokeExtensions.ThrowIfWin32Error("GetSystemPowerStatus");

        return sps;
    }

    private static bool TryGetBatteryHandleAndTag([NotNullWhen(true)] out SafeFileHandle? handle, out uint batteryTag)
    {
        handle = null;
        batteryTag = 0;

        if (!Devices.TryGetBattery(out handle) || handle is null)
        {
            return false;
        }

        if (TryGetBatteryTag(handle, out batteryTag))
        {
            return true;
        }

        Devices.InvalidateBattery();
        handle = null;
        batteryTag = 0;

        return false;
    }

    private static bool TryGetBatteryTag(SafeFileHandle handle, out uint tag)
    {
        if (PInvokeExtensions.TryDeviceIoControl(handle, PInvoke.IOCTL_BATTERY_QUERY_TAG, 0u, out tag))
        {
            return tag != 0;
        }

        tag = 0;
        return false;
    }

    private static bool TryGetBatteryInfo(SafeFileHandle handle, uint batteryTag, out BATTERY_INFORMATION batteryInformation)
    {
        var queryInformation = new BATTERY_QUERY_INFORMATION
        {
            BatteryTag = batteryTag,
            InformationLevel = BATTERY_QUERY_INFORMATION_LEVEL.BatteryInformation,
        };

        return PInvokeExtensions.TryDeviceIoControl(handle,
            PInvoke.IOCTL_BATTERY_QUERY_INFORMATION,
            queryInformation,
            out batteryInformation);
    }

    private static bool TryGetBatteryStatus(SafeFileHandle handle, uint batteryTag, out BATTERY_STATUS status)
    {
        var waitStatus = new BATTERY_WAIT_STATUS
        {
            BatteryTag = batteryTag,
        };

        return PInvokeExtensions.TryDeviceIoControl(handle,
            PInvoke.IOCTL_BATTERY_QUERY_STATUS,
            waitStatus,
            out status);
    }

    private static LENOVO_BATTERY_INFORMATION? FindLenovoBatteryInformation()
    {
        for (uint index = 0; index < 3; index++)
        {
            var info = GetLenovoBatteryInformation(index);
            if (info.Temperature is ushort.MinValue or ushort.MaxValue)
                continue;

            return info;
        }

        Log.Instance.Trace($"Battery data not found.");

        return null;
    }

    private static LENOVO_BATTERY_INFORMATION GetLenovoBatteryInformation(uint index)
    {
        var result = PInvokeExtensions.DeviceIoControl(Drivers.GetEnergy(),
            Drivers.IOCTL_ENERGY_BATTERY_INFORMATION,
            index,
            out LENOVO_BATTERY_INFORMATION bi);
        if (!result)
            PInvokeExtensions.ThrowIfWin32Error("DeviceIoControl, 0x83102138");

        return bi;
    }

    private static DateTime? DecodeDateTime(ushort s)
    {
        try
        {
            if (s < 1)
                return null;

            var date = new DateTime((s >> 9) + 1980, (s >> 5) & 15, (s & 31), 0, 0, 0, DateTimeKind.Unspecified);
            if (date.Year is < 2018 or > 2030)
                return null;
            return date;
        }
        catch
        {
            return null;
        }
    }

    private static double? DecodeTemperatureC(ushort s)
    {
        var value = (s - 2731.6) / 10.0;
        if (value < 0)
            return null;
        return value;
    }
}
