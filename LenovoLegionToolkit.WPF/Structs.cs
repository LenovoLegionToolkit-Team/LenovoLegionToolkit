using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Utils;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class AmdCcdGroup : INotifyPropertyChanged
{
    public string HeaderTitle { get; set; } = string.Empty;
    public string HeaderSubtitle { get; set; } = string.Empty;
    public ObservableCollection<AmdCoreItem> Cores { get; set; } = new();

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class AmdCoreItem : INotifyPropertyChanged
{
    public int Index { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    private double? _offsetValue;
    public double? OffsetValue
    {
        get => _offsetValue;
        set
        {
            if (_offsetValue != value)
            {
                _offsetValue = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isActive = true;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnPropertyChanged();
            }
        }
    }

    private bool _isEnabled = true;
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled != value)
            {
                _isEnabled = value;
                OnPropertyChanged();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public readonly struct DashboardGroup(DashboardGroupType type, string? customName, params DashboardItem[] items)
{
    public static DashboardGroup[] DefaultGroups => GetDefaultGroups();

    private static DashboardGroup[] GetDefaultGroups()
    {
        var mi = Compatibility.GetMachineInformationAsync().Result;
        var groups = new List<DashboardGroup>
        {
            new(DashboardGroupType.Power, null,
                DashboardItem.PowerMode,
                DashboardItem.BatteryMode,
                DashboardItem.BatteryNightChargeMode,
                DashboardItem.AlwaysOnUsb,
                DashboardItem.InstantBoot,
                DashboardItem.FlipToStart),
            new(DashboardGroupType.Graphics, null,
                DashboardItem.HybridMode,
                DashboardItem.DiscreteGpu,
                DashboardItem.OverclockDiscreteGpu),
            new(DashboardGroupType.Display, null,
                DashboardItem.Resolution,
                DashboardItem.RefreshRate,
                DashboardItem.DpiScale,
                DashboardItem.Hdr,
                DashboardItem.OverDrive,
                DashboardItem.TurnOffMonitors),
            new(DashboardGroupType.Other, null,
                DashboardItem.Microphone,
                DashboardItem.WhiteKeyboardBacklight,
                DashboardItem.PanelLogoBacklight,
                DashboardItem.PortsBacklight,
                DashboardItem.TouchpadLock,
                DashboardItem.FnLock,
                DashboardItem.WinKeyLock)
        };

        if (mi.LegionSeries is not (LegionSeries.ThinkBook or LegionSeries.IdeaPad))
        {
            return groups.ToArray();
        }

        var powerGroup = groups.First(g => g.Type == DashboardGroupType.Power);
        var items = powerGroup.Items.ToList();
        items.Insert(0, DashboardItem.ItsMode);
        groups[0] = new(DashboardGroupType.Power, null, items.ToArray());

        return groups.ToArray();
    }

    public DashboardGroupType Type { get; } = type;

    public string? CustomName { get; } = type == DashboardGroupType.Custom ? customName : null;

    public DashboardItem[] Items { get; } = items;

    public string GetName() => Type switch
    {
        DashboardGroupType.Power => Resource.DashboardPage_Power_Title,
        DashboardGroupType.Graphics => Resource.DashboardPage_Graphics_Title,
        DashboardGroupType.Display => Resource.DashboardPage_Display_Title,
        DashboardGroupType.Other => Resource.DashboardPage_Other_Title,
        DashboardGroupType.Custom => CustomName ?? string.Empty,
        _ => throw new InvalidOperationException($"Invalid type {Type}"),
    };

    public override string ToString() =>
        $"{nameof(Type)}: {Type}," +
        $" {nameof(CustomName)}: {CustomName}," +
        $" {nameof(Items)}: {string.Join(",", Items)}";
}

public readonly struct SensorGroup(SensorGroupType type, params SensorItem[] items)
{
    public static readonly SensorGroup[] DefaultGroups =
    [
        new(SensorGroupType.CPU,
            SensorItem.CpuUtilization,
            SensorItem.CpuFrequency,
            SensorItem.CpuFanSpeed,
            SensorItem.CpuTemperature,
            SensorItem.CpuPower),
        new(SensorGroupType.GPU,
            SensorItem.GpuUtilization,
            SensorItem.GpuFrequency,
            SensorItem.GpuFanSpeed,
            SensorItem.GpuCoreTemperature,
            SensorItem.GpuVramTemperature,
            SensorItem.GpuTemperatures,
            SensorItem.GpuPower),
        new(SensorGroupType.Motherboard,
            SensorItem.PchFanSpeed,
            SensorItem.PchTemperature),
        new(SensorGroupType.Battery,
            SensorItem.BatteryState,
            SensorItem.BatteryLevel),
        new(SensorGroupType.Memory,
            SensorItem.MemoryUtilization,
            SensorItem.MemoryTemperature),
        new(SensorGroupType.Disk,
            SensorItem.Disk1Temperature,
            SensorItem.Disk2Temperature)
    ];

    public SensorGroupType Type { get; } = type;

    public SensorItem[] Items { get; } = items;

    public string GetName() => Type switch
    {
        SensorGroupType.CPU => "CPU",
        SensorGroupType.GPU => "GPU",
        SensorGroupType.Motherboard => "Motherboard",
        SensorGroupType.Battery => "Battery",
        SensorGroupType.Memory => "Memory",
        SensorGroupType.Disk => "Disk",
        _ => throw new InvalidOperationException($"Invalid type {Type}")
    };

    public override string ToString() =>
        $"{nameof(Type)}: {Type}, {nameof(Items)}: [{string.Join(", ", Items)}]";
}

public readonly struct FpsDisplayData
{
    public string? FpsText { get; init; }
    public Brush? FpsBrush { get; init; }
    public string? LowFpsText { get; init; }
    public Brush? LowFpsBrush { get; init; }

    public string? FrameTimeText { get; init; }
    public Brush? FrameTimeBrush { get; init; }
}

public readonly struct SensorSnapshot
{
    public double CpuUsage { get; init; }
    public double CpuFrequency { get; init; }
    public double CpuPClock { get; init; }
    public double CpuEClock { get; init; }
    public double CpuTemp { get; init; }
    public double CpuPower { get; init; }
    public int CpuFanSpeed { get; init; }
    public double GpuUsage { get; init; }
    public double GpuFrequency { get; init; }
    public double GpuTemp { get; init; }
    public double GpuVramTemp { get; init; }
    public double GpuPower { get; init; }
    public int GpuFanSpeed { get; init; }
    public double MemUsage { get; init; }
    public double MemTemp { get; init; }
    public double PchTemp { get; init; }
    public int PchFanSpeed { get; init; }
    public double Disk1Temp { get; init; }
    public double Disk2Temp { get; init; }
}

public readonly struct ScreenInfo(Rect workArea, uint dpiX, uint dpiY, bool isPrimary)
{
    public Rect WorkArea { get; } = workArea;
    public uint DpiX { get; } = dpiX;
    public uint DpiY { get; } = dpiY;
    public bool IsPrimary { get; } = isPrimary;
}
