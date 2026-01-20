using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LenovoLegionToolkit.Lib.Utils;

namespace LenovoLegionToolkit.WPF;

public class Flags
{
    public bool IsTraceEnabled { get; }
    public bool Minimized { get; }
    public bool SkipCompatibilityCheck { get; }
    public bool Debug { get; }
    public bool DisableTrayTooltip { get; }
    public bool AllowAllPowerModesOnBattery { get; }
    public bool ForceDisableRgbKeyboardSupport { get; }
    public bool ForceDisableSpectrumKeyboardSupport { get; }
    public bool ForceDisableLenovoLighting { get; }
    public bool ExperimentalGPUWorkingMode { get; }
    public bool EnableCustomFanCurve { get; }
    public Uri? ProxyUrl { get; }
    public string? ProxyUsername { get; }
    public string? ProxyPassword { get; }
    public bool ProxyAllowAllCerts { get; }
    public bool DisableUpdateChecker { get; }
    public bool DisableConflictingSoftwareWarning { get; }

    public Flags(IEnumerable<string>? startupArgs)
    {
        var argsList = new List<string>();

        if (startupArgs != null)
        {
            argsList.AddRange(startupArgs);
        }

        var externalArgs = LoadExternalArgs();
        if (externalArgs is { Length: > 0 })
        {
            argsList.AddRange(externalArgs);
        }

        if (argsList.Count == 0)
        {
            return;
        }

        var args = argsList.ToArray();

        IsTraceEnabled = BoolValue(args, "--trace");
        Minimized = BoolValue(args, "--minimized");
        SkipCompatibilityCheck = BoolValue(args, "--skip-compat-check");
        Debug = BoolValue(args, "--debug");
        DisableTrayTooltip = BoolValue(args, "--disable-tray-tooltip");
        AllowAllPowerModesOnBattery = BoolValue(args, "--allow-all-power-modes-on-battery");
        ForceDisableRgbKeyboardSupport = BoolValue(args, "--force-disable-rgbkb");
        ForceDisableSpectrumKeyboardSupport = BoolValue(args, "--force-disable-spectrumkb");
        ForceDisableLenovoLighting = BoolValue(args, "--force-disable-lenovolighting");
        EnableCustomFanCurve = BoolValue(args, "--enable-custom-fan-curve");
        ExperimentalGPUWorkingMode = BoolValue(args, "--experimental-gpu-working-mode");
        ProxyUrl = Uri.TryCreate(StringValue(args, "--proxy-url"), UriKind.Absolute, out var uri) ? uri : null;
        ProxyUsername = StringValue(args, "--proxy-username");
        ProxyPassword = StringValue(args, "--proxy-password");
        ProxyAllowAllCerts = BoolValue(args, "--proxy-allow-all-certs");
        DisableUpdateChecker = BoolValue(args, "--disable-update-checker");
        DisableConflictingSoftwareWarning = BoolValue(args, "--disable-conflicting-software-warning");
    }

    private static string[] LoadExternalArgs()
    {
        try
        {
            var argsFile = Path.Combine(Folders.AppData, "args.txt");
            return !File.Exists(argsFile) ? [] : File.ReadAllLines(argsFile);
        }
        catch
        {
            return [];
        }
    }

    private static bool BoolValue(IEnumerable<string> values, string key) => values.Contains(key);

    private static string? StringValue(IEnumerable<string> values, string key)
    {
        var value = values.FirstOrDefault(s => s.StartsWith(key));
        return value?.Remove(0, key.Length + 1);
    }

    public override string ToString() =>
        $"{nameof(IsTraceEnabled)}: {IsTraceEnabled}," +
        $" {nameof(Minimized)}: {Minimized}," +
        $" {nameof(SkipCompatibilityCheck)}: {SkipCompatibilityCheck}," +
        $" {nameof(Debug)}: {Debug}," +
        $" {nameof(DisableTrayTooltip)}: {DisableTrayTooltip}," +
        $" {nameof(AllowAllPowerModesOnBattery)}: {AllowAllPowerModesOnBattery}," +
        $" {nameof(ForceDisableRgbKeyboardSupport)}: {ForceDisableRgbKeyboardSupport}," +
        $" {nameof(ForceDisableSpectrumKeyboardSupport)}: {ForceDisableSpectrumKeyboardSupport}," +
        $" {nameof(ForceDisableLenovoLighting)}: {ForceDisableLenovoLighting}," +
        $" {nameof(ExperimentalGPUWorkingMode)}: {ExperimentalGPUWorkingMode}," +
        $" {nameof(EnableCustomFanCurve)}: {EnableCustomFanCurve}," +
        $" {nameof(ProxyUrl)}: {ProxyUrl}," +
        $" {nameof(ProxyUsername)}: {ProxyUsername}," +
        $" {nameof(ProxyPassword)}: {ProxyPassword}," +
        $" {nameof(ProxyAllowAllCerts)}: {ProxyAllowAllCerts}," +
        $" {nameof(DisableUpdateChecker)}: {DisableUpdateChecker}, " +
        $" {nameof(DisableConflictingSoftwareWarning)}: {DisableConflictingSoftwareWarning}";
}