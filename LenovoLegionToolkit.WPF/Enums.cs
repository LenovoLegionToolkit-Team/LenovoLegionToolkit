using LenovoLegionToolkit.WPF.Resources;
using System.ComponentModel.DataAnnotations;

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

public enum GradientDirection
{
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Left_to_Right))]
    LeftToRight,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Right_to_Left))]
    RightToLeft,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Top_to_Bottom))]
    TopToBottom,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Bottom_to_Top))]
    BottomToTop
}

public enum LampEffectType
{
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Static))]
    Static,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Breathe))]
    Breathe,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Wave))]
    Wave,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Rainbow))]
    Rainbow,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Meteor))]
    Meteor,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Ripple))]
    Ripple,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Sparkle))]
    Sparkle,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Gradient))]
    Gradient,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Custom))]
    Custom,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Rainbow_Wave))]
    RainbowWave,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Spiral_Rainbow))]
    SpiralRainbow,
    [Display(ResourceType = typeof(Resource), Name = nameof(Resource.LampArrayRGBKeyboardPage_Aurora_Sync))]
    AuroraSync
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

public enum SnackbarType
{
    Success,
    Warning,
    Error,
    Info
}
