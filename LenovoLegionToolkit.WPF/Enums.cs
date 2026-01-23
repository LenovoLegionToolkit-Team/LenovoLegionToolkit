using System.ComponentModel.DataAnnotations;
using LenovoLegionToolkit.WPF.Resources;

namespace LenovoLegionToolkit.WPF;

public enum DashboardGroupType
{
    Power,
    Graphics,
    Display,
    Other,
    Custom
}

public enum DashboardItem
{
    PowerMode,
    BatteryMode,
    BatteryNightChargeMode,
    AlwaysOnUsb,
    InstantBoot,
    HybridMode,
    DiscreteGpu,
    OverclockDiscreteGpu,
    PanelLogoBacklight,
    PortsBacklight,
    Resolution,
    RefreshRate,
    DpiScale,
    Hdr,
    OverDrive,
    TurnOffMonitors,
    Microphone,
    FlipToStart,
    TouchpadLock,
    FnLock,
    WinKeyLock,
    WhiteKeyboardBacklight,
    ItsMode
}

public enum SensorGroupType
{
    CPU,
    GPU,
    Motherboard,
    Battery,
    Memory,
    Disk
}

public enum SensorItem
{
    CpuUtilization,
    CpuFrequency,
    CpuFanSpeed,
    CpuTemperature,
    CpuPower,
    GpuUtilization,
    GpuFrequency,
    GpuFanSpeed,
    GpuCoreTemperature,
    GpuVramTemperature,
    GpuTemperatures,
    GpuPower,
    PchFanSpeed,
    PchTemperature,
    BatteryState,
    BatteryLevel,
    MemoryUtilization,
    MemoryTemperature,
    Disk1Temperature,
    Disk2Temperature
}

public enum SnackbarType
{
    Success,
    Warning,
    Error,
    Info
}
