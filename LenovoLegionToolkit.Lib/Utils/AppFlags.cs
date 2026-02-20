using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LenovoLegionToolkit.Lib.Utils;

public class AppFlags
{
    private static AppFlags? _instance;
    public static AppFlags Instance => _instance ?? throw new InvalidOperationException("AppFlags must be initialized before access.");

    private bool _isTraceEnabled;
    public bool IsTraceEnabled
    {
        get => _isTraceEnabled;
        set { _isTraceEnabled = value; Save(); }
    }

    private bool _minimized;
    public bool Minimized
    {
        get => _minimized;
        set { _minimized = value; Save(); }
    }

    private bool _skipCompatibilityCheck;
    public bool SkipCompatibilityCheck
    {
        get => _skipCompatibilityCheck;
        set { _skipCompatibilityCheck = value; Save(); }
    }

    private bool _debug;
    public bool Debug
    {
        get => _debug;
        set { _debug = value; Save(); }
    }

    private bool _disableTrayTooltip;
    public bool DisableTrayTooltip
    {
        get => _disableTrayTooltip;
        set { _disableTrayTooltip = value; Save(); }
    }

    private bool _allowAllPowerModesOnBattery;
    public bool AllowAllPowerModesOnBattery
    {
        get => _allowAllPowerModesOnBattery;
        set { _allowAllPowerModesOnBattery = value; Save(); }
    }

    private bool _forceDisableRgbKeyboardSupport;
    public bool ForceDisableRgbKeyboardSupport
    {
        get => _forceDisableRgbKeyboardSupport;
        set { _forceDisableRgbKeyboardSupport = value; Save(); }
    }

    private bool _forceDisableSpectrumKeyboardSupport;
    public bool ForceDisableSpectrumKeyboardSupport
    {
        get => _forceDisableSpectrumKeyboardSupport;
        set { _forceDisableSpectrumKeyboardSupport = value; Save(); }
    }

    private bool _forceDisableLenovoLighting;
    public bool ForceDisableLenovoLighting
    {
        get => _forceDisableLenovoLighting;
        set { _forceDisableLenovoLighting = value; Save(); }
    }

    private bool _experimentalGPUWorkingMode;
    public bool ExperimentalGPUWorkingMode
    {
        get => _experimentalGPUWorkingMode;
        set { _experimentalGPUWorkingMode = value; Save(); }
    }

    private Uri? _proxyUrl;
    public Uri? ProxyUrl
    {
        get => _proxyUrl;
        set { _proxyUrl = value; Save(); }
    }

    private string? _proxyUsername;
    public string? ProxyUsername
    {
        get => _proxyUsername;
        set { _proxyUsername = value; Save(); }
    }

    private string? _proxyPassword;
    public string? ProxyPassword
    {
        get => _proxyPassword;
        set { _proxyPassword = value; Save(); }
    }

    private bool _proxyAllowAllCerts;
    public bool ProxyAllowAllCerts
    {
        get => _proxyAllowAllCerts;
        set { _proxyAllowAllCerts = value; Save(); }
    }

    private bool _disableUpdateChecker;
    public bool DisableUpdateChecker
    {
        get => _disableUpdateChecker;
        set { _disableUpdateChecker = value; Save(); }
    }

    private bool _disableConflictingSoftwareWarning;
    public bool DisableConflictingSoftwareWarning
    {
        get => _disableConflictingSoftwareWarning;
        set { _disableConflictingSoftwareWarning = value; Save(); }
    }

    private bool _enableHardwareAcceleration;
    public bool EnableHardwareAcceleration
    {
        get => _enableHardwareAcceleration;
        set { _enableHardwareAcceleration = value; Save(); }
    }

    public static void Initialize(IEnumerable<string>? startupArgs)
    {
        _instance = new AppFlags(startupArgs);
    }

    private AppFlags(IEnumerable<string>? startupArgs)
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

        _isTraceEnabled = BoolValue(args, "--trace");
        _minimized = BoolValue(args, "--minimized");
        _skipCompatibilityCheck = BoolValue(args, "--skip-compat-check");
        _debug = BoolValue(args, "--debug");
        _disableTrayTooltip = BoolValue(args, "--disable-tray-tooltip");
        _allowAllPowerModesOnBattery = BoolValue(args, "--allow-all-power-modes-on-battery");
        _forceDisableRgbKeyboardSupport = BoolValue(args, "--force-disable-rgbkb");
        _forceDisableSpectrumKeyboardSupport = BoolValue(args, "--force-disable-spectrumkb");
        _forceDisableLenovoLighting = BoolValue(args, "--force-disable-lenovolighting");
        _experimentalGPUWorkingMode = BoolValue(args, "--experimental-gpu-working-mode");
        _proxyUrl = Uri.TryCreate(StringValue(args, "--proxy-url"), UriKind.Absolute, out var uri) ? uri : null;
        _proxyUsername = StringValue(args, "--proxy-username");
        _proxyPassword = StringValue(args, "--proxy-password");
        _proxyAllowAllCerts = BoolValue(args, "--proxy-allow-all-certs");
        _disableUpdateChecker = BoolValue(args, "--disable-update-checker");
        _disableConflictingSoftwareWarning = BoolValue(args, "--disable-conflicting-software-warning");
        _enableHardwareAcceleration = BoolValue(args, "--enable-hw-accel");
    }

    private void Save()
    {
        try
        {
            var args = new List<string>();
            if (IsTraceEnabled) args.Add("--trace");
            if (Minimized) args.Add("--minimized");
            if (SkipCompatibilityCheck) args.Add("--skip-compat-check");
            if (Debug) args.Add("--debug");
            if (DisableTrayTooltip) args.Add("--disable-tray-tooltip");
            if (AllowAllPowerModesOnBattery) args.Add("--allow-all-power-modes-on-battery");
            if (ForceDisableRgbKeyboardSupport) args.Add("--force-disable-rgbkb");
            if (ForceDisableSpectrumKeyboardSupport) args.Add("--force-disable-spectrumkb");
            if (ForceDisableLenovoLighting) args.Add("--force-disable-lenovolighting");
            if (ExperimentalGPUWorkingMode) args.Add("--experimental-gpu-working-mode");
            if (ProxyUrl != null) args.Add($"--proxy-url={ProxyUrl}");
            if (!string.IsNullOrEmpty(ProxyUsername)) args.Add($"--proxy-username={ProxyUsername}");
            if (!string.IsNullOrEmpty(ProxyPassword)) args.Add($"--proxy-password={ProxyPassword}");
            if (ProxyAllowAllCerts) args.Add("--proxy-allow-all-certs");
            if (DisableUpdateChecker) args.Add("--disable-update-checker");
            if (DisableConflictingSoftwareWarning) args.Add("--disable-conflicting-software-warning");
            if (EnableHardwareAcceleration) args.Add("--enable-hw-accel");

            var argsFile = Path.Combine(Folders.AppData, "args.txt");
            File.WriteAllLines(argsFile, args);
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Failed to save AppFlags.", ex);
        }
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
        $" {nameof(ProxyUrl)}: {ProxyUrl}," +
        $" {nameof(ProxyUsername)}: {ProxyUsername}," +
        $" {nameof(ProxyPassword)}: {ProxyPassword}," +
        $" {nameof(ProxyAllowAllCerts)}: {ProxyAllowAllCerts}," +
        $"{nameof(DisableUpdateChecker)}: {DisableUpdateChecker}, " +
        $" {nameof(DisableConflictingSoftwareWarning)}: {DisableConflictingSoftwareWarning}, " +
        $" {nameof(EnableHardwareAcceleration)}: {EnableHardwareAcceleration}";
}
