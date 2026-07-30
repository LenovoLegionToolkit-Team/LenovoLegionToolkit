using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Management;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LenovoLegionToolkit.Lib.Controllers;
using LenovoLegionToolkit.Lib.Controllers.GodMode;
using LenovoLegionToolkit.Lib.Features;
using LenovoLegionToolkit.Lib.Resources;
using LenovoLegionToolkit.Lib.Settings;
using LenovoLegionToolkit.Lib.System;
using LenovoLegionToolkit.Lib.Utils;
using ZenStates.Core;

namespace LenovoLegionToolkit.Lib.Overclocking.Amd;

public sealed class AmdOverclockingController : IDisposable
{
    private const int THRESHOLD = 3;
    private const uint DOWNCORE_CMD_DEFAULT = 0x8000;
    private const uint DOWNCORE_CCD1_DISABLE_ALL = 0x81FF;
    private const uint DOWNCORE_CCD1_ENABLE_ALL = 0x8100;
    private const string WMI_AMD_ACPI = "AMD_ACPI";
    private const string WMI_SCOPE = @"root\wmi";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _defaultProfilePath = Path.Combine(Folders.AppData, "default_amd_overclocking.json");
    private readonly string _statusFilePath = Path.Combine(Folders.AppData, "system_status.json");
    private readonly AmdOverclockingSettings _settings;

    private Cpu? _cpu;
    private MachineInformation? _machineInformation;
    private ManagementObject? _classInstance;
    private bool _isInitialized;

    private List<AmdWmiCommand> _commandList = [];
    private AmdWmiCommand? _cachedDowncoreCmd;

    public bool DoNotApply { get; set; }

    public bool Enabled
    {
        get => _settings.Store.Enabled;
        set
        {
            _settings.Store.Enabled = value;
            _settings.SynchronizeStore();
        }
    }

    public bool AllowOnBattery
    {
        get => _settings.Store.AllowOnBattery;
        set
        {
            _settings.Store.AllowOnBattery = value;
            _settings.SynchronizeStore();
        }
    }

    public bool AllowInAllPowerModes
    {
        get => _settings.Store.AllowInAllPowerModes;
        set
        {
            _settings.Store.AllowInAllPowerModes = value;
            _settings.SynchronizeStore();
        }
    }

    public AmdOverclockingController(AmdOverclockingSettings settings)
    {
        _settings = settings;
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            _machineInformation = await Compatibility.GetMachineInformationAsync().ConfigureAwait(false);
            _cpu = new Cpu();

            UpdateShutdownStatus();
            FetchCommands();

            _isInitialized = true;
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"AmdOverclockingController initialization failed: {ex.Message}");
            _isInitialized = true;
            _cpu = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void UpdateShutdownStatus()
    {
        var info = LoadShutdownInfo();
        var count = info.Status == "Running" ? info.AbnormalCount + 1 : info.AbnormalCount;

        if (count > info.AbnormalCount)
        {
            Log.Instance.Trace($"Abnormal shutdown detected, count: {count}");
        }

        if (count >= THRESHOLD)
        {
            DoNotApply = true;
            Log.Instance.Trace($"Abnormal shutdown limit reached ({THRESHOLD}). Profile application disabled.");
            count = 0;
        }

        SaveShutdownInfo(new ShutdownInfo { Status = "Running", AbnormalCount = count });
    }

    public bool IsSupported() => _isInitialized && _machineInformation?.Properties.IsAmdDevice == true;

    public bool IsActive() => _settings.Store.Enabled;

    public Cpu GetCpu() => _cpu ?? throw new InvalidOperationException(Resource.AmdOverclocking_Not_Initialized_Message);

    public ShutdownInfo LoadShutdownInfo()
    {
        if (!File.Exists(_statusFilePath)) return new ShutdownInfo { Status = "Normal", AbnormalCount = 0 };
        try
        {
            using var stream = File.OpenRead(_statusFilePath);
            return JsonSerializer.Deserialize<ShutdownInfo>(stream);
        }
        catch
        {
            return new ShutdownInfo { Status = "Normal", AbnormalCount = 0 };
        }
    }

    public void SaveShutdownInfo(ShutdownInfo info)
    {
        try
        {
            File.WriteAllText(_statusFilePath, JsonSerializer.Serialize(info));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Save ShutdownInfo failed: {ex.Message}");
        }
    }

    public OverclockingProfile? LoadProfile(string? path = null)
    {
        if (path != null)
        {
            if (!File.Exists(path)) return null;

            try
            {
                return JsonSerializer.Deserialize<OverclockingProfile>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Load Profile Failed: {ex.Message}");
                return null;
            }
        }

        return _settings.GetProfile();
    }

    public void SaveProfile(OverclockingProfile profile, string? path = null)
    {
        if (path != null)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(path, JsonSerializer.Serialize(profile, options));
            }
            catch (Exception ex)
            {
                Log.Instance.Trace($"Save Profile Failed: {ex.Message}");
            }

            return;
        }

        _settings.SetProfile(profile);
    }

    public OverclockingProfile? LoadDefaultProfile()
    {
        if (!File.Exists(_defaultProfilePath)) return null;

        try
        {
            return JsonSerializer.Deserialize<OverclockingProfile>(File.ReadAllText(_defaultProfilePath));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Load Default Profile Failed: {ex.Message}");
            return null;
        }
    }

    public void SaveDefaultProfile(OverclockingProfile profile)
    {
        if (File.Exists(_defaultProfilePath)) return;

        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(_defaultProfilePath, JsonSerializer.Serialize(profile, options));
            Log.Instance.Trace($"Default profile saved.");
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Save Default Profile Failed: {ex.Message}");
        }
    }

    public async Task ApplyDefaultProfileAsync()
    {
        if (LoadDefaultProfile() is { } defaultProfile)
        {
            await ApplyProfileAsync(defaultProfile).ConfigureAwait(false);
            Log.Instance.Trace($"Default overclocking profile applied.");
        }
    }

    public async Task ApplyProfileAsync(OverclockingProfile profile)
    {
        if (DoNotApply)
        {
            Log.Instance.Trace($"Overclocking is disabled (DoNotApply). Skipping.");
            return;
        }

        if (!_settings.Store.AllowOnBattery)
        {
            var powerStatus = await Power.IsPowerAdapterConnectedAsync().ConfigureAwait(false);
            if (powerStatus is PowerAdapterStatus.Disconnected or PowerAdapterStatus.ConnectedLowWattage)
                throw new InvalidOperationException(Resource.AmdOverclocking_Ac_Message);
        }

        EnsureInitialized();

        await Task.Run(() =>
        {
            bool supportsCO = _cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin != 0;

            // Curve Optimizer
            if (supportsCO && profile.CoreValues?.Count > 0)
            {
                var applied = new List<string>();
                for (int i = 0; i < Math.Min(profile.CoreValues.Count, 16); i++)
                {
                    if (profile.CoreValues[i] is { } value && IsCoreActive(i))
                    {
                        if (_cpu.SetPsmMarginSingleCore(EncodeCoreMarginBitmask(i), (int)value))
                        {
                            applied.Add($"Core {i}: {value}");
                        }
                    }
                }
                Log.Instance.Trace($"Curve Optimizer applied: {(applied.Count > 0 ? string.Join(", ", applied) : "none")}");
            }

            // Curve Shaper
            if (profile.CurveShapeValues?.Count > 0)
            {
                var shaped = new List<string>();
                foreach (var kv in profile.CurveShapeValues)
                {
                    if (kv.Value.Count >= 3 && kv.Value.Any(v => v != 0))
                    {
                        var status = _cpu.SetCurveShaperMargin(kv.Value[2], kv.Value[1], kv.Value[0], (int)kv.Key);
                        shaped.Add($"Level {(int)kv.Key}:{(status == SMU.Status.OK ? "OK" : "FAIL")}");
                    }
                }
                Log.Instance.Trace($"Curve Shaper: {(shaped.Count > 0 ? string.Join(", ", shaped) : "none")}");
            }

            // Advanced Settings
            ApplyAndLog("FMax", profile.FMax, v => _cpu.SetFMax(v));
            ApplyAndLog("StapmLimit", profile.PowerLimit1, v => _cpu.SetStapmLimit((uint)v));
            ApplyAndLog("FastLimit", profile.PowerLimit2, v => _cpu.SetFastLimit((uint)v));
            ApplyAndLog("SlowLimit", profile.PowerLimit3, v => _cpu.SetSlowLimit((uint)v));
            ApplyAndLog("TDCSOC", profile.TDCSoc, v => _cpu.SetTDCSOCLimit((uint)v));
            ApplyAndLog("TDCVDD", profile.TDCVdd, v => _cpu.SetTDCVDDLimit((uint)v));
            ApplyAndLog("EDCSOC", profile.EDCSoc, v => _cpu.SetEDCSOCLimit((uint)v));
            ApplyAndLog("EDCVDD", profile.EDCVdd, v => _cpu.SetEDCVDDLimit((uint)v));
        }).ConfigureAwait(false);

        await ForceApplyPowerMappingAsync().ConfigureAwait(false);
        Log.Instance.Trace($"Overclocking profile applied successfully.");
    }

    public async Task ApplyInternalProfileAsync()
    {
        if (LoadProfile() is { } profile)
        {
            await ApplyProfileAsync(profile).ConfigureAwait(false);
        }
    }

    private static async Task ForceApplyPowerMappingAsync()
    {
        try
        {
            var powerModeFeature = IoCContainer.Resolve<PowerModeFeature>();
            var state = await powerModeFeature.GetStateAsync().ConfigureAwait(false);
            var settings = IoCContainer.Resolve<ApplicationSettings>();
            var mappingMode = settings.Store.PowerModeMappingMode;

            if (mappingMode == PowerModeMappingMode.WindowsPowerMode)
            {
                var windowsPowerModeController = IoCContainer.Resolve<WindowsPowerModeController>();
                await windowsPowerModeController.SetBalancedPowerModeAsync(skipThrottle: true).ConfigureAwait(false);
            }
            else if (mappingMode == PowerModeMappingMode.WindowsPowerPlan)
            {
                var windowsPowerPlanController = IoCContainer.Resolve<WindowsPowerPlanController>();
                await windowsPowerPlanController.SetBalancedPowerPlanAsync(skipThrottle: true).ConfigureAwait(false);
            }

            if (state == PowerModeState.GodMode)
            {
                var godModeController = IoCContainer.Resolve<GodModeController>();
                var (_, preset) = await godModeController.GetActivePresetAsync().ConfigureAwait(false);
                await powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(preset, skipThrottle: true).ConfigureAwait(false);
            }
            else
            {
                await powerModeFeature.EnsureCorrectWindowsPowerSettingsAreSetAsync(skipThrottle: true).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"ForceApplyPowerMappingAsync failed.", ex);
        }
    }

    public uint EncodeCoreMarginBitmask(int coreIndex, int coresPerCCD = 8)
    {
        EnsureInitialized();
        if (_cpu.smu.SMU_TYPE is >= SMU.SmuType.TYPE_APU0 and <= SMU.SmuType.TYPE_APU2)
            return (uint)coreIndex;

        var ccdIndex = coreIndex / coresPerCCD;
        var localCoreIndex = coreIndex % coresPerCCD;
        return (uint)(((ccdIndex << 8) | localCoreIndex) << 20);
    }

    public bool IsCoreActive(int coreIndex)
    {
        EnsureInitialized();

        return _cpu.GetPsmMarginSingleCore(EncodeCoreMarginBitmask(coreIndex)) != null;
    }

    private void ApplyAndLog<T>(string settingName, T? value, Action<T> applyAction) where T : struct
    {
        if (value.HasValue)
        {
            Log.Instance.Trace($"{settingName,-12} = {value.Value}");
            applyAction(value.Value);
        }
    }

    public static uint[] MakeCmdArgs(uint arg = 0, uint maxArgs = 6)
    {
        var cmdArgs = new uint[maxArgs];
        cmdArgs[0] = arg;
        return cmdArgs;
    }

    private string GetWmiInstanceName()
    {
        try
        {
            return WMI.GetInstanceName(WMI_SCOPE, WMI_AMD_ACPI);
        }
        catch
        {
            throw new NotSupportedException(Resource.AmdOverclocking_Not_Supported);
        }
    }

    public void FetchCommands()
    {
        try
        {
            _classInstance?.Dispose();
            _classInstance = new ManagementObject(WMI_SCOPE, $"{WMI_AMD_ACPI}.InstanceName='{GetWmiInstanceName()}'", null);

            var commands = new List<AmdWmiCommand>();
            string[] methods = ["GetObjectID", "GetObjectID2"];

            foreach (var method in methods)
            {
                var pack = WMI.InvokeMethodAndGetValue(_classInstance, method, "pack", null, 0);
                if (pack == null) continue;

                if (pack.GetPropertyValue("ID") is uint[] ids &&
                    pack.GetPropertyValue("IDString") is string[] names &&
                    pack.GetPropertyValue("Length") is byte count)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (string.IsNullOrWhiteSpace(names[i])) break;
                        commands.Add(new AmdWmiCommand
                        {
                            Name = names[i],
                            Id = ids[i],
                            IsSet = !names[i].StartsWith("Get", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            _commandList = commands;
            _cachedDowncoreCmd = _commandList.Find(i => i.Name.Contains("Software Downcore Config"));
        }
        catch (Exception ex)
        {
            Log.Instance.Trace($"Fetch WMI commands failed: {ex.Message}");
            _commandList = [];
            _cachedDowncoreCmd = null;
        }
    }

    public async Task ResetAllActiveCoresCoAsync()
    {
        EnsureInitialized();

        if (_cpu.smu.Rsmu.SMU_MSG_SetDldoPsmMargin == 0)
        {
            Log.Instance.Trace($"Current CPU does not support SMU_MSG_SetDldoPsmMargin.");
            return;
        }

        await Task.Run(() =>
        {
            for (var i = 0; i < 16; i++)
            {
                if (IsCoreActive(i))
                {
                    try
                    {
                        uint bitmask = EncodeCoreMarginBitmask(i);
                        _cpu.SetPsmMarginSingleCore(bitmask, 0);
                        Log.Instance.Trace($"Reset CO for Core {i} (Bitmask: 0x{bitmask:X}) to 0.");
                    }
                    catch (Exception ex)
                    {
                        Log.Instance.Trace($"Failed to reset CO for Core {i}: {ex.Message}");
                    }
                }
            }
        }).ConfigureAwait(false);
    }

    public bool SwitchProfile(CpuProfileMode mode)
    {
        EnsureInitialized();

        if (_cachedDowncoreCmd == null)
        {
            Log.Instance.Trace($"Downcore command not supported on this system.");
            return false;
        }

        uint subCommand = mode == CpuProfileMode.X3DGaming ? DOWNCORE_CCD1_DISABLE_ALL : DOWNCORE_CCD1_ENABLE_ALL;

        WMI.RunCommand(_classInstance, _cachedDowncoreCmd.Value.Id, DOWNCORE_CMD_DEFAULT);
        WMI.RunCommand(_classInstance, _cachedDowncoreCmd.Value.Id, subCommand);

        return true;
    }

    [MemberNotNull(nameof(_cpu), nameof(_machineInformation), nameof(_classInstance))]
    private void EnsureInitialized()
    {
        if (!_isInitialized || _cpu == null || _machineInformation == null || _classInstance == null)
        {
            throw new InvalidOperationException(Resource.AmdOverclocking_Not_Initialized_Message);
        }
    }

    public void Dispose()
    {
        _cpu?.Dispose();
        _classInstance?.Dispose();
        _lock.Dispose();
        GC.SuppressFinalize(this);
    }
}
