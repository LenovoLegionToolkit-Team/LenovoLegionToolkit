using System;
using System.Linq;
using LenovoLegionToolkit.Lib;
using LenovoLegionToolkit.Lib.Automation.Pipeline.Triggers;
using LenovoLegionToolkit.Lib.Station.Services;
using Wpf.Ui.Common;

namespace LenovoLegionToolkit.WPF.Extensions;

public static class AutomationPipelineTriggerExtensions
{
    public static SymbolRegular Icon(this IAutomationPipelineTrigger trigger) => trigger switch
    {
        IHybridModeAutomationPipelineTrigger => SymbolRegular.LeafOne24,
        IITSModeAutomationPipelineTrigger => SymbolRegular.Gauge24,
        IBatteryPercentageAutomationPipelineTrigger => SymbolRegular.Battery1024,
        IPowerStateAutomationPipelineTrigger => SymbolRegular.BatteryCharge24,
        IPowerModeAutomationPipelineTrigger => SymbolRegular.Gauge24,
        IGodModePresetChangedAutomationPipelineTrigger => SymbolRegular.Gauge24,
        IGameAutomationPipelineTrigger => SymbolRegular.XboxController24,
        IHDRPipelineTrigger => SymbolRegular.Hdr24,
        IProcessesAutomationPipelineTrigger => SymbolRegular.WindowConsole20,
        IUserInactivityPipelineTrigger => SymbolRegular.ClockAlarm24,
        ISessionLockPipelineTrigger => SymbolRegular.LockClosed24,
        ISessionUnlockPipelineTrigger => SymbolRegular.LockOpen24,
        ITimeAutomationPipelineTrigger => SymbolRegular.HourglassHalf24,
        IDeviceAutomationPipelineTrigger => SymbolRegular.UsbPlug24,
        INativeWindowsMessagePipelineTrigger => SymbolRegular.Desktop24,
        IOnStartupAutomationPipelineTrigger => SymbolRegular.Flash24,
        IOnResumeAutomationPipelineTrigger => SymbolRegular.Flash24,
        IWiFiConnectedPipelineTrigger => SymbolRegular.Wifi124,
        IWiFiDisconnectedPipelineTrigger => SymbolRegular.WifiOff24,
        IPeriodicAutomationPipelineTrigger => SymbolRegular.ArrowRepeatAll24,
        _ when IsExtensionTrigger(trigger) => GetExtensionTriggerIcon(trigger),
        _ => throw new ArgumentException($"Unsupported trigger {trigger.GetType().Name}")
    };

    private static bool IsExtensionTrigger(IAutomationPipelineTrigger trigger)
    {
        var registry = IoCContainer.Resolve<IAutomationTriggerRegistry>();
        return registry.Triggers.Any(t => t.TriggerType.IsInstanceOfType(trigger));
    }

    private static SymbolRegular GetExtensionTriggerIcon(IAutomationPipelineTrigger trigger)
    {
        var registry = IoCContainer.Resolve<IAutomationTriggerRegistry>();
        var extInfo = registry.Triggers.FirstOrDefault(t => t.TriggerType.IsInstanceOfType(trigger));
        return extInfo?.Icon.HasValue == true ? (SymbolRegular)extInfo.Icon.Value : SymbolRegular.PuzzlePiece24;
    }
}
